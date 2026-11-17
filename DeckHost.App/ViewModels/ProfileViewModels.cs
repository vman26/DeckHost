using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DeckHost.App.ViewModels;

public class ProfileItemViewModel : ViewModelBase
{
    private readonly Dictionary<string, ControlBindingViewModel> _bindingsByControlId = new(StringComparer.OrdinalIgnoreCase);

    // Bindings whose plugin/action isn't loaded right now. Kept as-is so saving doesn't drop them.
    private readonly Dictionary<string, ControlBinding> _unresolvedBindings = new(StringComparer.OrdinalIgnoreCase);
    private string _name;

    public ProfileItemViewModel(string id, string name, string deviceId, IEnumerable<string>? autoSwitchApps = null, ProfileBackground? background = null, bool? overridesBackground = null)
    {
        Id = id;
        _name = name;
        DeviceId = deviceId;
        BackgroundColor = background?.Color ?? string.Empty;
        BackgroundImagePath = background?.ImagePath ?? string.Empty;
        BackgroundLayers = background?.Layers ?? [];
        OverridesBackground = overridesBackground ?? background is { IsEmpty: false };
        AutoSwitchApps = new ObservableCollection<string>(autoSwitchApps ?? []);
        AutoSwitchApps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AutoSwitchSummary));
            OnPropertyChanged(nameof(HasAutoSwitchApps));
        };
    }

    public string Id { get; }

    /// <summary>For a page or folder: the layout it belongs to (see <see cref="DeviceProfile.ParentId"/>).</summary>
    public string? ParentId { get; init; }

    /// <summary>Null for a profile, else <see cref="ProfileKinds.Page"/> or <see cref="ProfileKinds.Folder"/>.</summary>
    public string? Kind { get; init; }

    public bool IsRoot => Kind is null;

    public bool IsFolder => Kind == ProfileKinds.Folder;

    /// <summary>The profile a page or folder belongs to; set when it's added to that profile's <see cref="Layouts"/>.</summary>
    public ProfileItemViewModel? Owner { get; set; }

    /// <summary>The profile this layout belongs to (a profile is its own).</summary>
    public ProfileItemViewModel RootProfile => Owner ?? this;

    /// <summary>A profile's other pages and its folders (nested folders too), in order.</summary>
    public ObservableCollection<ProfileItemViewModel> Layouts { get; } = [];

    /// <summary>The profile's pages: itself first, then its other pages.</summary>
    public IReadOnlyList<ProfileItemViewModel> PageLayouts => [RootProfile, .. RootProfile.Layouts.Where(static layout => layout.Kind == ProfileKinds.Page)];

    /// <summary>Every layout of the profile: itself, its pages and its folders.</summary>
    public IEnumerable<ProfileItemViewModel> AllLayouts => [RootProfile, .. RootProfile.Layouts];

    /// <summary>The layout this one belongs to, or null for a profile.</summary>
    public ProfileItemViewModel? ParentLayout => ParentId is null ? null : AllLayouts.FirstOrDefault(layout => layout.Id == ParentId);

    /// <summary>The page this layout is on (a folder's page is the page its outermost folder hangs from).</summary>
    public ProfileItemViewModel PageLayout
    {
        get
        {
            var current = this;
            for (var depth = 0; depth < 64 && current.IsFolder && current.ParentLayout is { } parent; depth++)
            {
                current = parent;
            }

            return current;
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? _name : value.Trim()))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public string DeviceId { get; }

    /// <summary>Profile background color (#RRGGBB) behind every control.</summary>
    public string BackgroundColor { get; set; }

    /// <summary>Profile background image or GIF; spans all controls.</summary>
    public string BackgroundImagePath { get; set; }

    /// <summary>Whether the profile uses its own background instead of the default; off keeps the stored background but doesn't use it.</summary>
    public bool OverridesBackground { get; set; }

    /// <summary>More background layers over the color and image (gradients, pictures, layers that follow a variable).</summary>
    public IReadOnlyList<BackgroundLayer> BackgroundLayers { get; set; }

    /// <summary>Process names that make this profile active while their window has focus.</summary>
    public ObservableCollection<string> AutoSwitchApps { get; }

    public bool HasAutoSwitchApps => AutoSwitchApps.Count > 0;

    public string AutoSwitchSummary => AutoSwitchApps.Count == 0
        ? "Only when chosen"
        : "Auto: " + string.Join(", ", AutoSwitchApps);

    public int BindingCount => _bindingsByControlId.Count;

    public int UnresolvedBindingCount => _unresolvedBindings.Count;

    public string Summary => $"{BindingCount} mapped control{(BindingCount == 1 ? string.Empty : "s")}"
        + (UnresolvedBindingCount > 0 ? $", {UnresolvedBindingCount} unavailable" : string.Empty);

    public ControlBindingViewModel? GetBinding(string controlId)
    {
        _bindingsByControlId.TryGetValue(controlId, out var binding);
        return binding;
    }

    /// <summary>The control's binding model, also when its plugin isn't loaded (null when unmapped).</summary>
    public ControlBinding? GetModel(string controlId) =>
        GetBinding(controlId)?.Model ?? _unresolvedBindings.GetValueOrDefault(controlId);

    public void SetBinding(ControlBindingViewModel binding)
    {
        _bindingsByControlId[binding.ControlId] = binding;
        _unresolvedBindings.Remove(binding.ControlId);
        RaiseCountsChanged();
    }

    public void AddUnresolvedBinding(ControlBinding binding)
    {
        _bindingsByControlId.Remove(binding.ControlId);
        _unresolvedBindings[binding.ControlId] = binding;
        RaiseCountsChanged();
    }

    public void RemoveBinding(string controlId)
    {
        var removed = _bindingsByControlId.Remove(controlId);
        removed |= _unresolvedBindings.Remove(controlId);
        if (removed)
        {
            RaiseCountsChanged();
        }
    }

    /// <summary>Removes everything and returns the models, so they can be resolved again (after plugin changes).</summary>
    public IReadOnlyList<ControlBinding> TakeAllBindings()
    {
        var models = ToModel().Bindings;
        _bindingsByControlId.Clear();
        _unresolvedBindings.Clear();
        RaiseCountsChanged();
        return models;
    }

    public DeviceProfile ToModel() => new(
        Id,
        Name,
        DeviceId,
        [
            .. _bindingsByControlId.Values.Select(static binding => binding.Model),
            .. _unresolvedBindings.Values,
        ],
        AutoSwitchApps.Count == 0 ? null : [.. AutoSwitchApps],
        string.IsNullOrWhiteSpace(BackgroundColor) && string.IsNullOrWhiteSpace(BackgroundImagePath) && BackgroundLayers.Count == 0
            ? null
            : new ProfileBackground(NullIfEmpty(BackgroundColor), NullIfEmpty(BackgroundImagePath)) { Layers = BackgroundLayers.Count == 0 ? null : [.. BackgroundLayers] })
    {
        ParentId = ParentId,
        Kind = Kind,
        OverridesBackground = OverridesBackground,
    };

    /// <summary>The profile and all its pages and folders, as saved and as the host gets them.</summary>
    public IEnumerable<DeviceProfile> ToModels() => AllLayouts.Select(static layout => layout.ToModel());

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void RaiseCountsChanged()
    {
        OnPropertyChanged(nameof(BindingCount));
        OnPropertyChanged(nameof(UnresolvedBindingCount));
        OnPropertyChanged(nameof(Summary));
    }
}

