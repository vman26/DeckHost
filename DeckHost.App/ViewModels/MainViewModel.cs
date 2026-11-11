using Avalonia.Media;
using DeckHost.App.Plugins;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Models;
using DeckHost.Core.State;
using DeckHost.Device.MiraBox;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace DeckHost.App.ViewModels;

internal static class EditorActionIds
{
    public const string MultiAction = "builtin.multi-action";
    public const string DisplayContent = "builtin.display-content";
    public const string BuiltInPluginId = "deckhost.plugin.builtin";
}

public class MainViewModel : ViewModelBase
{
    private readonly List<IApplicationPlugin> _applicationPlugins = [];
    private readonly IDeviceHostService _deviceHostService;
    private readonly IDevicePlugin _devicePlugin;
    private readonly Dictionary<string, ActionItemViewModel> _actionsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ControlItemViewModel> _controlsById = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressAppearanceSync;
    private DeviceDefinition? _selectedDevice;
    private string _searchText = string.Empty;
    private ControlItemViewModel? _selectedControl;
    private ProfileItemViewModel? _selectedProfile;
    private string _statusMessage = "Select a profile, then drag plugin actions onto the simulated device.";
    private string _connectionStatus = "Not Started";
    private string _connectionDetail = "The runtime host has not started yet.";
    private string _selectedAppearanceText = string.Empty;
    private string _selectedAppearanceImagePath = string.Empty;

    public MainViewModel()
        : this(CreateDefaultDeviceHostService(out var devicePlugin, out var applicationPlugins), devicePlugin, applicationPlugins)
    {
    }

    public MainViewModel(IDeviceHostService deviceHostService, IDevicePlugin devicePlugin, IEnumerable<IApplicationPlugin> applicationPlugins)
    {
        ArgumentNullException.ThrowIfNull(deviceHostService);
        ArgumentNullException.ThrowIfNull(devicePlugin);
        ArgumentNullException.ThrowIfNull(applicationPlugins);

        _deviceHostService = deviceHostService;
        _devicePlugin = devicePlugin;
        SelectedDevice = _devicePlugin.Devices.First();

        foreach (var control in SelectedDevice.Controls.Select(static control => new ControlItemViewModel(control)))
        {
            ControlItems.Add(control);
            _controlsById[control.Id] = control;
        }

        _applicationPlugins.AddRange(applicationPlugins);
        _deviceHostService.RuntimeLogReceived += OnRuntimeLogReceived;
        _deviceHostService.ConnectionStatusChanged += OnConnectionStatusChanged;

        PluginItems.Add(new PluginSummaryViewModel(
            _devicePlugin.Id,
            _devicePlugin.Name,
            "Device Plugin",
            $"{_devicePlugin.Devices.Count} device definition" + (_devicePlugin.Devices.Count == 1 ? string.Empty : "s"),
            "Available"));

        foreach (var plugin in _applicationPlugins)
        {
            PluginItems.Add(new PluginSummaryViewModel(
                plugin.Id,
                plugin.Name,
                "Application Plugin",
                $"{plugin.Actions.Count} action" + (plugin.Actions.Count == 1 ? string.Empty : "s"),
                "Loaded"));
        }

        foreach (var actionItem in _applicationPlugins
                     .SelectMany(plugin => plugin.Actions.Select(action => ActionItemViewModel.Create(plugin.Id, plugin.Name, action)))
                     .OrderBy(static action => action.PluginName)
                     .ThenBy(static action => action.Name))
        {
            _actionsByKey[actionItem.Key] = actionItem;
        }

        Profiles.Add(new ProfileItemViewModel("profile.default", "Default", SelectedDevice.Id));
        Profiles.Add(new ProfileItemViewModel("profile.streaming", "Streaming", SelectedDevice.Id));
        Profiles.Add(new ProfileItemViewModel("profile.media", "Media", SelectedDevice.Id));

        RefreshActionGroups();
        SelectedProfile = Profiles[0];
        SelectControl(ControlItems[0].Control);
        SyncProfilesToHost();
        SeedRuntimeState();
    }

