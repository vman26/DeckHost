using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using DeckHost.Core.Interfaces;

namespace DeckHost.App.Platform;

/// <summary>
/// Publishes the machine's CPU and memory load as variables (<see cref="CpuKey"/>, <see cref="MemoryKey"/>, 0 to 100) while
/// something follows them, so a background layer can react to them without the renderer reading the machine itself.
/// </summary>
internal sealed class SystemDataPublisher(IDeviceHostService host) : IDisposable
{
    public const string CpuKey = "system.cpu";
    public const string MemoryKey = "system.memory";

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private CancellationTokenSource? _running;
    private (bool Cpu, bool Memory) _wanted;

    public bool IsRunning => _running is not null;

    /// <summary>Starts or stops publishing for the variables something wants (anything else is ignored).</summary>
    public void Want(IEnumerable<string> keys)
    {
        var cpu = false;
        var memory = false;
        foreach (var key in keys)
        {
            cpu |= key == CpuKey;
            memory |= key == MemoryKey;
        }

        // Called with every edit of the profiles: nothing restarts while the same values are wanted.
        if (_wanted == (cpu, memory))
        {
            return;
        }

        _wanted = (cpu, memory);
        Stop();
        if (cpu || memory)
        {
            var source = new CancellationTokenSource();
            _running = source;
            _ = Task.Run(() => RunAsync(cpu, memory, source.Token));
        }
    }

    private async Task RunAsync(bool cpu, bool memory, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                if (cpu && SystemMetrics.CpuUsage() is { } load)
                {
                    host.SetVariable(CpuKey, Format(load * 100d));
                }

                if (memory && SystemMetrics.Memory() is { Total: > 0 } used)
                {
                    host.SetVariable(MemoryKey, Format((double)used.Used / used.Total * 100d));
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string Format(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private void Stop()
    {
        _running?.Cancel();
        _running?.Dispose();
        _running = null;
    }

    public void Dispose() => Stop();
}
