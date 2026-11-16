using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace DeckHost.App.ViewModels;

/// <summary>Editor for an action's settings, generated from its <see cref="ActionSettingDefinition"/>s.</summary>
public sealed class SettingsEditorViewModel : ViewModelBase
{
    /// <param name="controlInputs">
    /// The control's inputs: choice options needing another input are hidden, and settings that
    /// can differ per input (<see cref="ActionSettingDefinition.PerInput"/>) are shown once for each
    /// input when the control has more than one (a knob: pressed, turned left, turned right).
    /// </param>
    public SettingsEditorViewModel(IReadOnlyList<ActionSettingDefinition> definitions, ActionSettings current, InputCapabilities controlInputs = InputCapabilities.All)
    {
        var inputs = ControlInputs.Of(controlInputs);
        var splitByInput = inputs.Count > 1 && definitions.Any(static definition => definition.PerInput);

        _stored = [.. current.Select(static pair => pair.Key)];

        // Values no field shows (a Folder key's folder id, values a plugin stored) are kept as they are.
        _preserved = [.. current.Where(pair => !definitions.Any(definition =>
            pair.Key == definition.Key || pair.Key.StartsWith(definition.Key + "@", StringComparison.Ordinal)))];

        // Settings shared by all inputs first, then a group per input.
        foreach (var definition in definitions.Where(definition => !splitByInput || !definition.PerInput))
        {
            var value = current.TryGetValue(definition.Key, out var stored) ? stored : definition.DefaultValue;
            if (definition.PerInput && inputs.Count == 1)
            {
                // One input: its value is the setting's value (as the host resolves it for that input).
                value = current.ForInput(definitions, inputs[0]).Get(definition.Key, definition.DefaultValue);
            }

            AddField(definition, value, controlInputs, sectionTitle: null);
        }

        if (splitByInput)
        {
            foreach (var input in inputs)
            {
                var resolved = current.ForInput(definitions, input);
                var first = true;
                foreach (var definition in definitions.Where(static definition => definition.PerInput))
                {
                    var inputDefault = definition.InputDefaults is { } defaults && defaults.TryGetValue(input, out var forInput) ? forInput : definition.DefaultValue;
                    var forThisInput = definition with
                    {
                        Key = ControlInputs.SettingKey(definition.Key, input),
                        DefaultValue = inputDefault,
                        ShowWhen = definition.ShowWhen is { } condition
                            ? condition with { Key = ControlInputs.SettingKey(condition.Key, input) }
                            : null,
                    };
                    AddField(forThisInput, resolved.Get(definition.Key, definition.DefaultValue), controlInputs, first ? ControlInputs.Describe(input) : null);
                    first = false;
                }
            }
        }

        foreach (var field in Fields)
        {
            if (field.Definition.ShowWhen is { } condition &&
                Fields.FirstOrDefault(candidate => candidate.Definition.Key == condition.Key) is { } source)
            {
                field.ShowWhen(source, condition);
            }
        }
    }

    private void AddField(ActionSettingDefinition definition, string value, InputCapabilities controlInputs, string? sectionTitle)
    {
        SettingFieldViewModel field = definition.Kind switch
        {
            ActionSettingKind.Toggle => new ToggleSettingField(definition, value),
            ActionSettingKind.Choice => new ChoiceSettingField(definition, value, controlInputs),
            ActionSettingKind.FilePath => new FilePathSettingField(definition, value),
            ActionSettingKind.Hotkey => new HotkeySettingField(definition, value),
            ActionSettingKind.Secret => new SecretSettingField(definition, value),
            ActionSettingKind.Number => new NumberSettingField(definition, value),
            _ => new TextSettingField(definition, value),
        };

        field.SectionTitle = sectionTitle;
        field.ValueChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        Fields.Add(field);
    }

    public event EventHandler? Changed;

    public ObservableCollection<SettingFieldViewModel> Fields { get; } = [];

    public bool HasFields => Fields.Count > 0;

    public bool HasErrors => Fields.Any(static settingField => settingField.Error is not null);

    /// <summary>
    /// The values to store: what was stored before or was changed. A field still on its default that
    /// was never stored stays out, so opening the editor doesn't write defaults into the binding.
    /// </summary>
    public ActionSettings ToSettings() =>
        new(_preserved.Concat(Fields.Where(field => field.Error is null && !string.IsNullOrEmpty(field.Value) &&
                (_stored.Contains(field.Definition.Key) || !string.Equals(field.Value, field.Definition.DefaultValue, StringComparison.Ordinal)))
            .Select(static field => new KeyValuePair<string, string>(field.Definition.Key, field.Value))));

    private readonly IReadOnlyList<KeyValuePair<string, string>> _preserved;
    private readonly HashSet<string> _stored;
}

