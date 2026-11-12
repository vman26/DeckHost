using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;

namespace DeckHost.Device.MiraBox;

/// <summary>
/// Physical layout and SDK id mapping of the MiraBox / StreamDock N4 Pro, taken from the SDK's
/// StreamDockN4Pro.cpp:
/// <list type="bullet">
/// <item>10 LCD keys, 112x112, SDK key ids 6..15. The SDK numbers them from the bottom-left,
/// left to right and bottom to top, so the top row is 11..15 and the bottom row 6..10.</item>
/// <item>A touch strip with 4 zones, 176x112, SDK key ids 1..4 (only a release event per tap),
/// plus swipe left (28) and swipe right (29) for the whole strip.</item>
/// <item>4 knobs: rotate left 16/18/20/22, rotate right 17/19/21/23, press 24..27
/// (the N4 Pro sends no knob release). Each knob sits under one touch zone, which is its
/// display (<see cref="PhysicalControl.DisplayControlId"/>).</item>
/// <item>4 RGB LEDs.</item>
/// </list>
/// The positions below are designer coordinates (not device pixels). If a key lights up in the
/// wrong place on your unit, fix <see cref="KeyHardwareIds"/>; the Key ID (Diagnostic) action
/// shows the logical and SDK id on each key.
/// </summary>
internal static class N4ProLayout
{
    public const string KeyPrefix = "key.";
    public const string TouchPrefix = "touch.";
    public const string EncoderPrefix = "encoder.";
    public const string SwipeControlId = "touch.swipe";

    public const int KeyCount = 10;
    public const int TouchZoneCount = 4;
    public const int EncoderCount = 4;

    public const int DefaultKeySize = 112;
    public const int DefaultTouchZoneWidth = 176;
    public const int DefaultTouchZoneHeight = 112;

    /// <summary>SDK key id for key.0 .. key.9 in reading order (top-left first).</summary>
    internal static readonly int[] KeyHardwareIds = [11, 12, 13, 14, 15, 6, 7, 8, 9, 10];

    private const int FirstTouchZoneId = 1;
    private const int FirstKnobLeftId = 16;
    private const int FirstKnobRightId = 17;
    private const int FirstKnobPressId = 24;
    private const int SwipeLeftId = 28;
    private const int SwipeRightId = 29;

    // Designer layout.
    private const double Margin = 24;
    private const double KeySize = 100;
    private const double KeyGap = 16;
    private const double StripTop = 264;
    private const double ZoneGap = 8;
    private const double SwipeBarHeight = 28;
    private const double KnobSize = 80;

