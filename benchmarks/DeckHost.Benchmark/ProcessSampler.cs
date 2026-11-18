using System.Diagnostics;

namespace DeckHost.Benchmark;

/// <summary>One second of DeckHost: all its processes (the app and the plugin hosts it started) together.</summary>
public sealed record Sample(double CpuPercent, double MemoryMb, int Threads, int Processes);

/// <summary>
/// Samples every DeckHost process started since <paramref name="since"/> (the app and its plugin
/// hosts run the same program): CPU from the processor time used between samples, as % of one core,
/// and the working set.
/// </summary>
internal sealed class ProcessSampler(string processName, DateTime since)
{
    private readonly Dictionary<int, TimeSpan> _cpu = [];
    private DateTime _last = DateTime.UtcNow;

    /// <summary>Starts counting CPU from now (the time before isn't part of the next sample).</summary>
    public void Reset()
    {
        _cpu.Clear();
        foreach (var process in Processes())
        {
            using (process)
            {
                _cpu[process.Id] = process.TotalProcessorTime;
            }
        }

        _last = DateTime.UtcNow;
    }

    public Sample Take()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _last).TotalSeconds;
        _last = now;
        double cpu = 0, memory = 0;
        int threads = 0, count = 0;
        foreach (var process in Processes())
        {
            using (process)
            {
                try
                {
                    var total = process.TotalProcessorTime;
                    cpu += (total - _cpu.GetValueOrDefault(process.Id, total)).TotalSeconds;
                    _cpu[process.Id] = total;
                    memory += process.WorkingSet64 / 1024.0 / 1024.0;
                    threads += process.Threads.Count;
                    count++;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // It ended between listing and reading.
                }
            }
        }

        return new Sample(elapsed > 0 ? cpu / elapsed * 100 : 0, memory, threads, count);
    }

    /// <summary>Samples once a second for <paramref name="duration"/>.</summary>
    public async Task<IReadOnlyList<Sample>> SampleAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        Reset();
        var samples = new List<Sample>();
        var end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            samples.Add(Take());
        }

        return samples;
    }

    private IEnumerable<Process> Processes()
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            bool ours;
            try
            {
                ours = process.StartTime.ToUniversalTime() >= since.AddSeconds(-1);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                ours = false;
            }

            if (ours)
            {
                yield return process;
            }
            else
            {
                process.Dispose();
            }
        }
    }
}