public abstract class SettingFieldViewModel(ActionSettingDefinition definition, string value) : ViewModelBase
{
    private string _value = value;

    public event EventHandler? ValueChanged;

    public ActionSettingDefinition Definition { get; } = definition;

    public string Label => Definition.Label;

    public string? Description => Definition.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public string Placeholder => Definition.Placeholder ?? string.Empty;

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value ?? string.Empty))
            {
                OnValueSet();
                OnPropertyChanged(nameof(Error));
                OnPropertyChanged(nameof(HasError));
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public virtual string? Error => null;

    public bool HasError => Error is not null;

    /// <summary>A heading shown above the field: the input a group of per-input settings is for.</summary>
    public string? SectionTitle { get; set; }

    public bool HasSectionTitle => !string.IsNullOrEmpty(SectionTitle);

    /// <summary>False while the setting doesn't apply (<see cref="ActionSettingDefinition.ShowWhen"/>).</summary>
    public bool IsVisible { get; private set; } = true;

    internal void ShowWhen(SettingFieldViewModel source, SettingCondition condition)
    {
        void Update()
        {
            var visible = condition.IsMetBy(source.Value);
            if (visible != IsVisible)
            {
                IsVisible = visible;
                OnPropertyChanged(nameof(IsVisible));
            }
        }

        source.ValueChanged += (_, _) => Update();
        Update();
    }

    protected virtual void OnValueSet()
    {
    }
}

public sealed class TextSettingField(ActionSettingDefinition definition, string value) : SettingFieldViewModel(definition, value);

public sealed class SecretSettingField(ActionSettingDefinition definition, string value) : SettingFieldViewModel(definition, value);

public sealed class FilePathSettingField(ActionSettingDefinition definition, string value) : SettingFieldViewModel(definition, value);

/// <summary>A key combination: typed, or recorded by pressing it (see the Customize window).</summary>
public sealed class HotkeySettingField(ActionSettingDefinition definition, string value) : SettingFieldViewModel(definition, value)
{
    private bool _isRecording;
    private string _recordingPreview = string.Empty;

    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (SetProperty(ref _isRecording, value))
            {
                RecordingPreview = string.Empty;
                OnPropertyChanged(nameof(RecordButtonText));
                OnPropertyChanged(nameof(ShowRecordingHint));
            }
        }
    }

    /// <summary>Modifiers held so far while recording ("Ctrl+Shift+…").</summary>
    public string RecordingPreview
    {
        get => _recordingPreview;
        set => SetProperty(ref _recordingPreview, value);
    }

    public string RecordButtonText => IsRecording ? "Stop" : "Record";

    public bool ShowRecordingHint => IsRecording;

    public override string? Error =>
        string.IsNullOrWhiteSpace(Value) || Platform.Hotkey.TryParse(Value, out _, out _) ? null : InvalidMessage();

    private string InvalidMessage()
    {
        Platform.Hotkey.TryParse(Value, out _, out var error);
        return error;
    }
}

/// <summary>
/// A number. With a <see cref="NumberRange"/> it is edited with a slider and (optionally) a number
/// box next to it; without one only with a number box. An empty value means "the default".
/// </summary>
public sealed partial class NumberSettingField(ActionSettingDefinition definition, string value) : SettingFieldViewModel(definition, value)
{
    /// <summary>Used for the number box when the setting has no range: whole numbers, unbounded.</summary>
    private static readonly NumberRange Unbounded = new(-1_000_000_000, 1_000_000_000);

    public NumberRange? Range => Definition.Range;

    private NumberRange Effective => Range ?? Unbounded;

    public bool HasSlider => Range is not null;

    /// <summary>Number box next to the slider, or alone when there is no slider.</summary>
    public bool ShowInput => Range is null || Range.ShowInput;

    public bool ShowSliderWithInput => HasSlider && ShowInput;

    public bool ShowSliderOnly => HasSlider && !ShowInput;

    public bool ShowInputOnly => !HasSlider;

    public double Minimum => Effective.Minimum;

    public double Maximum => Effective.Maximum;

    public double Step => Effective.Step > 0 ? Effective.Step : 1;

    public double LargeStep => HasSlider ? Math.Max(Step, Effective.Snap(Minimum + ((Maximum - Minimum) / 10)) - Minimum) : Step * 10;

    public decimal InputMinimum => (decimal)Minimum;

    public decimal InputMaximum => (decimal)Maximum;

    public decimal InputIncrement => (decimal)Step;

    public string FormatString => Effective.IsWholeNumber ? "0" : "0.#########";

    /// <summary>True while the setting is empty, so the action (or device) uses its own default.</summary>
    public bool IsUnset => string.IsNullOrWhiteSpace(Value);

