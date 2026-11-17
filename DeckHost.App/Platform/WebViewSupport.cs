using System;
using System.IO;
using System.Linq;
using Microsoft.Web.WebView2.Core;

namespace DeckHost.App.Platform;

/// <summary>
/// Whether the built-in web view (plugin settings pages, HTML plugins) can be used on this computer. On Windows it
/// needs the Microsoft Edge WebView2 runtime; without it (or when its COM classes aren't registered) the web view
/// control throws on the UI thread after it was created, which used to end DeckHost. A failure is remembered
/// (<c>webview-disabled.txt</c>, with the runtime version it failed on) so the next start doesn't create it again.
/// </summary>
internal static class WebViewSupport
{
    private static readonly string DisabledFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "DeckHost", "webview-disabled.txt");

    private static bool? _available;

    /// <summary>The check for a runtime; tests replace it.</summary>
    public static Func<string?> RuntimeVersion { get; set; } = DetectRuntimeVersion;

    /// <summary>Folder of the failure marker; tests replace it.</summary>
    public static string MarkerPath { get; set; } = DisabledFile;

    /// <summary>Raised (on any thread) when a web view failed while running: views switch to their fallback.</summary>
    public static event Action? Failed;

    public static bool IsAvailable => !SafeMode.IsOn && (_available ??= Probe());

    public static void Reset() => _available = null;

    /// <summary>Puts the real runtime check and marker file back (tests).</summary>
    internal static void RestoreDefaults()
    {
        RuntimeVersion = DetectRuntimeVersion;
        MarkerPath = DisabledFile;
        _available = null;
    }

    /// <summary>A web view failed after it was created: no more web views in this run or the next ones (until the runtime changes).</summary>
    public static void ReportFailure(Exception exception)
    {
        _available = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, $"{RuntimeVersion() ?? "none"}{Environment.NewLine}{exception.GetType().Name}: {exception.Message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The marker is a courtesy; this run already stopped using web views.
        }

        Failed?.Invoke();
    }

    /// <summary>Whether this exception is a web view control failing (it arrives on the UI thread, not from a call we made).</summary>
    public static bool IsWebViewFailure(Exception exception) =>
        exception.StackTrace?.Contains("WebView", StringComparison.Ordinal) == true &&
        (exception is System.Runtime.InteropServices.COMException || exception.GetType().Name.Contains("WebView2", StringComparison.Ordinal));

    private static bool Probe()
    {
        var version = RuntimeVersion();
        if (string.IsNullOrEmpty(version))
        {
            return false;
        }

        // A failure on this very runtime version: don't try again until it is updated.
        try
        {
            if (File.Exists(MarkerPath) && File.ReadLines(MarkerPath).FirstOrDefault() == version)
            {
                return false;
            }
        }
        catch (IOException)
        {
        }

        return true;
    }

    private static string? DetectRuntimeVersion()
    {
        if (!OperatingSystem.IsWindows())
        {
            // WKWebView and WebKitGTK are checked by the view itself (it falls back after a few seconds).
            return "system";
        }

        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
