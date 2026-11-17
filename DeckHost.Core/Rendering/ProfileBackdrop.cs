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
                case BackgroundLayerKinds.Gradient or BackgroundLayerKinds.Radial:
                    top = ColorHalfWayAlong(layer);
                    break;
                default:
                    continue;
            }

            var alpha = top.Alpha / 255f * (float)layer.Opacity;
            color = new SKColor(Blend(top.Red, color.Red, alpha), Blend(top.Green, color.Green, alpha), Blend(top.Blue, color.Blue, alpha));
        }

        return (color.Red, color.Green, color.Blue);

        static SKColor ColorHalfWayAlong(ResolvedLayer layer)
        {
            var stops = layer.Stops ?? new GradientColors([layer.Color, layer.ToColor], [0f, 1f]);
            var after = Array.FindIndex(stops.Positions, static position => position >= 0.5f);
            if (after <= 0)
            {
                return stops.Colors[Math.Max(after, 0)];
            }

            var (from, to) = (stops.Colors[after - 1], stops.Colors[after]);
            var span = stops.Positions[after] - stops.Positions[after - 1];
            var t = span <= 0 ? 1f : (0.5f - stops.Positions[after - 1]) / span;
            return new SKColor(
                (byte)MathF.Round(from.Red + ((to.Red - from.Red) * t)), (byte)MathF.Round(from.Green + ((to.Green - from.Green) * t)),
                (byte)MathF.Round(from.Blue + ((to.Blue - from.Blue) * t)), (byte)MathF.Round(from.Alpha + ((to.Alpha - from.Alpha) * t)));
        }

        static byte Blend(byte top, byte bottom, float alpha) => (byte)MathF.Round((top * alpha) + (bottom * (1f - alpha)));
    }
}
