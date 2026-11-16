using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using System.Globalization;

namespace DeckHost.Device.MiraBox;

/// <summary>
/// The MiraBox / StreamDock models the vendor SDK knows, with their layouts. Taken from the SDK's
/// device classes (<c>native/StreamDock-SDK/CPP-SDK/src/HotspotDevice/*</c>): each class's
/// <c>_readValueMap</c> gives the logical ids, its <c>dispatchEvent</c> the event types and its
/// comments where the keys are. Only the N4 Pro has been checked on hardware.
/// <para>
/// Control ids follow DeckHost's convention: <c>key.N</c> for keys with a display in reading order
/// (top-left first), <c>touch.N</c> for touch zones, <c>encoder.N</c> for knobs and the side switches
/// (turned left/right and pressed), <c>button.N</c> for buttons without a display (left to right),
/// <c>screen.N</c> for the small second-screen displays. Positions are designer coordinates.
/// </para>
/// </summary>
internal static class MiraBoxModels
{
    public const string KeyPrefix = "key.";
    public const string TouchPrefix = "touch.";
    public const string EncoderPrefix = "encoder.";
    public const string ButtonPrefix = "button.";
    public const string ScreenPrefix = "screen.";
    public const string SwipeControlId = "touch.swipe";

    // Designer layout, shared by every model so they look alike.
    private const double Margin = 24;
    private const double KeySize = 100;
    private const double KeyGap = 16;
    private const double KnobSize = 80;
    private const double ButtonHeight = 40;

    private const InputCapabilities KeyInputs = InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress;
    private const InputCapabilities KnobInputs = InputCapabilities.Rotate | InputCapabilities.Press | InputCapabilities.Release;

    /// <summary>SDK origin types (<c>DeviceOriginType</c> in the SDK's streamdockinfo.h).</summary>
    public static class Origin
    {
        public const int SD293 = 0x01;
        public const int SD293s = 0x02;
        public const int N3 = 0x03;
        public const int N1 = 0x04;
        public const int N4 = 0x05;
        public const int M18 = 0x06;
        public const int N4Pro = 0x07;
        public const int XL = 0x08;
        public const int M3 = 0x09;
        public const int K1Pro = 0x0A;
        public const int Mini = 0x0B;
    }

    /// <summary>
    /// The N4 Pro: 10 keys (SDK 6–15, numbered from the bottom-left, so the top row is 11–15), a
    /// touch strip of 4 zones (1–4, a release per tap) with swipes (28/29), and 4 knobs (left
    /// 16/18/20/22, right 17/19/21/23, press 24–27, no release), each under its zone and with an RGB LED.
    /// </summary>
    public static MiraBoxModel N4Pro { get; } = new(Origin.N4Pro, "n4pro", "N4 Pro", "StreamDock N4 Pro", N4FamilyControls(withLeds: true))
    {
        ScreenWidth = 800,
        ScreenHeight = 480,
        RotationDegrees = 180,
        KeySize = 112,
        SecondKeyWidth = 176,
        SecondKeyHeight = 112,
        SecondDisplayId = "strip",
        SecondScreenGrid = (4, 1),
        KnobsReportNoRelease = true,
        HasDeviceConfig = true,
        Verified = true,
        Capabilities = DeviceCapabilities.Images | DeviceCapabilities.Touch | DeviceCapabilities.Brightness | DeviceCapabilities.RgbLights
            | DeviceCapabilities.Haptics | DeviceCapabilities.Backdrop | DeviceCapabilities.Firmware | DeviceCapabilities.Settings,
        Notes = ["The knobs report a press but no release: DeckHost adds the release.", "Every LED dark shows the firmware's own rainbow: DeckHost dims them instead."],
    };

