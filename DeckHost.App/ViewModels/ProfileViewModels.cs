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

    public ProfileItemViewModel(string id, string name, string deviceId, IEnumerable<string>? autoSwitchApps = null)
    {
        Id = id;
        _name = name;
        DeviceId = deviceId;
        AutoSwitchApps = new ObservableCollection<string>(autoSwitchApps ?? []);
        AutoSwitchApps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AutoSwitchSummary));
            OnPropertyChanged(nameof(HasAutoSwitchApps));
        };
    }

    public string Id { get; }

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
        AutoSwitchApps.Count == 0 ? null : [.. AutoSwitchApps]);

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

    public bool IsMultiAction => Model.ActionKind == ProfileActionKind.MultiAction;

    public bool IsDisplayContentOnly => PrimaryAction.IsDisplayContentAction;

    public string DisplayName => IsMultiAction
        ? $"{PrimaryAction.Name} ({Steps.Count} step{(Steps.Count == 1 ? string.Empty : "s")})"
        : IsDisplayContentOnly ? "Text / image only" : PrimaryAction.Name;

    public string CapabilitySummary => IsMultiAction
        ? Steps.Count == 0 ? "No steps yet: add them in Customize." : string.Join(" → ", Steps.Select(static step => step.Name))
        : PrimaryAction.Summary;
}
