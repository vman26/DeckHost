using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using DeckHost.Benchmark;
using DeckHost.Contracts.Enums;

// DeckHost's resource benchmark (docs/performance.md):
//   DeckHost.Benchmark --app <published DeckHost.App[.exe] or DeckHost.app> --out result.json
//       [--budgets benchmarks/budgets.json] [--previous last-release.json] [--summary summary.md]
//       [--phase-seconds 60] [--warmup-seconds 60] [--replace-data]
// Exit code: 0 within budget, 1 over a budget, 2 the run failed.
var options = Options.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("Usage: DeckHost.Benchmark --app <DeckHost program or DeckHost.app> --out <result.json> [--budgets <budgets.json>] [--previous <result.json>] [--summary <file.md>] [--phase-seconds 60] [--warmup-seconds 60] [--replace-data]");
    return 2;
}

try
{
    var result = await Benchmark.RunAsync(options, CancellationToken.None);
    File.WriteAllText(options.Output, result.ToJson());

    var system = Benchmark.SystemName;
    var budgets = options.Budgets is null ? null : Budgets.Parse(File.ReadAllText(options.Budgets));
    var limits = budgets?.For(system) ?? new Dictionary<string, double>();
    var previous = options.Previous is { } previousFile && File.Exists(previousFile) ? BenchmarkResult.FromJson(File.ReadAllText(previousFile)) : null;
    var summary = Report.Markdown(result, limits, previous);
    Console.WriteLine(summary);
    if ((options.Summary ?? Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY")) is { Length: > 0 } summaryFile)
    {
        File.AppendAllText(summaryFile, summary + Environment.NewLine);
    }

    var over = budgets?.Check(system, result.Metrics) ?? [];
    foreach (var violation in over)
    {
        var metric = Metrics.Find(violation.Metric);
        var label = metric?.Label ?? violation.Metric;
        Console.Error.WriteLine(metric is null
            ? $"Over budget: {label} = {violation.Value:0.##} (limit {violation.Limit:0.##})"
            : $"Over budget: {label} = {Report.Format(violation.Value, metric)} (limit {Report.Format(violation.Limit, metric)})");
    }

    return over.Count == 0 ? 0 : 1;
}
catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
{
    Console.Error.WriteLine($"The benchmark failed: {ex.Message}");
    return 2;
}

namespace DeckHost.Benchmark
{
    internal sealed record Options(string App, string Output, string? Budgets, string? Previous, string? Summary, TimeSpan Phase, TimeSpan Warmup, bool ReplaceData)
    {
        public static Options? Parse(string[] args)
        {
            string? Value(string name)
            {
                var index = Array.IndexOf(args, name);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            }

            double Seconds(string name, double fallback) =>
                Value(name) is { } text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : fallback;

            return Value("--app") is { } app && Value("--out") is { } output
                ? new Options(app, output, Value("--budgets"), Value("--previous"), Value("--summary"),
                    TimeSpan.FromSeconds(Seconds("--phase-seconds", 60)), TimeSpan.FromSeconds(Seconds("--warmup-seconds", 60)),
                    Array.IndexOf(args, "--replace-data") >= 0)
                : null;
        }
    }

    /// <summary>The run: idle with the window open, busy, then idle in the tray (a second start).</summary>
    internal static class Benchmark
    {
        /// <summary>Inputs per second in the busy minute.</summary>
        private const int InputsPerSecond = 10;

        /// <summary>A page flip every this many inputs (both pages are drawn in full).</summary>
        private const int InputsPerPageFlip = 50;

        private static readonly TimeSpan ImageTimeout = TimeSpan.FromSeconds(2);

        public static string SystemName =>
            OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

        public static async Task<BenchmarkResult> RunAsync(Options options, CancellationToken cancellationToken)
        {
            var program = ResolveProgram(options.App);
            var metrics = new Dictionary<string, double>();
            using var sandbox = Sandbox.Create(options.ReplaceData);

            // 1. Window open: start-up, idle, busy.
            sandbox.WriteSettings(startInTray: false);
            sandbox.WriteProfiles();
            await using (var run = await DeckHostRun.StartAsync(program, sandbox, inTray: false, cancellationToken))
            {
                metrics[Metrics.StartupFirstFrame] = run.FirstFrameSeconds;
                Log($"First key image after {run.FirstFrameSeconds:0.00} s; warming up for {options.Warmup.TotalSeconds:0} s.");
                await Task.Delay(options.Warmup, cancellationToken);
                run.EnsureRunning("the warm-up");

                Log($"Idle, window open: {options.Phase.TotalSeconds:0} s.");
                var idle = await run.Sampler.SampleAsync(options.Phase, cancellationToken);
                run.EnsureRunning("the idle minute with the window");
                metrics[Metrics.IdleWindowCpu] = Stats.Average(Cpu(idle));
                metrics[Metrics.IdleWindowCpuP95] = Stats.Percentile(Cpu(idle), 95);
                metrics[Metrics.IdleWindowMemory] = Stats.Average(Memory(idle));

                Log($"Busy, {InputsPerSecond} inputs a second: {options.Phase.TotalSeconds:0} s.");
                var (busy, latencies, frames) = await BusyAsync(run, options.Phase, cancellationToken);
                run.EnsureRunning("the busy minute");
                metrics[Metrics.BusyCpu] = Stats.Average(Cpu(busy));
                metrics[Metrics.BusyCpuP95] = Stats.Percentile(Cpu(busy), 95);
                metrics[Metrics.BusyMemoryMax] = Stats.Max(Memory(busy));
                metrics[Metrics.BusyMemoryGrowth] = Stats.Growth(Memory(busy));
                metrics[Metrics.BusyFrames] = frames / options.Phase.TotalSeconds;
                metrics[Metrics.LatencyP50] = Stats.Percentile(latencies, 50);
                metrics[Metrics.LatencyP95] = Stats.Percentile(latencies, 95);
                metrics[Metrics.LatencyMax] = Stats.Max(latencies);
                metrics[Metrics.Threads] = busy.Count == 0 ? 0 : busy[^1].Threads;
                metrics[Metrics.Processes] = busy.Count == 0 ? 0 : busy.Max(static sample => sample.Processes);
            }

            // 2. In the tray, as after signing in with "Start minimized".
            sandbox.WriteSettings(startInTray: true);
            await using (var tray = await DeckHostRun.StartAsync(program, sandbox, inTray: true, cancellationToken))
            {
                Log($"In the tray: warming up for {options.Warmup.TotalSeconds:0} s, then {options.Phase.TotalSeconds:0} s idle.");
                await Task.Delay(options.Warmup, cancellationToken);
                var idle = await tray.Sampler.SampleAsync(options.Phase, cancellationToken);
                tray.EnsureRunning("the idle minute in the tray");
                metrics[Metrics.IdleTrayCpu] = Stats.Average(Cpu(idle));
                metrics[Metrics.IdleTrayMemory] = Stats.Average(Memory(idle));
            }

            return new BenchmarkResult(VersionOf(program), DateTimeOffset.UtcNow, Machine(), metrics);
        }

        /// <summary>
        /// Presses the counter key and turns the knob in turn, <see cref="InputsPerSecond"/> times a second,
        /// flipping the page now and then; times each counter press until its new image arrives.
        /// </summary>
        private static async Task<(IReadOnlyList<Sample> Samples, List<double> Latencies, int Frames)> BusyAsync(DeckHostRun run, TimeSpan duration, CancellationToken cancellationToken)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var sampling = run.Sampler.SampleAsync(duration, stop.Token);
            var latencies = new List<double>();
            var framesBefore = run.ImageCount;
            var interval = TimeSpan.FromSeconds(1.0 / InputsPerSecond);
            var end = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
            for (var input = 0; Stopwatch.GetTimestamp() < end; input++)
            {
                var next = Task.Delay(interval, cancellationToken);
                if (input % InputsPerPageFlip == InputsPerPageFlip - 1)
                {
                    run.Send(Sandbox.PageKey, InputCapabilities.Press);
                    run.Send(Sandbox.PageKey, InputCapabilities.Release);

                    // Both pages draw everything: let that settle before timing presses again.
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                    run.ClearImages();
                }
                else if (input % 2 == 0)
                {
                    run.ClearImages();
                    var pressed = Stopwatch.GetTimestamp();
                    run.Send(Sandbox.CounterKey, InputCapabilities.Press);
                    run.Send(Sandbox.CounterKey, InputCapabilities.Release);
                    if (await run.WaitForImageAsync(Sandbox.CounterKey, ImageTimeout, cancellationToken) is { } shown)
                    {
                        latencies.Add((shown - pressed) * 1000.0 / Stopwatch.Frequency);
                    }
                    else
                    {
                        // No picture: is DeckHost still there? (A crash would otherwise be measured as idle.)
                        run.EnsureRunning("the busy minute");
                        latencies.Add(ImageTimeout.TotalMilliseconds);
                    }
                }
                else
                {
                    run.Send(Sandbox.Knob, InputCapabilities.Rotate, input % 4 == 1 ? 1 : -1);
                }

                await next;
            }

            var samples = await sampling;
            return (samples, latencies, run.ImageCount - framesBefore);
        }

        /// <summary>The program inside a macOS bundle; otherwise the path given.</summary>
        private static string ResolveProgram(string app)
        {
            var path = Path.GetFullPath(app);
            if (Directory.Exists(path) && path.TrimEnd('/').EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                path = Path.Combine(path, "Contents", "MacOS", "DeckHost.App");
            }

            return File.Exists(path) ? path : throw new InvalidOperationException($"No DeckHost program at {path}.");
        }

        /// <summary>DeckHost's version, from the informational version of DeckHost.App.dll next to the program.</summary>
        private static string VersionOf(string program)
        {
            var library = Path.Combine(Path.GetDirectoryName(program)!, "DeckHost.App.dll");
            var version = File.Exists(library) ? FileVersionInfo.GetVersionInfo(library).ProductVersion : null;
            if (version is null)
            {
                return "unknown";
            }

            // "1.3.0-alpha.3+4b09090a5183…" (CI) or "…+Branch.develop.Sha.4b09090a…" (local): the version and a 7-character commit.
            var plus = version.IndexOf('+', StringComparison.Ordinal);
            if (plus < 0)
            {
                return version;
            }

            var commit = version[(plus + 1)..].Split('.')[^1];
            return $"{version[..plus]}+{commit[..Math.Min(7, commit.Length)]}";
        }

        private static MachineInfo Machine() => new(
            SystemName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            Environment.ProcessorCount,
            Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0 / 1024.0, 1));

        private static List<double> Cpu(IEnumerable<Sample> samples) => [.. samples.Select(static sample => sample.CpuPercent)];

        private static List<double> Memory(IEnumerable<Sample> samples) => [.. samples.Select(static sample => sample.MemoryMb)];

        private static void Log(string text) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {text}");
    }
}