    public ObservableCollection<PluginActionGroupViewModel> ActionGroups { get; } = new();

    public ObservableCollection<PluginSummaryViewModel> PluginItems { get; } = new();

    public ObservableCollection<RuntimeLogEntry> RuntimeLogs { get; } = new();

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = new();

    public ObservableCollection<ControlItemViewModel> ControlItems { get; } = new();

    public DeviceDefinition? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                OnPropertyChanged(nameof(DeviceName));
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshActionGroups();
            }
        }
    }

    public ControlItemViewModel? SelectedControl
    {
        get => _selectedControl;
        set
        {
            if (SetProperty(ref _selectedControl, value))
            {
                foreach (var item in ControlItems)
                {
                    item.IsSelected = item == value;
                }

                SyncSelectedAppearanceEditors();
                OnPropertyChanged(nameof(SelectedControlName));
                OnPropertyChanged(nameof(SelectedControlType));
                OnPropertyChanged(nameof(SelectedControlInputs));
                OnPropertyChanged(nameof(SelectedControlOutputs));
                OnPropertyChanged(nameof(SelectedAssignmentName));
                OnPropertyChanged(nameof(SelectedAssignmentCapabilities));
                OnPropertyChanged(nameof(HasSelectedAssignment));
                OnPropertyChanged(nameof(CanEditSelectedAppearance));
                OnPropertyChanged(nameof(SelectedAssignmentIsMultiAction));
                OnPropertyChanged(nameof(SelectedMultiActionSteps));
            }
        }
    }

    public ProfileItemViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                ApplyProfileBindings();
                SyncProfilesToHost();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public string ConnectionDetail
    {
        get => _connectionDetail;
        private set => SetProperty(ref _connectionDetail, value);
    }

    public string SelectedAppearanceText
    {
        get => _selectedAppearanceText;
        set
        {
            if (!SetProperty(ref _selectedAppearanceText, value) || _suppressAppearanceSync)
            {
                return;
            }

            UpdateSelectedControlAppearance(text: value, imagePath: null);
        }
    }

    public string SelectedAppearanceImagePath
    {
        get => _selectedAppearanceImagePath;
        set
        {
            if (!SetProperty(ref _selectedAppearanceImagePath, value) || _suppressAppearanceSync)
            {
                return;
            }

            UpdateSelectedControlAppearance(text: null, imagePath: value);
        }
    }

    public string DeviceName => SelectedDevice?.Name ?? "No device selected";

    public string CurrentPageName => "Page 1";

    public string SelectedProfileName => SelectedProfile?.Name ?? "No profile selected";

    public string SelectedProfileSummary => SelectedProfile?.Summary ?? "No active mappings";

    public bool HasRuntimeLogs => RuntimeLogs.Count > 0;

    public double CanvasWidth => ControlItems.Count == 0 ? 0d : ControlItems.Max(static control => control.X + control.Width);

    public double CanvasHeight => ControlItems.Count == 0 ? 0d : ControlItems.Max(static control => control.Y + control.Height);

    public string SelectedControlName => SelectedControl?.DisplayName ?? "No control selected";

    public string SelectedControlType => SelectedControl?.TypeLabel ?? "Select a control on the canvas";

    public string SelectedControlInputs => SelectedControl?.InputsLabel ?? "—";

    public string SelectedControlOutputs => SelectedControl?.OutputsLabel ?? "—";

    public string SelectedAssignmentName => SelectedControl?.AssignedBinding?.DisplayName ?? "Unassigned";

    public string SelectedAssignmentCapabilities => SelectedControl?.AssignedBinding is null
        ? "Drop a compatible action onto the selected control."
        : SelectedControl.AssignedBinding.CapabilitySummary;

    public bool HasSelectedAssignment => SelectedControl?.AssignedBinding is not null;

    public bool CanEditSelectedAppearance => SelectedControl?.SupportsAppearance == true;

    public bool SelectedAssignmentIsMultiAction => SelectedControl?.AssignedBinding?.IsMultiAction == true;

    public bool ShowMultiActionHint => !SelectedAssignmentIsMultiAction;

    public ObservableCollection<ActionItemViewModel> SelectedMultiActionSteps =>
        SelectedControl?.AssignedBinding?.Steps ?? EmptyActionCollection.Instance;

    public bool CanDropAction(IDeckAction action, PhysicalControl targetControl)
    {
        if (!TryGetActionItem(action, out var actionItem))
        {
            return false;
        }

        if (actionItem.RequiresOutputCapableControl && targetControl.Outputs == OutputCapabilities.None)
        {
            return false;
        }

        if (actionItem.RequiresInteractiveInput && targetControl.Inputs == InputCapabilities.None)
        {
            return false;
        }

        var inputsMatch = (targetControl.Inputs & action.RequiredInputs) == action.RequiredInputs;
        var outputsMatch = (targetControl.Outputs & action.RequiredOutputs) == action.RequiredOutputs;
        return inputsMatch && outputsMatch;
    }

    public void AssignActionToControl(PhysicalControl control, IDeckAction action)
    {
        if (SelectedProfile is null ||
            !_controlsById.TryGetValue(control.Id, out var controlItem) ||
            !TryGetActionItem(action, out var actionItem))
        {
            return;
        }

        SelectControl(control);

        if (!CanDropAction(action, control))
        {
            StatusMessage = $"{action.Name} is not compatible with {control.DisplayName}.";
            return;
        }

        var existingBinding = SelectedProfile.GetBinding(control.Id);
        ControlBindingViewModel updatedBinding;

        if (actionItem.IsMultiActionScaffold)
        {
            updatedBinding = CreateOrPromoteToMultiActionBinding(control.Id, existingBinding);
            StatusMessage = existingBinding is null
                ? $"Created a Multi Action scaffold on {control.DisplayName}."
                : $"Converted {control.DisplayName} into a Multi Action binding.";
        }
        else if (existingBinding?.IsMultiAction == true)
        {
            if (!actionItem.CanBeMultiActionStep)
            {
                StatusMessage = $"{actionItem.Name} cannot be added as a Multi Action step.";
                return;
            }

            existingBinding.AddStep(actionItem);
            updatedBinding = existingBinding;
            StatusMessage = $"Added {actionItem.Name} as a step on {control.DisplayName}.";
        }
        else
        {
            updatedBinding = CreateSingleActionBinding(control.Id, actionItem, existingBinding);
            StatusMessage = $"Assigned {actionItem.Name} to {control.DisplayName}.";
        }

        SelectedProfile.SetBinding(updatedBinding);
        AttachBinding(controlItem, updatedBinding);
        SyncProfilesToHost();
    }

    public void RemoveAssignment(PhysicalControl control)
    {
        if (SelectedProfile is null || !_controlsById.TryGetValue(control.Id, out var controlItem))
        {
            return;
        }

        SelectedProfile.RemoveBinding(control.Id);
        AttachBinding(controlItem, null);
        StatusMessage = $"Removed the mapping from {control.DisplayName}.";
        SyncProfilesToHost();
    }

    public void RemoveAssignment(string controlId)
    {
        if (_controlsById.TryGetValue(controlId, out var controlItem))
        {
            RemoveAssignment(controlItem.Control);
        }
    }

    private void SeedRuntimeState()
    {
        RuntimeLogs.Clear();
        foreach (var entry in _deviceHostService.RuntimeLogs)
        {
            RuntimeLogs.Add(entry);
        }

        RefreshConnectionStatus();
        OnPropertyChanged(nameof(HasRuntimeLogs));
    }

    private void RefreshConnectionStatus()
    {
        var deviceId = SelectedDevice?.Id;
        var status = deviceId is null
            ? null
            : _deviceHostService.ConnectionStatuses.FirstOrDefault(candidate => string.Equals(candidate.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));

        ConnectionStatus = status?.State ?? "Unknown";
        ConnectionDetail = status?.Message ?? "No runtime status reported for the selected device.";
    }

    private void OnRuntimeLogReceived(RuntimeLogEntry entry)
    {
        RuntimeLogs.Add(entry);
        if (RuntimeLogs.Count > 100)
        {
            RuntimeLogs.RemoveAt(0);
        }

        OnPropertyChanged(nameof(HasRuntimeLogs));
    }

    private void OnConnectionStatusChanged(DeviceConnectionStatus status)
    {
        if (SelectedDevice is not null && string.Equals(status.DeviceId, SelectedDevice.Id, StringComparison.OrdinalIgnoreCase))
        {
            ConnectionStatus = status.State;
            ConnectionDetail = status.Message;
        }
    }

    public ControlCustomizationViewModel CreateCustomizationViewModel(string controlId)
    {
        if (!_controlsById.TryGetValue(controlId, out var controlItem))
        {
            throw new InvalidOperationException($"Unknown control '{controlId}'.");
        }

        var binding = SelectedProfile?.GetBinding(controlId);
        return new ControlCustomizationViewModel(
            controlItem.Id,
            controlItem.DisplayName,
            controlItem.TypeLabel,
            controlItem.SupportsAppearance,
            binding?.AppearanceText ?? string.Empty,
            binding?.AppearanceImagePath ?? string.Empty);
    }

    public void ApplyCustomization(ControlCustomizationViewModel customization)
    {
        if (!_controlsById.TryGetValue(customization.ControlId, out var controlItem))
        {
            return;
        }

        SelectControl(controlItem.Control);

        if (customization.SupportsAppearance)
        {
            UpdateSelectedControlAppearance(customization.DisplayText, customization.ImagePath);
            StatusMessage = $"Updated visible output for {controlItem.DisplayName}.";
        }
    }

    public void SelectControl(PhysicalControl control)
    {
        if (_controlsById.TryGetValue(control.Id, out var controlItem))
        {
            SelectedControl = controlItem;
        }
    }

    private void RefreshActionGroups()
    {
        var filteredActions = _actionsByKey.Values
            .Where(action => string.IsNullOrWhiteSpace(SearchText)
                || action.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || action.PluginName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static action => action.PluginName)
            .ThenBy(static action => action.Name)
            .ToList();

        ActionGroups.Clear();
        foreach (var group in filteredActions.GroupBy(static action => new { action.PluginId, action.PluginName }))
        {
            ActionGroups.Add(new PluginActionGroupViewModel(group.Key.PluginId, group.Key.PluginName, group));
        }
    }

    private void ApplyProfileBindings()
    {
        foreach (var controlItem in ControlItems)
        {
            AttachBinding(controlItem, SelectedProfile?.GetBinding(controlItem.Id));
        }

        SyncSelectedAppearanceEditors();
        OnPropertyChanged(nameof(SelectedAssignmentName));
        OnPropertyChanged(nameof(SelectedAssignmentCapabilities));
        OnPropertyChanged(nameof(HasSelectedAssignment));
        OnPropertyChanged(nameof(CanEditSelectedAppearance));
        OnPropertyChanged(nameof(SelectedAssignmentIsMultiAction));
        OnPropertyChanged(nameof(SelectedMultiActionSteps));
        StatusMessage = SelectedProfile is null ? "No profile selected." : $"Editing profile {SelectedProfile.Name}.";
    }

    private void AttachBinding(ControlItemViewModel controlItem, ControlBindingViewModel? binding)
    {
        controlItem.AssignedBinding = binding;

        if (binding is not null)
        {
            binding.Changed -= OnBindingChanged;
            binding.Changed += OnBindingChanged;
        }

        if (SelectedControl == controlItem)
        {
            SyncSelectedAppearanceEditors();
            OnPropertyChanged(nameof(SelectedAssignmentName));
            OnPropertyChanged(nameof(SelectedAssignmentCapabilities));
            OnPropertyChanged(nameof(HasSelectedAssignment));
            OnPropertyChanged(nameof(CanEditSelectedAppearance));
            OnPropertyChanged(nameof(SelectedAssignmentIsMultiAction));
                OnPropertyChanged(nameof(ShowMultiActionHint));
            OnPropertyChanged(nameof(SelectedMultiActionSteps));
        }
    }

    private void OnBindingChanged(object? sender, EventArgs e)
    {
        if (SelectedControl?.AssignedBinding == sender)
        {
            SyncSelectedAppearanceEditors();
            OnPropertyChanged(nameof(SelectedAssignmentName));
            OnPropertyChanged(nameof(SelectedAssignmentCapabilities));
            OnPropertyChanged(nameof(CanEditSelectedAppearance));
            OnPropertyChanged(nameof(SelectedAssignmentIsMultiAction));
            OnPropertyChanged(nameof(ShowMultiActionHint));
            OnPropertyChanged(nameof(SelectedMultiActionSteps));
        }
    }

    private void SyncSelectedAppearanceEditors()
    {
        _suppressAppearanceSync = true;
        try
        {
            SelectedAppearanceText = SelectedControl?.AssignedBinding?.AppearanceText ?? string.Empty;
            SelectedAppearanceImagePath = SelectedControl?.AssignedBinding?.AppearanceImagePath ?? string.Empty;
        }
        finally
        {
            _suppressAppearanceSync = false;
        }
    }

    private void UpdateSelectedControlAppearance(string? text, string? imagePath)
    {
        if (SelectedControl is null || SelectedProfile is null || !SelectedControl.SupportsAppearance)
        {
            return;
        }

        var binding = SelectedProfile.GetBinding(SelectedControl.Id);
        if (binding is null)
        {
            if (!_actionsByKey.TryGetValue(BuildActionKey(EditorActionIds.BuiltInPluginId, EditorActionIds.DisplayContent), out var displayAction))
            {
                return;
            }

            binding = CreateSingleActionBinding(SelectedControl.Id, displayAction, existingBinding: null);
            SelectedProfile.SetBinding(binding);
            AttachBinding(SelectedControl, binding);
            StatusMessage = $"Created display content binding for {SelectedControl.DisplayName}.";
        }

        binding.SetAppearance(text ?? binding.AppearanceText, imagePath ?? binding.AppearanceImagePath);
        SyncProfilesToHost();
    }

    private void SyncProfilesToHost()
    {
        _deviceHostService.SetProfiles(Profiles.Select(static profile => profile.ToModel()).ToList());

        if (SelectedProfile is not null)
        {
            _deviceHostService.SetActiveProfile(SelectedProfile.Id);
        }
    }

    private ControlBindingViewModel CreateSingleActionBinding(string controlId, ActionItemViewModel actionItem, ControlBindingViewModel? existingBinding)
    {
        return new ControlBindingViewModel(
            controlId,
            ProfileActionKind.Single,
            actionItem,
            existingBinding?.AppearanceText,
            existingBinding?.AppearanceImagePath,
            []);
    }

    private ControlBindingViewModel CreateOrPromoteToMultiActionBinding(string controlId, ControlBindingViewModel? existingBinding)
    {
        var multiAction = _actionsByKey[BuildActionKey(EditorActionIds.BuiltInPluginId, EditorActionIds.MultiAction)];
        var steps = new List<ActionItemViewModel>();

        if (existingBinding is not null)
        {
            if (existingBinding.IsMultiAction)
            {
                steps.AddRange(existingBinding.Steps);
            }
            else if (existingBinding.PrimaryAction.CanBeMultiActionStep)
            {
                steps.Add(existingBinding.PrimaryAction);
            }
        }

        return new ControlBindingViewModel(
            controlId,
            ProfileActionKind.MultiAction,
            multiAction,
            existingBinding?.AppearanceText,
            existingBinding?.AppearanceImagePath,
            steps);
    }

    private bool TryGetActionItem(IDeckAction action, out ActionItemViewModel actionItem)
    {
        actionItem = _actionsByKey.Values.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Action, action) || string.Equals(candidate.Id, action.Id, StringComparison.OrdinalIgnoreCase))!;
        return actionItem is not null;
    }

    private static string BuildActionKey(string pluginId, string actionId) => $"{pluginId}:{actionId}";

    private static IDeviceHostService CreateDefaultDeviceHostService(out IDevicePlugin devicePlugin, out IReadOnlyList<IApplicationPlugin> applicationPlugins)
    {
        devicePlugin = new MiraBoxDevicePlugin();
        applicationPlugins = FirstPartyApplicationPlugins.Create();
        return new DeviceHostService([devicePlugin], applicationPlugins, new VariableStore());
    }
}

