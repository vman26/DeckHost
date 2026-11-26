using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace DeckHost.App.Platform;

/// <summary>
/// Whether an app is running, for Launch App's green dot, and closing it. Listing processes takes
/// a moment, so <see cref="IsRunning"/> answers from a cache that is refreshed in the background.
/// </summary>
internal static class RunningApps
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(2);
    private static readonly ConcurrentDictionary<string, (bool Running, long At)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> Refreshing = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The process name a program path starts: "C:\…\obs64.exe" → obs64, "/Applications/OBS.app" → OBS (pure).</summary>
    public static string ProcessName(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim().Trim('"').TrimEnd('/', '\\');
        return trimmed.Length == 0 ? string.Empty : Path.GetFileNameWithoutExtension(trimmed.Replace('\\', '/').Split('/')[^1]);
    }

    public static bool IsRunning(string processName)
    {
        if (processName.Length == 0)
        {
            return false;
        }

        Cache.TryGetValue(processName, out var entry);
        if (Environment.TickCount64 - entry.At > MaxAge.TotalMilliseconds && Refreshing.TryAdd(processName, 0))
        {
            _ = Task.Run(() =>
            {
                try
                {
                    Cache[processName] = (Find(processName).Length > 0, Environment.TickCount64);
                }
                finally
                {
                    Refreshing.TryRemove(processName, out _);
                }
            });
        }

        return entry.Running;
    }

    /// <summary>Asks every process with this name to close its window (as clicking ✕ would), then forgets the cached state.</summary>
    public static void Close(string processName)
    {
        if (processName.Length == 0)
        {
            throw new InvalidOperationException("Choose a program in Customize.");
        }

        var processes = Find(processName);
        if (processes.Length == 0)
        {
            throw new InvalidOperationException("It isn't running.");
        }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (OperatingSystem.IsMacOS())
                    {
                        ExternalTool.Run("osascript", ["-e", $"tell application \"{processName.Replace("\"", string.Empty, StringComparison.Ordinal)}\" to quit"]);
                        break;
                    }

                    if (!process.CloseMainWindow())
                    {
                        // No window to close (a tray app, or Linux without one): ask it to end.
                        process.Kill();
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Exited meanwhile.
                }
            }
        }

        Cache.TryRemove(processName, out _);
    }

    private static Process[] Find(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName);
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }
}
