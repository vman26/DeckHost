using DeckHost.Core.Support;
using System;
using System.IO;
using System.Threading.Tasks;

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

    public static string CrashDirectory { get; } = Path.Combine(DataDirectory, "crashes");

    private static bool _installed;

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
}