public class PluginActionGroupViewModel : ViewModelBase
{
    private bool _isExpanded = true;

    public PluginActionGroupViewModel(string pluginId, string pluginName, IEnumerable<ActionItemViewModel> actions)
    {
        PluginId = pluginId;
        PluginName = pluginName;
        foreach (var action in actions)
        {
            Actions.Add(action);
        }
    }

    public string PluginId { get; }

    public string PluginName { get; }

    public ObservableCollection<ActionItemViewModel> Actions { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

public class PluginSummaryViewModel : ViewModelBase
{
    public PluginSummaryViewModel(string id, string name, string kind, string contents, string status)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Contents = contents;
        Status = status;
    }

    public string Id { get; }

    public string Name { get; }

    public string Kind { get; }

    public string Contents { get; }

    public string Status { get; }
}

public class ControlCustomizationViewModel : ViewModelBase
{
    private string _displayText;
    private string _imagePath;

    public ControlCustomizationViewModel(
        string controlId,
        string displayName,
        string controlType,
        bool supportsAppearance,
        string displayText,
        string imagePath)
    {
        ControlId = controlId;
        DisplayName = displayName;
        ControlType = controlType;
        SupportsAppearance = supportsAppearance;
        _displayText = displayText;
        _imagePath = imagePath;
    }

