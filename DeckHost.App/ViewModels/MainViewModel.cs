using Avalonia.Threading;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Models;
using DeckHost.Core.Profiles;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DeckHost.App.ViewModels;

public class MainViewModel : ViewModelBase
{
    private const int MaxVisibleLogEntries = 200;
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly IDeviceHostService _deviceHostService;
    private readonly ProfileStore? _profileStore;
    private readonly DispatcherTimer _saveTimer;
    private readonly Dictionary<string, ActionItemViewModel> _actionsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ControlItemViewModel> _controlsById = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressAppearanceSync;
    private bool _loadingProfiles;
    private DeviceDefinition? _selectedDevice;
    private string _searchText = string.Empty;
    private ControlItemViewModel? _selectedControl;
    private ProfileItemViewModel? _selectedProfile;
    private string _statusMessage = "Select a profile, then drag plugin actions onto the device.";
    private string _connectionStatus = "Not Started";
    private string _connectionDetail = "The runtime host has not started yet.";
    private string _selectedAppearanceText = string.Empty;
    private string _selectedAppearanceImagePath = string.Empty;

    public MainViewModel(
        IDeviceHostService deviceHostService,
        IEnumerable<IDevicePlugin> devicePlugins,
        IEnumerable<IApplicationPlugin> applicationPlugins,
        ProfileStore? profileStore = null)
    {
        ArgumentNullException.ThrowIfNull(deviceHostService);
        ArgumentNullException.ThrowIfNull(devicePlugins);
        ArgumentNullException.ThrowIfNull(applicationPlugins);

        _deviceHostService = deviceHostService;
        _profileStore = profileStore;
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => FlushPendingSave();

        SelectedDevice = _deviceHostService.Devices.FirstOrDefault()
            ?? throw new InvalidOperationException("No device plugin provided a device definition.");

        foreach (var control in SelectedDevice.Controls.Select(static control => new ControlItemViewModel(control)))
        {
            ControlItems.Add(control);
            _controlsById[control.Id] = control;
        }

        foreach (var plugin in devicePlugins)
        {
            PluginItems.Add(new PluginSummaryViewModel(
                plugin.Id,
                plugin.Name,
                "Device Plugin",
                $"{plugin.Devices.Count} device definition" + (plugin.Devices.Count == 1 ? string.Empty : "s"),
                "Available"));
        }

        var applicationPluginList = applicationPlugins.ToList();
        foreach (var plugin in applicationPluginList)
        {
            PluginItems.Add(new PluginSummaryViewModel(
                plugin.Id,
                plugin.Name,
                "Application Plugin",
                $"{plugin.Actions.Count} action" + (plugin.Actions.Count == 1 ? string.Empty : "s"),
                "Loaded"));

            foreach (var action in plugin.Actions)
            {
                var item = ActionItemViewModel.Create(plugin.Id, plugin.Name, action);
                _actionsByKey.TryAdd(item.Key, item);
            }
        }

        // The host raises these on its worker thread; the UI may only be touched on the UI thread.
        _deviceHostService.RuntimeLogReceived += entry => Dispatcher.UIThread.Post(() => OnRuntimeLogReceived(entry));
        _deviceHostService.ConnectionStatusChanged += status => Dispatcher.UIThread.Post(() => OnConnectionStatusChanged(status));
        _deviceHostService.DevicesChanged += () => Dispatcher.UIThread.Post(OnDevicesChanged);

        RefreshActionGroups();
        LoadProfiles();
        SelectControl(ControlItems[0].Control);
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
        private set
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
                RaiseSelectedAssignmentChanged();
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
                OnPropertyChanged(nameof(SelectedProfileName));
                OnPropertyChanged(nameof(SelectedProfileSummary));
                ApplyProfileBindings();
                if (!_loadingProfiles)
                {
                    SyncProfilesToHost();
                }
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

    public string DeviceName => SelectedDevice is null
        ? "No device selected"
        : SelectedDevice.IsConnected ? SelectedDevice.Name : $"{SelectedDevice.Name} (offline)";

    /// <summary>Pages are not implemented yet; a profile is one page of the device.</summary>
    public string CurrentPageName => SelectedProfile?.Name ?? "No profile";

    public string SelectedProfileName => SelectedProfile?.Name ?? "No profile selected";

    public string SelectedProfileSummary => SelectedProfile?.Summary ?? "No active mappings";

    public bool HasRuntimeLogs => RuntimeLogs.Count > 0;

    public double CanvasWidth => ControlItems.Count == 0 ? 0d : ControlItems.Max(static control => control.X + control.Width) + 24;

    public double CanvasHeight => ControlItems.Count == 0 ? 0d : ControlItems.Max(static control => control.Y + control.Height) + 24;

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
                ? $"Created a Multi Action on {control.DisplayName}."
                : $"Converted {control.DisplayName} into a Multi Action.";
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
            controlItem.SupportsLight,
            binding?.AppearanceText ?? string.Empty,
            binding?.AppearanceImagePath ?? string.Empty,
            binding?.LightFeedback);
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
        }

        if (customization.SupportsLight && !customization.HasLightError)
        {
            var binding = SelectedProfile?.GetBinding(controlItem.Id);
            if (binding is null && customization.LightEnabled)
            {
                StatusMessage = $"Assign an action to {controlItem.DisplayName} before setting its light.";
                return;
            }

            binding?.SetLightFeedback(customization.ToRgbFeedback());
            SyncProfilesToHost();
        }

        StatusMessage = $"Updated {controlItem.DisplayName}.";
    }

    public void SelectControl(PhysicalControl control)
    {
        if (_controlsById.TryGetValue(control.Id, out var controlItem))
        {
            SelectedControl = controlItem;
        }
    }

    /// <summary>Writes pending profile changes right away (called on shutdown).</summary>
    public void FlushPendingSave()
    {
        _saveTimer.Stop();
        if (_profileStore is null)
        {
            return;
        }

        try
        {
            _profileStore.Save(new ProfileDocument(
                ProfileDocument.CurrentVersion,
                SelectedProfile?.Id,
                Profiles.Select(static profile => profile.ToModel()).ToList()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not save profiles to {_profileStore.FilePath}: {ex.Message}";
        }
    }

    // ---------------------------------------------------------------- profiles

    private void LoadProfiles()
    {
        _loadingProfiles = true;
        try
        {
            var document = TryLoadDocument();
            var unresolved = 0;

            foreach (var profile in document?.Profiles ?? [])
            {
                var item = new ProfileItemViewModel(profile.Id, profile.Name, profile.DeviceId);
                foreach (var binding in profile.Bindings)
                {
                    var bindingViewModel = FromModel(binding);
                    if (bindingViewModel is null)
                    {
                        item.AddUnresolvedBinding(binding);
                        unresolved++;
                    }
                    else
                    {
                        item.SetBinding(bindingViewModel);
                    }
                }

                Profiles.Add(item);
            }

            if (Profiles.Count == 0)
            {
                Profiles.Add(new ProfileItemViewModel("profile.default", "Default", SelectedDevice!.Id));
                Profiles.Add(new ProfileItemViewModel("profile.streaming", "Streaming", SelectedDevice.Id));
                Profiles.Add(new ProfileItemViewModel("profile.media", "Media", SelectedDevice.Id));
            }

            SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == document?.SelectedProfileId) ?? Profiles[0];

            if (unresolved > 0)
            {
                StatusMessage = $"{unresolved} saved mapping(s) use actions that aren't loaded; they are kept but inactive.";
            }
        }
        finally
        {
            _loadingProfiles = false;
        }

        _deviceHostService.SetProfiles(Profiles.Select(static profile => profile.ToModel()).ToList());
        if (SelectedProfile is not null)
        {
            _deviceHostService.SetActiveProfile(SelectedProfile.Id);
        }
    }

    private ProfileDocument? TryLoadDocument()
    {
        if (_profileStore is null)
        {
            return null;
        }

        try
        {
            return _profileStore.Load();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Keep the unreadable file for the user instead of overwriting it with defaults.
            var backupPath = $"{_profileStore.FilePath}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Move(_profileStore.FilePath, backupPath);
            }
            catch (Exception moveException) when (moveException is IOException or UnauthorizedAccessException)
            {
                backupPath = _profileStore.FilePath;
            }

            StatusMessage = $"Could not read saved profiles ({ex.Message}). The file was kept at {backupPath}.";
            return null;
        }
    }

    private ControlBindingViewModel? FromModel(ControlBinding binding)
    {
        if (!_actionsByKey.TryGetValue(EditorActionIds.Key(binding.Action.PluginId, binding.Action.ActionId), out var primary))
        {
            return null;
        }

        var steps = new List<ActionItemViewModel>();
        foreach (var step in binding.Steps.OrderBy(static step => step.Order))
        {
            if (!_actionsByKey.TryGetValue(EditorActionIds.Key(step.Action.PluginId, step.Action.ActionId), out var stepAction))
            {
                return null;
            }

            steps.Add(stepAction);
        }

        return new ControlBindingViewModel(
            binding.ControlId,
            binding.ActionKind,
            primary,
            binding.Appearance.Text,
            binding.Appearance.ImagePath,
            steps,
            binding.Appearance.RgbFeedback);
    }

    private void SyncProfilesToHost()
    {
        _deviceHostService.SetProfiles(Profiles.Select(static profile => profile.ToModel()).ToList());

        if (SelectedProfile is not null)
        {
            _deviceHostService.SetActiveProfile(SelectedProfile.Id);
        }

        OnPropertyChanged(nameof(SelectedProfileSummary));
        OnPropertyChanged(nameof(CurrentPageName));
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ---------------------------------------------------------------- runtime state

    private void SeedRuntimeState()
    {
        RuntimeLogs.Clear();
        foreach (var entry in _deviceHostService.RuntimeLogs.TakeLast(MaxVisibleLogEntries))
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

        if (status is not null)
        {
            ConnectionStatus = status.State;
            ConnectionDetail = status.Message;
        }
    }

    private void OnRuntimeLogReceived(RuntimeLogEntry entry)
    {
        RuntimeLogs.Add(entry);
        while (RuntimeLogs.Count > MaxVisibleLogEntries)
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

    private void OnDevicesChanged()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var updated = _deviceHostService.Devices.FirstOrDefault(device => string.Equals(device.Id, SelectedDevice.Id, StringComparison.OrdinalIgnoreCase));
        if (updated is not null)
        {
            SelectedDevice = updated;
            OnPropertyChanged(nameof(DeviceName));
        }

        RefreshConnectionStatus();
    }

    // ---------------------------------------------------------------- editor helpers

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
        RaiseSelectedAssignmentChanged();
        OnPropertyChanged(nameof(CurrentPageName));
        StatusMessage = SelectedProfile is null ? "No profile selected." : $"Editing profile {SelectedProfile.Name}.";
    }

    private void AttachBinding(ControlItemViewModel controlItem, ControlBindingViewModel? binding)
    {
        if (controlItem.AssignedBinding is not null)
        {
            controlItem.AssignedBinding.Changed -= OnBindingChanged;
        }

        controlItem.AssignedBinding = binding;

        if (binding is not null)
        {
            binding.Changed -= OnBindingChanged;
            binding.Changed += OnBindingChanged;
        }

        if (SelectedControl == controlItem)
        {
            SyncSelectedAppearanceEditors();
            RaiseSelectedAssignmentChanged();
        }
    }

    private void OnBindingChanged(object? sender, EventArgs e)
    {
        if (SelectedControl?.AssignedBinding == sender)
        {
            SyncSelectedAppearanceEditors();
            RaiseSelectedAssignmentChanged();
        }
    }

    private void RaiseSelectedAssignmentChanged()
    {
        OnPropertyChanged(nameof(SelectedAssignmentName));
        OnPropertyChanged(nameof(SelectedAssignmentCapabilities));
        OnPropertyChanged(nameof(HasSelectedAssignment));
        OnPropertyChanged(nameof(CanEditSelectedAppearance));
        OnPropertyChanged(nameof(SelectedAssignmentIsMultiAction));
        OnPropertyChanged(nameof(ShowMultiActionHint));
        OnPropertyChanged(nameof(SelectedMultiActionSteps));
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
            if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(imagePath))
            {
                return;
            }

            if (!_actionsByKey.TryGetValue(EditorActionIds.Key(EditorActionIds.BuiltInPluginId, EditorActionIds.DisplayContent), out var displayAction))
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

    private static ControlBindingViewModel CreateSingleActionBinding(string controlId, ActionItemViewModel actionItem, ControlBindingViewModel? existingBinding) =>
        new(
            controlId,
            ProfileActionKind.Single,
            actionItem,
            existingBinding?.AppearanceText,
            existingBinding?.AppearanceImagePath,
            [],
            existingBinding?.LightFeedback);

    private ControlBindingViewModel CreateOrPromoteToMultiActionBinding(string controlId, ControlBindingViewModel? existingBinding)
    {
        var multiAction = _actionsByKey[EditorActionIds.Key(EditorActionIds.BuiltInPluginId, EditorActionIds.MultiAction)];
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
            steps,
            existingBinding?.LightFeedback);
    }

    private bool TryGetActionItem(IDeckAction action, out ActionItemViewModel actionItem)
    {
        actionItem = _actionsByKey.Values.FirstOrDefault(candidate => ReferenceEquals(candidate.Action, action))
            ?? _actionsByKey.Values.FirstOrDefault(candidate => string.Equals(candidate.Id, action.Id, StringComparison.OrdinalIgnoreCase))!;
        return actionItem is not null;
    }
}
