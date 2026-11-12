using System.Globalization;

namespace DeckHost.Contracts.Models;

public enum ActionSettingKind
{
    Text,
    /// <summary>Hidden while typing; still stored in plain text in the profiles file.</summary>
    Secret,
    Number,
    Toggle,
    Choice,
    FilePath,

    /// <summary>A key combination such as "Ctrl+Shift+M"; the editor can record it from a key press.</summary>
    Hotkey,
}

public sealed record ActionSettingOption(string Value, string Label)
{
    /// <summary>
    /// The input a control needs for this option to make sense (e.g. Rotate for "turned left").
    /// The editor hides the option on controls without that input. None shows it everywhere.
    /// </summary>
    public Enums.InputCapabilities Requires { get; init; }
}

/// <summary>
/// Describes one setting an action needs (an app to launch, a scene name, ...). The editor in
/// the Customize window is generated from these definitions.
/// </summary>
public sealed record ActionSettingDefinition(
    string Key,
    string Label,
    ActionSettingKind Kind = ActionSettingKind.Text,
    string DefaultValue = "",
    string? Description = null,
    IReadOnlyList<ActionSettingOption>? Options = null,
    string? Placeholder = null)
{
    /// <summary>
    /// For a <see cref="ActionSettingKind.Number"/> setting: its range. With a range the editor shows
    /// a slider (and, unless <see cref="NumberRange.ShowInput"/> is off, a number box for exact
    /// values); without one only a number box. A property rather than a constructor parameter, so
    /// plugins built against the earlier constructor keep working.
    /// </summary>
    public NumberRange? Range { get; init; }

    /// <summary>
    /// The setting can have its own value for each input of the control: on a knob, one for
    /// pressing, one for turning left and one for turning right (see <see cref="ControlInputs"/>).
    /// The editor then shows the setting once per input, and the host hands the action the value
    /// for the input that happened, under the plain <see cref="Key"/>, so the action reads it as
    /// usual. Stored as <c>key@input</c> (<see cref="ControlInputs.SettingKey"/>).
    /// </summary>
    public bool PerInput { get; init; }

    /// <summary>
    /// For a <see cref="PerInput"/> setting: defaults that differ per input, by
    /// <see cref="ControlInputs"/> id (e.g. turning left: previous, turning right: next). An input
    /// without one uses the binding's plain value, then <see cref="DefaultValue"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? InputDefaults { get; init; }

    /// <summary>
    /// Shows the setting only while another setting has one of the given values (e.g. a profile
    /// name only for "a specific profile"). For a <see cref="PerInput"/> setting, the other setting
    /// of the same input.
    /// </summary>
    public SettingCondition? ShowWhen { get; init; }
}

/// <summary>A condition on another setting's value: <paramref name="Key"/> is one of <paramref name="Values"/>.</summary>
public sealed record SettingCondition(string Key, params string[] Values)
{
    public bool IsMetBy(string? value) => Values.Contains(value ?? string.Empty, StringComparer.Ordinal);
}

/// <summary>
/// The values a <see cref="ActionSettingKind.Number"/> setting can take: from
/// <paramref name="Minimum"/> to <paramref name="Maximum"/> in steps of <paramref name="Step"/>.
/// Values are stored as invariant-culture text, whole numbers when the step is whole.
/// </summary>
public sealed record NumberRange(double Minimum, double Maximum, double Step = 1)
{
    /// <summary>Shown after the value, for example "%" or "ms".</summary>
    public string? Unit { get; init; }

    /// <summary>Whether a number box is shown next to the slider, to type an exact value.</summary>
    public bool ShowInput { get; init; } = true;

    /// <summary>
    /// For a setting that is empty by default (the action or device then keeps its own default):
    /// that default value. The slider rests there until it is moved, and the editor shows it.
    /// </summary>
    public double? UnsetValue { get; init; }

    public bool IsWholeNumber => Step > 0 && Math.Abs(Step - Math.Round(Step)) < 1e-9;

    /// <summary>Clamps to the range and rounds to the nearest step, counted from <see cref="Minimum"/>.</summary>
    public double Snap(double value)
    {
        var clamped = Math.Clamp(value, Math.Min(Minimum, Maximum), Math.Max(Minimum, Maximum));
        if (Step <= 0)
        {
            return clamped;
        }

        var snapped = Minimum + (Math.Round((clamped - Minimum) / Step, MidpointRounding.AwayFromZero) * Step);
        return Math.Round(Math.Clamp(snapped, Math.Min(Minimum, Maximum), Math.Max(Minimum, Maximum)), 9);
    }

    /// <summary>The value as it is stored: invariant culture, no decimals for whole steps.</summary>
    public string Format(double value) =>
        IsWholeNumber
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.#########", CultureInfo.InvariantCulture);

    /// <summary>The value with its unit, for display ("75 %").</summary>
    public string Describe(double value) => string.IsNullOrEmpty(Unit) ? Format(value) : $"{Format(value)} {Unit}";

    public bool Contains(double value) => value >= Math.Min(Minimum, Maximum) - 1e-9 && value <= Math.Max(Minimum, Maximum) + 1e-9;
}
