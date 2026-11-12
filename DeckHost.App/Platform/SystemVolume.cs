using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DeckHost.App.Platform;

/// <summary>
/// Reads the level and mute state of the default playback device through Windows Core Audio
/// (IAudioEndpointVolume). Read-only: the volume itself is changed with the media keys, so the
/// Windows volume overlay and every app keep working as usual.
/// </summary>
internal static class SystemVolume
{
    private const int DataFlowRender = 0;
    private const int RoleMultimedia = 1;
    private const int ClsCtxAll = 0x17;

    /// <summary>
    /// The master volume (0..1) and whether it is muted. Null when it can't be read: not on
    /// Windows, no playback device, or the audio service refuses.
    /// </summary>
    public static (double Level, bool Muted)? Read() => OperatingSystem.IsWindows() ? ReadWindows() : null;

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
