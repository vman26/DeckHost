using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DeckHost.App.ViewModels;

/// <summary>
/// Everything the Customize window edits for one control: its text and image, the bound
/// action's settings, Multi Action steps (each with their own settings) and the LED. It never
/// changes the profile itself; <see cref="BuildBinding"/> produces the new binding.
/// </summary>
public partial class ControlCustomizationViewModel : ViewModelBase
{
    private readonly ControlBinding? _original;
    private readonly ActionItemViewModel? _primaryAction;
    private readonly ActionItemViewModel? _displayContentAction;
    private string _displayText;
    private string _imagePath;
    private string _backgroundColor;
    private bool _lightEnabled;
    private string _activeColor;
    private string _idleColor;
    private string _activeDurationText;
    private bool _pulseOnActive;
    private ActionItemViewModel? _selectedStepAction;

    /// <summary>The inputs this control has (used to offer only the triggers it can produce).</summary>
    public DeckHost.Contracts.Enums.InputCapabilities ControlInputs { get; }

    public ControlCustomizationViewModel(
        ControlItemViewModel control,
        ControlBindingViewModel? binding,
        IEnumerable<ActionItemViewModel> availableActions,
        ActionItemViewModel? displayContentAction,
        string? linkedDisplayName = null)
    {
        LinkedDisplayName = control.Control.HasLinkedDisplay ? linkedDisplayName ?? control.Control.DisplayControlId : null;
        _showOnLinkedDisplay = binding?.Model.Appearance.ShowOnLinkedDisplay ?? true;
        ControlId = control.Id;
        ControlInputs = control.Control.Inputs;
        DisplayName = control.DisplayName;
        ControlType = control.TypeLabel;
        SupportsAppearance = control.SupportsAppearance;
        SupportsLight = control.SupportsLight;

        _original = binding?.Model;
        _primaryAction = binding?.PrimaryAction;
        _displayContentAction = displayContentAction;
        _displayText = binding?.AppearanceText ?? string.Empty;
        _imagePath = binding?.AppearanceImagePath ?? string.Empty;
        _backgroundColor = binding?.Model.Appearance.BackgroundColor ?? string.Empty;

        var style = binding?.Model.Appearance.TextStyle;
        _textColor = style?.Color ?? string.Empty;
        _fontFamily = style?.FontFamily ?? string.Empty;
        _fontSizeText = style?.Size?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _bold = style?.Bold ?? false;
        _italic = style?.Italic ?? false;
        _wrap = style?.Wrap ?? true;
        _horizontal = style?.Horizontal?.ToString() ?? AutomaticAlignment;
        _vertical = style?.Vertical?.ToString() ?? AutomaticAlignment;

        if (binding is not null && !binding.IsMultiAction && binding.PrimaryAction.HasSettings)
        {
            ActionSettings = new SettingsEditorViewModel(binding.PrimaryAction.SettingDefinitions, binding.Settings, ControlInputs);
            ActionSettings.Changed += (_, _) => RaiseChanged();
        }

        IsMultiAction = binding?.IsMultiAction == true;
        if (IsMultiAction)
        {
            foreach (var step in binding!.Steps)
            {
                Steps.Add(CreateStep(step.Action, step.Settings));
            }

            AvailableStepActions = [.. availableActions
                .Where(action => action.CanBeMultiActionStep && action.ShowInPalette && action.FitsControl(control.Control))
                .OrderBy(static action => action.PluginName)
                .ThenBy(static action => action.Name)];
            _selectedStepAction = AvailableStepActions.FirstOrDefault();
        }
        else
        {
            AvailableStepActions = [];
        }

        var feedback = binding?.LightFeedback;
        _lightEnabled = feedback is not null;
        feedback ??= new RgbFeedback();
        _activeColor = "#" + feedback.ActiveColor.TrimStart('#');
        _idleColor = "#" + feedback.IdleColor.TrimStart('#');
        _activeDurationText = feedback.ActiveDuration.ToString(CultureInfo.InvariantCulture);
        _pulseOnActive = feedback.PulseOnActive;
    }

    /// <summary>Raised on every edit, for the live preview on the device.</summary>
    public event EventHandler? Changed;

    public string ControlId { get; }

    public string DisplayName { get; }

    public string ControlType { get; }

    public ControlBinding? OriginalBinding => _original;

    // ---------------------------------------------------------------- action

    public bool HasAction => _primaryAction is not null && !_primaryAction.IsDisplayContentAction;

