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
    public SettingsEditorViewModel(IReadOnlyList<ActionSettingDefinition> definitions, ActionSettings current)
    {
        foreach (var definition in definitions)
        {
            var value = current.TryGetValue(definition.Key, out var stored) ? stored : definition.DefaultValue;
            SettingFieldViewModel field = definition.Kind switch
            {
                ActionSettingKind.Toggle => new ToggleSettingField(definition, value),
                ActionSettingKind.Choice => new ChoiceSettingField(definition, value),
                ActionSettingKind.FilePath => new FilePathSettingField(definition, value),
                ActionSettingKind.Hotkey => new HotkeySettingField(definition, value),
                ActionSettingKind.Secret => new SecretSettingField(definition, value),
                ActionSettingKind.Number => new NumberSettingField(definition, value),
                _ => new TextSettingField(definition, value),
            };

            field.ValueChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            Fields.Add(field);
        }
    }

    public event EventHandler? Changed;

    public ObservableCollection<SettingFieldViewModel> Fields { get; } = [];

    public bool HasFields => Fields.Count > 0;

    public bool HasErrors => Fields.Any(static settingField => settingField.Error is not null);

    public ActionSettings ToSettings() =>
        new(Fields.Where(static field => field.Error is null && !string.IsNullOrEmpty(field.Value))
            .Select(static field => new KeyValuePair<string, string>(field.Definition.Key, field.Value)));
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

public sealed class NumberSettingField(ActionSettingDefinition definition, string value) : SettingFieldViewModel(definition, value)
{
    public override string? Error =>
        string.IsNullOrWhiteSpace(Value) || int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            ? null
            : "Enter a whole number.";
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
    public ChoiceSettingField(ActionSettingDefinition definition, string value)
        : base(definition, value)
    {
        Options = definition.Options ?? [];
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
