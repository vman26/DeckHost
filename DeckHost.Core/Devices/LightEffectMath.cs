using DeckHost.Contracts.Models;
using SkiaSharp;

namespace DeckHost.Core.Devices;

/// <summary>
/// The color of an LED under a <see cref="LightStyle"/> at a moment in time. Brightness changes
/// go through a gamma curve: LEDs are linear, eyes are not, so a linear fade looks like it jumps at
/// the dark end and lingers at the bright end.
/// </summary>
public static class LightEffectMath
{
    /// <summary>Breathing never goes fully dark, so the light still reads as on.</summary>
    private const double BreatheFloor = 0.04;

    private const double Gamma = 2.2;

    /// <summary>
    /// The LED's color at <paramref name="clock"/>. <paramref name="phase"/> is the LED's place in
    /// the device's lights (0, 1, 2, ...) and <paramref name="count"/> how many there are, for effects
    /// that run across them.
    /// </summary>
    public static (byte R, byte G, byte B) ColorAt(LightStyle style, TimeSpan clock, int phase = 0, int count = 4)
    {
        ArgumentNullException.ThrowIfNull(style);
        count = Math.Max(1, count);
        var seconds = clock.TotalSeconds;
        var period = Math.Clamp(style.PeriodSeconds, LightStyle.MinPeriodSeconds, LightStyle.MaxPeriodSeconds);
        var spread = style.Spread ?? DefaultSpread(style.Effect);
        var cycle = Fraction((seconds / period) + (phase * spread));
        var color = RgbFeedback.ParseColor(style.Color);

        switch (style.Effect)
        {
            case LightEffects.Off:
                return (0, 0, 0);

            case LightEffects.Breathe:
                return Dim(color, BreatheFloor + ((1 - BreatheFloor) * Perceived(0.5 - (0.5 * Math.Cos(2 * Math.PI * cycle)))));

            case LightEffects.Pulse:
                // Two beats in the first half of the cycle, the second softer, then rest.
                return Dim(color, Perceived(Math.Max(Beat(cycle, 0.08), 0.6 * Beat(cycle, 0.3))));

            case LightEffects.Blink:
                // On for half the cycle, with short smooth edges instead of a hard cut.
                return Dim(color, SmoothStep(0, 0.06, cycle) * (1 - SmoothStep(0.5, 0.56, cycle)));

            case LightEffects.ColorCycle:
            {
                var colors = Palette(style);
                var position = cycle * colors.Count;
                var index = (int)Math.Floor(position) % colors.Count;
                return Mix(colors[index], colors[(index + 1) % colors.Count], SmoothStep(0, 1, position - Math.Floor(position)));
            }

            case LightEffects.Rainbow:
            {
                var rgb = SKColor.FromHsv((float)(cycle * 360d), 100, 100);
                return (rgb.Red, rgb.Green, rgb.Blue);
            }

            case LightEffects.Wave:
                return Dim(color, Perceived(Math.Pow(0.5 + (0.5 * Math.Cos(2 * Math.PI * cycle)), 2)));

            case LightEffects.Chase:
            {
                // The head runs over the LEDs once per cycle; behind it the light fades out.
                var head = Fraction(seconds / period) * count;
                var behind = Fraction((head - phase) / count) * count;
                var level = behind < 1 ? 1 - (0.25 * behind) : Math.Max(0, 0.75 - (0.45 * (behind - 1)));
                return Dim(color, Perceived(level));
            }

            case LightEffects.Candle:
            {
                // Smooth noise at a few speeds, so it flickers without blinking.
                var t = seconds * 6 / period;
                var flicker = (0.55 * Noise(t, phase)) + (0.3 * Noise(t * 2.7, phase + 17)) + (0.15 * Noise(t * 7.1, phase + 31));
                var level = 0.45 + (0.55 * flicker);
                var warm = Mix(color, (255, 120, 20), 0.25 * (1 - flicker));
                return Dim(warm, Perceived(level));
            }

            case LightEffects.Sparkle:
            {
                // Each LED twinkles once per cycle, at a random moment, then fades.
                var t = (seconds / period) + Hash(phase, 7);
                var since = Fraction(t) - (Hash((int)Math.Floor(t), phase) * 0.7);
                var level = since < 0 ? 0 : since < 0.04 ? since / 0.04 : Math.Max(0, 1 - ((since - 0.04) / 0.3));
                return Dim(color, 0.05 + (0.95 * Perceived(level)));
            }

            case LightEffects.Custom:
                return Custom(style, seconds, phase, spread);

            default:
                return color;
        }
    }

