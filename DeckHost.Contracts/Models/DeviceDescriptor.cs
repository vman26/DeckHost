namespace DeckHost.Contracts.Models;

/// <summary>Who made a device and which one it is (master plan 7.1.1); every field is optional.</summary>
/// <param name="Manufacturer">"MiraBox", "Elgato", …</param>
/// <param name="Model">"StreamDock N4 Pro", "Stream Deck +", …</param>
/// <param name="SerialNumber">The device's serial number, when it reports one.</param>
/// <param name="HardwareRevision">A hardware revision, when the device reports one.</param>
/// <param name="FirmwareVersion">The firmware version, when the device reports one.</param>
/// <param name="Connection">How it's connected: "USB", "Network", "Virtual", …</param>
public sealed record DeviceIdentity(
    string? Manufacturer = null,
    string? Model = null,
    string? SerialNumber = null,
    string? HardwareRevision = null,
    string? FirmwareVersion = null,
    string? Connection = null);

/// <summary>
/// A display or a region of one (master plan 7.1.3): its pixel size and the controls whose images
/// are shown on it. Controls point back with <see cref="PhysicalControl.DisplayId"/>.
/// </summary>
/// <param name="Id">Stable id within the device: "main", "strip", "keys".</param>
/// <param name="Width">Width in pixels, as the device draws it (before <see cref="RotationDegrees"/>).</param>
/// <param name="Height">Height in pixels.</param>
public sealed record DeviceDisplay(string Id, int Width, int Height)
{
    /// <summary>How the device turns what it gets (0, 90, 180, 270); device plugins apply it.</summary>
    public int RotationDegrees { get; init; }

    /// <summary>The most frames a second the display takes; null when it doesn't say.</summary>
    public int? MaxFramesPerSecond { get; init; }

    /// <summary>The controls on this display, in reading order.</summary>
    public IReadOnlyList<string> ControlIds { get; init; } = [];
}

/// <summary>What a device can do (master plan 7.1.4). Query before using a feature.</summary>
[Flags]
public enum DeviceCapabilities
{
    None = 0,

    /// <summary>Shows pictures on its controls.</summary>
    Images = 1 << 0,

    /// <summary>Plays animated pictures by itself (else the host sends frames).</summary>
    AnimatedImages = 1 << 1,

    /// <summary>Reports touches on a surface.</summary>
    Touch = 1 << 2,

    /// <summary>Reports several touches at once.</summary>
    MultiTouch = 1 << 3,

    /// <summary>Its screen brightness can be set.</summary>
    Brightness = 1 << 4,

    /// <summary>Has RGB lights the host can set.</summary>
    RgbLights = 1 << 5,

    /// <summary>Can vibrate.</summary>
    Haptics = 1 << 6,

    /// <summary>Shows a background behind all controls (<see cref="Interfaces.IDeviceBackdropOutput"/>).</summary>
    Backdrop = 1 << 7,

    /// <summary>Reports its firmware (<see cref="Interfaces.IDeviceFirmware"/>).</summary>
    Firmware = 1 << 8,

    /// <summary>Has its own settings page (<see cref="Interfaces.IConfigurableDevicePlugin"/>).</summary>
    Settings = 1 << 9,
}

/// <summary>What a device can't do, or not fully (master plan 7.1.1 "limitations"); null fields: no limit known.</summary>
public sealed record DeviceLimits
{
    /// <summary>The most image updates a second the device keeps up with, for all its controls.</summary>
    public int? MaxFramesPerSecond { get; init; }

    /// <summary>False when transparent pixels can't be shown (the host flattens them).</summary>
    public bool? Transparency { get; init; }

    /// <summary>Bits per pixel of its screens (16 for RGB565), when fewer than 24.</summary>
    public int? ColorDepthBits { get; init; }

    /// <summary>Anything else worth knowing, in words: "no knob release events", …</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>A size in millimetres (physical dimensions, master plan 7.1.3).</summary>
public sealed record PhysicalSize(double WidthMillimetres, double HeightMillimetres);

/// <summary>The outline of a control, for drawing it like the hardware (master plan 7.1.2).</summary>
public enum ControlShape
{
    Rectangle,
    Round,
}

/// <summary>
/// Which face of the device a control is on (master plan 7.1.3). Most are on the front; a wheel
/// on the side of the device is drawn on that edge, not as part of the front.
/// </summary>
public enum DeviceSide
{
    Front,
    Left,
    Right,
    Top,
    Bottom,
    Back,
}
