using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;

namespace DeckHost.Device.Elgato;

/// <summary>
/// A Stream Deck model this plugin drives: its USB product id, key grid and image size, how key
/// images are oriented, and for the Stream Deck + its dials and touch strip. All of them use the
/// current ("second generation") protocol: JPEG key images in 1024-byte reports.
/// </summary>
public sealed record StreamDeckModel(
    string Name,
    int ProductId,
    int Columns,
    int Rows,
    int KeyImageSize,
    bool FlipKeyImages,
    int Dials = 0,
    int TouchStripWidth = 0,
    int TouchStripHeight = 0,
    bool HasDisplays = true)
{
    public const int VendorId = 0x0fd9;

    public int KeyCount => Columns * Rows;

    /// <summary>The models DeckHost knows, by USB product id.</summary>
    public static IReadOnlyList<StreamDeckModel> All { get; } =
    [
        new("Stream Deck", 0x006d, 5, 3, 72, FlipKeyImages: true),
        new("Stream Deck MK.2", 0x0080, 5, 3, 72, FlipKeyImages: true),
        new("Stream Deck MK.2", 0x00a5, 5, 3, 72, FlipKeyImages: true),
        new("Stream Deck XL", 0x006c, 8, 4, 96, FlipKeyImages: true),
        new("Stream Deck XL", 0x008f, 8, 4, 96, FlipKeyImages: true),
        new("Stream Deck Neo", 0x009a, 4, 2, 96, FlipKeyImages: true),
        new("Stream Deck +", 0x0084, 4, 2, 120, FlipKeyImages: false, Dials: 4, TouchStripWidth: 800, TouchStripHeight: 100),
        new("Stream Deck Pedal", 0x0086, 3, 1, 0, FlipKeyImages: false, HasDisplays: false),
    ];

    /// <summary>Older models with a different protocol (BMP images): recognized, not driven yet.</summary>
    public static IReadOnlyDictionary<int, string> NotYetSupported { get; } = new Dictionary<int, string>
    {
        [0x0060] = "Stream Deck (first edition)",
        [0x0063] = "Stream Deck Mini",
        [0x0090] = "Stream Deck Mini",
    };

    public static StreamDeckModel? Find(int productId) => All.FirstOrDefault(model => model.ProductId == productId);

    /// <summary>
    /// The controls as DeckHost lays them out: keys key.0… in reading order; on the Stream Deck +
    /// the touch strip as four zones (touch.0–3, one above each dial), a swipe over it
    /// (touch.swipe) and the dials (encoder.0–3), each linked to its zone.
    /// </summary>
    public IReadOnlyList<PhysicalControl> Controls()
    {
        const int Key = 72;
        const int Gap = 14;
        var keyInputs = InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress;
        var controls = new List<PhysicalControl>();
        for (var index = 0; index < KeyCount; index++)
        {
            var column = index % Columns;
            var row = index / Columns;
            controls.Add(new PhysicalControl(
                $"key.{index}",
                $"Key {index + 1}",
                ControlType.Key,
                new Rect(column * (Key + Gap), row * (Key + Gap), Key, Key),
                keyInputs,
                HasDisplays ? OutputCapabilities.ColorImage | OutputCapabilities.Text : OutputCapabilities.None,
                HasDisplays ? KeyImageSize : 0,
                HasDisplays ? KeyImageSize : 0,
                HardwareId: index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (Dials == 0)
        {
            return controls;
        }

        var top = Rows * (Key + Gap);
        var width = (Columns * (Key + Gap)) - Gap;
        var zoneWidth = (width - ((Dials - 1) * 6)) / (double)Dials;
        for (var dial = 0; dial < Dials; dial++)
        {
            controls.Add(new PhysicalControl(
                $"touch.{dial}",
                $"Touch {dial + 1}",
                ControlType.TouchSurface,
                new Rect(dial * (zoneWidth + 6), top, zoneWidth, 44),
                InputCapabilities.Press | InputCapabilities.Release,
                OutputCapabilities.ColorImage | OutputCapabilities.Text,
                TouchStripWidth / Dials,
                TouchStripHeight));
        }

        controls.Add(new PhysicalControl("touch.swipe", "Touch Strip Swipe", ControlType.TouchSurface, new Rect(0, top + 48, width, 12), InputCapabilities.Swipe, OutputCapabilities.None));
        for (var dial = 0; dial < Dials; dial++)
        {
            controls.Add(new PhysicalControl(
                $"encoder.{dial}",
                $"Dial {dial + 1}",
                ControlType.Encoder,
                new Rect((dial * (zoneWidth + 6)) + ((zoneWidth - 56) / 2), top + 72, 56, 56),
                InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.Rotate,
                OutputCapabilities.None,
                DisplayControlId: $"touch.{dial}"));
        }

        return controls;
    }
}