    public string ControlId { get; }

    public string DisplayName { get; }

    public string ControlType { get; }

    public bool SupportsAppearance { get; }

    public bool ShowAppearanceEditor => SupportsAppearance;

    public bool ShowUnsupportedMessage => !SupportsAppearance;

    public string DisplayText
    {
        get => _displayText;
        set => SetProperty(ref _displayText, value);
    }

    public string ImagePath
    {
        get => _imagePath;
        set => SetProperty(ref _imagePath, value);
    }
}

public class ActionItemViewModel : ViewModelBase
{
    private ActionItemViewModel(string pluginId, string pluginName, IDeckAction action)
    {
        PluginId = pluginId;
        PluginName = pluginName;
        Action = action;
        Id = action.Id;
        Name = action.Name;
        InputsLabel = FormatCapabilities(action.RequiredInputs, "No input required");
        OutputsLabel = FormatCapabilities(action.RequiredOutputs, "No output required");
    }

    public string PluginId { get; }

    public string PluginName { get; }

    public string Id { get; }

    public string Key => $"{PluginId}:{Id}";

    public string Name { get; }

    public IDeckAction Action { get; }

    public string InputsLabel { get; }

    public string OutputsLabel { get; }

    public bool IsMultiActionScaffold => Id == EditorActionIds.MultiAction;