    /// <summary>The N4: the N4 Pro's controls and ids without the LEDs.</summary>
    public static MiraBoxModel N4 { get; } = new(Origin.N4, "n4", "N4", "StreamDock N4", N4FamilyControls(withLeds: false))
    {
        ScreenWidth = 800,
        ScreenHeight = 480,
        RotationDegrees = 180,
        KeySize = 112,
        SecondKeyWidth = 176,
        SecondKeyHeight = 112,
        SecondDisplayId = "strip",
        SecondScreenGrid = (4, 1),
        KnobsReportNoRelease = true,
        Capabilities = DeviceCapabilities.Images | DeviceCapabilities.Touch | DeviceCapabilities.Brightness | DeviceCapabilities.Firmware | DeviceCapabilities.Settings,
        Notes = ["The knobs report a press but no release: DeckHost adds the release."],
    };

    /// <summary>The 293 (V2 100 px, V3 112 px keys): 15 keys, SDK 1–15 from the bottom-left.</summary>
    public static MiraBoxModel Sd293 { get; } = new(Origin.SD293, "293", "293", "StreamDock 293", [.. Keys(5, 3, BottomLeftRows(5, 3, 1), Margin, Margin)])
    {
        ScreenWidth = 800,
        ScreenHeight = 480,
        RotationDegrees = 180,
        KeySize = 112,
        Capabilities = DeviceCapabilities.Images | DeviceCapabilities.Brightness | DeviceCapabilities.Firmware | DeviceCapabilities.Settings,
    };

    /// <summary>
    /// The 293s: 15 keys, SDK 1–15 from the top-right counted top to bottom, then right to left;
    /// three small displays (16–18) that show images but can't be pressed.
    /// </summary>
    public static MiraBoxModel Sd293s { get; } = new(Origin.SD293s, "293s", "293s", "StreamDock 293s",
        [
            .. Keys(5, 3, TopRightColumns(5, 3, 1), Margin, Margin),
            .. Enumerable.Range(0, 3).Select(index => Screen(index, Margin + (5 * (KeySize + KeyGap)), Margin + (index * (KeySize + KeyGap)) + 10, 80, 16 + index, pressable: false)),
        ])
    {
        ScreenWidth = 854,
        ScreenHeight = 480,
        RotationDegrees = 270,
        KeySize = 96,
        SecondKeyWidth = 80,
        SecondKeyHeight = 80,
        SecondScreenGrid = (1, 3),
        Capabilities = DeviceCapabilities.Images | DeviceCapabilities.Brightness | DeviceCapabilities.Firmware | DeviceCapabilities.Settings,
        Notes = ["The three small displays only show pictures; they can't be pressed."],
    };

    /// <summary>The M18 (V2, V2.5, V3): 15 keys (1–15 from the bottom-left) and three buttons below (16–18).</summary>
    public static MiraBoxModel M18 { get; } = new(Origin.M18, "m18", "M18", "StreamDock M18",
        [
            .. Keys(5, 3, BottomLeftRows(5, 3, 1), Margin, Margin),
            .. ButtonRow(3, [16, 17, 18], Margin, Margin + (3 * (KeySize + KeyGap)), (5 * KeySize) + (4 * KeyGap)),
        ])
    {
        ScreenWidth = 480,
        ScreenHeight = 272,
        KeySize = 64,
        Capabilities = DeviceCapabilities.Images | DeviceCapabilities.Brightness | DeviceCapabilities.Firmware | DeviceCapabilities.Settings,
        Notes = ["Its 24 RGB LEDs aren't driven by DeckHost yet."],
    };

