using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace DeckHost.App.Platform;

/// <summary>
/// Reports the app that has focus, checked twice a second: on Windows the focused window's
/// process, on macOS NSWorkspace's frontmost app, on Linux the active X11 window's process
/// (through xprop; Wayland has no common way to ask, so there only X apps are seen). Polling runs
/// off the UI thread; the event is raised on it. DeckHost's own windows are ignored, so editing a
/// profile never switches away from it.
/// </summary>
internal sealed partial class ForegroundAppWatcher : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly Timer _timer;
    private int _lastProcessId;
    private string? _lastTitle;
    private int _polling;

    public ForegroundAppWatcher()
    {
        _timer = new Timer(_ => Poll());
    }

    /// <summary>
    /// Raised on the UI thread with the process name (without .exe) and the window title (null where
    /// it can't be read: macOS, Wayland) when another app gets focus or the focused window's title changes.
    /// </summary>
    public event Action<string, string?>? ForegroundAppChanged;

    public static bool IsSupported =>
        OperatingSystem.IsWindows() ||
        OperatingSystem.IsMacOS() ||
        (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) && ExternalTool.Exists("xprop"));

    /// <summary>Why automatic switching doesn't work here, for the Profile Manager.</summary>
    public static string UnsupportedReason => OperatingSystem.IsLinux()
        ? "Automatic switching needs an X11 session (or XWayland) and xprop (package x11-utils)."
        : "Automatic switching isn't supported on this system.";

    public string? CurrentApp { get; private set; }

    /// <summary>The focused window's title, when it can be read.</summary>
    public string? CurrentTitle { get; private set; }

    public void Start()
    {
        if (IsSupported)
        {
            _timer.Change(TimeSpan.Zero, Interval);
        }
    }

    public void Dispose() => _timer.Dispose();

    /// <summary>Names of running apps that have a window, for picking one in the Profile Manager.</summary>
    public static IReadOnlyList<string> GetRunningApps()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (OperatingSystem.IsMacOS())
        {
            names.UnionWith(MacNative.RunningApps());
        }
        else if (OperatingSystem.IsLinux())
        {
            foreach (var window in LinuxWindows())
            {
                if (LinuxWindowProcess(window) is { } process && process.Id != Environment.ProcessId)
                {
                    names.Add(process.Name);
                }
            }
        }
        else
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id != Environment.ProcessId && process.MainWindowHandle != IntPtr.Zero)
                        {
                            names.Add(process.ProcessName);
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // Exited or protected process.
                    }
                }
            }
        }

        names.Remove(string.Empty);
        return names.ToList();
    }

    private void Poll()
    {
        // A slow xprop must not pile up polls.
        if (Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }

        try
        {
            if (FindForegroundApp() is not { } app || (app.ProcessId == _lastProcessId && app.Title == _lastTitle))
            {
                return;
            }

            if (app.ProcessId == _ownProcessId)
            {
                // Don't switch while DeckHost has focus, but report the next app again even if it's the previous one.
                _lastProcessId = 0;
                return;
            }

            _lastProcessId = app.ProcessId;
            _lastTitle = app.Title;
            Dispatcher.UIThread.Post(() =>
            {
                CurrentApp = app.Name;
                CurrentTitle = app.Title;
                ForegroundAppChanged?.Invoke(app.Name, app.Title);
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or ExternalException)
        {
            // Nothing focused, or the app just exited: try again on the next tick.
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private static (int ProcessId, string Name, string? Title)? FindForegroundApp()
    {
        if (OperatingSystem.IsWindows())
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out var processId) == 0)
            {
                return null;
            }

            using var process = Process.GetProcessById((int)processId);
            return ((int)processId, process.ProcessName, WindowsTitle(window));
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacNative.FrontmostApp() is { } mac ? (mac.ProcessId, mac.Name, null) : null;
        }

        if (OperatingSystem.IsLinux())
        {
            var active = ExternalTool.TryRun("xprop", ["-root", "_NET_ACTIVE_WINDOW"]);
            return WindowIds(active).FirstOrDefault() is { } window && LinuxWindowProcess(window) is { } linux
                ? (linux.Id, linux.Name, ParseWindowName(ExternalTool.TryRun("xprop", ["-id", window, "_NET_WM_NAME"])))
                : null;
        }

        return null;
    }

    private static IEnumerable<string> LinuxWindows() => WindowIds(ExternalTool.TryRun("xprop", ["-root", "_NET_CLIENT_LIST"]));

    /// <summary>"_NET_ACTIVE_WINDOW(WINDOW): window id # 0x3e00007" → "0x3e00007" (0x0 means none).</summary>
    internal static IEnumerable<string> WindowIds(string? xpropOutput) =>
        xpropOutput is null
            ? []
            : WindowIdRegex().Matches(xpropOutput).Select(static match => match.Value).Where(static id => Convert.ToInt64(id, 16) != 0);

    private static (int Id, string Name)? LinuxWindowProcess(string window)
    {
        var output = ExternalTool.TryRun("xprop", ["-id", window, "_NET_WM_PID"]);
        var match = output is null ? null : PidRegex().Match(output);
        if (match is not { Success: true } || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
        {
            return null;
        }

        try
        {
            // The executable's name, as the Profile Manager lists it and users type it.
            return (processId, File.ReadAllText($"/proc/{processId}/comm").Trim());
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>"_NET_WM_NAME(UTF8_STRING) = \"Inbox - Mail\"" → Inbox - Mail (escaped quotes kept as quotes).</summary>
    internal static string? ParseWindowName(string? xpropOutput)
    {
        var match = xpropOutput is null ? null : WindowNameRegex().Match(xpropOutput);
        return match is { Success: true } ? match.Groups[1].Value.Replace("\\\"", "\"", StringComparison.Ordinal) : null;
    }

    private static string? WindowsTitle(IntPtr window)
    {
        var length = GetWindowTextLengthW(window);
        if (length <= 0)
        {
            return null;
        }

        var buffer = new char[length + 1];
        var copied = GetWindowTextW(window, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : null;
    }

    [GeneratedRegex("=\\s*\"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex WindowNameRegex();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr window, [Out] char[] text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLengthW(IntPtr window);

    [GeneratedRegex("0x[0-9a-fA-F]+")]
    private static partial Regex WindowIdRegex();

    [GeneratedRegex(@"=\s*(\d+)")]
    private static partial Regex PidRegex();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
