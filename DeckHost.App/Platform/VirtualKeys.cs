namespace DeckHost.App.Platform;

/// <summary>
/// Windows virtual-key codes of the media keys. Plain numbers, kept apart from
/// <see cref="WindowsInput"/> so the action list can name them on any platform; only sending
/// them is Windows-only.
/// </summary>
internal static class VirtualKeys
{
    public const ushort MediaNextTrack = 0xB0;
    public const ushort MediaPrevTrack = 0xB1;
    public const ushort MediaStop = 0xB2;
    public const ushort MediaPlayPause = 0xB3;
    public const ushort VolumeMute = 0xAD;
    public const ushort VolumeDown = 0xAE;
    public const ushort VolumeUp = 0xAF;
}
