using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace DeckHost.App.Platform;

/// <summary>
/// Reports the process name of the focused window (Windows only), checked twice a second on the
/// UI thread. DeckHost's own windows are ignored, so editing a profile never switches away from it.
/// </summary>
internal sealed class ForegroundAppWatcher : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly int _ownProcessId = Environment.ProcessId;
    private uint _lastProcessId;

    public ForegroundAppWatcher()
    {
        _timer.Tick += (_, _) => Poll();
    }

    /// <summary>Raised with the process name (without .exe) when another app gets focus.</summary>
    public event Action<string>? ForegroundAppChanged;

    public static bool IsSupported => OperatingSystem.IsWindows();

    public string? CurrentApp { get; private set; }

    public void Start()
    {
        if (IsSupported)
        {
            _timer.Start();
            Poll();
        }
    }

    public void Dispose() => _timer.Stop();

    /// <summary>Names of running apps that have a window, for picking one in the Profile Manager.</summary>
    public static IReadOnlyList<string> GetRunningApps()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
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

        return names.ToList();
    }

    private void Poll()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out var processId) == 0 || processId == _lastProcessId)
        {
            return;
        }

        if (processId == _ownProcessId)
        {
            // Don't switch while DeckHost has focus, but report the next app again even if it's the previous one.
            _lastProcessId = 0;
            return;
        }

        _lastProcessId = processId;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            CurrentApp = process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return;
        }

        ForegroundAppChanged?.Invoke(CurrentApp);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
