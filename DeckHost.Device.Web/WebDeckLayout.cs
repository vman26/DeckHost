using System.Globalization;
using System.Text.Json;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;

namespace DeckHost.Device.Web;

/// <summary>The web deck's grid of keys and the messages between DeckHost and the page, as pure functions.</summary>
public static class WebDeckLayout
{
    public const int MinSize = 1;
    public const int MaxColumns = 8;
    public const int MaxRows = 6;

    /// <summary>Key images are drawn at this size; the page scales them to the screen.</summary>
    public const int KeyPixels = 144;

    public static IReadOnlyList<PhysicalControl> Controls(int columns, int rows)
    {
        columns = Math.Clamp(columns, MinSize, MaxColumns);
        rows = Math.Clamp(rows, MinSize, MaxRows);
        const int Key = 72;
        const int Gap = 14;
        var controls = new List<PhysicalControl>();
        for (var index = 0; index < columns * rows; index++)
        {
            controls.Add(new PhysicalControl(
                $"key.{index}",
                $"Key {index + 1}",
                ControlType.Key,
                new Rect(index % columns * (Key + Gap), index / columns * (Key + Gap), Key, Key),
                InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress,
                OutputCapabilities.ColorImage | OutputCapabilities.Text,
                KeyPixels,
                KeyPixels,
                HardwareId: index.ToString(CultureInfo.InvariantCulture)));
        }

        return controls;
    }

    /// <summary>The first message to a page: the grid.</summary>
    public static string LayoutMessage(string name, int columns, int rows) =>
        JsonSerializer.Serialize(new { type = "layout", name, columns, rows });

    /// <summary>A key's image (PNG, base64).</summary>
    public static string ImageMessage(string controlId, byte[] png) =>
        JsonSerializer.Serialize(new { type = "image", key = controlId, png = Convert.ToBase64String(png) });

    /// <summary>
    /// A message from the page: <c>{"type":"down"|"up","key":"key.3"}</c>. Returns false for
    /// anything else, including keys outside the grid.
    /// </summary>
    public static bool TryParseInput(string json, int keyCount, out string controlId, out InputCapabilities input)
    {
        controlId = string.Empty;
        input = InputCapabilities.None;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var id = key.GetString()!;
            if (!id.StartsWith("key.", StringComparison.Ordinal) ||
                !int.TryParse(id.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                index >= keyCount)
            {
                return false;
            }

            input = type.GetString() switch
            {
                "down" => InputCapabilities.Press,
                "up" => InputCapabilities.Release,
                _ => InputCapabilities.None,
            };
            controlId = id;
            return input != InputCapabilities.None;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
