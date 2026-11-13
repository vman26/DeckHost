using System.Text.Json.Serialization;

namespace DeckHost.Contracts.Models;

/// <summary>The effects a control's LED can show while idle.</summary>
public static class LightEffects
{
    /// <summary>One steady color.</summary>
    public const string Solid = "solid";

    /// <summary>The color fading in and out.</summary>
    public const string Breathe = "breathe";

    /// <summary>Two quick beats, then a rest, like a heartbeat.</summary>
    public const string Pulse = "pulse";

    /// <summary>On and off (with soft edges).</summary>
    public const string Blink = "blink";

    /// <summary>Fading smoothly from one color to the next (<see cref="LightStyle.Colors"/>).</summary>
    public const string ColorCycle = "cycle";

    /// <summary>Cycling through all colors, each LED a little ahead of the previous one.</summary>
    public const string Rainbow = "rainbow";

    /// <summary>A wave of brightness running across the LEDs.</summary>
    public const string Wave = "wave";

    /// <summary>A light with a fading tail running from LED to LED.</summary>
    public const string Chase = "chase";

    /// <summary>A flickering flame.</summary>
    public const string Candle = "candle";

    /// <summary>Random twinkles that flash and fade out.</summary>
    public const string Sparkle = "sparkle";

    /// <summary>The user's own sequence of colors (<see cref="LightStyle.Steps"/>).</summary>
    public const string Custom = "custom";

    /// <summary>Dark.</summary>
    public const string Off = "off";

    /// <summary>All effects, in the order the editors list them.</summary>
    public static IReadOnlyList<string> All { get; } = [Off, Solid, Breathe, Pulse, Blink, ColorCycle, Rainbow, Wave, Chase, Candle, Sparkle, Custom];

    public static bool IsAnimated(string? effect) => effect is not (null or Solid or Off) && All.Contains(effect);

    /// <summary>Effects drawn in the style's own color (the others pick their colors themselves or use a list).</summary>
    public static bool UsesColor(string? effect) => effect is Solid or Breathe or Pulse or Blink or Wave or Chase or Candle or Sparkle;

    /// <summary>Effects that use a list of colors (<see cref="LightStyle.Colors"/>).</summary>
    public static bool UsesColors(string? effect) => effect is ColorCycle;

    /// <summary>Effects where the LEDs differ from each other, so the spread between them matters.</summary>
    public static bool UsesSpread(string? effect) => effect is ColorCycle or Rainbow or Wave or Breathe or Pulse or Blink or Custom;
}

/// <summary>
/// One step of a custom light effect (<see cref="LightEffects.Custom"/>): fade to
/// <paramref name="Color"/> in <paramref name="FadeSeconds"/>, then keep it for <paramref name="HoldSeconds"/>.
/// </summary>
public sealed record LightStep(
    [property: JsonPropertyName("color")] string Color,
    [property: JsonPropertyName("fadeSeconds")] double FadeSeconds = 0.5,
    [property: JsonPropertyName("holdSeconds")] double HoldSeconds = 0.5)
{
    public const double MaxSeconds = 30;
}

/// <summary>
/// How an LED looks while idle: a color, an effect (<see cref="LightEffects"/>) and how long one
/// cycle of the effect takes. Used as the default for all of a device's lights; a control can
/// override it (<see cref="RgbFeedback.IdleEffect"/>, <see cref="RgbFeedback.IdleLight"/>).
/// </summary>
public sealed record LightStyle(
    [property: JsonPropertyName("color")] string Color = "0000FF",
    [property: JsonPropertyName("effect")] string Effect = LightEffects.Solid,
    [property: JsonPropertyName("periodSeconds")] double PeriodSeconds = LightStyle.DefaultPeriodSeconds)
{
    public const double DefaultPeriodSeconds = 3;

    public const double MinPeriodSeconds = 0.2;

    public const double MaxPeriodSeconds = 20;

    /// <summary>The colors of <see cref="LightEffects.ColorCycle"/> (#RRGGBB); empty: the style's color and its opposite.</summary>
    [JsonPropertyName("colors")]
    public IReadOnlyList<string>? Colors { get; init; }

    /// <summary>The steps of <see cref="LightEffects.Custom"/>, played in order and then from the start.</summary>
    [JsonPropertyName("steps")]
    public IReadOnlyList<LightStep>? Steps { get; init; }

    /// <summary>
    /// How far each LED runs ahead of the previous one, as a part of the cycle (0: all the same,
    /// 0.25: a quarter). Null for the effect's own default.
    /// </summary>
    [JsonPropertyName("spread")]
    public double? Spread { get; init; }

    // Records compare lists by reference; compare their contents, so an equal style is equal.
    public bool Equals(LightStyle? other) =>
        other is not null &&
        string.Equals(Color, other.Color, StringComparison.OrdinalIgnoreCase) &&
        Effect == other.Effect &&
        PeriodSeconds.Equals(other.PeriodSeconds) &&
        Spread.Equals(other.Spread) &&
        (Colors ?? []).SequenceEqual(other.Colors ?? [], StringComparer.OrdinalIgnoreCase) &&
        (Steps ?? []).SequenceEqual(other.Steps ?? []);

    public override int GetHashCode() => HashCode.Combine(Color.ToUpperInvariant(), Effect, PeriodSeconds, Spread, Colors?.Count ?? 0, Steps?.Count ?? 0);
}

/// <summary>One LED's color, for <see cref="Interfaces.IDeviceLightBatchOutput"/>.</summary>
public sealed record LightColor(string ControlId, byte Red, byte Green, byte Blue);
