using DeckHost.Core.Support;
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace DeckHost.App.Platform;

/// <summary>
/// Writes a crash report when DeckHost goes down with an unhandled exception (<see cref="CrashReports"/>).
/// Nothing is sent anywhere: the next start offers to report it (Help and support).
/// </summary>
internal static class CrashReporter
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "DeckHost");

    public static string CrashDirectory { get; internal set; } = Path.Combine(DataDirectory, "crashes");

    private static bool _installed;
    private static bool _dispatcherInstalled;

    /// <summary>How many recovered exceptions in a row are tolerated (tests replace it).</summary>
    internal static RecoveryLimiter Limiter { get; set; } = new();

    /// <summary>Remembers whether runs end well, for safe mode (set at start).</summary>
    public static StartupGuard? Guard { get; set; }

    /// <summary>Raised on the UI thread's behalf when an unexpected exception was recovered from instead of ending DeckHost.</summary>
    public static event Action<Exception>? Recovered;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                Guard?.MarkCrashed();
                CrashReports.Write(CrashDirectory, exception, AppInfo.Version, e.IsTerminating ? "unhandled exception" : "unhandled exception (not fatal)");
                try
                {
                    NLog.LogManager.GetLogger("Crash").Fatal(exception, "DeckHost closed unexpectedly.");
                    NLog.LogManager.Flush(TimeSpan.FromSeconds(2));
                }
                catch (Exception)
                {
                    // The report is written; the log is a bonus.
                }
            }
        };

        // A task nobody waited for failed: not a crash, but worth a line in the log.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            NLog.LogManager.GetLogger("Tasks").Warn(e.Exception, "A background task failed unobserved.");
            e.SetObserved();
        };

        CrashReports.Prune(CrashDirectory);
    }

    /// <summary>
    /// The safety net: an exception nobody caught on the UI thread (a web view that fails to start, a bug in a handler) is
    /// written to a crash report and DeckHost carries on, with a notice, instead of closing. A storm of them
    /// (<see cref="RecoveryLimiter"/>) is not recovered from. Call once Avalonia's dispatcher exists.
    /// </summary>
    public static void InstallDispatcherSafetyNet()
    {
        if (_dispatcherInstalled)
        {
            return;
        }

        _dispatcherInstalled = true;
        Dispatcher.UIThread.UnhandledException += (_, e) => e.Handled = Recover(e.Exception);
    }

    /// <summary>Handles one unexpected exception; true when DeckHost can go on.</summary>
    internal static bool Recover(Exception exception)
    {
        if (WebViewSupport.IsWebViewFailure(exception))
        {
            WebViewSupport.ReportFailure(exception);
        }

        if (!Limiter.Allow(DateTimeOffset.UtcNow))
        {
            Guard?.MarkCrashed();
            return false;
        }

        CrashReports.Write(CrashDirectory, exception, AppInfo.Version, "recovered exception");
        try
        {
            NLog.LogManager.GetLogger("Crash").Error(exception, "An unexpected error was recovered from.");
        }
        catch (Exception)
        {
            // The report is written; the log is a bonus.
        }

        Recovered?.Invoke(exception);
        return true;
    }
}