    public string ActionName => _primaryAction?.Name ?? string.Empty;

    public string ActionPluginName => _primaryAction?.PluginName ?? string.Empty;

    public string ActionDescription => _primaryAction?.Description ?? string.Empty;

    public bool HasActionDescription => HasAction && !string.IsNullOrWhiteSpace(_primaryAction?.Description);

    public SettingsEditorViewModel? ActionSettings { get; }

    /// <summary>Whether the action has its own settings page (Stream Deck property inspector).</summary>
    public bool HasSettingsPage => HasAction && !IsMultiAction && _primaryAction?.Action.HasSettingsPage == true;

    private Uri? _settingsPageUri;

    /// <summary>The plugin's settings page, shown in the built-in browser next to the editor.</summary>
    public Uri? SettingsPageUri
    {
        get => _settingsPageUri;
        set
        {
            if (SetProperty(ref _settingsPageUri, value))
            {
                OnPropertyChanged(nameof(ShowSettingsPage));
            }
        }
    }

    public bool ShowSettingsPage => SettingsPageUri is not null;

    [RelayCommand]
    private void OpenSettingsPageInBrowser()
    {
        if (SettingsPageUri is { } uri)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }

    [RelayCommand]
    private void ClearImage() => ImagePath = string.Empty;

    // ---------------------------------------------------------------- text style

    public const string AutomaticAlignment = "Automatic";

    private string _textColor;
    private string _fontFamily;
    private string _fontSizeText;
    private bool _bold;
    private bool _italic;
    private bool _wrap;
    private string _horizontal;
    private string _vertical;

    public IReadOnlyList<string> HorizontalOptions { get; } = [AutomaticAlignment, nameof(TextHorizontalAlignment.Left), nameof(TextHorizontalAlignment.Center), nameof(TextHorizontalAlignment.Right)];

    public IReadOnlyList<string> VerticalOptions { get; } = [AutomaticAlignment, nameof(TextVerticalAlignment.Top), nameof(TextVerticalAlignment.Middle), nameof(TextVerticalAlignment.Bottom)];

    /// <summary>Installed font families (empty = the default font).</summary>
    public IReadOnlyList<string> FontFamilies { get; } = LoadFontFamilies();

    public string TextColor
    {
        get => _textColor;
        set => SetStyle(ref _textColor, value ?? string.Empty);
    }

    public string FontFamily
    {
        get => _fontFamily;
        set => SetStyle(ref _fontFamily, value ?? string.Empty);
    }

    public string FontSizeText
    {
        get => _fontSizeText;
        set => SetStyle(ref _fontSizeText, value ?? string.Empty);
    }

    public bool Bold
    {
        get => _bold;
        set => SetStyle(ref _bold, value);
    }

    public bool Italic
    {
        get => _italic;
        set => SetStyle(ref _italic, value);
    }

    public bool Wrap
    {
        get => _wrap;
        set => SetStyle(ref _wrap, value);
    }

    public string Horizontal
    {
        get => _horizontal;
        set => SetStyle(ref _horizontal, value ?? AutomaticAlignment);
    }

    public string Vertical
    {
        get => _vertical;
        set => SetStyle(ref _vertical, value ?? AutomaticAlignment);
    }

