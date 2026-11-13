using System.Text.Json.Serialization;

namespace DeckHost.Contracts.Models;

/// <summary>The effects a control's LED can show while idle.</summary>
public static class LightEffects
{
    /// <summary>One steady color.</summary>
    public const string Solid = "solid";

    /// <summary>The color fading in and out.</summary>
    public const string Breathe = "breathe";

    /// <summary>Cycling through all colors, each LED a little ahead of the previous one.</summary>
    public const string Rainbow = "rainbow";

    /// <summary>Dark.</summary>
    public const string Off = "off";

    public static bool IsAnimated(string? effect) => effect is Breathe or Rainbow;
}

/// <summary>
/// How an LED looks while idle: a color, an effect (<see cref="LightEffects"/>) and how long one
/// cycle of the effect takes. Used as the default for all of a device's lights; a control can
/// override it (<see cref="RgbFeedback.IdleEffect"/>).
/// </summary>
public sealed record LightStyle(
    [property: JsonPropertyName("color")] string Color = "0000FF",
    [property: JsonPropertyName("effect")] string Effect = LightEffects.Solid,
    [property: JsonPropertyName("periodSeconds")] double PeriodSeconds = LightStyle.DefaultPeriodSeconds)
{
    public const double DefaultPeriodSeconds = 3;

    public const double MinPeriodSeconds = 0.5;

    public const double MaxPeriodSeconds = 20;
}

/// <summary>One LED's color, for <see cref="Interfaces.IDeviceLightBatchOutput"/>.</summary>
public sealed record LightColor(string ControlId, byte Red, byte Green, byte Blue);
