using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using System.Globalization;

namespace DeckHost.Device.MiraBox;

/// <summary>Which screen a control's image is on: none, the main screen behind the keys, or the second screen.</summary>
internal enum MiraBoxScreen
{
    None,
    Main,
    Second,
}

/// <summary>An SDK event (event type + the SDK's logical control id) and what it means for a control.</summary>
internal sealed record MiraBoxInput(MiraBoxEventType Event, int SdkId, InputCapabilities Input, int Value);

/// <summary>One control of a model: where it is in the editor, its image key and the SDK events it reports.</summary>
internal sealed record MiraBoxControl(string Id, string Name, ControlType Type, Rect Bounds, InputCapabilities Inputs)
{
    /// <summary>The SDK key id its image is sent to, for controls with a display.</summary>
    public int? ImageKeyId { get; init; }

    public MiraBoxScreen Screen { get; init; }

    public int? LedIndex { get; init; }

    public string? DisplayControlId { get; init; }

    public ControlShape Shape { get; init; }

    public DeviceSide Side { get; init; }

    public IReadOnlyList<MiraBoxInput> Events { get; init; } = [];

    /// <summary>The SDK ids, for the Key ID diagnostic action and the logs.</summary>
    public string? HardwareId { get; init; }
}

/// <summary>
/// One MiraBox / StreamDock model as the vendor SDK describes it (<c>native/StreamDock-SDK/CPP-SDK/src/HotspotDevice</c>):
/// its controls with their SDK ids, its screens and what it can do. <see cref="MiraBoxModels"/> lists them.
/// The SDK's device class translates the raw HID values into its logical ids and event types; this
/// maps those to DeckHost controls and back.
/// </summary>
internal sealed class MiraBoxModel
{
    public const string MainDisplayId = "main";

    private readonly Dictionary<(MiraBoxEventType Event, int SdkId), (string ControlId, InputCapabilities Input, int Value)> _inputs = [];
    private readonly Dictionary<string, MiraBoxControl> _byId = new(StringComparer.OrdinalIgnoreCase);

    public MiraBoxModel(int originType, string slug, string shortName, string modelName, IReadOnlyList<MiraBoxControl> controls)
    {
        OriginType = originType;
        Slug = slug;
        ShortName = shortName;
        ModelName = modelName;
        Controls = controls;
        foreach (var control in controls)
        {
            _byId.Add(control.Id, control);
            foreach (var input in control.Events)
            {
                _inputs.Add((input.Event, input.SdkId), (control.Id, input.Input, input.Value));
            }
        }
    }

    /// <summary>The SDK's <c>DeviceOriginType</c>.</summary>
    public int OriginType { get; }

    /// <summary>The device id part: <c>mirabox.{Slug}</c>.</summary>
    public string Slug { get; }

    /// <summary>"N4 Pro", "XL", ...</summary>
    public string ShortName { get; }

    /// <summary>"StreamDock N4 Pro", ...</summary>
    public string ModelName { get; }

    public IReadOnlyList<MiraBoxControl> Controls { get; }

    /// <summary>The main screen's size and rotation when the SDK doesn't report them.</summary>
    public int ScreenWidth { get; init; }

    public int ScreenHeight { get; init; }

    public int RotationDegrees { get; init; }

    /// <summary>Key image size when the SDK doesn't report it.</summary>
    public int KeySize { get; init; }

    /// <summary>Second-screen image size when the SDK doesn't report it.</summary>
    public int SecondKeyWidth { get; init; }

    public int SecondKeyHeight { get; init; }

    /// <summary>The second screen's id in the descriptor ("strip" for the N4's touch strip).</summary>
    public string SecondDisplayId { get; init; } = "second";

    /// <summary>How the second screen's images sit next to each other (columns, rows).</summary>
    public (int Columns, int Rows) SecondScreenGrid { get; init; } = (1, 1);

    /// <summary>The knobs report a press but no release (N4, N4 Pro, N3): DeckHost adds the release.</summary>
    public bool KnobsReportNoRelease { get; init; }

    /// <summary>The SDK's device configuration (vibration, boot animation, LED behaviour): only the N4 Pro's items are known.</summary>
    public bool HasDeviceConfig { get; init; }

