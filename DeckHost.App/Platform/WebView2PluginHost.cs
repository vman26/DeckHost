using Avalonia.Threading;
using DeckHost.Core.StreamDeck;
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace DeckHost.App.Platform;

/// <summary>
/// Runs HTML-based Stream Deck plugins in hidden WebView2 pages (one per plugin), like the
/// Stream Deck app does. The page connects to DeckHost's Stream Deck server once loaded; its
/// console output goes to the DeckHost log.
/// </summary>
public sealed class WebView2PluginHost : IHtmlPluginHost
{
    private const int WsPopup = unchecked((int)0x80000000);

    public static bool IsSupported => OperatingSystem.IsWindows();

    public IDisposable Start(string pluginUuid, Uri page, string startScript, Action<string, string> log)
    {
        var handle = new PageHandle();
        Dispatcher.UIThread.Post(async () => await handle.StartAsync(pluginUuid, page, startScript, log));
        return handle;
    }

    private sealed class PageHandle : IDisposable
    {
        private IntPtr _window;
        private CoreWebView2Controller? _controller;
        private bool _disposed;

        public async Task StartAsync(string pluginUuid, Uri page, string startScript, Action<string, string> log)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                // A hidden, never-shown top-level window owns the browser.
                _window = CreateWindowEx(0, "Static", "DeckHost plugin " + pluginUuid, WsPopup, -32000, -32000, 1024, 768, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeckHost", "webview");
                var environment = await CoreWebView2Environment.CreateAsync(null, userData);
                var controller = await environment.CreateCoreWebView2ControllerAsync(_window);
                if (_disposed)
                {
                    controller.Close();
                    return;
                }

                _controller = controller;

                // Visible to WebView2 (so timers aren't throttled), while its window stays hidden.
                controller.IsVisible = true;
                controller.Bounds = new System.Drawing.Rectangle(0, 0, 1024, 768);
                var web = controller.CoreWebView2;
                web.Settings.AreDefaultScriptDialogsEnabled = false;
                web.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                    {
                        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
                    }
                };

                await web.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
                web.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled").DevToolsProtocolEventReceived += (_, e) =>
                    LogConsole(pluginUuid, e.ParameterObjectAsJson, log);

                web.NavigationCompleted += async (_, e) =>
                {
                    if (!e.IsSuccess)
                    {
                        log("Warning", $"{pluginUuid}: its page didn't load ({e.WebErrorStatus}).");
                        return;
                    }

                    try
                    {
                        await web.ExecuteScriptAsync(startScript);
                    }
                    catch (Exception ex) when (ex is COMException or InvalidOperationException)
                    {
                        log("Warning", $"{pluginUuid}: could not start its page: {ex.Message}");
                    }
                };
                web.Navigate(page.AbsoluteUri);
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or ArgumentException)
            {
                log("Error", ex is WebView2RuntimeNotFoundException
                    ? $"{pluginUuid} can't run: the Microsoft Edge WebView2 runtime isn't installed."
                    : $"{pluginUuid} can't run in the built-in browser: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _disposed = true;
            Dispatcher.UIThread.Post(() =>
            {
                _controller?.Close();
                _controller = null;
                if (_window != IntPtr.Zero)
                {
                    DestroyWindow(_window);
                    _window = IntPtr.Zero;
                }
            });
        }

        private static void LogConsole(string pluginUuid, string json, Action<string, string> log)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : "log";
                var parts = new System.Collections.Generic.List<string>();
                if (root.TryGetProperty("args", out var args))
                {
                    foreach (var arg in args.EnumerateArray())
                    {
                        parts.Add(arg.TryGetProperty("value", out var value) ? value.ToString()
                            : arg.TryGetProperty("description", out var description) ? description.GetString() ?? string.Empty
                            : string.Empty);
                    }
                }

                var level = type is "error" or "assert" ? "Warning" : "Debug";
                log(level, $"{pluginUuid} console.{type}: {string.Join(" ", parts)}");
            }
            catch (JsonException)
            {
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr window);
    }
}
