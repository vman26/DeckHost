namespace DeckHost.Benchmark;

/// <summary>One measured number: its key in result.json and budgets.json, how to show it, and whether smaller is better.</summary>
public sealed record MetricInfo(string Key, string Label, string Unit, int Decimals = 1);

/// <summary>Every number the benchmark reports, in the order reports show them.</summary>
public static class Metrics
{
    public const string StartupFirstFrame = "startup.firstFrameSeconds";
    public const string IdleWindowCpu = "idleWindow.cpuPercent";
    public const string IdleWindowCpuP95 = "idleWindow.cpuPercentP95";
    public const string IdleWindowMemory = "idleWindow.memoryMb";
    public const string IdleTrayCpu = "idleTray.cpuPercent";
    public const string IdleTrayMemory = "idleTray.memoryMb";
    public const string BusyCpu = "busy.cpuPercent";
    public const string BusyCpuP95 = "busy.cpuPercentP95";
    public const string BusyMemoryMax = "busy.memoryMaxMb";
    public const string BusyMemoryGrowth = "busy.memoryGrowthMb";
    public const string BusyFrames = "busy.framesPerSecond";
    public const string LatencyP50 = "latency.p50Ms";
    public const string LatencyP95 = "latency.p95Ms";
    public const string LatencyMax = "latency.maxMs";
    public const string Threads = "process.threads";
    public const string Processes = "process.count";

    public static IReadOnlyList<MetricInfo> All { get; } =
    [
        new(StartupFirstFrame, "Start to first key image", "s"),
        new(IdleWindowCpu, "Idle, window open: CPU", "% of a core"),
        new(IdleWindowCpuP95, "Idle, window open: CPU (95th percentile)", "% of a core"),
        new(IdleWindowMemory, "Idle, window open: memory", "MB", 0),
        new(IdleTrayCpu, "Idle, in the tray: CPU", "% of a core"),
        new(IdleTrayMemory, "Idle, in the tray: memory", "MB", 0),
        new(BusyCpu, "Busy (10 inputs/s): CPU", "% of a core"),
        new(BusyCpuP95, "Busy: CPU (95th percentile)", "% of a core"),
        new(BusyMemoryMax, "Busy: highest memory", "MB", 0),
        new(BusyMemoryGrowth, "Busy: memory growth over the minute", "MB"),
        new(BusyFrames, "Busy: key images sent", "per s", 0),
        new(LatencyP50, "Press → new key image (median)", "ms", 0),
        new(LatencyP95, "Press → new key image (95th percentile)", "ms", 0),
        new(LatencyMax, "Press → new key image (slowest)", "ms", 0),
        new(Threads, "Threads", "", 0),
        new(Processes, "Processes", "", 0),
    ];

    public static MetricInfo? Find(string key) => All.FirstOrDefault(metric => metric.Key == key);
}

/// <summary>Plain statistics over samples.</summary>
public static class Stats
{
    public static double Average(IReadOnlyCollection<double> values) => values.Count == 0 ? 0 : values.Average();

    public static double Max(IReadOnlyCollection<double> values) => values.Count == 0 ? 0 : values.Max();

    /// <summary>The value below which <paramref name="percent"/> % of the samples fall (nearest rank).</summary>
    public static double Percentile(IReadOnlyCollection<double> values, double percent)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToArray();
        var rank = (int)Math.Ceiling(percent / 100 * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    /// <summary>The change between the first and the last few samples (both averaged, so one sample's noise doesn't decide it).</summary>
    public static double Growth(IReadOnlyList<double> values, int window = 5)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var take = Math.Max(1, Math.Min(window, values.Count / 2));
        return values.TakeLast(take).Average() - values.Take(take).Average();
    }
}
