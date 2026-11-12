using DeckHost.Contracts.Models;
using SkiaSharp;

namespace DeckHost.Core.Rendering;

/// <summary>
/// The color of a device's screen behind and between its controls (see
/// <see cref="Contracts.Interfaces.IDeviceBackdropOutput"/>). The editor's canvas uses the same
/// color, so it shows what the device shows.
/// </summary>
public static class ProfileBackdrop
{
    /// <summary>
    /// The profile's background color over the base color every control starts with, exactly as
    /// the controls themselves draw it. A background image isn't part of it: the image is drawn
    /// per control.
    /// </summary>
    public static (byte Red, byte Green, byte Blue) GetColor(ProfileBackground? profileBackground)
    {
        var color = new SKColor(ControlRenderer.BackgroundColor);
        if (!string.IsNullOrWhiteSpace(profileBackground?.Color) && SKColor.TryParse(profileBackground.Color.Trim(), out var parsed))
        {
            var alpha = parsed.Alpha / 255f;
            color = new SKColor(Blend(parsed.Red, color.Red, alpha), Blend(parsed.Green, color.Green, alpha), Blend(parsed.Blue, color.Blue, alpha));
        }

        return (color.Red, color.Green, color.Blue);

        static byte Blend(byte top, byte bottom, float alpha) => (byte)MathF.Round((top * alpha) + (bottom * (1f - alpha)));
    }
}
