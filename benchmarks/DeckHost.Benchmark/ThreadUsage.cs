using System.Globalization;

namespace DeckHost.Benchmark;

/// <summary>
/// Which of DeckHost's threads used the CPU during a phase (Linux only, from /proc): the UI thread, the
/// host worker, the render thread or the garbage collector. Printed with every phase, so a number over
/// its budget says where the time went.
/// </summary>
internal static class ThreadUsage
{
    /// <summary>CPU ticks so far per thread id, with the thread's name; empty where /proc isn't there.</summary>
    public static Dictionary<int, (string Name, long Ticks)> Snapshot(int processId)
    {
        var threads = new Dictionary<int, (string Name, long Ticks)>();
        var tasks = $"/proc/{processId}/task";
        if (!OperatingSystem.IsLinux() || !Directory.Exists(tasks))
        {
            return threads;
        }

        foreach (var task in Directory.EnumerateDirectories(tasks))
        {
            try
            {
                if (int.TryParse(Path.GetFileName(task), out var id) && ParseStat(File.ReadAllText(Path.Combine(task, "stat"))) is { } stat)
                {
                    // The main thread is Avalonia's UI thread; the others without a name of their own are
                    // the render thread and thread-pool threads.
                    threads[id] = id == processId ? ("UI thread", stat.Ticks) : stat;
                }
            }
            catch (IOException)
            {
                // The thread ended while it was being read.
            }
        }

        return threads;
    }

    /// <summary>
    /// The busiest threads between two snapshots, as "name 1.2 %" (% of one core over
    /// <paramref name="seconds"/>), busiest first; empty without /proc.
    /// </summary>
    public static string Busiest(Dictionary<int, (string Name, long Ticks)> before, Dictionary<int, (string Name, long Ticks)> after, double seconds, int count = 5)
    {
        const double TicksPerSecond = 100; // USER_HZ on Linux.
        var busiest = after
            .Select(thread => (thread.Value.Name, Ticks: thread.Value.Ticks - (before.TryGetValue(thread.Key, out var earlier) ? earlier.Ticks : 0)))
            .Where(static thread => thread.Ticks > 0)
            .OrderByDescending(static thread => thread.Ticks)
            .Take(count)
            .Select(thread => string.Create(CultureInfo.InvariantCulture, $"{thread.Name} {thread.Ticks / TicksPerSecond / seconds * 100:0.0} %"));
        return string.Join(", ", busiest);
    }

    /// <summary>
    /// The name and user + system CPU ticks of one /proc/&lt;pid&gt;/task/&lt;tid&gt;/stat line; the name
    /// (in parentheses) may itself contain spaces and parentheses.
    /// </summary>
    internal static (string Name, long Ticks)? ParseStat(string line)
    {
        var open = line.IndexOf('(');
        var close = line.LastIndexOf(')');
        if (open < 0 || close < open)
        {
            return null;
        }

        // After the name: state (field 3), ..., utime (field 14) and stime (field 15).
        var fields = line[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 12
            && long.TryParse(fields[11], NumberStyles.None, CultureInfo.InvariantCulture, out var user)
            && long.TryParse(fields[12], NumberStyles.None, CultureInfo.InvariantCulture, out var system)
            ? (line[(open + 1)..close], user + system)
            : null;
    }
}
