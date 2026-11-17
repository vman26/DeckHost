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

    /// <summary>Why the built-in browser can't be used right now (null while it can): shown instead of a bare "isn't available".</summary>
    public static string? Reason { get; private set; }

    public static void Reset()
    {
        _available = null;
        Reason = null;
    }

    /// <summary>
    /// Forgets a remembered failure and tries the web view again (the "Try again" button): the runtime may have been
    /// installed or repaired since.
    /// </summary>
    public static void Retry()
    {
        try
        {
            File.Delete(MarkerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without the file being gone the next start still tries once the runtime version changes.
        }

        Reset();
    }

    /// <summary>Puts the real runtime check and marker file back (tests).</summary>
    internal static void RestoreDefaults()
    {
        RuntimeVersion = DetectRuntimeVersion;
        MarkerPath = DisabledFile;
        Reset();
    }

    /// <summary>
    /// A web view failed after it was created: no more web views in this run. When the failure says the runtime itself
    /// can't be used (not registered, not found) it is remembered for the next starts too, until the runtime changes;
    /// a view that was only torn down while it started (<c>E_ABORT</c>, e.g. the editor was rebuilt) is not.
    /// </summary>
    public static void ReportFailure(Exception exception)
    {
        _available = false;
        Reason = $"The web view stopped working ({Describe(exception)}).";
        if (IsRuntimeUnusable(exception))
        {
            Remember(exception);
        }

        Failed?.Invoke();
    }

    /// <summary>Whether the failure means this computer's web view runtime can't be used at all, so trying again at the next start would fail too.</summary>
    internal static bool IsRuntimeUnusable(Exception exception) =>
        exception is System.Runtime.InteropServices.COMException { HResult: ClassNotRegistered or FileNotFound or ModuleNotFound } ||
        exception.GetType().Name.Contains("RuntimeNotFound", StringComparison.Ordinal);

    internal static bool IsUnusableHResult(int hresult) => hresult is ClassNotRegistered or FileNotFound or ModuleNotFound;

    private static string Describe(Exception exception) =>
        $"0x{exception.HResult:X8}: {exception.GetType().Name}: {exception.Message}".ReplaceLineEndings(" ");

    private const int ClassNotRegistered = unchecked((int)0x80040154);
    private const int FileNotFound = unchecked((int)0x80070002);
    private const int ModuleNotFound = unchecked((int)0x8007007E);

    private static void Remember(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            // Line 1: the runtime version, line 2: the error code (so a transient failure from an older version isn't mistaken for a lasting one), line 3: the text.
            File.WriteAllText(MarkerPath, string.Join(Environment.NewLine, RuntimeVersion() ?? "none", $"0x{exception.HResult:X8}", Describe(exception)) + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The marker is a courtesy; this run already stopped using web views.
        }
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
            Reason = OperatingSystem.IsWindows()
                ? "The Microsoft Edge WebView2 runtime isn't installed (or can't be found)."
                : "No web view is available on this system.";
            return false;
        }

        return !FailureRememberedFor(version);
    }

    /// <summary>
    /// A failure is remembered only when it was on this runtime version and said the runtime can't be used. A marker from an
    /// older DeckHost (it kept every failure, also a view that was only torn down while starting) is removed.
    /// </summary>
    private static bool FailureRememberedFor(string version)
    {
        try
        {
            if (!File.Exists(MarkerPath))
            {
                return false;
            }

            var lines = File.ReadAllLines(MarkerPath);
            var lasting = lines.Length >= 2 && lines[0] == version &&
                lines[1].StartsWith("0x", StringComparison.Ordinal) &&
                int.TryParse(lines[1][2..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var hresult) &&
                IsUnusableHResult(hresult);
            if (lasting)
            {
                Reason = $"The WebView2 runtime failed to start on this computer ({(lines.Length > 2 ? lines[2] : lines[1])}).";
                return true;
            }

            File.Delete(MarkerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return false;
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
