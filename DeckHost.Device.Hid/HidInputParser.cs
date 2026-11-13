using DeckHost.Contracts.Enums;

namespace DeckHost.Device.Hid;

/// <summary>An input event from a generic device, as DeckHost sees it.</summary>
public readonly record struct HidInputEvent(string ControlId, InputCapabilities Event, int Value);

/// <summary>
/// Turns a generic device's input reports into DeckHost input events, following its description:
/// keys report Press and Release when their state changes, knobs Rotate by the steps they turned.
/// Keeps the state between reports, so one parser per open device.
/// </summary>
public sealed class HidInputParser(HidDeviceDescription description)
{
    private readonly Dictionary<string, bool> _down = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _positions = new(StringComparer.Ordinal);

    public HidDeviceDescription Description { get; } = description;

    public IReadOnlyList<HidInputEvent> Parse(ReadOnlySpan<byte> report)
    {
        var events = new List<HidInputEvent>();
        foreach (var control in Description.Controls)
        {
            if (control.Kind == HidControlKind.Knob)
            {
                if (Steps(control.Id, control.Input, report) is { } steps and not 0)
                {
                    events.Add(new HidInputEvent(control.Id, InputCapabilities.Rotate, steps));
                }

                if (control.Press is { } press)
                {
                    Track(control.Id, press, report, events);
                }
            }
            else
            {
                Track(control.Id, control.Input, report, events);
            }
        }

        return events;
    }

    /// <summary>Whether the input reads as pressed in this report; null when the report doesn't carry it.</summary>
    public static bool? IsPressed(HidInput input, ReadOnlySpan<byte> report)
    {
        if (!Applies(input, report))
        {
            return null;
        }

        var value = report[input.Byte];
        var pressed = input.Kind switch
        {
            HidInputKind.Bit => (value & input.Mask) != 0,
            HidInputKind.Value => value == input.Value,
            _ => value != 0,
        };
        return pressed != input.Invert;
    }

    private void Track(string controlId, HidInput input, ReadOnlySpan<byte> report, List<HidInputEvent> events)
    {
        if (IsPressed(input, report) is not { } pressed)
        {
            return;
        }

        if (pressed != _down.GetValueOrDefault(controlId))
        {
            _down[controlId] = pressed;
            events.Add(new HidInputEvent(controlId, pressed ? InputCapabilities.Press : InputCapabilities.Release, pressed ? 1 : 0));
        }
    }

    private int? Steps(string controlId, HidInput input, ReadOnlySpan<byte> report)
    {
        if (!Applies(input, report))
        {
            return null;
        }

        int steps;
        if (input.Kind == HidInputKind.Absolute)
        {
            var position = report[input.Byte];
            if (!_positions.TryGetValue(controlId, out var last))
            {
                _positions[controlId] = position;
                return null;
            }

            _positions[controlId] = position;
            steps = (sbyte)(byte)(position - last);
        }
        else
        {
            steps = (sbyte)report[input.Byte];
        }

        return input.Invert ? -steps : steps;
    }

    private static bool Applies(HidInput input, ReadOnlySpan<byte> report) =>
        input.Byte >= 0 && input.Byte < report.Length && (input.ReportId is not { } id || (report.Length > 0 && report[0] == id));
}