    public DeviceCapabilities Capabilities { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Checked on real hardware; the others follow the SDK's description.</summary>
    public bool Verified { get; init; }

    /// <summary>LEDs DeckHost drives (tied to controls).</summary>
    public int LedCount => Controls.Count(static control => control.LedIndex is not null);

    public string DefaultDeviceId => $"mirabox.{Slug}";

    public DeviceDefinition Describe(string deviceId, string name, MiraBoxDeviceInfo? info, bool isConnected, string? detail)
    {
        var keyWidth = info is { KeyWidth: > 0 } ? info.KeyWidth : KeySize;
        var keyHeight = info is { KeyHeight: > 0 } ? info.KeyHeight : KeySize;
        var secondWidth = info is { SecondScreenWidth: > 0 } ? info.SecondScreenWidth : SecondKeyWidth;
        var secondHeight = info is { SecondScreenHeight: > 0 } ? info.SecondScreenHeight : SecondKeyHeight;

        var controls = Controls.Select(control =>
        {
            var (width, height) = control.Screen switch
            {
                MiraBoxScreen.Main => (keyWidth, keyHeight),
                MiraBoxScreen.Second => (secondWidth, secondHeight),
                _ => (0, 0),
            };
            return new PhysicalControl(
                control.Id,
                control.Name,
                control.Type,
                control.Bounds,
                control.Inputs,
                control.Screen == MiraBoxScreen.None ? OutputCapabilities.None : OutputCapabilities.ColorImage | OutputCapabilities.Text,
                width,
                height,
                control.HardwareId ?? control.ImageKeyId?.ToString(CultureInfo.InvariantCulture),
                control.LedIndex,
                control.DisplayControlId)
            {
                DisplayId = control.Screen switch
                {
                    MiraBoxScreen.Main => MainDisplayId,
                    MiraBoxScreen.Second => SecondDisplayId,
                    _ => null,
                },
                Shape = control.Shape,
                Side = control.Side,
            };
        }).ToList();

        var displays = new List<DeviceDisplay>();
        var onMain = controls.Where(static control => control.DisplayId == MainDisplayId).Select(static control => control.Id).ToList();
        if (onMain.Count > 0)
        {
            displays.Add(new DeviceDisplay(
                MainDisplayId,
                info is { Width: > 0 } ? info.Width : ScreenWidth,
                info is { Height: > 0 } ? info.Height : ScreenHeight)
            {
                RotationDegrees = RotationDegrees,
                ControlIds = onMain,
            });
        }

        var onSecond = controls.Where(control => control.DisplayId == SecondDisplayId).Select(static control => control.Id).ToList();
        if (onSecond.Count > 0)
        {
            displays.Add(new DeviceDisplay(SecondDisplayId, secondWidth * SecondScreenGrid.Columns, secondHeight * SecondScreenGrid.Rows)
            {
                ControlIds = onSecond,
            });
        }

        return new DeviceDefinition(deviceId, name, controls, isConnected, detail)
        {
            Identity = new DeviceIdentity("MiraBox", ModelName, info?.SerialNumber, FirmwareVersion: info?.FirmwareVersion, Connection: "USB"),
            Displays = displays,
            Capabilities = Capabilities,
            Limits = new DeviceLimits
            {
                Notes = Verified
                    ? Notes
                    : [.. Notes, "Layout from MiraBox's SDK, not yet checked on this model: if a key or knob does the wrong thing, please report it."],
            },
        };
    }

    public MiraBoxControl? Find(string controlId) => _byId.GetValueOrDefault(controlId);

    /// <summary>SDK key id that receives this control's image, or null when the control has no image.</summary>
    public int? GetImageKeyId(string controlId) => Find(controlId)?.ImageKeyId;

    public int? GetLedIndex(string controlId) => Find(controlId)?.LedIndex;

    /// <summary>Maps an SDK event to a control. Returns false for events this model doesn't use.</summary>
    public bool TryMapInput(MiraBoxEventType eventType, int sdkControlId, out string controlId, out InputCapabilities inputEvent, out int value)
    {
        if (_inputs.TryGetValue((eventType, sdkControlId), out var mapped))
        {
            (controlId, inputEvent, value) = mapped;
            return true;
        }

        controlId = string.Empty;
        inputEvent = InputCapabilities.None;
        value = 0;
        return false;
    }

    /// <summary>Whether DeckHost synthesizes a long press for this control (keys and buttons).</summary>
    public bool TracksLongPress(string controlId) =>
        Find(controlId) is { } control && control.Inputs.HasFlag(InputCapabilities.LongPress);
}
