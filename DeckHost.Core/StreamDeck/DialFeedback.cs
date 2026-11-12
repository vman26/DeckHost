using System.Globalization;
using System.Text.Json.Nodes;

namespace DeckHost.Core.StreamDeck;

/// <summary>
/// What a dial action shows on its touch-strip segment on a Stream Deck + (the setFeedback
/// event). DeckHost keeps the items every built-in layout shares: the title, the value and the
/// indicator bar. Updates are partial, so each one is merged into the previous state.
/// </summary>
/// <param name="Indicator">Level of the indicator bar, 0..1 (plugins send 0..100).</param>
internal sealed record DialFeedback(string? Title = null, string? Value = null, double? Indicator = null)
{
    public static DialFeedback Merge(DialFeedback? current, JsonObject? payload)
    {
        current ??= new DialFeedback();
        if (payload is null)
        {
            return current;
        }

        return current with
        {
            Title = payload.ContainsKey("title") ? Text(payload["title"]) : current.Title,
            Value = payload.ContainsKey("value") ? Text(payload["value"]) : current.Value,
            Indicator = payload.ContainsKey("indicator")
                ? Number(payload["indicator"]) is { } percent ? Math.Clamp(percent / 100d, 0d, 1d) : null
                : current.Indicator,
        };
    }

    // An item is its value, or an object with the value and layout details (color, opacity, ...).
    private static JsonValue? ItemValue(JsonNode? node) => (node is JsonObject item ? item["value"] : node) as JsonValue;

    private static string? Text(JsonNode? node) => ItemValue(node)?.ToString();

    private static double? Number(JsonNode? node)
    {
        if (ItemValue(node) is not { } value)
        {
            return null;
        }

        return value.TryGetValue<double>(out var number) || double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }
}