    /// <summary>The XL: 32 keys (1–32 from the bottom-left) and two side switches (left/right/press: 33/34/37 and 35/36/38).</summary>
    public static MiraBoxModel XL { get; } = new(Origin.XL, "xl", "XL", "StreamDock XL",
        [
            .. Keys(8, 4, BottomLeftRows(8, 4, 1), Margin, Margin),
            Switch(0, Margin, Margin + (4 * (KeySize + KeyGap)), left: 33, right: 34, press: 37),
            Switch(1, Margin + (7 * (KeySize + KeyGap)), Margin + (4 * (KeySize + KeyGap)), left: 35, right: 36, press: 38),
        ])
    {
        ScreenWidth = 1024,
        ScreenHeight = 600,
        RotationDegrees = 180,
        KeySize = 80,
        Capabilities = DeviceCapabilities.Images | DeviceCapabilities.Brightness | DeviceCapabilities.Firmware | DeviceCapabilities.Settings,
        Notes = ["The side switches act as knobs: pushed left or right they turn, pushed in they press.", "Its 6 RGB LEDs aren't driven by DeckHost yet."],
    };

    /// <summary>The M3: 15 keys (1–15 from the bottom-left) and three knobs in a column (bottom to top: 16/17 + 22, 18/19 + 23, 20/21 + 24).</summary>
    public static MiraBoxModel M3 { get; } = new(Origin.M3, "m3", "M3", "StreamDock M3",
        [
            .. Keys(5, 3, BottomLeftRows(5, 3, 1), Margin, Margin),
            Knob(0, Margin + (5 * (KeySize + KeyGap)) + 10, Margin + 10, left: 20, right: 21, press: 24, release: true),
            Knob(1, Margin + (5 * (KeySize + KeyGap)) + 10, Margin + KeySize + KeyGap + 10, left: 18, right: 19, press: 23, release: true),
            Knob(2, Margin + (5 * (KeySize + KeyGap)) + 10, Margin + (2 * (KeySize + KeyGap)) + 10, left: 16, right: 17, press: 22, release: true),
        ])
    {
        ScreenWidth = 854,
        ScreenHeight = 480,
        RotationDegrees = 270,
        KeySize = 96,
        Capabilities = DeviceCapabilities.Images | DeviceCapabilities.Brightness | DeviceCapabilities.Firmware | DeviceCapabilities.Settings,
    };

    /// <summary>
    /// The N3 (V2, V2.5, V3): 6 keys (1–6 from the top-left), three buttons below them (7–9) and
    /// three knobs on the left (top: press 12, left 15, right 18; bottom-left 10/13/16; bottom-right
    /// 11/14/17; no release).
    /// </summary>
    public static MiraBoxModel N3 { get; } = new(Origin.N3, "n3", "N3", "StreamDock N3",
        [
            .. Keys(3, 2, TopLeftRows(3, 2, 1), Margin + 230, Margin),
            .. ButtonRow(3, [7, 8, 9], Margin + 230, Margin + (2 * (KeySize + KeyGap)), (3 * KeySize) + (2 * KeyGap)),
            Knob(0, Margin + 65, Margin + 10, left: 15, right: 18, press: 12, release: false),
            Knob(1, Margin, Margin + 120, left: 13, right: 16, press: 10, release: false),
            Knob(2, Margin + 130, Margin + 120, left: 14, right: 17, press: 11, release: false),
        ])
    {
        ScreenWidth = 320,
        ScreenHeight = 240,
        KeySize = 64,
        KnobsReportNoRelease = true,
        Capabilities = DeviceCapabilities.Images | DeviceCapabilities.Brightness | DeviceCapabilities.Firmware | DeviceCapabilities.Settings,
        Notes = ["The knobs report a press but no release: DeckHost adds the release."],
    };