    public bool IsDisplayContentAction => Id == EditorActionIds.DisplayContent;

    public bool RequiresOutputCapableControl => IsDisplayContentAction;

    public bool RequiresInteractiveInput => IsMultiActionScaffold;

    public bool CanBeMultiActionStep => !IsMultiActionScaffold && !IsDisplayContentAction;

    public string Summary => $"{InputsLabel} / {OutputsLabel}";

    public ActionReference ToActionReference() => new(
        PluginId,
        PluginName,
        Id,
        Name,
        Action.RequiredInputs,
        Action.RequiredOutputs);

    public static ActionItemViewModel Create(string pluginId, string pluginName, IDeckAction action) => new(pluginId, pluginName, action);

    private static string FormatCapabilities(InputCapabilities capabilities, string emptyText)
    {
        if (capabilities == InputCapabilities.None)
        {
            return emptyText;
        }

        return string.Join(", ", Enum.GetValues<InputCapabilities>()
            .Where(flag => flag is not InputCapabilities.None and not InputCapabilities.All && capabilities.HasFlag(flag)));
    }

    private static string FormatCapabilities(OutputCapabilities capabilities, string emptyText)
    {
        if (capabilities == OutputCapabilities.None)
        {
            return emptyText;
        }

        return string.Join(", ", Enum.GetValues<OutputCapabilities>()
            .Where(flag => flag is not OutputCapabilities.None and not OutputCapabilities.All && capabilities.HasFlag(flag)));
    }
}

public class ProfileItemViewModel : ViewModelBase
{
    private readonly Dictionary<string, ControlBindingViewModel> _bindingsByControlId = new(StringComparer.OrdinalIgnoreCase);

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