/// <summary>
/// An immutable binding (<see cref="Model"/>) plus the loaded actions it refers to. Edits create
/// a new model, and with it a new view model.
/// </summary>
public class ControlBindingViewModel : ViewModelBase
{
    public ControlBindingViewModel(ControlBinding model, ActionItemViewModel primaryAction, IReadOnlyList<StepItemViewModel> steps)
    {
        Model = model;
        PrimaryAction = primaryAction;
        Steps = new ObservableCollection<StepItemViewModel>(steps);
    }

    public ControlBinding Model { get; }

    public string ControlId => Model.ControlId;

    public ActionItemViewModel PrimaryAction { get; }

    public ActionSettings Settings => Model.Action.EffectiveSettings;

    public ObservableCollection<StepItemViewModel> Steps { get; }

    public string AppearanceText => Model.Appearance.Text ?? string.Empty;

    public string AppearanceImagePath => Model.Appearance.ImagePath ?? string.Empty;

    public RgbFeedback? LightFeedback => Model.Appearance.RgbFeedback;

    /// <summary>A binding with steps: a Multi Action, a Multi Action Switch or Key Logic.</summary>
    public bool IsMultiAction => Model.ActionKind is not (ProfileActionKind.Single or ProfileActionKind.Workflow);

    /// <summary>A visual workflow (<see cref="ControlBinding.Workflow"/>); its action nodes are <see cref="Steps"/>.</summary>
    public bool IsWorkflow => Model.ActionKind == ProfileActionKind.Workflow;

    public bool IsDisplayContentOnly => PrimaryAction.IsDisplayContentAction;

    public string DisplayName => IsWorkflow
        ? $"{PrimaryAction.Name} ({Model.Workflow?.Nodes.Count ?? 0} node{(Model.Workflow?.Nodes.Count == 1 ? string.Empty : "s")})"
        : IsMultiAction
        ? $"{PrimaryAction.Name} ({Steps.Count} step{(Steps.Count == 1 ? string.Empty : "s")})"
        : IsDisplayContentOnly ? "Text / image only" : PrimaryAction.Name;

    public string CapabilitySummary => IsWorkflow
        ? Steps.Count == 0 ? "No actions yet: open the workflow editor." : string.Join(", ", Steps.Select(static step => step.Name).Distinct())
        : IsMultiAction
        ? Steps.Count == 0 ? "No steps yet: add them in Customize." : string.Join(" → ", Steps.Select(static step => step.Name))
        : PrimaryAction.Summary;
}