    /// <summary>What the slider shows while the setting is empty.</summary>
    private double RestingValue =>
        Effective.Snap(Range?.UnsetValue ?? (TryParse(Definition.DefaultValue, out var fallback) ? fallback : Minimum));

    /// <summary>The slider position: the value, or where an empty setting rests.</summary>
    public double SliderValue
    {
        get => TryParse(Value, out var number) ? Effective.Snap(number) : RestingValue;
        set
        {
            if (!double.IsFinite(value))
            {
                return;
            }

            var snapped = Effective.Snap(value);

            // The slider reports its first position too; that must not turn "default" into a value.
            if (IsUnset && Math.Abs(snapped - RestingValue) < Step / 2)
            {
                return;
            }

            Value = Effective.Format(snapped);
        }
    }

    /// <summary>The number box's value; empty when the setting is empty.</summary>
    public decimal? NumberValue
    {
        get => TryParse(Value, out var number) ? (decimal)number : null;
        set => Value = value is null ? string.Empty : Effective.Format(Range is { } range ? range.Snap((double)value.Value) : (double)value.Value);
    }

    /// <summary>The value with its unit, shown next to a slider without a number box.</summary>
    public string ValueText => IsUnset
        ? Range?.UnsetValue is { } unset ? $"Default ({Effective.Describe(unset)})" : "Default"
        : TryParse(Value, out var number) ? Effective.Describe(number) : Value;

    /// <summary>Above the slider: the value when there is no number box, and "Default (…)" while unset.</summary>
    public bool ShowValueText => ShowSliderOnly || (HasSlider && IsUnset);

    /// <summary>Placeholder of the empty number box: the value used meanwhile, if known.</summary>
    public string InputPlaceholder => Range?.UnsetValue is { } unset
        ? Effective.Format(unset)
        : string.IsNullOrEmpty(Placeholder) ? "Default" : Placeholder;

    public string UnitText => Range?.Unit ?? string.Empty;

    public bool ShowUnit => ShowInput && !string.IsNullOrEmpty(UnitText);

    public bool CanReset => !string.Equals(Value, Definition.DefaultValue, StringComparison.Ordinal);

    public string ResetToolTip => string.IsNullOrEmpty(Definition.DefaultValue)
        ? "Back to the default"
        : $"Back to the default ({Effective.Describe(TryParse(Definition.DefaultValue, out var fallback) ? fallback : 0)})";

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Reset() => Value = Definition.DefaultValue;

    public override string? Error
    {
        get
        {
            if (IsUnset)
            {
                return null;
            }

            if (!TryParse(Value, out var number))
            {
                return Effective.IsWholeNumber ? "Enter a whole number." : "Enter a number.";
            }

            if (Effective.IsWholeNumber && Math.Abs(number - Math.Round(number)) > 1e-9)
            {
                return "Enter a whole number.";
            }

            return Range is { } range && !range.Contains(number)
                ? $"Use a number from {range.Format(range.Minimum)} to {range.Format(range.Maximum)}."
                : null;
        }
    }

    protected override void OnValueSet()
    {
        OnPropertyChanged(nameof(SliderValue));
        OnPropertyChanged(nameof(NumberValue));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(IsUnset));
        OnPropertyChanged(nameof(ShowValueText));
        OnPropertyChanged(nameof(CanReset));
    }

    private static bool TryParse(string? text, out double number) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
}

public sealed class ToggleSettingField(ActionSettingDefinition definition, string value) : SettingFieldViewModel(definition, value)
{
    public bool IsChecked
    {
        get => bool.TryParse(Value, out var isChecked) && isChecked;
        set => Value = value ? "true" : "false";
    }

    protected override void OnValueSet() => OnPropertyChanged(nameof(IsChecked));
}

public sealed class ChoiceSettingField : SettingFieldViewModel
{
    public ChoiceSettingField(ActionSettingDefinition definition, string value, InputCapabilities controlInputs = InputCapabilities.All)
        : base(definition, value)
    {
        Options = (definition.Options ?? [])
            .Where(option => option.Requires == InputCapabilities.None || (controlInputs & option.Requires) != 0)
            .ToList();

        // A stored choice this control can't produce (e.g. "press" on the swipe bar) falls back to the first option.
        if (Options.Count > 0 && Options.All(option => option.Value != Value))
        {
            Value = Options[0].Value;
        }
    }

    public IReadOnlyList<ActionSettingOption> Options { get; }

    public ActionSettingOption? SelectedOption
    {
        get => Options.FirstOrDefault(option => option.Value == Value) ?? Options.FirstOrDefault();
        set
        {
            if (value is not null)
            {
                Value = value.Value;
            }
        }
    }

    protected override void OnValueSet() => OnPropertyChanged(nameof(SelectedOption));
}