    public string Summary => $"{BindingCount} mapped control{(BindingCount == 1 ? string.Empty : "s")}";

    public ControlBindingViewModel? GetBinding(string controlId)
    {
        _bindingsByControlId.TryGetValue(controlId, out var binding);
        return binding;
    }

    public void SetBinding(ControlBindingViewModel binding)
    {
        _bindingsByControlId[binding.ControlId] = binding;
        OnPropertyChanged(nameof(BindingCount));
        OnPropertyChanged(nameof(Summary));
    }

    public void RemoveBinding(string controlId)
    {
        if (_bindingsByControlId.Remove(controlId))
        {
            OnPropertyChanged(nameof(BindingCount));
            OnPropertyChanged(nameof(Summary));
        }
    }

    public DeviceProfile ToModel() => new(
        Id,
        Name,
        DeviceId,
        _bindingsByControlId.Values.Select(static binding => binding.ToModel()).ToList());
}

public class ControlBindingViewModel : ViewModelBase
{
    private string _appearanceText;
    private string _appearanceImagePath;

    public ControlBindingViewModel(
        string controlId,
        ProfileActionKind actionKind,
        ActionItemViewModel primaryAction,
        string? appearanceText,
        string? appearanceImagePath,
        IEnumerable<ActionItemViewModel> steps)
    {
        ControlId = controlId;
        ActionKind = actionKind;
        PrimaryAction = primaryAction;
        _appearanceText = appearanceText ?? string.Empty;
        _appearanceImagePath = appearanceImagePath ?? string.Empty;
        Steps = new ObservableCollection<ActionItemViewModel>(steps);
        Steps.CollectionChanged += OnStepsChanged;
    }

