using System.Collections.Concurrent;
using System.Diagnostics;
using DeckHost.Contracts.Enums;
using DeckHost.Device.Simulator;

namespace DeckHost.Benchmark;

/// <summary>A started DeckHost with the simulated N4 Pro attached: images it sends are timed per control.</summary>
internal sealed class DeckHostRun : IAsyncDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    private readonly Process _process;
    private readonly Sandbox _sandbox;
    private readonly ConcurrentQueue<string> _output;
    private readonly ConcurrentQueue<(string Control, long Ticks)> _images = new();
    private SimulatorClient? _client;
    private int _imageCount;

    private DeckHostRun(Process process, Sandbox sandbox, ConcurrentQueue<string> output, DateTime started, string processName)
    {
        _process = process;
        _sandbox = sandbox;
        _output = output;
        Started = started;
        Sampler = new ProcessSampler(processName, started);
    }

    public DateTime Started { get; }

    /// <summary>The DeckHost process (not its plugin hosts).</summary>
    public int ProcessId => _process.Id;

    public ProcessSampler Sampler { get; }

    /// <summary>Seconds from starting the program to the first key image on the simulated device.</summary>
    public double FirstFrameSeconds { get; private set; }

    /// <summary>Key images received so far.</summary>
    public int ImageCount => Volatile.Read(ref _imageCount);

    /// <summary>Starts DeckHost and attaches the simulator once its endpoint is up; returns after the first key image.</summary>
    public static async Task<DeckHostRun> StartAsync(string program, Sandbox sandbox, bool inTray, CancellationToken cancellationToken)
    {
        File.Delete(Path.Combine(sandbox.DataDirectory, SimulatorWire.EndpointFileName));
        var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var (name, value) in sandbox.Environment)
        {
            start.Environment[name] = value;
        }

        if (inTray)
        {
            // As at sign-in: with "Start minimized" DeckHost stays in the tray (Program.StartedAtLogin).
            start.ArgumentList.Add("--startup");
        }

        var started = DateTime.UtcNow;
        var clock = Stopwatch.StartNew();
        var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} didn't start.");
        var output = new ConcurrentQueue<string>();
        process.OutputDataReceived += (_, line) => Keep(output, line.Data);
        process.ErrorDataReceived += (_, line) => Keep(output, line.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var run = new DeckHostRun(process, sandbox, output, started, ProcessName(program));
        try
        {
            SimulatorEndpoint? endpoint;
            while ((endpoint = SimulatorWire.ReadEndpoint(sandbox.DataDirectory)) is null)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException($"DeckHost ended with code {process.ExitCode} before the simulator endpoint was up.{Diagnose(sandbox, output, process)}");
                }

                if (clock.Elapsed > StartTimeout)
                {
                    throw new TimeoutException($"No simulator endpoint in {Path.Combine(sandbox.DataDirectory, SimulatorWire.EndpointFileName)} after {StartTimeout.TotalSeconds:0} s.{Diagnose(sandbox, output, process)}");
                }

                await Task.Delay(50, cancellationToken);
            }

            var firstImage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            run._client = await ConnectAsync(endpoint, clock, cancellationToken);
            run._client.MessageReceived += (_, message) =>
            {
                if (message.Type == SimulatorMessage.Image && message.Control is { } control)
                {
                    run._images.Enqueue((control, Stopwatch.GetTimestamp()));
                    Interlocked.Increment(ref run._imageCount);
                    firstImage.TrySetResult();
                }
            };
            run._client.Start();
            run._client.Attach(Sandbox.ModelKey);
            await firstImage.Task.WaitAsync(StartTimeout, cancellationToken);
            run.FirstFrameSeconds = clock.Elapsed.TotalSeconds;
            return run;
        }
        catch
        {
            await run.DisposeAsync();
            throw;
        }
    }

    private const int OutputLinesKept = 40;

    private static void Keep(ConcurrentQueue<string> output, string? line)
    {
        if (line is null)
        {
            return;
        }

        output.Enqueue(line);
        while (output.Count > OutputLinesKept && output.TryDequeue(out _))
        {
        }
    }

    /// <summary>What to look at when DeckHost didn't come up: its output, the files in the sandbox and the end of its log.</summary>
    private static string Diagnose(Sandbox sandbox, IEnumerable<string> output, Process process)
    {
        var text = new System.Text.StringBuilder();
        if (OperatingSystem.IsMacOS() && !process.HasExited)
        {
            // Where the stuck program waits: macOS's sampler, two seconds of stacks.
            var report = Path.Combine(sandbox.Root, "sample.txt");
            using var sample = Process.Start(new ProcessStartInfo("/usr/bin/sample")
            {
                UseShellExecute = false,
                ArgumentList = { process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "2", "-file", report },
            });
            sample?.WaitForExit(15_000);
            if (File.Exists(report))
            {
                text.AppendLine().AppendLine("Stack sample (start):");
                foreach (var line in File.ReadLines(report).Take(120))
                {
                    text.AppendLine("  " + line);
                }
            }
        }

        text.AppendLine().AppendLine("DeckHost's output (last lines):");
        foreach (var line in output)
        {
            text.AppendLine("  " + line);
        }

        // AttributesToSkip none: on Linux and macOS a dot folder (.config) counts as hidden.
        var everywhere = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        var folders = new[] { sandbox.Root, sandbox.DataDirectory }
            .Where(folder => Directory.Exists(folder) && (folder == sandbox.Root || !folder.StartsWith(sandbox.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .ToList();
        foreach (var folder in folders)
        {
            text.AppendLine($"Files under {folder}:");
            foreach (var file in Directory.EnumerateFiles(folder, "*", everywhere).Take(60))
            {
                text.AppendLine("  " + Path.GetRelativePath(folder, file));
            }
        }

        // DeckHost's own crash report (Platform/CrashReporter), and on macOS the system's report of a native crash.
        var reports = new List<string>();
        var crashes = Path.Combine(sandbox.DataDirectory, "crashes");
        if (Directory.Exists(crashes))
        {
            reports.AddRange(Directory.EnumerateFiles(crashes).OrderByDescending(File.GetLastWriteTimeUtc).Take(1));
        }

        var systemReports = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Library", "Logs", "DiagnosticReports");
        if (OperatingSystem.IsMacOS() && Directory.Exists(systemReports))
        {
            reports.AddRange(Directory.EnumerateFiles(systemReports, "DeckHost*")
                .Where(static file => File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddMinutes(-15))
                .OrderByDescending(File.GetLastWriteTimeUtc).Take(1));
        }

        foreach (var report in reports)
        {
            text.AppendLine($"Crash report {report}:");
            foreach (var line in File.ReadLines(report).Take(120))
            {
                text.AppendLine("  " + line);
            }
        }

        var log = folders.SelectMany(folder => Directory.EnumerateFiles(folder, "deckhost-*.log", everywhere)).FirstOrDefault();
        if (log is not null)
        {
            text.AppendLine($"End of {Path.GetFileName(log)}:");
            using var reader = new StreamReader(new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            foreach (var line in reader.ReadToEnd().Split('\n').TakeLast(OutputLinesKept))
            {
                text.AppendLine("  " + line.TrimEnd());
            }
        }

        return text.ToString();
    }

    /// <summary>The name the system lists the program under: "DeckHost.App" (not a file extension to drop), without ".exe" on Windows.</summary>
    internal static string ProcessName(string program)
    {
        var name = Path.GetFileName(program);
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>The endpoint file appears a moment before the listener accepts: retry for a few seconds.</summary>
    private static async Task<SimulatorClient> ConnectAsync(SimulatorEndpoint endpoint, Stopwatch clock, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                return await SimulatorClient.ConnectAsync(endpoint, "DeckHost benchmark", cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException && clock.Elapsed < StartTimeout)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    /// <exception cref="InvalidOperationException">DeckHost has ended (crashed): with what explains it.</exception>
    public void EnsureRunning(string phase)
    {
        if (_process.HasExited)
        {
            throw new InvalidOperationException($"DeckHost ended with code {_process.ExitCode} during {phase}.{Diagnose(_sandbox, _output, _process)}");
        }
    }

    public void Send(string control, InputCapabilities input, int value = 0) => _client!.SendInput(control, input, value);

    /// <summary>Forgets the images received so far (before a press whose answer is timed).</summary>
    public void ClearImages() => _images.Clear();

    /// <summary>Waits for the next image of <paramref name="control"/>; its time stamp, or null after <paramref name="timeout"/>.</summary>
    public async Task<long?> WaitForImageAsync(string control, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var end = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < end)
        {
            while (_images.TryDequeue(out var image))
            {
                if (image.Control == control)
                {
                    return image.Ticks;
                }
            }

            await Task.Delay(1, cancellationToken);
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
        }

        _process.Dispose();
    }
}
