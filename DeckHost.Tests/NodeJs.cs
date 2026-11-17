using System.ComponentModel;
using System.Diagnostics;

namespace DeckHost.Tests;

/// <summary>
/// Whether Node.js can run here, for the tests that start a fake Stream Deck plugin. The probe runs once
/// per test run: classes run in parallel, and a cold start of Node on a busy CI machine can take several seconds.
/// </summary>
internal static class NodeJs
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    private static readonly Lazy<bool> Probe = new(CheckNode);

    public static bool IsAvailable => Probe.Value;

    private static bool CheckNode()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return false;
            }

            _ = process.StandardOutput.ReadToEndAsync();
            if (process.WaitForExit(ProbeTimeout))
            {
                return process.ExitCode == 0;
            }

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It ended between the timeout and the kill.
            }

            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
