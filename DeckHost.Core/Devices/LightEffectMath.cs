using DeckHost.Contracts.Models;
using SkiaSharp;

namespace DeckHost.Core.Devices;

/// <summary>The color an LED shows at a moment, for a <see cref="LightStyle"/>.</summary>
public static class LightEffectMath
{
    /// <summary>Breathing never goes fully dark, so the light still reads as on.</summary>
    private const double BreatheFloor = 0.08;

    /// <summary>
    /// The LED's color at <paramref name="clock"/>. <paramref name="phase"/> (0, 1, 2, ...) puts each
    /// LED of a device a bit further along a rainbow, so the colors run across the knobs.
    /// </summary>
    public static (byte R, byte G, byte B) ColorAt(LightStyle style, TimeSpan clock, int phase = 0)
    {
        ArgumentNullException.ThrowIfNull(style);
        var period = Math.Clamp(style.PeriodSeconds, LightStyle.MinPeriodSeconds, LightStyle.MaxPeriodSeconds);
        var cycle = (clock.TotalSeconds / period) % 1d;
        var (red, green, blue) = RgbFeedback.ParseColor(style.Color);
        switch (style.Effect)
        {
            case LightEffects.Off:
                return (0, 0, 0);
            case LightEffects.Breathe:
                var level = BreatheFloor + ((1 - BreatheFloor) * (0.5 - (0.5 * Math.Cos(2 * Math.PI * cycle))));
                return (Scale(red, level), Scale(green, level), Scale(blue, level));
            case LightEffects.Rainbow:
                var hue = (float)((((cycle + (phase * 0.25)) % 1d) + 1d) % 1d * 360d);
                var color = SKColor.FromHsv(hue, 100, 100);
                return (color.Red, color.Green, color.Blue);
            default:
                return (red, green, blue);
        }

        static byte Scale(byte value, double level) => (byte)Math.Round(value * Math.Clamp(level, 0, 1));
    }
}
