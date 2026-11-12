using DeckHost.Contracts.Enums;

namespace DeckHost.Contracts.Models;

/// <summary>
/// The inputs a control can produce, by direction: the ids used by the "Run when" trigger and by
/// <see cref="ActionSettingDefinition.PerInput"/> settings.
/// </summary>
public static class ControlInputs
{
    public const string Press = "press";
    public const string Release = "release";
    public const string LongPress = "longpress";
    public const string RotateLeft = "rotate-left";
    public const string RotateRight = "rotate-right";
    public const string SwipeLeft = "swipe-left";
    public const string SwipeRight = "swipe-right";

    /// <summary>The input an event is: its type, and for turning and swiping its direction.</summary>
    public static string? FromEvent(InputCapabilities eventType, int value) => eventType switch
    {
        InputCapabilities.Press => Press,
        InputCapabilities.Release => Release,
        InputCapabilities.LongPress => LongPress,
        InputCapabilities.Rotate when value < 0 => RotateLeft,
        InputCapabilities.Rotate when value > 0 => RotateRight,
        InputCapabilities.Swipe when value < 0 => SwipeLeft,
        InputCapabilities.Swipe when value > 0 => SwipeRight,
        _ => null,
    };

    /// <summary>
    /// The inputs a control with these capabilities can be set up for, in the order the editor
    /// shows them. A release isn't one: it only ends a press.
    /// </summary>
    public static IReadOnlyList<string> Of(InputCapabilities inputs)
    {
        var list = new List<string>();
        if (inputs.HasFlag(InputCapabilities.Press))
        {
            list.Add(Press);
        }

        if (inputs.HasFlag(InputCapabilities.LongPress))
        {
            list.Add(LongPress);
        }

        if (inputs.HasFlag(InputCapabilities.Rotate))
        {
            list.Add(RotateLeft);
            list.Add(RotateRight);
        }

        if (inputs.HasFlag(InputCapabilities.Swipe))
        {
            list.Add(SwipeLeft);
            list.Add(SwipeRight);
        }

        return list;
    }

    /// <summary>How the editor names an input ("When pressed", "When turned left", ...).</summary>
    public static string Describe(string input) => input switch
    {
        Press => "When pressed",
        Release => "When released",
        LongPress => "When held down",
        RotateLeft => "When turned left",
        RotateRight => "When turned right",
        SwipeLeft => "When swiped left",
        SwipeRight => "When swiped right",
        _ => input,
    };

    /// <summary>Where a <see cref="ActionSettingDefinition.PerInput"/> setting's value for one input is stored.</summary>
    public static string SettingKey(string key, string input) => $"{key}@{input}";
}
