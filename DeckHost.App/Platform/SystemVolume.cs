using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DeckHost.App.Platform;

/// <summary>
/// Reads the level and mute state of the default playback device: Windows Core Audio
/// (IAudioEndpointVolume), on Linux wpctl / pactl / amixer, on macOS osascript. Read-only: the
/// volume itself is changed with the media keys (<see cref="SystemControl"/>), so the system's
/// volume overlay and every app keep working as usual.
/// <para>
/// Windows answers in microseconds, so it is read on every call. Elsewhere reading starts a
/// process, which mustn't happen while the host renders: <see cref="Read"/> returns the last
/// reading and refreshes it in the background when it's older than <see cref="MaxAge"/>.
/// </para>
/// </summary>
internal static partial class SystemVolume
{
    private const int DataFlowRender = 0;
    private const int RoleMultimedia = 1;
    private const int ClsCtxAll = 0x17;
    private static readonly TimeSpan MaxAge = TimeSpan.FromMilliseconds(500);

    private static readonly object CacheLock = new();
    private static (double Level, bool Muted)? s_cached;
    private static long s_cachedAt = long.MinValue / 2;
    private static int s_refreshing;

    /// <summary>
    /// The master volume (0..1) and whether it is muted. Null when it can't be read (yet): no
    /// playback device, no audio tool, or the audio service refuses.
    /// </summary>
    public static (double Level, bool Muted)? Read()
    {
        if (OperatingSystem.IsWindows())
        {
            return ReadWindows();
        }

        lock (CacheLock)
        {
            if (Environment.TickCount64 - s_cachedAt > MaxAge.TotalMilliseconds && Interlocked.Exchange(ref s_refreshing, 1) == 0)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        Refresh();
                    }
                    finally
                    {
                        Volatile.Write(ref s_refreshing, 0);
                    }
                });
            }

            return s_cached;
        }
    }

    /// <summary>Reads the volume now (blocking; not on the host worker). Used right after changing it.</summary>
    public static (double Level, bool Muted)? Refresh()
    {
        var reading = OperatingSystem.IsWindows() ? ReadWindows()
            : OperatingSystem.IsMacOS() ? ReadMac()
            : OperatingSystem.IsLinux() ? ReadLinux()
            : null;
        lock (CacheLock)
        {
            s_cached = reading;
            s_cachedAt = Environment.TickCount64;
        }

        return reading;
    }

    /// <summary>"Volume: 0.40 [MUTED]" (wpctl).</summary>
    internal static (double Level, bool Muted)? ParseWpctl(string? output)
    {
        var match = output is null ? null : Regex.Match(output, @"Volume:\s*([0-9.]+)");
        return match is { Success: true } && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var level)
            ? (Math.Clamp(level, 0, 1), output!.Contains("[MUTED]", StringComparison.Ordinal))
            : null;
    }

    /// <summary>The first "NN%" of pactl get-sink-volume or amixer, and pactl's "Mute: yes" or amixer's "[off]".</summary>
    internal static (double Level, bool Muted)? ParsePercent(string? volumeOutput, string? muteOutput)
    {
        var match = volumeOutput is null ? null : Regex.Match(volumeOutput, @"(\d+)%");
        if (match is not { Success: true })
        {
            return null;
        }

        var muted = (muteOutput ?? volumeOutput!).Contains("Mute: yes", StringComparison.OrdinalIgnoreCase) ||
                    (muteOutput ?? volumeOutput!).Contains("[off]", StringComparison.Ordinal);
        return (Math.Clamp(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) / 100d, 0, 1), muted);
    }

    private static (double Level, bool Muted)? ReadLinux() => ExternalTool.FirstAvailable("wpctl", "pactl", "amixer") switch
    {
        "wpctl" => ParseWpctl(ExternalTool.TryRun("wpctl", ["get-volume", "@DEFAULT_AUDIO_SINK@"])),
        "pactl" => ParsePercent(
            ExternalTool.TryRun("pactl", ["get-sink-volume", "@DEFAULT_SINK@"]),
            ExternalTool.TryRun("pactl", ["get-sink-mute", "@DEFAULT_SINK@"])),
        "amixer" => ParsePercent(ExternalTool.TryRun("amixer", ["sget", "Master"]), null),
        _ => null,
    };

    /// <summary>osascript prints "42, false" for the output volume and whether it is muted.</summary>
    private static (double Level, bool Muted)? ReadMac()
    {
        var output = ExternalTool.TryRun("osascript", ["-e", "set s to get volume settings", "-e", "return (output volume of s as text) & \",\" & (output muted of s as text)"]);
        var parts = output?.Trim().Split(',', StringSplitOptions.TrimEntries);
        return parts is { Length: 2 } && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)
            ? (Math.Clamp(percent / 100d, 0, 1), string.Equals(parts[1], "true", StringComparison.OrdinalIgnoreCase))
            : null;
    }

    [SupportedOSPlatform("windows")]
    private static (double Level, bool Muted)? ReadWindows()
    {
        object? enumerator = null;
        IMMDevice? device = null;
        object? endpoint = null;
        try
        {
            // Looked up on every read, so a change of the default device (headset plugged in) is followed.
            enumerator = new MMDeviceEnumerator();
            if (((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(DataFlowRender, RoleMultimedia, out device) < 0 || device is null)
            {
                return null;
            }

            var endpointVolumeId = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref endpointVolumeId, ClsCtxAll, IntPtr.Zero, out endpoint) < 0 || endpoint is not IAudioEndpointVolume volume)
            {
                return null;
            }

            if (volume.GetMasterVolumeLevelScalar(out var level) < 0 || volume.GetMute(out var muted) < 0)
            {
                return null;
            }

            return (Math.Clamp(level, 0f, 1f), muted);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            Release(endpoint);
            Release(device);
            Release(enumerator);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    // Only the methods used are named; the others keep their place in the vtable.

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumerator;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, int classContext, IntPtr activationParameters, [MarshalAs(UnmanagedType.IUnknown)] out object? instance);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int GetChannelCount(out uint channelCount);

        [PreserveSig]
        int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

        [PreserveSig]
        int GetMasterVolumeLevel(out float levelDb);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float levelDb);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
