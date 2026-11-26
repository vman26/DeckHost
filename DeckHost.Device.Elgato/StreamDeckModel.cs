using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;

namespace DeckHost.Device.Elgato;

/// <summary>
/// A Stream Deck model this plugin drives: its USB product id, key grid and image size, how key
/// images are oriented, and for the Stream Deck + its dials and touch strip. Most use the current
/// ("second generation") protocol: JPEG key images in 1024-byte reports. The first Stream Deck and
/// the Mini use the first one (<see cref="Generation"/> 1): BMP key images, other report layouts.
/// </summary>
/// <param name="Generation">The protocol: 2 (JPEG) or 1 (BMP; first edition and Mini).</param>
/// <param name="ImageReportLength">Length of the reports that carry key images (first generation: 8191 on the first edition, 1024 on the Mini).</param>
/// <param name="KeysRightToLeft">The first edition numbers its keys from the right in each row.</param>
/// <param name="TransposeKeyImages">The Mini's key images are mirrored on the diagonal (turned 90° and flipped).</param>
/// <param name="TouchPoints">Touch buttons without a display but with an RGB light (the Neo's two arrows), reported after the keys.</param>
/// <param name="InfoScreenWidth">Width of a small screen without input (the Neo's info bar), 0 when there is none.</param>
/// <param name="InfoScreenHeight">Height of that screen.</param>
/// <param name="Verified">Checked on real hardware; the others are built from the published protocol and say so.</param>
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
    bool HasDisplays = true,
    int Generation = 2,
    int ImageReportLength = 1024,
    bool KeysRightToLeft = false,
    bool TransposeKeyImages = false,
    int TouchPoints = 0,
    int InfoScreenWidth = 0,
    int InfoScreenHeight = 0,
    bool Verified = false)
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
        new("Stream Deck Neo", 0x009a, 4, 2, 96, FlipKeyImages: true, TouchPoints: 2, InfoScreenWidth: 248, InfoScreenHeight: 58),
        new("Stream Deck +", 0x0084, 4, 2, 120, FlipKeyImages: false, Dials: 4, TouchStripWidth: 800, TouchStripHeight: 100),
        new("Stream Deck Pedal", 0x0086, 3, 1, 0, FlipKeyImages: false, HasDisplays: false),
        new("Stream Deck (first edition)", 0x0060, 5, 3, 72, FlipKeyImages: true, Generation: 1, ImageReportLength: 8191, KeysRightToLeft: true),
        new("Stream Deck Mini", 0x0063, 3, 2, 80, FlipKeyImages: false, Generation: 1, TransposeKeyImages: true),
        new("Stream Deck Mini", 0x0090, 3, 2, 80, FlipKeyImages: false, Generation: 1, TransposeKeyImages: true),
        new("Stream Deck Mini (Discord edition)", 0x00b3, 3, 2, 80, FlipKeyImages: false, Generation: 1, TransposeKeyImages: true),
        new("Stream Deck Module 6", 0x00b8, 3, 2, 80, FlipKeyImages: false, Generation: 1, TransposeKeyImages: true),
        new("Stream Deck Module 15", 0x00b9, 5, 3, 72, FlipKeyImages: true),
        new("Stream Deck Module 32", 0x00ba, 8, 4, 96, FlipKeyImages: true),
    ];

    /// <summary>
    /// Models recognized but not driven yet: DeckHost says so in the log and on the Devices page
    /// instead of ignoring them.
    /// </summary>
    public static IReadOnlyDictionary<int, string> NotYetSupported { get; } = new Dictionary<int, string>
    {
        [0x00aa] = "Stream Deck Studio",
    };

    /// <summary>The control id of touch point <paramref name="index"/> (the Neo's arrows).</summary>
    public static string TouchPointId(int index) => $"button.{index}";

    /// <summary>The control id of the info screen.</summary>
    public const string InfoScreenId = "screen.0";

    public const string InfoScreenDisplayId = "info";

    /// <summary>
    /// The device's own key number for DeckHost's key <paramref name="key"/> (reading order). The same
    /// mapping works both ways.
    /// </summary>
    public int HardwareKey(int key)
    {
        if (!KeysRightToLeft)
        {
            return key;
        }

        var column = key % Columns;
        return key - column + (Columns - 1 - column);
    }

    public static StreamDeckModel? Find(int productId) => All.FirstOrDefault(model => model.ProductId == productId);

    /// <summary>
    /// The controls as DeckHost lays them out: keys key.0… in reading order; on the Stream Deck +
    /// the touch strip as four zones (touch.0–3, one above each dial), a swipe over it
    /// (touch.swipe) and the dials (encoder.0–3), each linked to its zone.
    /// </summary>
    /// <summary>Display ids in the descriptor: the key screens and the + touch strip.</summary>
    public const string KeysDisplayId = "keys";

    public const string StripDisplayId = "strip";

    /// <summary>The device's descriptor (master plan 7.1): controls, identity, displays and capabilities.</summary>
    public DeviceDefinition Describe(string deviceId, bool isConnected, string? serial, string? firmware, string? detail)
    {
        var controls = Controls();
        var displays = new List<DeviceDisplay>();
        if (HasDisplays)
        {
            displays.Add(new DeviceDisplay(KeysDisplayId, Columns * KeyImageSize, Rows * KeyImageSize)
            {
                RotationDegrees = FlipKeyImages ? 180 : 0,
                ControlIds = [.. controls.Where(static control => control.DisplayId == KeysDisplayId).Select(static control => control.Id)],
            });
        }

        if (InfoScreenWidth > 0)
        {
            displays.Add(new DeviceDisplay(InfoScreenDisplayId, InfoScreenWidth, InfoScreenHeight)
            {
                RotationDegrees = FlipKeyImages ? 180 : 0,
                ControlIds = [InfoScreenId],
            });
        }

        if (Dials > 0)
        {
            displays.Add(new DeviceDisplay(StripDisplayId, TouchStripWidth, TouchStripHeight)
            {
                ControlIds = [.. controls.Where(static control => control.DisplayId == StripDisplayId).Select(static control => control.Id)],
            });
        }

        return new DeviceDefinition(deviceId, Name, controls, isConnected, detail)
        {
            Identity = new DeviceIdentity("Elgato", Name, serial, FirmwareVersion: firmware, Connection: "USB"),
            Displays = displays,
            Capabilities = (HasDisplays ? DeviceCapabilities.Images | DeviceCapabilities.Brightness : DeviceCapabilities.None)
                | (Dials > 0 ? DeviceCapabilities.Touch : DeviceCapabilities.None)
                | (TouchPoints > 0 ? DeviceCapabilities.RgbLights : DeviceCapabilities.None)
                | DeviceCapabilities.Firmware | DeviceCapabilities.Settings,
            Limits = LimitNotes() is { Count: > 0 } notes ? new DeviceLimits { Notes = notes } : null,
        };
    }

    private List<string> LimitNotes()
    {
        var notes = new List<string>();
        if (Generation == 1)
        {
            notes.Add("Key images are sent as bitmaps, one key at a time.");
        }

        if (!Verified)
        {
            notes.Add("Preview: built from the published protocol and not yet checked on this model. Please report how it works.");
        }

        return notes;
    }

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
                HardwareId: index.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                DisplayId = HasDisplays ? KeysDisplayId : null,
            });
        }

        var below = Rows * (Key + Gap);
        if (InfoScreenWidth > 0 || TouchPoints > 0)
        {
            var rowWidth = (Columns * (Key + Gap)) - Gap;
            const int PointSize = 36;
            var screenWidth = rowWidth - (TouchPoints > 0 ? 2 * (PointSize + Gap) : 0);
            var screenHeight = InfoScreenHeight > 0 ? Math.Round(screenWidth * InfoScreenHeight / (double)InfoScreenWidth) : PointSize;
            var screenLeft = TouchPoints > 0 ? PointSize + Gap : 0;
            if (InfoScreenWidth > 0)
            {
                controls.Add(new PhysicalControl(
                    InfoScreenId,
                    "Info screen",
                    ControlType.Display,
                    new Rect(screenLeft, below, screenWidth, screenHeight),
                    InputCapabilities.None,
                    OutputCapabilities.ColorImage | OutputCapabilities.Text,
                    InfoScreenWidth,
                    InfoScreenHeight)
                {
                    DisplayId = InfoScreenDisplayId,
                });
            }

            for (var point = 0; point < TouchPoints; point++)
            {
                var left = point == 0 ? 0 : rowWidth - PointSize;
                controls.Add(new PhysicalControl(
                    TouchPointId(point),
                    point == 0 ? "Left touch point" : "Right touch point",
                    ControlType.Button,
                    new Rect(left, below + ((screenHeight - PointSize) / 2), PointSize, PointSize),
                    keyInputs,
                    OutputCapabilities.None,
                    HardwareId: (KeyCount + point).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    LedIndex: point)
                {
                    Shape = ControlShape.Round,
                });
            }
        }

        if (Dials == 0)
        {
            return controls;
        }

        var top = below + (InfoScreenWidth > 0 || TouchPoints > 0 ? 60 : 0);
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
                TouchStripHeight)
            {
                DisplayId = StripDisplayId,
            });
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
                DisplayControlId: $"touch.{dial}")
            {
                Shape = ControlShape.Round,
            });
        }

        return controls;
    }
}
