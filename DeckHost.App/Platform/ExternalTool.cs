using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DeckHost.App.Platform;

/// <summary>
/// Runs command-line tools that Linux and macOS provide for what Windows offers as an API
/// (playerctl, wpctl, xdotool, osascript, ...). Arguments are passed as a list, never through a
/// shell, so text from settings can't turn into commands.
/// </summary>
internal static class ExternalTool
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly ConcurrentDictionary<string, bool> Available = new(StringComparer.Ordinal);

    /// <summary>Whether a program with this name is on the PATH (cached).</summary>
    public static bool Exists(string name) => Available.GetOrAdd(name, static tool =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, tool))));

    /// <summary>The first of the tools that is installed, or null.</summary>
    public static string? FirstAvailable(params string[] names) => names.FirstOrDefault(Exists);

    /// <summary>Runs a tool and returns its standard output. Throws with the tool's own message when it fails.</summary>
    public static string Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{fileName} did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout ?? DefaultTimeout))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException($"{fileName} did not finish in time.");
        }

        if (process.ExitCode != 0)
        {
            var message = error.Result.Trim();
            throw new InvalidOperationException(message.Length > 0 ? $"{fileName}: {message}" : $"{fileName} failed (exit code {process.ExitCode}).");
        }

        return output.Result;
    }

    /// <summary>Like <see cref="Run"/>, but returns null instead of throwing (for readings).</summary>
    public static string? TryRun(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        try
        {
            return Run(fileName, arguments, timeout);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }
}