    private void SetStyle<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            RaiseChanged();
        }
    }

    private TextStyle? BuildTextStyle()
    {
        var color = IsHexColor(TextColor) ? "#" + NormalizeColor(TextColor) : null;
        float? size = float.TryParse(FontSizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 6 and <= 200 ? parsed : null;
        TextHorizontalAlignment? horizontal = Enum.TryParse<TextHorizontalAlignment>(Horizontal, out var h) ? h : null;
        TextVerticalAlignment? vertical = Enum.TryParse<TextVerticalAlignment>(Vertical, out var v) ? v : null;
        var family = string.IsNullOrWhiteSpace(FontFamily) ? null : FontFamily.Trim();

        var style = new TextStyle(color, family, size, Bold, Italic, horizontal, vertical, Wrap);
        return style == new TextStyle() ? null : style;
    }

    private static IReadOnlyList<string> LoadFontFamilies()
    {
        try
        {
            return [.. SkiaSharp.SKFontManager.Default.FontFamilies.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException)
        {
            return [];
        }
    }

    public bool HasActionSettings => ActionSettings?.HasFields == true;

    // ---------------------------------------------------------------- multi action

    public bool IsMultiAction { get; }

    public ObservableCollection<MultiActionStepEditorViewModel> Steps { get; } = [];

    public bool HasNoSteps => Steps.Count == 0;

    public IReadOnlyList<ActionItemViewModel> AvailableStepActions { get; }

    public ActionItemViewModel? SelectedStepAction
    {
        get => _selectedStepAction;
        set => SetProperty(ref _selectedStepAction, value);
    }

    [RelayCommand]
    private void AddStep()
    {
        if (SelectedStepAction is null)
        {
            return;
        }

        Steps.Add(CreateStep(SelectedStepAction, SelectedStepAction.DefaultSettings));
        OnStepsChanged();
    }

    internal void RemoveStep(MultiActionStepEditorViewModel step)
    {
        if (Steps.Remove(step))
        {
            OnStepsChanged();
        }
    }

    internal void MoveStep(MultiActionStepEditorViewModel step, int offset)
    {
        var index = Steps.IndexOf(step);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Steps.Count)
        {
            return;
        }

        Steps.Move(index, target);
        OnStepsChanged();
    }

    // ---------------------------------------------------------------- appearance

    private bool _showOnLinkedDisplay;

    public bool SupportsAppearance { get; }

    /// <summary>Name of the display this control borrows (the touch zone above a knob); null when it has its own or none.</summary>
    public string? LinkedDisplayName { get; }

    public bool HasLinkedDisplay => LinkedDisplayName is not null;

    public string ShowOnLinkedDisplayLabel => $"Show this on {LinkedDisplayName}";

    public string LinkedDisplayHint =>
        $"It fills {LinkedDisplayName} while that has no action of its own, and tapping it then works like pressing this control. " +
        $"When {LinkedDisplayName} has its own action, this one gets a strip along the edge.";

    /// <summary>Whether the control's action is drawn on the display it borrows.</summary>
    public bool ShowOnLinkedDisplay
    {
        get => _showOnLinkedDisplay;
        set
        {
            if (SetProperty(ref _showOnLinkedDisplay, value))
            {
                RaiseChanged();
            }
        }
    }

    public string DisplayText
    {
        get => _displayText;
        set
        {
            if (SetProperty(ref _displayText, value))
            {
                RaiseChanged();
            }
        }
    }

    /// <summary>Solid background (#RRGGBB) under the image and text; empty for the profile background.</summary>
    public string BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (SetProperty(ref _backgroundColor, value))
            {
                OnPropertyChanged(nameof(BackgroundColorError));
                OnPropertyChanged(nameof(HasBackgroundColorError));
                RaiseChanged();
            }
        }
    }

    public string? BackgroundColorError =>
        string.IsNullOrWhiteSpace(BackgroundColor) || IsHexColor(BackgroundColor) ? null : "Use #RRGGBB, for example #1D4ED8.";

    public bool HasBackgroundColorError => BackgroundColorError is not null;

    public string ImagePath
    {
        get => _imagePath;
        set
        {
            if (SetProperty(ref _imagePath, value))
            {
                RaiseChanged();
            }
        }
    }

    // ---------------------------------------------------------------- light

    public bool SupportsLight { get; }

    public bool ShowLightEditor => SupportsLight && HasAction;

    public bool ShowLightNeedsAction => SupportsLight && !HasAction;

    public bool LightEnabled
    {
        get => _lightEnabled;
        set
        {
            if (SetProperty(ref _lightEnabled, value))
            {
                RaiseChanged();
            }
        }
    }

    public string ActiveColor
    {
        get => _activeColor;
        set => SetLightField(ref _activeColor, value);
    }

    public string IdleColor
    {
        get => _idleColor;
        set => SetLightField(ref _idleColor, value);
    }

    public string ActiveDurationText
    {
        get => _activeDurationText;
        set => SetLightField(ref _activeDurationText, value);
    }

    public bool PulseOnActive
    {
        get => _pulseOnActive;
        set
        {
            if (SetProperty(ref _pulseOnActive, value))
            {
                RaiseChanged();
            }
        }
    }

    public string? LightError =>
        !IsHexColor(ActiveColor) || !IsHexColor(IdleColor) ? "Colors must look like #RRGGBB, for example #00FF00."
        : !TryParseDuration(out _) ? "Duration must be a whole number of milliseconds from 0 to 10000."
        : null;

    public bool HasLightError => LightError is not null;

    public bool ShowNothingToCustomize => !SupportsAppearance && !HasActionSettings && !IsMultiAction && !SupportsLight;

    // ---------------------------------------------------------------- result

    /// <summary>
    /// The binding as currently edited, or null when the control ends up with nothing on it.
    /// Fields with invalid input keep their previous value.
    /// </summary>
    public ControlBinding? BuildBinding()
    {
        var text = SupportsAppearance && !string.IsNullOrWhiteSpace(DisplayText) ? DisplayText : null;
        var image = SupportsAppearance && !string.IsNullOrWhiteSpace(ImagePath) ? ImagePath.Trim() : null;
        var light = BuildLight();
        var background = SupportsAppearance && IsHexColor(BackgroundColor) ? "#" + NormalizeColor(BackgroundColor) : null;
        var appearance = new ControlAppearance(text, image, light, background, SupportsAppearance ? BuildTextStyle() : null)
        {
            ShowOnLinkedDisplay = ShowOnLinkedDisplay,
        };

        if (_original is null || _primaryAction is null)
        {
            // An empty control that only shows text/an image.
            return (text is null && image is null && background is null) || _displayContentAction is null
                ? null
                : new ControlBinding(ControlId, ProfileActionKind.Single, _displayContentAction.ToActionReference(), appearance, []);
        }

        if (_primaryAction.IsDisplayContentAction && text is null && image is null && background is null)
        {
            return null;
        }

        var action = ActionSettings is null
            ? _original.Action
            : _primaryAction.ToActionReference(ActionSettings.ToSettings());

        IReadOnlyList<MultiActionStep> steps = IsMultiAction
            ? [.. Steps.Select((step, index) => new MultiActionStep(index, step.Action.ToActionReference(step.Settings?.ToSettings())))]
            : _original.Steps;

        return _original with { Action = action, Appearance = appearance, Steps = steps };
    }

    private RgbFeedback? BuildLight()
    {
        if (!SupportsLight)
        {
            return null;
        }

        if (HasLightError)
        {
            return _original?.Appearance.RgbFeedback;
        }

        if (!LightEnabled || !TryParseDuration(out var duration))
        {
            return null;
        }

        return new RgbFeedback(NormalizeColor(ActiveColor), NormalizeColor(IdleColor), duration, PulseOnActive);
    }

    private MultiActionStepEditorViewModel CreateStep(ActionItemViewModel action, ActionSettings settings)
    {
        var step = new MultiActionStepEditorViewModel(this, action, settings);
        step.Changed += (_, _) => RaiseChanged();
        return step;
    }

    private void OnStepsChanged()
    {
        OnPropertyChanged(nameof(HasNoSteps));
        foreach (var step in Steps)
        {
            step.RefreshPosition();
        }

        RaiseChanged();
    }

    private void SetLightField(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            OnPropertyChanged(nameof(LightError));
            OnPropertyChanged(nameof(HasLightError));
            RaiseChanged();
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private bool TryParseDuration(out int duration) =>
        int.TryParse(ActiveDurationText, NumberStyles.None, CultureInfo.InvariantCulture, out duration) && duration <= 10000;

    private static string NormalizeColor(string value) => value.Trim().TrimStart('#').ToUpperInvariant();

    private static bool IsHexColor(string value) => HexColorRegex().IsMatch(value.Trim());

    [GeneratedRegex("^#?[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorRegex();
}

public partial class MultiActionStepEditorViewModel : ViewModelBase
{
    private readonly ControlCustomizationViewModel _owner;

    public MultiActionStepEditorViewModel(ControlCustomizationViewModel owner, ActionItemViewModel action, ActionSettings settings)
    {
        _owner = owner;
        Action = action;
        if (action.HasSettings)
        {
            Settings = new SettingsEditorViewModel(action.SettingDefinitions, settings, owner.ControlInputs);
            Settings.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Changed;

    public ActionItemViewModel Action { get; }

    public SettingsEditorViewModel? Settings { get; }

    public bool HasSettings => Settings is not null;

    public string Title => $"{Position}. {Action.Name}";

    public string PluginName => Action.PluginName;

    public int Position => _owner.Steps.IndexOf(this) + 1;

    public bool CanMoveUp => Position > 1;

    public bool CanMoveDown => Position < _owner.Steps.Count;

    public void RefreshPosition()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    [RelayCommand]
    private void Remove() => _owner.RemoveStep(this);

    [RelayCommand]
    private void MoveUp() => _owner.MoveStep(this, -1);

    [RelayCommand]
    private void MoveDown() => _owner.MoveStep(this, 1);
}
