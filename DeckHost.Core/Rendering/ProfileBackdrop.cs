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

        // Only what stays the same: layers that follow a variable would change the device's backdrop on every
        // value, so they show on the controls only. A gradient counts as the color half way along it.
        var steady = profileBackground is null
            ? null
            : profileBackground with { Layers = profileBackground.Layers?.Where(static layer => layer.Binding is null).ToList() };
        foreach (var layer in BackgroundLayers.Resolve(steady, null))
        {
            SKColor top;
            switch (layer.Kind)
            {
                case BackgroundLayerKinds.Solid:
                    top = layer.Color;
                    break;
                case BackgroundLayerKinds.Gradient:
                    top = new SKColor(
                        (byte)((layer.Color.Red + layer.ToColor.Red) / 2), (byte)((layer.Color.Green + layer.ToColor.Green) / 2),
                        (byte)((layer.Color.Blue + layer.ToColor.Blue) / 2), (byte)((layer.Color.Alpha + layer.ToColor.Alpha) / 2));
                    break;
                default:
                    continue;
            }

            var alpha = top.Alpha / 255f * (float)layer.Opacity;
            color = new SKColor(Blend(top.Red, color.Red, alpha), Blend(top.Green, color.Green, alpha), Blend(top.Blue, color.Blue, alpha));
        }

        return (color.Red, color.Green, color.Blue);

        static byte Blend(byte top, byte bottom, float alpha) => (byte)MathF.Round((top * alpha) + (bottom * (1f - alpha)));
    }
}
