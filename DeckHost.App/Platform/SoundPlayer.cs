using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

namespace DeckHost.App.Platform;

/// <summary>
/// Plays sound files for the soundboard actions: on Windows through MCI (winmm, which plays WAV,
/// MP3 and WMA without extra software), on macOS with afplay, on Linux with the first of mpv,
/// ffplay, pw-play, paplay and aplay that is installed. Each sound has an id (the control), so a
/// key can stop its own sound; <see cref="StopAll"/> stops every one. Playing never blocks the
/// caller: everything runs on background threads.
/// </summary>
internal static class SoundPlayer
{
    private static readonly ConcurrentDictionary<string, Playback> Playing = new(StringComparer.Ordinal);
    private static int s_aliasCounter;

    /// <summary>The players tried on Linux, best first (mpv and ffplay play MP3; the others WAV, OGG and FLAC).</summary>
    internal static readonly string[] LinuxPlayers = ["mpv", "ffplay", "pw-play", "paplay", "aplay"];

    /// <summary>Raised (on a background thread) when a sound stops by itself or is stopped.</summary>
    public static event Action<string>? Stopped;

    /// <summary>Whether the sound with this id is playing.</summary>
    public static bool IsPlaying(string id) => Playing.ContainsKey(id);

    /// <summary>Plays <paramref name="path"/> at <paramref name="volume"/> percent; a sound already playing under <paramref name="id"/> is stopped first.</summary>
    public static void Play(string id, string path, int volume, string? outputDevice = null)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The sound file doesn't exist.", path);
        }

        Stop(id);
        volume = Math.Clamp(volume, 0, 100);
        Playback playback = OperatingSystem.IsWindows()
            ? new MciPlayback(id, path, volume)
            : new ProcessPlayback(id, StartProcess(path, volume, outputDevice));
        Playing[id] = playback;
        playback.Start();
    }

    /// <summary>Stops the sound with this id, if it plays.</summary>
    public static void Stop(string id)
    {
        if (Playing.TryRemove(id, out var playback))
        {
            playback.Stop();
            Stopped?.Invoke(id);
        }
    }

    /// <summary>Stops every sound.</summary>
    public static void StopAll()
    {
        foreach (var id in Playing.Keys)
        {
            Stop(id);
        }
    }

    private static void Ended(string id, Playback playback)
    {
        if (Playing.TryRemove(new KeyValuePair<string, Playback>(id, playback)))
        {
            Stopped?.Invoke(id);
        }
    }

    /// <summary>The command line that plays a file on Linux or macOS (pure, for the tests).</summary>
    internal static (string FileName, IReadOnlyList<string> Arguments)? PlayerCommand(string player, string path, int volume, string? outputDevice)
    {
        var fraction = (volume / 100d).ToString("0.##", CultureInfo.InvariantCulture);
        return player switch
        {
            "afplay" => ("afplay", ["-v", fraction, path]),
            "mpv" => ("mpv", ["--no-video", "--really-quiet", $"--volume={volume}", .. outputDevice is { Length: > 0 } ? new[] { $"--audio-device={outputDevice}" } : [], path]),
            "ffplay" => ("ffplay", ["-nodisp", "-autoexit", "-loglevel", "quiet", "-volume", volume.ToString(CultureInfo.InvariantCulture), path]),
            "pw-play" => ("pw-play", ["--volume", fraction, .. outputDevice is { Length: > 0 } ? new[] { "--target", outputDevice } : [], path]),
            "paplay" => ("paplay", [$"--volume={(int)Math.Round(volume / 100d * 65536)}", .. outputDevice is { Length: > 0 } ? new[] { $"--device={outputDevice}" } : [], path]),
            "aplay" => ("aplay", ["-q", path]),
            _ => null,
        };
    }

    private static Process StartProcess(string path, int volume, string? outputDevice)
    {
        var player = OperatingSystem.IsMacOS() ? "afplay" : ExternalTool.FirstAvailable(LinuxPlayers)
            ?? throw new PlatformNotSupportedException("Install mpv, ffplay (FFmpeg) or PipeWire's pw-play to play sounds.");
        var (fileName, arguments) = PlayerCommand(player, path, volume, outputDevice)!.Value;
        var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }

    private abstract class Playback(string id)
    {
        public string Id { get; } = id;

        public abstract void Start();

        public abstract void Stop();
    }

    private sealed class ProcessPlayback(string id, Process process) : Playback(id)
    {
        public override void Start()
        {
            process.Exited += (_, _) =>
            {
                Ended(Id, this);
                process.Dispose();
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        public override void Stop()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }
        }
    }

    /// <summary>
    /// One sound through MCI on its own thread: MCI ties a device to the thread that opened it, and
    /// "play … wait" blocks that thread until the end, so stopping is a "stop" sent from another thread.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private sealed class MciPlayback(string id, string path, int volume) : Playback(id)
    {
        private readonly string _alias = "deckhost" + Interlocked.Increment(ref s_aliasCounter).ToString(CultureInfo.InvariantCulture);

        public override void Start()
        {
            var thread = new Thread(() =>
            {
                try
                {
                    if (Send($"open \"{path}\" type mpegvideo alias {_alias}") != 0 && Send($"open \"{path}\" alias {_alias}") != 0)
                    {
                        return;
                    }

                    Send($"setaudio {_alias} volume to {volume * 10}");
                    Send($"play {_alias} wait");
                }
                finally
                {
                    Send($"close {_alias}");
                    Ended(Id, this);
                }
            })
            { IsBackground = true, Name = "DeckHost sound" };
            thread.Start();
        }

        public override void Stop() => Send($"stop {_alias}");

        private static int Send(string command) => mciSendStringW(command, null, 0, IntPtr.Zero);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendStringW(string command, StringBuilder? returnText, int returnLength, IntPtr callback);
    }
}
