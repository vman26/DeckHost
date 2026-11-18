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
    private readonly ConcurrentQueue<(string Control, long Ticks)> _images = new();
    private SimulatorClient? _client;
    private int _imageCount;

    private DeckHostRun(Process process, DateTime started, string processName)
    {
        _process = process;
        Started = started;
        Sampler = new ProcessSampler(processName, started);
    }

    public DateTime Started { get; }

    public ProcessSampler Sampler { get; }

    /// <summary>Seconds from starting the program to the first key image on the simulated device.</summary>
    public double FirstFrameSeconds { get; private set; }

    /// <summary>Key images received so far.</summary>
    public int ImageCount => Volatile.Read(ref _imageCount);

    /// <summary>Starts DeckHost and attaches the simulator once its endpoint is up; returns after the first key image.</summary>
    public static async Task<DeckHostRun> StartAsync(string program, Sandbox sandbox, bool inTray, CancellationToken cancellationToken)
    {
        var endpointFile = Path.Combine(sandbox.DataDirectory, SimulatorWire.EndpointFileName);
        File.Delete(endpointFile);
        var start = new ProcessStartInfo(program) { UseShellExecute = false };
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
        var run = new DeckHostRun(process, started, ProcessName(program));
        try
        {
            SimulatorEndpoint? endpoint;
            while ((endpoint = SimulatorWire.ReadEndpoint(sandbox.DataDirectory)) is null)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException($"DeckHost ended with code {process.ExitCode} before the simulator endpoint was up.");
                }

                if (clock.Elapsed > StartTimeout)
                {
                    throw new TimeoutException($"No simulator endpoint in {endpointFile} after {StartTimeout.TotalSeconds:0} s.");
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