    public event EventHandler? Changed;

    public string ControlId { get; }

    public ProfileActionKind ActionKind { get; }

    public ActionItemViewModel PrimaryAction { get; }

    public ObservableCollection<ActionItemViewModel> Steps { get; }

    public string AppearanceText
    {
        get => _appearanceText;
        set
        {
            if (SetProperty(ref _appearanceText, value))
            {
                RaiseChanged();
            }
        }
    }

    public string AppearanceImagePath
    {
        get => _appearanceImagePath;
        set
        {
            if (SetProperty(ref _appearanceImagePath, value))
            {
                RaiseChanged();
            }
        }
    }

    public bool IsMultiAction => ActionKind == ProfileActionKind.MultiAction;

    public string DisplayName => IsMultiAction
        ? $"{PrimaryAction.Name} ({Steps.Count} step{(Steps.Count == 1 ? string.Empty : "s")})"
        : PrimaryAction.Name;

    public string CapabilitySummary => IsMultiAction
        ? $"{Steps.Count} programmed step{(Steps.Count == 1 ? string.Empty : "s")}" 
        : $"Requires {PrimaryAction.InputsLabel} / {PrimaryAction.OutputsLabel}";

    public void AddStep(ActionItemViewModel action)
    {
        Steps.Add(action);
    }

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

    public ControlBinding ToModel() => new(
        ControlId,
        ActionKind,
        PrimaryAction.ToActionReference(),
        new ControlAppearance(
            string.IsNullOrWhiteSpace(AppearanceText) ? null : AppearanceText,
            string.IsNullOrWhiteSpace(AppearanceImagePath) ? null : AppearanceImagePath),
        Steps.Select((step, index) => new MultiActionStep(index, step.ToActionReference())).ToList());

    private void OnStepsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(CapabilitySummary));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public class ControlItemViewModel : ViewModelBase
{
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush AssignedBrush = new SolidColorBrush(Color.Parse("#0F766E"));
    private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.Parse("#27272A"));

    private ControlBindingViewModel? _assignedBinding;
    private bool _isSelected;