    /// <summary>
    /// The N1: 15 keys (1–15 from the top-left, 3 wide), three small displays above them (16–18:
    /// the first two are buttons, the third shows the knob) and a knob (press 18, left 19, right 20).
    /// </summary>
    public static MiraBoxModel N1 { get; } = new(Origin.N1, "n1", "N1", "StreamDock N1",
        [
            Screen(0, Margin + 14, Margin + KnobSize + 16, 72, 16, pressable: true),
            Screen(1, Margin + KeySize + KeyGap + 14, Margin + KnobSize + 16, 72, 17, pressable: true),
            Screen(2, Margin + (2 * (KeySize + KeyGap)) + 14, Margin + KnobSize + 16, 72, 18, pressable: false),
            Knob(0, Margin + (2 * (KeySize + KeyGap)) + 10, Margin, left: 19, right: 20, press: 18, release: true) with { DisplayControlId = $"{ScreenPrefix}2" },
            .. Keys(3, 5, TopLeftRows(3, 5, 1), Margin, Margin + KnobSize + 16 + 72 + KeyGap),
        ])
    {
        ScreenWidth = 480,
        ScreenHeight = 854,
        KeySize = 96,
        SecondKeyWidth = 64,
        SecondKeyHeight = 64,
        SecondScreenGrid = (3, 1),
        Capabilities = DeviceCapabilities.Images | DeviceCapabilities.Brightness | DeviceCapabilities.Firmware | DeviceCapabilities.Settings,
    };

    /// <summary>The K1 Pro keyboard's macro part: 6 keys (1–6) and three knobs (press 7–9, left 10–12, right 13–15).</summary>
    public static MiraBoxModel K1Pro { get; } = new(Origin.K1Pro, "k1pro", "K1 Pro", "K1 Pro",
        [
            Knob(0, Margin + 10, Margin, left: 10, right: 13, press: 7, release: true),
            Knob(1, Margin + KeySize + KeyGap + 10, Margin, left: 11, right: 14, press: 8, release: true),
            Knob(2, Margin + (2 * (KeySize + KeyGap)) + 10, Margin, left: 12, right: 15, press: 9, release: true),
            .. Keys(3, 2, TopLeftRows(3, 2, 1), Margin, Margin + KnobSize + KeyGap),
        ])
    {
        ScreenWidth = 3 * 64,
        ScreenHeight = 2 * 64,
        RotationDegrees = 90,
        KeySize = 64,
        Capabilities = DeviceCapabilities.Images | DeviceCapabilities.Brightness | DeviceCapabilities.Firmware | DeviceCapabilities.Settings,
        Notes = ["Only the display keys and knobs are used; the keyboard stays a keyboard."],
    };

    /// <summary>The Mini: 6 keys (1–6 from the top-left) and two side switches (left/right/press: 7/8/9 and 10/11/12).</summary>
    public static MiraBoxModel Mini { get; } = new(Origin.Mini, "mini", "Mini", "StreamDock Mini",
        [
            .. Keys(3, 2, TopLeftRows(3, 2, 1), Margin, Margin),
            Switch(0, Margin, Margin + (2 * (KeySize + KeyGap)), left: 7, right: 8, press: 9),
            Switch(1, Margin + (2 * (KeySize + KeyGap)), Margin + (2 * (KeySize + KeyGap)), left: 10, right: 11, press: 12),
        ])
    {
        ScreenWidth = 320,
        ScreenHeight = 240,
        RotationDegrees = 270,
        KeySize = 64,
        Capabilities = DeviceCapabilities.Images | DeviceCapabilities.Brightness | DeviceCapabilities.Firmware | DeviceCapabilities.Settings,
        Notes = ["The side switches act as knobs: pushed left or right they turn, pushed in they press.", "Its 12 RGB LEDs aren't driven by DeckHost yet."],
    };

    public static IReadOnlyList<MiraBoxModel> All { get; } = [N4Pro, N4, Sd293, Sd293s, M18, XL, M3, N3, N1, K1Pro, Mini];

    /// <summary>The model for the SDK's origin type, or null for one DeckHost doesn't know.</summary>
    public static MiraBoxModel? For(int originType) => All.FirstOrDefault(model => model.OriginType == originType);

    // ---------------------------------------------------------------- SDK numberings

    /// <summary>SDK ids in reading order for keys numbered from the bottom-left, left to right, bottom to top.</summary>
    internal static int[] BottomLeftRows(int columns, int rows, int first) =>
        [.. Enumerable.Range(0, columns * rows).Select(index => first + ((rows - 1 - (index / columns)) * columns) + (index % columns))];