    /// <summary>The default distance between neighboring LEDs, as a part of the cycle.</summary>
    public static double DefaultSpread(string effect) => effect switch
    {
        LightEffects.Rainbow or LightEffects.ColorCycle => 0.25,
        LightEffects.Wave => 0.18,
        _ => 0,
    };

    /// <summary>The length of one round of a custom effect (its steps' fades and holds).</summary>
    public static double CustomLength(LightStyle style) =>
        (style.Steps ?? []).Sum(static step => Math.Clamp(step.FadeSeconds, 0, LightStep.MaxSeconds) + Math.Clamp(step.HoldSeconds, 0, LightStep.MaxSeconds));

    private static (byte, byte, byte) Custom(LightStyle style, double seconds, int phase, double spread)
    {
        var steps = style.Steps ?? [];
        if (steps.Count == 0)
        {
            return RgbFeedback.ParseColor(style.Color);
        }

        var length = CustomLength(style);
        if (length <= 0)
        {
            return RgbFeedback.ParseColor(steps[0].Color);
        }

        var time = Fraction((seconds / length) + (phase * spread)) * length;
        var previous = RgbFeedback.ParseColor(steps[^1].Color);
        foreach (var step in steps)
        {
            var fade = Math.Clamp(step.FadeSeconds, 0, LightStep.MaxSeconds);
            var hold = Math.Clamp(step.HoldSeconds, 0, LightStep.MaxSeconds);
            var target = RgbFeedback.ParseColor(step.Color);
            if (time < fade)
            {
                return Mix(previous, target, SmoothStep(0, 1, time / fade));
            }

            time -= fade;
            if (time < hold)
            {
                return target;
            }

            time -= hold;
            previous = target;
        }

        return previous;
    }

    private static IReadOnlyList<(byte, byte, byte)> Palette(LightStyle style)
    {
        var colors = (style.Colors ?? [])
            .Where(static hex => !string.IsNullOrWhiteSpace(hex))
            .Select(RgbFeedback.ParseColor)
            .ToList();
        if (colors.Count >= 2)
        {
            return colors;
        }

        // One color: fade between it and its opposite hue.
        var first = colors.Count == 1 ? colors[0] : RgbFeedback.ParseColor(style.Color);
        new SKColor(first.Item1, first.Item2, first.Item3).ToHsv(out var hue, out var saturation, out var value);
        var opposite = SKColor.FromHsv((hue + 180) % 360, saturation, value);
        return [first, (opposite.Red, opposite.Green, opposite.Blue)];
    }

    /// <summary>A smooth bump around <paramref name="center"/> (one heartbeat).</summary>
    private static double Beat(double cycle, double center)
    {
        var distance = Math.Abs(cycle - center) / 0.09;
        return distance >= 1 ? 0 : 0.5 + (0.5 * Math.Cos(Math.PI * distance));
    }

    /// <summary>Brightness as the eye sees it → LED drive level.</summary>
    private static double Perceived(double level) => Math.Pow(Math.Clamp(level, 0, 1), Gamma);

    private static double SmoothStep(double from, double to, double value)
    {
        var t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - (2 * t));
    }

    private static double Fraction(double value) => value - Math.Floor(value);

    /// <summary>Smooth value noise in 0..1.</summary>
    private static double Noise(double t, int seed)
    {
        var i = (int)Math.Floor(t);
        var f = SmoothStep(0, 1, t - i);
        return (Hash(i, seed) * (1 - f)) + (Hash(i + 1, seed) * f);
    }

    /// <summary>A repeatable pseudo-random number in 0..1.</summary>
    private static double Hash(int value, int seed)
    {
        unchecked
        {
            var h = (uint)((value * 374761393) + (seed * 668265263));
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    private static (byte, byte, byte) Dim((byte R, byte G, byte B) color, double level) =>
        (Scale(color.R, level), Scale(color.G, level), Scale(color.B, level));

    private static (byte, byte, byte) Mix((byte R, byte G, byte B) from, (byte R, byte G, byte B) to, double amount) =>
        (Lerp(from.R, to.R, amount), Lerp(from.G, to.G, amount), Lerp(from.B, to.B, amount));

    private static byte Lerp(byte from, byte to, double amount) => (byte)Math.Round(from + ((to - from) * Math.Clamp(amount, 0, 1)));

    private static byte Scale(byte value, double level) => (byte)Math.Round(value * Math.Clamp(level, 0, 1));
}