    public ControlItemViewModel(PhysicalControl control)
    {
        Control = control;
        Id = control.Id;
        DisplayName = control.DisplayName;
        Type = control.Type;
        X = control.Bounds.X;
        Y = control.Bounds.Y;
        Width = control.Bounds.Width;
        Height = control.Bounds.Height;
        InputsLabel = FormatCapabilities(control.Inputs);
        OutputsLabel = FormatCapabilities(control.Outputs);
        SupportsAppearance = control.Outputs != OutputCapabilities.None;
    }

    public PhysicalControl Control { get; }

    public string Id { get; }

    public string DisplayName { get; }

    public ControlType Type { get; }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public bool SupportsAppearance { get; }

    public string InputsLabel { get; }

    public string OutputsLabel { get; }

    public string TypeLabel => Type switch
    {
        ControlType.Key => "Key",
        ControlType.Encoder => "Encoder",
        ControlType.TouchSurface => "Touch Surface",
        ControlType.Display => "Display",
        ControlType.Slider => "Slider",
        _ => Type.ToString(),
    };

    public ControlBindingViewModel? AssignedBinding
    {
        get => _assignedBinding;
        set
        {
            if (ReferenceEquals(_assignedBinding, value))
            {
                return;
            }

            if (_assignedBinding is not null)
            {
                _assignedBinding.Changed -= OnBindingChanged;
            }

            _assignedBinding = value;

            if (_assignedBinding is not null)
            {
                _assignedBinding.Changed += OnBindingChanged;
            }

            OnPropertyChanged(nameof(AssignedBinding));
            RefreshComputedState();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(SurfaceBrush));
            }
        }
    }

    public string AssignmentLabel => AssignedBinding?.DisplayName ?? "Drop action here";

    public bool HasAssignment => AssignedBinding is not null;

    public string CapabilitySummary => $"{InputsLabel} / {OutputsLabel}";

    public string PreviewText => string.IsNullOrWhiteSpace(AssignedBinding?.AppearanceText)
        ? AssignedBinding?.PrimaryAction.Name ?? DisplayName
        : AssignedBinding.AppearanceText;

    public string PreviewImageLabel => string.IsNullOrWhiteSpace(AssignedBinding?.AppearanceImagePath)
        ? string.Empty
        : System.IO.Path.GetFileName(AssignedBinding.AppearanceImagePath);

    public bool HasPreviewImage => !string.IsNullOrWhiteSpace(AssignedBinding?.AppearanceImagePath);

    public bool ShowPreviewLabel => SupportsAppearance;

    public IBrush SurfaceBrush => IsSelected ? SelectedBrush : AssignedBinding is null ? DefaultBrush : AssignedBrush;

    public double CornerRadius => Type == ControlType.Encoder ? Width / 2d : 14d;

    private void OnBindingChanged(object? sender, EventArgs e)
    {
        RefreshComputedState();
    }

    private void RefreshComputedState()
    {
        OnPropertyChanged(nameof(HasAssignment));
        OnPropertyChanged(nameof(AssignmentLabel));
        OnPropertyChanged(nameof(SurfaceBrush));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(PreviewImageLabel));
        OnPropertyChanged(nameof(HasPreviewImage));
    }

    private static string FormatCapabilities(InputCapabilities capabilities)
    {
        if (capabilities == InputCapabilities.None)
        {
            return "None";
        }

        return string.Join(", ", Enum.GetValues<InputCapabilities>()
            .Where(flag => flag is not InputCapabilities.None and not InputCapabilities.All && capabilities.HasFlag(flag)));
    }

    private static string FormatCapabilities(OutputCapabilities capabilities)
    {
        if (capabilities == OutputCapabilities.None)
        {
            return "None";
        }

        return string.Join(", ", Enum.GetValues<OutputCapabilities>()
            .Where(flag => flag is not OutputCapabilities.None and not OutputCapabilities.All && capabilities.HasFlag(flag)));
    }
}

internal sealed class EmptyActionCollection : ObservableCollection<ActionItemViewModel>
{
    public static EmptyActionCollection Instance { get; } = new();
}
