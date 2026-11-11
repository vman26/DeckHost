using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace DeckHost.App.ViewModels;

public class ProfileItemViewModel : ViewModelBase
{
    private readonly Dictionary<string, ControlBindingViewModel> _bindingsByControlId = new(StringComparer.OrdinalIgnoreCase);

    // Bindings whose plugin/action isn't loaded right now. Kept as-is so saving doesn't drop them.
    private readonly Dictionary<string, ControlBinding> _unresolvedBindings = new(StringComparer.OrdinalIgnoreCase);

    public ProfileItemViewModel(string id, string name, string deviceId)
    {
        Id = id;
        Name = name;
        DeviceId = deviceId;
    }

    public string Id { get; }

    public string Name { get; }

    public string DeviceId { get; }

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

    public DeviceProfile ToModel() => new(
        Id,
        Name,
        DeviceId,
        [
            .. _bindingsByControlId.Values.Select(static binding => binding.ToModel()),
            .. _unresolvedBindings.Values,
        ]);

    private void RaiseCountsChanged()
    {
        OnPropertyChanged(nameof(BindingCount));
        OnPropertyChanged(nameof(UnresolvedBindingCount));
        OnPropertyChanged(nameof(Summary));
    }
}

public class ControlBindingViewModel : ViewModelBase
{
    private string _appearanceText;
    private string _appearanceImagePath;
    private RgbFeedback? _lightFeedback;

    public ControlBindingViewModel(
        string controlId,
        ProfileActionKind actionKind,
        ActionItemViewModel primaryAction,
        string? appearanceText,
        string? appearanceImagePath,
        IEnumerable<ActionItemViewModel> steps,
        RgbFeedback? lightFeedback = null)
    {
        ControlId = controlId;
        ActionKind = actionKind;
        PrimaryAction = primaryAction;
        _appearanceText = appearanceText ?? string.Empty;
        _appearanceImagePath = appearanceImagePath ?? string.Empty;
        _lightFeedback = lightFeedback;
        Steps = new ObservableCollection<ActionItemViewModel>(steps);
        Steps.CollectionChanged += OnStepsChanged;
    }

    public event EventHandler? Changed;

    public string ControlId { get; }

    public ProfileActionKind ActionKind { get; }

    public ActionItemViewModel PrimaryAction { get; }

    public ObservableCollection<ActionItemViewModel> Steps { get; }

    public string AppearanceText => _appearanceText;

    public string AppearanceImagePath => _appearanceImagePath;

    public RgbFeedback? LightFeedback => _lightFeedback;

    public bool IsMultiAction => ActionKind == ProfileActionKind.MultiAction;

    public string DisplayName => IsMultiAction
        ? $"{PrimaryAction.Name} ({Steps.Count} step{(Steps.Count == 1 ? string.Empty : "s")})"
        : PrimaryAction.Name;

    public string CapabilitySummary => IsMultiAction
        ? $"{Steps.Count} programmed step{(Steps.Count == 1 ? string.Empty : "s")}"
        : $"Requires {PrimaryAction.InputsLabel} / {PrimaryAction.OutputsLabel}";

    public void AddStep(ActionItemViewModel action) => Steps.Add(action);

    public void SetAppearance(string text, string imagePath)
    {
        var changed = false;

        if (_appearanceText != text)
        {
            _appearanceText = text;
            OnPropertyChanged(nameof(AppearanceText));
            changed = true;
        }

        if (_appearanceImagePath != imagePath)
        {
            _appearanceImagePath = imagePath;
            OnPropertyChanged(nameof(AppearanceImagePath));
            changed = true;
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    public void SetLightFeedback(RgbFeedback? feedback)
    {
        if (_lightFeedback == feedback)
        {
            return;
        }

        _lightFeedback = feedback;
        OnPropertyChanged(nameof(LightFeedback));
        RaiseChanged();
    }

    public ControlBinding ToModel() => new(
        ControlId,
        ActionKind,
        PrimaryAction.ToActionReference(),
        new ControlAppearance(
            string.IsNullOrWhiteSpace(AppearanceText) ? null : AppearanceText,
            string.IsNullOrWhiteSpace(AppearanceImagePath) ? null : AppearanceImagePath,
            LightFeedback),
        Steps.Select((step, index) => new MultiActionStep(index, step.ToActionReference())).ToList());

    private void OnStepsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RaiseChanged();

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(CapabilitySummary));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