    /// <summary>SDK ids in reading order for keys numbered from the top-left, left to right, top to bottom.</summary>
    internal static int[] TopLeftRows(int columns, int rows, int first) =>
        [.. Enumerable.Range(first, columns * rows)];

    /// <summary>SDK ids in reading order for keys numbered from the top-right, top to bottom, right to left.</summary>
    internal static int[] TopRightColumns(int columns, int rows, int first) =>
        [.. Enumerable.Range(0, columns * rows).Select(index => first + ((columns - 1 - (index % columns)) * rows) + (index / columns))];

    // ---------------------------------------------------------------- builders

    private static MiraBoxControl[] N4FamilyControls(bool withLeds)
    {
        const int ZoneCount = 4;
        const double StripTop = 264;
        const double ZoneGap = 8;
        const double SwipeBarHeight = 28;
        var stripWidth = (5 * KeySize) + (4 * KeyGap);
        var zoneWidth = (stripWidth - ((ZoneCount - 1) * ZoneGap)) / ZoneCount;
        var zoneHeight = Math.Round(zoneWidth * 112 / 176);
        var swipeTop = StripTop + zoneHeight + 8;
        var knobTop = swipeTop + SwipeBarHeight + 20;

        var controls = new List<MiraBoxControl>(Keys(5, 2, BottomLeftRows(5, 2, 6), Margin, Margin));
        for (var index = 0; index < ZoneCount; index++)
        {
            var sdkId = 1 + index;
            controls.Add(new MiraBoxControl($"{TouchPrefix}{index}", $"Touch {index + 1}", ControlType.TouchSurface, new Rect(Margin + (index * (zoneWidth + ZoneGap)), StripTop, zoneWidth, zoneHeight), InputCapabilities.Press)
            {
                ImageKeyId = sdkId,
                Screen = MiraBoxScreen.Second,

                // Touch zones only report the end of a tap; it counts as a press.
                Events = [new(MiraBoxEventType.KeyRelease, sdkId, InputCapabilities.Press, 1)],
            });
        }

        controls.Add(new MiraBoxControl(SwipeControlId, "Touch Strip Swipe", ControlType.TouchSurface, new Rect(Margin, swipeTop, stripWidth, SwipeBarHeight), InputCapabilities.Swipe)
        {
            HardwareId = "28/29",
            Events =
            [
                new(MiraBoxEventType.SwipeLeft, 28, InputCapabilities.Swipe, -1),
                new(MiraBoxEventType.SwipeRight, 29, InputCapabilities.Swipe, 1),
            ],
        });

        for (var index = 0; index < ZoneCount; index++)
        {
            var center = Margin + (index * (zoneWidth + ZoneGap)) + (zoneWidth / 2);
            controls.Add(Knob(index, center - (KnobSize / 2), knobTop, left: 16 + (index * 2), right: 17 + (index * 2), press: 24 + index, release: false) with
            {
                LedIndex = withLeds ? index : null,
                DisplayControlId = $"{TouchPrefix}{index}",
            });
        }

        return [.. controls];
    }

    /// <summary>A grid of keys with a display, <c>key.0</c> top-left; <paramref name="sdkIds"/> in reading order.</summary>
    private static IEnumerable<MiraBoxControl> Keys(int columns, int rows, int[] sdkIds, double left, double top) =>
        Enumerable.Range(0, columns * rows).Select(index => new MiraBoxControl(
            $"{KeyPrefix}{index}",
            $"Key {index + 1}",
            ControlType.Key,
            new Rect(left + (index % columns * (KeySize + KeyGap)), top + (index / columns * (KeySize + KeyGap)), KeySize, KeySize),
            KeyInputs)
        {
            ImageKeyId = sdkIds[index],
            Screen = MiraBoxScreen.Main,
            Events =
            [
                new(MiraBoxEventType.KeyPress, sdkIds[index], InputCapabilities.Press, 1),
                new(MiraBoxEventType.KeyRelease, sdkIds[index], InputCapabilities.Release, 0),
            ],
        });

