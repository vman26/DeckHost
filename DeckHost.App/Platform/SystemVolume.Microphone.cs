using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace DeckHost.App.Platform;

/// <summary>
/// The default microphone's mute state: Windows Core Audio on the default capture device, Linux
/// wpctl / pactl / amixer (Capture), macOS the input volume (muting sets it to 0 and unmuting
/// restores the level it had). Reading is cached like the playback volume, so the host never
/// waits for a process while it renders.
/// </summary>
internal static partial class SystemVolume
{
    private const int DataFlowCapture = 1;
    private const int RoleConsole = 0;
    private const int MacDefaultInputVolume = 75;

    private static readonly object MicrophoneLock = new();
    private static bool? s_microphoneMuted;
    private static long s_microphoneReadAt = long.MinValue / 2;
    private static int s_microphoneRefreshing;
    private static int s_macInputVolumeBeforeMute = MacDefaultInputVolume;

    /// <summary>Whether the default microphone is muted (the last reading; null when unknown).</summary>
    public static bool? MicrophoneMuted()
    {
        lock (MicrophoneLock)
        {
            if (Environment.TickCount64 - s_microphoneReadAt > MaxAge.TotalMilliseconds * 4 && Interlocked.Exchange(ref s_microphoneRefreshing, 1) == 0)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        RememberMicrophone(ReadMicrophone());
                    }
                    finally
                    {
                        Volatile.Write(ref s_microphoneRefreshing, 0);
                    }
                });
            }

            return s_microphoneMuted;
        }
    }

    /// <summary>Mutes (true), unmutes (false) or toggles (null) the default microphone; returns the new state. Blocking.</summary>
    public static bool SetMicrophoneMuted(bool? mute)
    {
        var target = mute ?? !(ReadMicrophone() ?? false);
        if (OperatingSystem.IsWindows())
        {
            WithCaptureEndpoint(volume =>
            {
                var context = Guid.Empty;
                Marshal.ThrowExceptionForHR(volume.SetMute(target, ref context));
            });
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (target)
            {
                if (ReadMacInputVolume() is > 0 and var level)
                {
                    s_macInputVolumeBeforeMute = level;
                }

                ExternalTool.Run("osascript", ["-e", "set volume input volume 0"]);
            }
            else
            {
                ExternalTool.Run("osascript", ["-e", $"set volume input volume {s_macInputVolumeBeforeMute.ToString(CultureInfo.InvariantCulture)}"]);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            var state = target ? "1" : "0";
            switch (ExternalTool.FirstAvailable("wpctl", "pactl", "amixer"))
            {
                case "wpctl":
                    ExternalTool.Run("wpctl", ["set-mute", "@DEFAULT_AUDIO_SOURCE@", state]);
                    break;
                case "pactl":
                    ExternalTool.Run("pactl", ["set-source-mute", "@DEFAULT_SOURCE@", state]);
                    break;
                case "amixer":
                    ExternalTool.Run("amixer", ["sset", "Capture", target ? "nocap" : "cap"]);
                    break;
                default:
                    throw new PlatformNotSupportedException("Install PipeWire (wpctl), PulseAudio (pactl) or ALSA utilities (amixer) to mute the microphone.");
            }
        }
        else
        {
            throw new PlatformNotSupportedException("Muting the microphone isn't supported on this system.");
        }

        RememberMicrophone(target);
        return target;
    }

    private static void RememberMicrophone(bool? muted)
    {
        lock (MicrophoneLock)
        {
            s_microphoneMuted = muted;
            s_microphoneReadAt = Environment.TickCount64;
        }
    }

    private static bool? ReadMicrophone()
    {
        if (OperatingSystem.IsWindows())
        {
            bool? muted = null;
            try
            {
                WithCaptureEndpoint(volume => muted = volume.GetMute(out var value) >= 0 ? value : null);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                return null;
            }

            return muted;
        }

        if (OperatingSystem.IsMacOS())
        {
            return ReadMacInputVolume() is { } level ? level == 0 : null;
        }

        if (OperatingSystem.IsLinux())
        {
            return ExternalTool.FirstAvailable("wpctl", "pactl", "amixer") switch
            {
                "wpctl" => ParseWpctl(ExternalTool.TryRun("wpctl", ["get-volume", "@DEFAULT_AUDIO_SOURCE@"]))?.Muted,
                "pactl" => ExternalTool.TryRun("pactl", ["get-source-mute", "@DEFAULT_SOURCE@"]) is { } output ? output.Contains("yes", StringComparison.OrdinalIgnoreCase) : null,
                "amixer" => ExternalTool.TryRun("amixer", ["sget", "Capture"]) is { } output ? output.Contains("[off]", StringComparison.Ordinal) : null,
                _ => null,
            };
        }

        return null;
    }

    private static int? ReadMacInputVolume()
    {
        var output = ExternalTool.TryRun("osascript", ["-e", "input volume of (get volume settings)"]);
        return int.TryParse(output?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) ? level : null;
    }

    [SupportedOSPlatform("windows")]
    private static void WithCaptureEndpoint(Action<IAudioEndpointVolume> use)
    {
        object? enumerator = null;
        IMMDevice? device = null;
        object? endpoint = null;
        try
        {
            enumerator = new MMDeviceEnumerator();
            if (((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(DataFlowCapture, RoleConsole, out device) < 0 || device is null)
            {
                throw new InvalidOperationException("There is no microphone.");
            }

            var endpointVolumeId = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref endpointVolumeId, ClsCtxAll, IntPtr.Zero, out endpoint) < 0 || endpoint is not IAudioEndpointVolume volume)
            {
                throw new InvalidOperationException("The microphone can't be muted.");
            }

            use(volume);
        }
        finally
        {
            Release(endpoint);
            Release(device);
            Release(enumerator);
        }
    }
}
