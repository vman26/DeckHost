using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;

namespace DeckHost.Device.Hid;

/// <summary>
/// A USB HID device DeckHost doesn't know by itself, described by the user in the device creator:
/// which device (vendor and product id, optionally the HID interface by usage page), its controls,
/// where they sit, and where in the device's input reports each one shows up. Saved as JSON in
/// <c>devices/hid/&lt;id&gt;.json</c>.
/// </summary>
public sealed record HidDeviceDescription(string Id, string Name, int VendorId, int ProductId, IReadOnlyList<HidControl> Controls)
{
    /// <summary>The HID interface to open when the device has several (its top-level usage page); null for any.</summary>
    public int? UsagePage { get; init; }

    /// <summary>The top-level usage within <see cref="UsagePage"/>; null for any.</summary>
    public int? Usage { get; init; }

    public const int CurrentFormat = 1;

    public int Format { get; init; } = CurrentFormat;

    /// <summary>DeckHost's device id for it.</summary>
    [JsonIgnore]
    public string DeviceId => "hid." + Id;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Reads a description; null when the text isn't one.</summary>
    public static HidDeviceDescription? FromJson(string json)
    {
        try
        {
            var description = JsonSerializer.Deserialize<HidDeviceDescription>(json, JsonOptions);
            return description is { Id.Length: > 0, Controls: not null } ? description : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A file-name-safe id from a name ("My pedal" → "my-pedal").</summary>
    public static string MakeId(string name)
    {
        var id = new string([.. name.ToLowerInvariant().Select(static c => char.IsAsciiLetterOrDigit(c) ? c : '-')]).Trim('-');
        while (id.Contains("--", StringComparison.Ordinal))
        {
            id = id.Replace("--", "-", StringComparison.Ordinal);
        }

        return id.Length == 0 ? "device" : id[..Math.Min(id.Length, 40)];
    }

    /// <summary>The controls as DeckHost lays them out (no displays: generic devices only send input).</summary>
    public IReadOnlyList<PhysicalControl> PhysicalControls() =>
    [
        .. Controls.Select(static control => new PhysicalControl(
            control.Id,
            control.Name,
            control.Kind == HidControlKind.Knob ? ControlType.Encoder : ControlType.Key,
            new Rect(control.X, control.Y, control.Width, control.Height),
            control.Kind == HidControlKind.Knob
                ? InputCapabilities.Rotate | (control.Press is null ? InputCapabilities.None : InputCapabilities.Press | InputCapabilities.Release)
                : InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress,
            OutputCapabilities.None,
            HardwareId: control.Input.Describe())),
    ];
}

public enum HidControlKind
{
    Key,
    Knob,
}

/// <summary>
/// One control: <see cref="Input"/> is the key's state or the knob's turning; a knob that can be
/// pushed has its push in <see cref="Press"/>. Position and size are in the editor's layout units
/// (a key is 72).
/// </summary>
public sealed record HidControl(string Id, string Name, HidControlKind Kind, double X, double Y, double Width, double Height, HidInput Input)
{
    public HidInput? Press { get; init; }
}

public enum HidInputKind
{
    /// <summary>Pressed while <c>report[Byte] &amp; Mask</c> is set (or clear when <see cref="HidInput.Invert"/>).</summary>
    Bit,

    /// <summary>Pressed while <c>report[Byte] == Value</c> (or differs when inverted).</summary>
    Value,

    /// <summary>A knob that reports steps: <c>(sbyte)report[Byte]</c> per report, 0 when still.</summary>
    Relative,

    /// <summary>A knob that reports its position 0–255: the change since the last report, wrapping around.</summary>
    Absolute,
}

/// <summary>
/// Where an input shows up in the device's reports. <see cref="Byte"/> counts from the start of the
/// report as read, whose first byte is the report id (0 for devices without numbered reports);
/// <see cref="ReportId"/>, when set, limits it to reports with that id.
/// </summary>
public sealed record HidInput(HidInputKind Kind, int Byte, int Mask = 0, int Value = 0, int? ReportId = null, bool Invert = false)
{
    /// <summary>"byte 3, bit 0x04" and the like, for the editor and diagnostics.</summary>
    public string Describe()
    {
        var where = string.Create(CultureInfo.InvariantCulture, $"byte {Byte}");
        var report = ReportId is { } id ? string.Create(CultureInfo.InvariantCulture, $" of report {id}") : string.Empty;
        return Kind switch
        {
            HidInputKind.Bit => string.Create(CultureInfo.InvariantCulture, $"{where}{report}, bit 0x{Mask:X2}{(Invert ? " (pressed when clear)" : string.Empty)}"),
            HidInputKind.Value => string.Create(CultureInfo.InvariantCulture, $"{where}{report} = {Value}{(Invert ? " (released)" : string.Empty)}"),
            HidInputKind.Relative => $"{where}{report}, steps{(Invert ? ", reversed" : string.Empty)}",
            _ => $"{where}{report}, position{(Invert ? ", reversed" : string.Empty)}",
        };
    }
}
