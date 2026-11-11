using System.Text.Json.Serialization;

namespace DeckHost.Contracts.Models;

/// <summary>
/// RGB feedback configuration for a control's action response.
/// </summary>
public sealed record RgbFeedback(
    /// <summary>
    /// RGB color when the action is activated (R, G, B 0-255)
    /// </summary>
    [property: JsonPropertyName("activeColor")]
    string ActiveColor = "00FF00", // Green by default

    /// <summary>
    /// RGB color when the action completes or returns to idle (R, G, B 0-255)
    /// </summary>
    [property: JsonPropertyName("idleColor")]
    string IdleColor = "0000FF",   // Blue by default

    /// <summary>
    /// Duration in milliseconds to display active color (0 = instant toggle)
    /// </summary>
    [property: JsonPropertyName("activeDuration")]
    int ActiveDuration = 200,

    /// <summary>
    /// Whether to pulse/flash the LED rather than solid color
    /// </summary>
    [property: JsonPropertyName("pulseOnActive")]
    bool PulseOnActive = false)
{
    /// <summary>
    /// Parse RGB hex string (e.g., "FF0000") to (R, G, B) tuple.
    /// </summary>
    public static (byte R, byte G, byte B) ParseColor(string hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor) || hexColor.Length < 6)
            return (0, 0, 0);

        var hex = hexColor.TrimStart('#');
        if (byte.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return (r, g, b);
        }

        return (0, 0, 0);
    }

    /// <summary>
    /// Get the active color as (R, G, B) tuple.
    /// </summary>
    public (byte R, byte G, byte B) GetActiveRgb() => ParseColor(ActiveColor);

    /// <summary>
    /// Get the idle color as (R, G, B) tuple.
    /// </summary>
    public (byte R, byte G, byte B) GetIdleRgb() => ParseColor(IdleColor);
}
