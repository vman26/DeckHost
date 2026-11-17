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
    private int? _dropIndex;
    private readonly ActionItemViewModel? _primaryAction;
    private readonly ActionItemViewModel? _displayContentAction;
    private string _displayText;
    private string _imagePath;
    private string _iconPath;
    private string _backgroundColor;
    private string _backgroundMode;
    private string _tileFrom;
    private string _tileTo;
    private double _tileCorner;
    private bool _lightEnabled;
    private string _activeColor;
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
        _iconPath = binding?.Model.Appearance.IconPath ?? string.Empty;
        _backgroundColor = binding?.Model.Appearance.BackgroundColor ?? string.Empty;
        var tile = binding?.Model.Appearance.Tile;
        _tileFrom = tile?.From ?? string.Empty;
        _tileTo = tile?.To ?? string.Empty;
        _tileCorner = tile?.CornerPercent ?? TileStyle.DefaultCornerPercent;
        _backgroundMode = !string.IsNullOrWhiteSpace(_backgroundColor) ? BackgroundModeColor
            : binding?.Model.Appearance.ShowTile == true ? BackgroundModeTile
            : BackgroundModeTransparent;

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
        _kind = binding?.Model.ActionKind ?? ProfileActionKind.Single;
        StepGroupOptions = [.. StepGroups.For(_kind).Select(group => new ActionSettingOption(group, GroupLabel(group)))];
        if (IsMultiAction)
        {
            // The step view models are in the order of the model's steps.
            foreach (var (step, model) in binding!.Steps.Zip(binding.Model.Steps.OrderBy(static step => step.Order)))
            {
                Steps.Add(CreateStep(step.Action, step.Settings, StepGroups.Of(model, _kind), model.DelayMs));
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
        _activeDurationText = feedback.ActiveDuration.ToString(CultureInfo.InvariantCulture);
        _pulseOnActive = feedback.PulseOnActive;
        IdleLight = new LightStyleEditorViewModel(feedback.IdleStyle);
        IdleLight.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(LightError));
            OnPropertyChanged(nameof(HasLightError));
            RaiseChanged();
        };
    }

    /// <summary>The control's light while idle: effect, colors, custom steps.</summary>
    public LightStyleEditorViewModel IdleLight { get; }

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

    [RelayCommand]
    private void ClearIcon() => IconPath = string.Empty;

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
    private static readonly Lazy<IReadOnlyList<string>> Fonts = new(LoadFontFamilies);

    /// <summary>The installed fonts (read once; the editor is created for every selected control).</summary>
    public IReadOnlyList<string> FontFamilies => Fonts.Value;

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

    /// <summary>Where a dragged action or step would land (null: no drag over the steps).</summary>
    public int? DropIndex
    {
        get => _dropIndex;
        set
        {
            if (_dropIndex == value)
            {
                return;
            }

            _dropIndex = value;
            for (var index = 0; index < Steps.Count; index++)
            {
                Steps[index].IsDropBefore = value == index;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDropAtEnd));
        }
    }

    public bool IsDropAtEnd => _dropIndex is { } index && index >= Steps.Count;

    /// <summary>Whether <paramref name="action"/> can be a step here.</summary>
    public bool CanInsertStep(Contracts.Interfaces.IDeckAction action) =>
        IsMultiAction && AvailableStepActions.Any(item => ReferenceEquals(item.Action, action));

    /// <summary>Adds <paramref name="action"/> as a step at <paramref name="index"/> (dropped from the action list).</summary>
    public bool InsertStep(Contracts.Interfaces.IDeckAction action, int index)
    {
        if (!IsMultiAction || AvailableStepActions.FirstOrDefault(item => ReferenceEquals(item.Action, action)) is not { } item)
        {
            return false;
        }

        // A dropped step joins the set of the step it lands next to.
        var at = Math.Clamp(index, 0, Steps.Count);
        var group = (at > 0 ? Steps[at - 1] : Steps.FirstOrDefault())?.Group?.Value ?? StepGroupOptions.FirstOrDefault()?.Value;
        Steps.Insert(at, CreateStep(item, item.DefaultSettings, group, 0));
        OnStepsChanged();
        return true;
    }

    /// <summary>Moves the step at <paramref name="from"/> so it lands before the step now at <paramref name="to"/> (dragged).</summary>
    public void MoveStepTo(int from, int to)
    {
        if (from < 0 || from >= Steps.Count)
        {
            return;
        }

        // Dropping below itself counts the gap it leaves.
        var target = Math.Clamp(to > from ? to - 1 : to, 0, Steps.Count - 1);
        if (target == from)
        {
            return;
        }

        Steps.Move(from, target);
        OnStepsChanged();
    }

    /// <summary>Put the control back as it was when the editor opened.</summary>
    public event EventHandler? RevertRequested;

    /// <summary>Remove the control's binding.</summary>
    public event EventHandler? RemoveRequested;

    public bool HasAssignment => _original is not null;

    /// <summary>The folder a Folder key opens (null for other actions).</summary>
    public string? FolderId =>
        _original?.Action.ActionId == SystemPlugins.FolderAction.ActionId &&
        _original.Action.EffectiveSettings.TryGetValue(SystemPlugins.FolderAction.FolderIdKey, out var id) ? id : null;

    public bool IsFolder => FolderId is not null;

    /// <summary>Show the folder in the editor (its keys can then be set up).</summary>
    public event EventHandler<string>? OpenFolderRequested;

    /// <summary>A visual workflow: its editor opens as a page.</summary>
    public bool IsWorkflow => _original?.ActionKind == ProfileActionKind.Workflow;

    /// <summary>Steps or a plain action that can become a workflow doing the same.</summary>
    public bool CanConvertToWorkflow => _original is not null && !IsWorkflow && _primaryAction is { IsDisplayContentAction: false } &&
                                        (IsMultiAction || _primaryAction.CanBeMultiActionStep);

    /// <summary>The workflow in a line: what starts it and how many nodes it has.</summary>
    public string WorkflowSummary
    {
        get
        {
            if (_original?.Workflow is not { } workflow)
            {
                return string.Empty;
            }

            var triggers = DeckHost.Core.Workflows.WorkflowEngine.TriggersOf(workflow).Select(WorkflowTriggers.Describe).ToList();
            var actions = workflow.Nodes.Where(static node => node.Kind == WorkflowNodeKinds.Action)
                .Select(static node => node.Action?.ActionName).OfType<string>().Distinct().ToList();
            return (string.IsNullOrWhiteSpace(workflow.Name) ? string.Empty : $"{workflow.Name}. ") +
                   (triggers.Count == 0 ? "Nothing starts it yet." : $"Starts when {string.Join(" / ", triggers).ToLowerInvariant()}.") +
                   (actions.Count == 0 ? " No actions yet." : $" Runs {string.Join(", ", actions.Take(6))}{(actions.Count > 6 ? ", …" : string.Empty)}.") +
                   $" {workflow.Nodes.Count} node{(workflow.Nodes.Count == 1 ? string.Empty : "s")}.";
        }
    }

    public event EventHandler? OpenWorkflowRequested;

    public event EventHandler? ConvertToWorkflowRequested;

    [RelayCommand]
    private void OpenWorkflow() => OpenWorkflowRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ConvertToWorkflow() => ConvertToWorkflowRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenFolder()
    {
        if (FolderId is { } id)
        {
            OpenFolderRequested?.Invoke(this, id);
        }
    }

    [RelayCommand]
    private void Revert() => RevertRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void AddStep()
    {
        if (SelectedStepAction is null)
        {
            return;
        }

        Steps.Add(CreateStep(SelectedStepAction, SelectedStepAction.DefaultSettings, StepGroupOptions.FirstOrDefault()?.Value, 0));
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

    /// <summary>
    /// Background (#RRGGBB, or #AARRGGBB below full opacity) under the image and text, in place of
    /// the action's tile; empty for the action's own look. Transparent shows the profile background.
    /// </summary>
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

    public const string BackgroundModeTransparent = "transparent";
    public const string BackgroundModeTile = "tile";
    public const string BackgroundModeColor = "color";

    /// <summary>The control's background: transparent (the default; the profile background shows), the action's tile, or a color.</summary>
    public IReadOnlyList<ActionSettingOption> BackgroundModes { get; } =
    [
        new(BackgroundModeTransparent, "Transparent (profile background)"),
        new(BackgroundModeTile, "The action's colored tile"),
        new(BackgroundModeColor, "A color of my own"),
    ];

    public ActionSettingOption SelectedBackgroundMode
    {
        get => BackgroundModes.First(mode => mode.Value == _backgroundMode);
        set
        {
            if (value is null || value.Value == _backgroundMode)
            {
                return;
            }

            _backgroundMode = value.Value;
            if (_backgroundMode == BackgroundModeColor)
            {
                if (string.IsNullOrWhiteSpace(BackgroundColor))
                {
                    BackgroundColor = "#1E293B";
                }
            }
            else
            {
                BackgroundColor = string.Empty;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowBackgroundColor));
            OnPropertyChanged(nameof(ShowTileSettings));
            RaiseChanged();
        }
    }

    public bool ShowBackgroundColor => _backgroundMode == BackgroundModeColor;

    public bool ShowTileSettings => _backgroundMode == BackgroundModeTile;

    /// <summary>The tile's (first) color; empty for the action's own colors.</summary>
    public string TileFrom
    {
        get => _tileFrom;
        set
        {
            if (SetProperty(ref _tileFrom, value ?? string.Empty))
            {
                RaiseChanged();
            }
        }
    }

    /// <summary>The tile's second color (a gradient to it); empty for one color (or the action's gradient).</summary>
    public string TileTo
    {
        get => _tileTo;
        set
        {
            if (SetProperty(ref _tileTo, value ?? string.Empty))
            {
                RaiseChanged();
            }
        }
    }

    /// <summary>Corner rounding in percent of the key's width: 0 square, 50 round.</summary>
    public double TileCorner
    {
        get => _tileCorner;
        set
        {
            var snapped = Math.Round(Math.Clamp(value, 0, 50));
            if (SetProperty(ref _tileCorner, snapped))
            {
                OnPropertyChanged(nameof(TileCornerText));
                RaiseChanged();
            }
        }
    }

    public string TileCornerText => _tileCorner switch
    {
        0 => "Square",
        >= 50 => "Round",
        _ => $"{_tileCorner:0} %",
    };

    [RelayCommand]
    private void ResetTile()
    {
        TileFrom = string.Empty;
        TileTo = string.Empty;
        TileCorner = TileStyle.DefaultCornerPercent;
    }

    public string? BackgroundColorError =>
        string.IsNullOrWhiteSpace(BackgroundColor) || IsHexColor(BackgroundColor, allowAlpha: true) ? null : "Use #RRGGBB (or #AARRGGBB with opacity), for example #1D4ED8.";

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

    /// <summary>An icon from the icon library on the action's tile, in place of its own icon; empty for the action's own.</summary>
    public string IconPath
    {
        get => _iconPath;
        set
        {
            if (SetProperty(ref _iconPath, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasIcon));
                OnPropertyChanged(nameof(IconName));
                RaiseChanged();
            }
        }
    }

    public bool HasIcon => !string.IsNullOrWhiteSpace(IconPath);

    /// <summary>The icon's file name, shown next to the Choose button.</summary>
    public string IconName => HasIcon ? System.IO.Path.GetFileNameWithoutExtension(IconPath) : "The action's own icon";

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
        get => IdleLight.Color;
        set => IdleLight.Color = value;
    }

    public string ActiveDurationText
    {
        get => _activeDurationText;
        set => SetLightField(ref _activeDurationText, value);
    }

    /// <summary>The idle effects for this control's own light.</summary>
    public IReadOnlyList<ActionSettingOption> IdleEffects => IdleLight.Effects;

    public ActionSettingOption SelectedIdleEffect
    {
        get => IdleLight.SelectedEffect;
        set => IdleLight.SelectedEffect = value;
    }

    /// <summary>Seconds per cycle of the idle effect.</summary>
    public double EffectPeriod
    {
        get => IdleLight.PeriodSeconds;
        set => IdleLight.PeriodSeconds = value;
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
        !IsHexColor(ActiveColor) || (IdleLight.ShowColor && !IsHexColor(IdleColor)) ? "Colors must look like #RRGGBB, for example #00FF00."
        : !TryParseDuration(out _) ? "Duration must be a whole number of milliseconds from 0 to 10000."
        : null;

    public bool HasLightError => LightError is not null;

    public bool ShowNothingToCustomize => !SupportsAppearance && !HasActionSettings && !IsMultiAction && !IsWorkflow && !SupportsLight;

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
        var background = SupportsAppearance && IsHexColor(BackgroundColor, allowAlpha: true) ? "#" + NormalizeColor(BackgroundColor) : null;
        var icon = SupportsAppearance && !string.IsNullOrWhiteSpace(IconPath) ? IconPath.Trim() : null;
        var appearance = new ControlAppearance(text, image, light, background, SupportsAppearance ? BuildTextStyle() : null)
        {
            ShowOnLinkedDisplay = ShowOnLinkedDisplay,
            IconPath = icon,
            ShowTile = SupportsAppearance && _backgroundMode == BackgroundModeTile,
            Tile = SupportsAppearance && _backgroundMode == BackgroundModeTile ? BuildTileStyle() : null,
        };

        if (_original is null || _primaryAction is null)
        {
            // An empty control that only shows text, an image or an icon.
            return (text is null && image is null && background is null && icon is null) || _displayContentAction is null
                ? null
                : new ControlBinding(ControlId, ProfileActionKind.Single, _displayContentAction.ToActionReference(), appearance, []);
        }

        if (_primaryAction.IsDisplayContentAction && text is null && image is null && background is null && icon is null)
        {
            return null;
        }

        var action = ActionSettings is null
            ? _original.Action
            : _primaryAction.ToActionReference(ActionSettings.ToSettings());

        IReadOnlyList<MultiActionStep> steps = IsMultiAction
            ? [.. Steps.Select((step, index) => new MultiActionStep(index, step.Action.ToActionReference(step.Settings?.ToSettings()))
            {
                Group = step.Group?.Value,
                DelayMs = step.DelayMilliseconds,
            })]
            : _original.Steps;

        return _original with { Action = action, Appearance = appearance, Steps = steps };
    }

    /// <summary>The tile's look; null when nothing differs from the action's own.</summary>
    private TileStyle? BuildTileStyle()
    {
        var from = IsHexColor(TileFrom, allowAlpha: true) ? "#" + NormalizeColor(TileFrom) : null;
        var to = IsHexColor(TileTo, allowAlpha: true) ? "#" + NormalizeColor(TileTo) : null;
        double? corner = Math.Abs(TileCorner - TileStyle.DefaultCornerPercent) < 0.5 ? null : TileCorner;
        return from is null && to is null && corner is null ? null : new TileStyle(from, to, corner);
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

        // The simple fields stay filled, for older DeckHost versions; IdleLight carries everything.
        var idle = IdleLight.ToStyle() ?? new LightStyle(Effect: LightEffects.Off);
        var simple = idle.Colors is null && idle.Steps is null && idle.Spread is null;
        return new RgbFeedback(NormalizeColor(ActiveColor), idle.Color, duration, PulseOnActive)
        {
            IdleEffect = idle.Effect == LightEffects.Solid ? null : idle.Effect,
            EffectPeriodSeconds = LightEffects.IsAnimated(idle.Effect) ? idle.PeriodSeconds : null,
            IdleLight = simple ? null : idle,
        };
    }

    private readonly ProfileActionKind _kind;

    /// <summary>The sets a step can be in: First/Second press (Multi Action Switch), Press/Double press/Hold (Key Logic).</summary>
    public IReadOnlyList<ActionSettingOption> StepGroupOptions { get; }

    public bool HasStepGroups => StepGroupOptions.Count > 0;

    /// <summary>What the steps card is called for this kind of binding.</summary>
    public string StepsTitle => _kind switch
    {
        ProfileActionKind.MultiActionSwitch => "STEPS (EACH PRESS RUNS THE NEXT SET)",
        ProfileActionKind.KeyLogic => "STEPS (FOR A PRESS, A DOUBLE PRESS OR HOLDING)",
        _ => "STEPS (RUN IN THIS ORDER)",
    };

    private static string GroupLabel(string group) => group switch
    {
        StepGroups.First => "First press",
        StepGroups.Second => "Second press",
        StepGroups.Press => "Press",
        StepGroups.DoublePress => "Double press",
        StepGroups.Hold => "Hold",
        _ => group,
    };

    private MultiActionStepEditorViewModel CreateStep(ActionItemViewModel action, ActionSettings settings, string? group, int delayMs)
    {
        var step = new MultiActionStepEditorViewModel(this, action, settings)
        {
            Group = StepGroupOptions.FirstOrDefault(option => option.Value == group) ?? StepGroupOptions.FirstOrDefault(),
            DelayMilliseconds = delayMs,
        };
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

    private static bool IsHexColor(string value, bool allowAlpha = false) =>
        (allowAlpha ? HexColorWithAlphaRegex() : HexColorRegex()).IsMatch(value.Trim());

    [GeneratedRegex("^#?[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorRegex();

    [GeneratedRegex("^#?([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex HexColorWithAlphaRegex();
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

    private ActionSettingOption? _group;
    private int _delayMilliseconds;

    /// <summary>The set the step is in (Multi Action Switch, Key Logic); null for a Multi Action.</summary>
    public ActionSettingOption? Group
    {
        get => _group;
        set
        {
            if (value is not null && SetProperty(ref _group, value))
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public IReadOnlyList<ActionSettingOption> GroupOptions => _owner.StepGroupOptions;

    public bool HasGroup => _owner.HasStepGroups;

    /// <summary>How long to wait before this step, in milliseconds.</summary>
    public int DelayMilliseconds
    {
        get => _delayMilliseconds;
        set
        {
            if (SetProperty(ref _delayMilliseconds, Math.Clamp(value, 0, StepGroups.MaxDelayMs)))
            {
                OnPropertyChanged(nameof(Delay));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>The wait for the number box (decimal, as NumericUpDown wants).</summary>
    public decimal? Delay
    {
        get => _delayMilliseconds;
        set => DelayMilliseconds = (int)Math.Round(value ?? 0);
    }

    public ActionItemViewModel Action { get; }

    public SettingsEditorViewModel? Settings { get; }

    public bool HasSettings => Settings is not null;

    public string Title => $"{Position}. {Action.Name}";

    public string PluginName => Action.PluginName;

    public int Position => _owner.Steps.IndexOf(this) + 1;

    public bool CanMoveUp => Position > 1;

    public bool CanMoveDown => Position < _owner.Steps.Count;

    private bool _isDropBefore;

    /// <summary>A dragged action or step would land just above this step.</summary>
    public bool IsDropBefore
    {
        get => _isDropBefore;
        set => SetProperty(ref _isDropBefore, value);
    }

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