    /// <summary>Buttons without a display spread over <paramref name="width"/>, left to right.</summary>
    private static IEnumerable<MiraBoxControl> ButtonRow(int count, int[] sdkIds, double left, double top, double width)
    {
        const double Gap = 16;
        var buttonWidth = (width - ((count - 1) * Gap)) / count;
        return Enumerable.Range(0, count).Select(index => new MiraBoxControl(
            $"{ButtonPrefix}{index}",
            $"Button {index + 1}",
            ControlType.Button,
            new Rect(left + (index * (buttonWidth + Gap)), top, buttonWidth, ButtonHeight),
            KeyInputs)
        {
            HardwareId = sdkIds[index].ToString(CultureInfo.InvariantCulture),
            Events =
            [
                new(MiraBoxEventType.KeyPress, sdkIds[index], InputCapabilities.Press, 1),
                new(MiraBoxEventType.KeyRelease, sdkIds[index], InputCapabilities.Release, 0),
            ],
        });
    }

    /// <summary>A knob: <paramref name="left"/>/<paramref name="right"/> are its turn ids, <paramref name="release"/> whether it reports one.</summary>
    private static MiraBoxControl Knob(int index, double x, double y, int left, int right, int press, bool release) =>
        new($"{EncoderPrefix}{index}", $"Knob {index + 1}", ControlType.Encoder, new Rect(x, y, KnobSize, KnobSize), KnobInputs)
        {
            Shape = ControlShape.Round,
            HardwareId = string.Create(CultureInfo.InvariantCulture, $"{left}/{right}/{press}"),
            Events =
            [
                new(MiraBoxEventType.KnobLeft, left, InputCapabilities.Rotate, -1),
                new(MiraBoxEventType.KnobRight, right, InputCapabilities.Rotate, 1),
                new(MiraBoxEventType.KnobPress, press, InputCapabilities.Press, 1),
                .. release ? new MiraBoxInput[] { new(MiraBoxEventType.KnobRelease, press, InputCapabilities.Release, 0) } : [],
            ],
        };

    /// <summary>A side switch (XL, Mini): pushed left or right it turns like a knob, pushed in it presses.</summary>
    private static MiraBoxControl Switch(int index, double x, double y, int left, int right, int press) =>
        new($"{EncoderPrefix}{index}", $"Switch {index + 1}", ControlType.Encoder, new Rect(x, y, KeySize, ButtonHeight + 20), KnobInputs)
        {
            HardwareId = string.Create(CultureInfo.InvariantCulture, $"{left}/{right}/{press}"),
            Events =
            [
                new(MiraBoxEventType.DipLeft, left, InputCapabilities.Rotate, -1),
                new(MiraBoxEventType.DipRight, right, InputCapabilities.Rotate, 1),
                new(MiraBoxEventType.DipPress, press, InputCapabilities.Press, 1),
                new(MiraBoxEventType.DipRelease, press, InputCapabilities.Release, 0),
            ],
        };

    /// <summary>A small second-screen display; <paramref name="pressable"/> when it is also a button.</summary>
    private static MiraBoxControl Screen(int index, double x, double y, double size, int sdkId, bool pressable) =>
        new($"{ScreenPrefix}{index}", $"Display {index + 1}", pressable ? ControlType.Key : ControlType.Display, new Rect(x, y, size, size), pressable ? KeyInputs : InputCapabilities.None)
        {
            ImageKeyId = sdkId,
            Screen = MiraBoxScreen.Second,
            Events = pressable
                ?
                [
                    new(MiraBoxEventType.KeyPress, sdkId, InputCapabilities.Press, 1),
                    new(MiraBoxEventType.KeyRelease, sdkId, InputCapabilities.Release, 0),
                ]
                : [],
        };
}