    public static DeviceDefinition Create(string deviceId, string name, MiraBoxDeviceInfo? info, bool isConnected, string? detail)
    {
        var keyWidth = info is { KeyWidth: > 0 } ? info.KeyWidth : DefaultKeySize;
        var keyHeight = info is { KeyHeight: > 0 } ? info.KeyHeight : DefaultKeySize;
        var zoneWidth = info is { SecondScreenWidth: > 0 } ? info.SecondScreenWidth : DefaultTouchZoneWidth;
        var zoneHeight = info is { SecondScreenHeight: > 0 } ? info.SecondScreenHeight : DefaultTouchZoneHeight;
        var ledCount = info?.LedCount ?? EncoderCount;

        var stripWidth = (5 * KeySize) + (4 * KeyGap);
        var zoneDesignWidth = (stripWidth - ((TouchZoneCount - 1) * ZoneGap)) / TouchZoneCount;
        var zoneDesignHeight = Math.Round(zoneDesignWidth * zoneHeight / zoneWidth);

        var controls = new List<PhysicalControl>();

        for (var index = 0; index < KeyCount; index++)
        {
            var row = index / 5;
            var column = index % 5;
            controls.Add(new PhysicalControl(
                $"{KeyPrefix}{index}",
                $"Key {index + 1}",
                ControlType.Key,
                new Rect(Margin + (column * (KeySize + KeyGap)), Margin + (row * (KeySize + KeyGap)), KeySize, KeySize),
                InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress,
                OutputCapabilities.ColorImage | OutputCapabilities.Text,
                keyWidth,
                keyHeight,
                KeyHardwareIds[index].ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        for (var index = 0; index < TouchZoneCount; index++)
        {
            controls.Add(new PhysicalControl(
                $"{TouchPrefix}{index}",
                $"Touch {index + 1}",
                ControlType.TouchSurface,
                new Rect(Margin + (index * (zoneDesignWidth + ZoneGap)), StripTop, zoneDesignWidth, zoneDesignHeight),
                InputCapabilities.Press,
                OutputCapabilities.ColorImage | OutputCapabilities.Text,
                zoneWidth,
                zoneHeight,
                (FirstTouchZoneId + index).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var swipeTop = StripTop + zoneDesignHeight + 8;
        controls.Add(new PhysicalControl(
            SwipeControlId,
            "Touch Strip Swipe",
            ControlType.TouchSurface,
            new Rect(Margin, swipeTop, stripWidth, SwipeBarHeight),
            InputCapabilities.Swipe,
            OutputCapabilities.None,
            HardwareId: $"{SwipeLeftId}/{SwipeRightId}"));

        var knobTop = swipeTop + SwipeBarHeight + 20;
        for (var index = 0; index < EncoderCount; index++)
        {
            var zoneCenter = Margin + (index * (zoneDesignWidth + ZoneGap)) + (zoneDesignWidth / 2);
            controls.Add(new PhysicalControl(
                $"{EncoderPrefix}{index}",
                $"Knob {index + 1}",
                ControlType.Encoder,
                new Rect(zoneCenter - (KnobSize / 2), knobTop, KnobSize, KnobSize),
                InputCapabilities.Rotate | InputCapabilities.Press | InputCapabilities.Release,
                OutputCapabilities.None,
                HardwareId: $"{FirstKnobLeftId + (index * 2)}/{FirstKnobRightId + (index * 2)}/{FirstKnobPressId + index}",
                LedIndex: index < ledCount ? index : null,
                DisplayControlId: $"{TouchPrefix}{index}"));
        }

        return new DeviceDefinition(deviceId, name, controls, isConnected, detail);
    }

    /// <summary>SDK key id that receives this control's image, or null when the control has no image.</summary>
    public static int? GetImageKeyId(string controlId)
    {
        if (TryParseIndex(controlId, KeyPrefix, KeyCount, out var key))
        {
            return KeyHardwareIds[key];
        }

        if (TryParseIndex(controlId, TouchPrefix, TouchZoneCount, out var zone))
        {
            return FirstTouchZoneId + zone;
        }

        return null;
    }

    public static int? GetLedIndex(string controlId) =>
        TryParseIndex(controlId, EncoderPrefix, EncoderCount, out var encoder) ? encoder : null;

    /// <summary>Maps an SDK event to a control. Returns false for events the N4 Pro layout doesn't use.</summary>
    public static bool TryMapInput(MiraBoxEventType eventType, int sdkControlId, out string controlId, out InputCapabilities inputEvent, out int value)
    {
        controlId = string.Empty;
        inputEvent = InputCapabilities.None;
        value = 0;

        switch (eventType)
        {
            case MiraBoxEventType.KeyPress or MiraBoxEventType.KeyRelease when Array.IndexOf(KeyHardwareIds, sdkControlId) >= 0:
                controlId = $"{KeyPrefix}{Array.IndexOf(KeyHardwareIds, sdkControlId)}";
                inputEvent = eventType == MiraBoxEventType.KeyPress ? InputCapabilities.Press : InputCapabilities.Release;
                value = eventType == MiraBoxEventType.KeyPress ? 1 : 0;
                return true;

            // Touch zones only report the end of a tap; treat it as a press.
            case MiraBoxEventType.KeyRelease when sdkControlId is >= FirstTouchZoneId and < FirstTouchZoneId + TouchZoneCount:
                controlId = $"{TouchPrefix}{sdkControlId - FirstTouchZoneId}";
                inputEvent = InputCapabilities.Press;
                value = 1;
                return true;

            case MiraBoxEventType.KnobLeft when IsKnobRotationId(sdkControlId, FirstKnobLeftId):
                controlId = $"{EncoderPrefix}{(sdkControlId - FirstKnobLeftId) / 2}";
                inputEvent = InputCapabilities.Rotate;
                value = -1;
                return true;

            case MiraBoxEventType.KnobRight when IsKnobRotationId(sdkControlId, FirstKnobRightId):
                controlId = $"{EncoderPrefix}{(sdkControlId - FirstKnobRightId) / 2}";
                inputEvent = InputCapabilities.Rotate;
                value = 1;
                return true;

            case MiraBoxEventType.KnobPress or MiraBoxEventType.KnobRelease when sdkControlId is >= FirstKnobPressId and < FirstKnobPressId + EncoderCount:
                controlId = $"{EncoderPrefix}{sdkControlId - FirstKnobPressId}";
                inputEvent = eventType == MiraBoxEventType.KnobPress ? InputCapabilities.Press : InputCapabilities.Release;
                value = eventType == MiraBoxEventType.KnobPress ? 1 : 0;
                return true;

            case MiraBoxEventType.SwipeLeft:
            case MiraBoxEventType.SwipeRight:
                controlId = SwipeControlId;
                inputEvent = InputCapabilities.Swipe;
                value = eventType == MiraBoxEventType.SwipeLeft ? -1 : 1;
                return true;

            default:
                return false;
        }
    }

    private static bool IsKnobRotationId(int sdkControlId, int firstId) =>
        sdkControlId >= firstId && sdkControlId < firstId + (EncoderCount * 2) && (sdkControlId - firstId) % 2 == 0;

    private static bool TryParseIndex(string controlId, string prefix, int count, out int index)
    {
        index = -1;
        return controlId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(controlId.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index) &&
               index >= 0 && index < count;
    }
}
