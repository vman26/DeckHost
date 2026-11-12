using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DeckHost.App.Views;

/// <summary>
/// Embeds Microsoft Edge WebView2 (Windows) in an Avalonia window. Used to show Stream Deck
/// plugins' settings pages (property inspectors) inside DeckHost instead of an external browser.
/// Links a page opens in a new window go to the default browser.
/// </summary>
public sealed class WebView2Host : NativeControlHost
{
    public static readonly StyledProperty<Uri?> SourceProperty = AvaloniaProperty.Register<WebView2Host, Uri?>(nameof(Source));

    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipChildren = 0x02000000;

    private IntPtr _window;
    private CoreWebView2Controller? _controller;

    static WebView2Host()
    {
        SourceProperty.Changed.AddClassHandler<WebView2Host>((host, _) => host.Navigate());
        BoundsProperty.Changed.AddClassHandler<WebView2Host>((host, _) => host.UpdateBounds());
    }

    public Uri? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Set when WebView2 couldn't start (no runtime installed, not Windows).</summary>
    public event Action<string>? Failed;

    public static bool IsSupported => OperatingSystem.IsWindows();

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            return base.CreateNativeControlCore(parent);
        }

        _window = CreateWindowEx(0, "Static", string.Empty, WsChild | WsVisible | WsClipChildren, 0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        _ = InitializeAsync();
        return new PlatformHandle(_window, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _controller?.Close();
        _controller = null;
        if (OperatingSystem.IsWindows() && _window != IntPtr.Zero)
        {
            DestroyWindow(_window);
            _window = IntPtr.Zero;
            return;
        }

        base.DestroyNativeControlCore(control);
    }

    private async Task InitializeAsync()
    {
        try
        {
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeckHost", "webview");
            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            _controller = await environment.CreateCoreWebView2ControllerAsync(_window);
            _controller.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 45, 45, 45);
            _controller.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _controller.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _controller.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                // Plugins open help pages and authorisation flows this way: use the real browser.
                e.Handled = true;
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
                }
            };
            UpdateBounds();
            Navigate();
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or ArgumentException)
        {
            Failed?.Invoke(ex is WebView2RuntimeNotFoundException
                ? "The Microsoft Edge WebView2 runtime isn't installed."
                : ex.Message);
        }
    }

    private void Navigate()
    {
        if (_controller is not null && Source is { } source)
        {
            _controller.CoreWebView2.Navigate(source.AbsoluteUri);
        }
    }

    private void UpdateBounds()
    {
        if (_controller is null)
        {
            return;
        }

        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, (int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale));
        if (OperatingSystem.IsWindows() && _window != IntPtr.Zero)
        {
            MoveWindow(_window, 0, 0, (int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale), true);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
}
