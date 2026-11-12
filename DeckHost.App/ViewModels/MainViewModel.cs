using Avalonia.Threading;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Models;
using DeckHost.Core.Plugins;
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
    private readonly PluginCatalog _pluginCatalog;
    private readonly ProfileStore? _profileStore;
    private readonly IPluginStateTransfer? _pluginState;
    private readonly DispatcherTimer _saveTimer;
    private readonly Dictionary<string, ActionItemViewModel> _actionsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ControlItemViewModel> _controlsById = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressAppearanceSync;
    private bool _suppressHostSync;
    private DeviceDefinition? _selectedDevice;
    private string _searchText = string.Empty;
    private ControlItemViewModel? _selectedControl;
    private ProfileItemViewModel? _selectedProfile;
    private string _statusMessage = "Select a profile, then drag plugin actions onto the device.";
    private string _connectionStatus = "Not Started";
    private string _connectionDetail = "The runtime host has not started yet.";
    private string _selectedAppearanceText = string.Empty;
    private string _selectedAppearanceImagePath = string.Empty;
    private readonly Platform.ForegroundAppWatcher _foregroundAppWatcher = new();
    private string? _manualProfileId;
    private bool _autoSwitchEnabled = true;
    private bool _switchingAutomatically;

    public MainViewModel(IDeviceHostService deviceHostService, PluginCatalog pluginCatalog, ProfileStore? profileStore = null, IPluginStateTransfer? pluginState = null)
    {
        ArgumentNullException.ThrowIfNull(deviceHostService);
        ArgumentNullException.ThrowIfNull(pluginCatalog);

        _deviceHostService = deviceHostService;
        _pluginCatalog = pluginCatalog;
        _profileStore = profileStore;
        _pluginState = pluginState;
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => FlushPendingSave();

        SelectedDevice = _deviceHostService.Devices.FirstOrDefault()
            ?? throw new InvalidOperationException("No device plugin provided a device definition.");

        foreach (var control in SelectedDevice.Controls.Select(static control => new ControlItemViewModel(control)))
        {
            ControlItems.Add(control);
            _controlsById[control.Id] = control;
        }

        // The host raises these on its worker thread; the UI may only be touched on the UI thread.
        _deviceHostService.RuntimeLogReceived += entry => Dispatcher.UIThread.Post(() => OnRuntimeLogReceived(entry));
        _deviceHostService.ConnectionStatusChanged += status => Dispatcher.UIThread.Post(() => OnConnectionStatusChanged(status));
        _deviceHostService.DevicesChanged += () => Dispatcher.UIThread.Post(OnDevicesChanged);
        _deviceHostService.ActiveProfileChanged += (deviceId, profileId) => Dispatcher.UIThread.Post(() => OnHostActiveProfileChanged(deviceId, profileId));
        _deviceHostService.ControlFrameRendered += frame => Dispatcher.UIThread.Post(() => OnControlFrameRendered(frame));
        _deviceHostService.InputReceived += input => Dispatcher.UIThread.Post(() => OnInputReceived(input));
        _pluginCatalog.Changed += (_, _) => Dispatcher.UIThread.Post(OnPluginsChanged);

        RebuildActions();
        LoadProfiles();
        SelectControl(ControlItems[0].Control);
        SeedRuntimeState();

        _foregroundAppWatcher.ForegroundAppChanged += OnForegroundAppChanged;
        _foregroundAppWatcher.Start();
    }

    public PluginCatalog PluginCatalog => _pluginCatalog;

    public IDeviceHostService DeviceHost => _deviceHostService;

    public ObservableCollection<PluginActionGroupViewModel> ActionGroups { get; } = new();

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
                if (!_switchingAutomatically && value is not null)
                {
                    // Chosen by hand (or by a Switch Profile key): the profile to return to when no app rule matches.
                    _manualProfileId = value.Id;
                }

                OnPropertyChanged(nameof(SelectedProfileName));
                OnPropertyChanged(nameof(SelectedProfileSummary));
                ApplyProfileBindings();
                if (!_suppressHostSync)
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

    /// <summary>
    /// The device's screen color between the controls for the selected profile (its background
    /// color, else the base color), so the canvas shows what the device shows.
    /// </summary>
    public Avalonia.Media.IBrush CanvasBackground
    {
        get
        {
            var (red, green, blue) = Core.Rendering.ProfileBackdrop.GetColor(SelectedProfile is null ? null : new ProfileBackground(SelectedProfile.BackgroundColor));
            return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(red, green, blue));
        }
    }

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

    public ObservableCollection<StepItemViewModel> SelectedMultiActionSteps =>
        SelectedControl?.AssignedBinding?.Steps ?? EmptyStepCollection.Instance;

    // ---------------------------------------------------------------- assigning actions

    public bool CanDropAction(IDeckAction action, PhysicalControl targetControl) =>
        TryGetActionItem(action, out var actionItem) && actionItem.FitsControl(targetControl);

    public void AssignActionToControl(PhysicalControl control, IDeckAction action)
    {
        if (SelectedProfile is null ||
            !_controlsById.ContainsKey(control.Id) ||
            !TryGetActionItem(action, out var actionItem))
        {
            return;
        }

        SelectControl(control);

        if (!actionItem.FitsControl(control))
        {
            StatusMessage = $"{action.Name} is not compatible with {control.DisplayName}.";
            return;
        }

        var existing = SelectedProfile.GetBinding(control.Id);
        var appearance = existing?.Model.Appearance ?? new ControlAppearance();
        ControlBinding updated;

        if (actionItem.IsMultiActionScaffold)
        {
            IReadOnlyList<MultiActionStep> steps = existing switch
            {
                { IsMultiAction: true } => existing.Model.Steps,
                { PrimaryAction.CanBeMultiActionStep: true } => [new MultiActionStep(0, existing.Model.Action)],
                _ => [],
            };

            updated = new ControlBinding(control.Id, ProfileActionKind.MultiAction, actionItem.ToActionReference(), appearance, steps);
            StatusMessage = existing is null
                ? $"Created a Multi Action on {control.DisplayName}. Add steps by dropping actions on it or in Customize."
                : $"Converted {control.DisplayName} into a Multi Action.";
        }
        else if (existing?.IsMultiAction == true)
        {
            if (!actionItem.CanBeMultiActionStep)
            {
                StatusMessage = $"{actionItem.Name} cannot be a Multi Action step.";
                return;
            }

            var step = new MultiActionStep(existing.Model.Steps.Count, actionItem.ToActionReference(actionItem.DefaultSettings));
            updated = existing.Model with { Steps = [.. existing.Model.Steps, step] };
            StatusMessage = $"Added {actionItem.Name} as step {updated.Steps.Count} on {control.DisplayName}.";
        }
        else
        {
            updated = new ControlBinding(control.Id, ProfileActionKind.Single, actionItem.ToActionReference(actionItem.DefaultSettings), appearance, []);
            StatusMessage = actionItem.HasSettings
                ? $"Assigned {actionItem.Name} to {control.DisplayName}. Right-click it and choose Customize to set it up."
                : $"Assigned {actionItem.Name} to {control.DisplayName}.";
        }

        ApplyBinding(control.Id, updated);
    }

    public void RemoveAssignment(PhysicalControl control)
    {
        if (SelectedProfile is null || !_controlsById.ContainsKey(control.Id))
        {
            return;
        }

        ApplyBinding(control.Id, null);
        StatusMessage = $"Removed the mapping from {control.DisplayName}.";
    }

    public void RemoveAssignment(string controlId)
    {
        if (_controlsById.TryGetValue(controlId, out var controlItem))
        {
            RemoveAssignment(controlItem.Control);
        }
    }

    public bool CanMoveBinding(string sourceControlId, string targetControlId) =>
        SelectedProfile?.GetBinding(sourceControlId) is { } source &&
        _controlsById.TryGetValue(targetControlId, out var target) &&
        source.PrimaryAction.FitsControl(target.Control);

    /// <summary>
    /// Moves a binding (with its settings, appearance and steps) to another control. When the
    /// target is taken the two swap places; with <paramref name="copy"/> the source keeps its binding.
    /// </summary>
    public void MoveBinding(string sourceControlId, string targetControlId, bool copy)
    {
        if (SelectedProfile is null ||
            SelectedProfile.GetBinding(sourceControlId) is not { } source ||
            !_controlsById.TryGetValue(sourceControlId, out var sourceControl) ||
            !_controlsById.TryGetValue(targetControlId, out var targetControl))
        {
            return;
        }

        if (!source.PrimaryAction.FitsControl(targetControl.Control))
        {
            StatusMessage = $"{source.DisplayName} doesn't fit on {targetControl.DisplayName}.";
            return;
        }

        var moved = source.Model with { ControlId = targetControlId };
        var existing = SelectedProfile.GetBinding(targetControlId);
        var deviceId = SelectedDevice!.Id;
        var profileId = SelectedProfile.Id;

        // Plugin-held settings (Stream Deck plugins) travel with the binding.
        if (copy)
        {
            _pluginState?.MoveControlSettings(deviceId, profileId, sourceControlId, targetControlId, copy: true);
            ApplyBinding(targetControlId, moved);
            StatusMessage = $"Copied {source.DisplayName} to {targetControl.DisplayName}.";
        }
        else if (existing is null)
        {
            _pluginState?.MoveControlSettings(deviceId, profileId, sourceControlId, targetControlId, copy: false);
            ApplyBinding(targetControlId, moved);
            ApplyBinding(sourceControlId, null);
            StatusMessage = $"Moved {source.DisplayName} to {targetControl.DisplayName}.";
        }
        else if (existing.PrimaryAction.FitsControl(sourceControl.Control))
        {
            const string swapSlot = "__swap__";
            _pluginState?.MoveControlSettings(deviceId, profileId, targetControlId, swapSlot, copy: false);
            _pluginState?.MoveControlSettings(deviceId, profileId, sourceControlId, targetControlId, copy: false);
            _pluginState?.MoveControlSettings(deviceId, profileId, swapSlot, sourceControlId, copy: false);
            ApplyBinding(targetControlId, moved);
            ApplyBinding(sourceControlId, existing.Model with { ControlId = sourceControlId });
            StatusMessage = $"Swapped {sourceControl.DisplayName} and {targetControl.DisplayName}.";
        }
        else
        {
            StatusMessage = $"Can't swap: {existing.DisplayName} doesn't fit on {sourceControl.DisplayName}. Remove it first, or Ctrl+drag to copy.";
            return;
        }

        SelectControl(targetControl.Control);
    }

    /// <summary>Replaces (or with null removes) the selected profile's binding for a control.</summary>
    public void ApplyBinding(string controlId, ControlBinding? model)
    {
        if (SelectedProfile is null || !_controlsById.TryGetValue(controlId, out var controlItem))
        {
            return;
        }

        if (model is null)
        {
            SelectedProfile.RemoveBinding(controlId);
            AttachBinding(controlItem, null);
        }
        else if (Resolve(model) is { } binding)
        {
            SelectedProfile.SetBinding(binding);
            AttachBinding(controlItem, binding);
        }
        else
        {
            SelectedProfile.AddUnresolvedBinding(model);
            AttachBinding(controlItem, null);
        }

        SyncProfilesToHost();
    }

    // ---------------------------------------------------------------- customization

    public ControlCustomizationViewModel CreateCustomizationViewModel(string controlId)
    {
        if (!_controlsById.TryGetValue(controlId, out var controlItem))
        {
            throw new InvalidOperationException($"Unknown control '{controlId}'.");
        }

        _actionsByKey.TryGetValue(EditorActionIds.Key(EditorActionIds.BuiltInPluginId, EditorActionIds.DisplayContent), out var displayContent);
        var linkedDisplayName = controlItem.Control.DisplayControlId is { } displayId && _controlsById.TryGetValue(displayId, out var display)
            ? display.DisplayName
            : null;
        return new ControlCustomizationViewModel(controlItem, SelectedProfile?.GetBinding(controlId), _actionsByKey.Values, displayContent, linkedDisplayName);
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
                _manualProfileId ?? SelectedProfile?.Id,
                Profiles.Select(static profile => profile.ToModel()).ToList(),
                AutoSwitchEnabled));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not save profiles to {_profileStore.FilePath}: {ex.Message}";
        }
    }

    // ---------------------------------------------------------------- live preview and test mode

    private bool _isTestMode;

    /// <summary>When on, clicking a control presses it (and the wheel turns knobs) instead of selecting/dragging.</summary>
    public bool IsTestMode
    {
        get => _isTestMode;
        set
        {
            if (SetProperty(ref _isTestMode, value))
            {
                StatusMessage = value
                    ? "Test mode: click controls to press them, use the mouse wheel on knobs, click the left/right half of the swipe bar."
                    : "Test mode off.";
            }
        }
    }

    /// <summary>Sends a simulated input for a control through the normal runtime path.</summary>
    public void Simulate(ControlItemViewModel control, Contracts.Enums.InputCapabilities inputEvent, int value)
    {
        if (SelectedDevice is null)
        {
            return;
        }

        _deviceHostService.SimulateInput(new DeviceInputEvent(SelectedDevice.Id, control.Id, inputEvent, value));
    }

    private void OnControlFrameRendered(ControlFrame frame)
    {
        if (SelectedDevice is not null &&
            string.Equals(frame.DeviceId, SelectedDevice.Id, StringComparison.OrdinalIgnoreCase) &&
            _controlsById.TryGetValue(frame.ControlId, out var control))
        {
            control.SetPreview(frame.Width, frame.Height, frame.Rgba);
        }
    }

    private void OnInputReceived(DeviceInputEvent input)
    {
        if (SelectedDevice is not null &&
            string.Equals(input.DeviceId, SelectedDevice.Id, StringComparison.OrdinalIgnoreCase) &&
            _controlsById.TryGetValue(input.ControlId, out var control))
        {
            control.ShowInput(input.EventType, input.Value);
        }
    }

    // ---------------------------------------------------------------- profile management

    /// <summary>Whether profiles follow the focused app (see each profile's AutoSwitchApps).</summary>
    public bool AutoSwitchEnabled
    {
        get => _autoSwitchEnabled;
        set
        {
            if (SetProperty(ref _autoSwitchEnabled, value))
            {
                ScheduleSave();
                if (value && _foregroundAppWatcher.CurrentApp is { } app)
                {
                    OnForegroundAppChanged(app);
                }
                else if (!value)
                {
                    ReturnToManualProfile();
                }
            }
        }
    }

    public bool AutoSwitchSupported => Platform.ForegroundAppWatcher.IsSupported;

    public string CurrentAppName => _foregroundAppWatcher.CurrentApp ?? "—";

    public ProfileItemViewModel CreateProfile(string name, ProfileItemViewModel? copyFrom = null)
    {
        // A duplicate is a full working copy: bindings, appearance, background, app rules and plugin settings.
        var source = copyFrom?.ToModel();
        var profile = new ProfileItemViewModel($"profile.{Guid.NewGuid():N}"[..20], UniqueProfileName(name), SelectedDevice!.Id, source?.AutoSwitchApps, source?.Background);
        if (copyFrom is not null && source is not null)
        {
            _pluginState?.CopyProfileSettings(copyFrom.Id, profile.Id);
            foreach (var binding in source.Bindings)
            {
                if (Resolve(binding) is { } resolved)
                {
                    profile.SetBinding(resolved);
                }
                else
                {
                    profile.AddUnresolvedBinding(binding);
                }
            }
        }

        Profiles.Add(profile);
        NotifyProfilesEdited();
        StatusMessage = copyFrom is null ? $"Created profile {profile.Name}." : $"Duplicated {copyFrom.Name} as {profile.Name}.";
        return profile;
    }

    /// <summary>Deletes a profile. The last profile can't be deleted.</summary>
    public bool DeleteProfile(ProfileItemViewModel profile)
    {
        if (Profiles.Count <= 1 || !Profiles.Contains(profile))
        {
            return false;
        }

        var index = Profiles.IndexOf(profile);
        if (_manualProfileId == profile.Id)
        {
            _manualProfileId = null;
        }

        if (SelectedProfile == profile)
        {
            SelectedProfile = Profiles[index == 0 ? 1 : index - 1];
        }

        Profiles.Remove(profile);
        NotifyProfilesEdited();
        StatusMessage = $"Deleted profile {profile.Name}.";
        return true;
    }

    /// <summary>Moves a profile up (-1) or down (+1); the order is also used by Next/Previous Profile.</summary>
    public void MoveProfile(ProfileItemViewModel profile, int offset)
    {
        var index = Profiles.IndexOf(profile);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Profiles.Count)
        {
            return;
        }

        Profiles.Move(index, target);
        NotifyProfilesEdited();
    }

    /// <summary>Call after renaming a profile or changing its app list.</summary>
    public void NotifyProfilesEdited()
    {
        SyncProfilesToHost();
        OnPropertyChanged(nameof(SelectedProfileName));
    }

    private string UniqueProfileName(string name)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "Profile" : name.Trim();
        var candidate = baseName;
        for (var number = 2; Profiles.Any(profile => string.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase)); number++)
        {
            candidate = $"{baseName} {number}";
        }

        return candidate;
    }

    private void OnForegroundAppChanged(string appName)
    {
        OnPropertyChanged(nameof(CurrentAppName));
        if (!AutoSwitchEnabled || SelectedDevice is null)
        {
            return;
        }

        var match = AutoProfileSelector.FindProfileForApp(Profiles.Select(static profile => profile.ToModel()), SelectedDevice.Id, appName);
        if (match is null)
        {
            ReturnToManualProfile();
            return;
        }

        if (Profiles.FirstOrDefault(profile => profile.Id == match.Id) is { } profile && profile != SelectedProfile)
        {
            ActivateAutomatically(profile);
            StatusMessage = $"Switched to {profile.Name} because {appName} is active.";
        }
    }

    private void ReturnToManualProfile()
    {
        if (_manualProfileId is not null &&
            SelectedProfile?.Id != _manualProfileId &&
            Profiles.FirstOrDefault(profile => profile.Id == _manualProfileId) is { } manual)
        {
            ActivateAutomatically(manual);
            StatusMessage = $"Back to {manual.Name}.";
        }
    }

    private void ActivateAutomatically(ProfileItemViewModel profile)
    {
        _switchingAutomatically = true;
        try
        {
            SelectedProfile = profile;
        }
        finally
        {
            _switchingAutomatically = false;
        }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ---------------------------------------------------------------- plugins

    private void OnPluginsChanged()
    {
        _deviceHostService.SetApplicationPlugins(_pluginCatalog.EnabledApplicationPlugins);
        RebuildActions();

        // Bindings to a disabled plugin become "unavailable" (kept); re-enabled ones come back.
        foreach (var profile in Profiles)
        {
            foreach (var model in profile.TakeAllBindings())
            {
                if (Resolve(model) is { } binding)
                {
                    profile.SetBinding(binding);
                }
                else
                {
                    profile.AddUnresolvedBinding(model);
                }
            }
        }

        ApplyProfileBindings();
        SyncProfilesToHost();
    }

    private void RebuildActions()
    {
        _actionsByKey.Clear();
        foreach (var plugin in _pluginCatalog.EnabledApplicationPlugins)
        {
            foreach (var action in plugin.Actions)
            {
                var item = ActionItemViewModel.Create(plugin.Id, plugin.Name, action);
                _actionsByKey.TryAdd(item.Key, item);
            }
        }

        RefreshActionGroups();
    }

    // ---------------------------------------------------------------- profiles

    private void LoadProfiles()
    {
        _suppressHostSync = true;
        try
        {
            var document = TryLoadDocument();
            var unresolved = 0;

            foreach (var profile in document?.Profiles ?? [])
            {
                var item = new ProfileItemViewModel(profile.Id, profile.Name, profile.DeviceId, profile.AutoSwitchApps, profile.Background);
                foreach (var binding in profile.Bindings)
                {
                    if (Resolve(binding) is { } bindingViewModel)
                    {
                        item.SetBinding(bindingViewModel);
                    }
                    else
                    {
                        item.AddUnresolvedBinding(binding);
                        unresolved++;
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
            _autoSwitchEnabled = document?.AutoSwitchEnabled ?? true;

            if (unresolved > 0)
            {
                StatusMessage = $"{unresolved} saved mapping(s) use plugins that aren't enabled; they are kept but inactive.";
            }
        }
        finally
        {
            _suppressHostSync = false;
        }

        _deviceHostService.SetApplicationPlugins(_pluginCatalog.EnabledApplicationPlugins);
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

    /// <summary>Links a binding to the loaded actions; null when its plugin (or a step's) isn't enabled.</summary>
    private ControlBindingViewModel? Resolve(ControlBinding binding)
    {
        if (!_actionsByKey.TryGetValue(EditorActionIds.Key(binding.Action.PluginId, binding.Action.ActionId), out var primary))
        {
            return null;
        }

        var steps = new List<StepItemViewModel>();
        foreach (var step in binding.Steps.OrderBy(static step => step.Order))
        {
            if (!_actionsByKey.TryGetValue(EditorActionIds.Key(step.Action.PluginId, step.Action.ActionId), out var stepAction))
            {
                return null;
            }

            steps.Add(new StepItemViewModel(stepAction, step.Action.EffectiveSettings));
        }

        return new ControlBindingViewModel(binding, primary, steps);
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
        OnPropertyChanged(nameof(CanvasBackground));
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnHostActiveProfileChanged(string deviceId, string profileId)
    {
        // A Switch Profile action on the device changed the profile: follow it in the editor.
        if (SelectedDevice is null ||
            !string.Equals(deviceId, SelectedDevice.Id, StringComparison.OrdinalIgnoreCase) ||
            SelectedProfile?.Id == profileId ||
            Profiles.FirstOrDefault(profile => profile.Id == profileId) is not { } profile)
        {
            return;
        }

        _suppressHostSync = true;
        try
        {
            SelectedProfile = profile;
        }
        finally
        {
            _suppressHostSync = false;
        }

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
            .Where(action => action.ShowInPalette)
            .Where(action => string.IsNullOrWhiteSpace(SearchText)
                || action.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || action.PluginName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || (action.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false))
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
        OnPropertyChanged(nameof(SelectedProfileSummary));
        OnPropertyChanged(nameof(CanvasBackground));
        StatusMessage = SelectedProfile is null ? "No profile selected." : $"Editing profile {SelectedProfile.Name}.";
    }

    private void AttachBinding(ControlItemViewModel controlItem, ControlBindingViewModel? binding)
    {
        controlItem.AssignedBinding = binding;

        if (SelectedControl == controlItem)
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

    /// <summary>Appearance edits from the inspector. An empty control gets a text/image-only binding.</summary>
    private void UpdateSelectedControlAppearance(string? text, string? imagePath)
    {
        if (SelectedControl is null || SelectedProfile is null || !SelectedControl.SupportsAppearance)
        {
            return;
        }

        var existing = SelectedProfile.GetBinding(SelectedControl.Id);
        var newText = text ?? existing?.AppearanceText ?? string.Empty;
        var newImage = imagePath ?? existing?.AppearanceImagePath ?? string.Empty;
        // Everything else (text style, background color, light) stays as it is.
        var appearance = (existing?.Model.Appearance ?? new ControlAppearance()) with
        {
            Text = string.IsNullOrWhiteSpace(newText) ? null : newText,
            ImagePath = string.IsNullOrWhiteSpace(newImage) ? null : newImage,
        };

        if (existing is null)
        {
            if (appearance.Text is null && appearance.ImagePath is null ||
                !_actionsByKey.TryGetValue(EditorActionIds.Key(EditorActionIds.BuiltInPluginId, EditorActionIds.DisplayContent), out var displayAction))
            {
                return;
            }

            ApplyBinding(SelectedControl.Id, new ControlBinding(SelectedControl.Id, ProfileActionKind.Single, displayAction.ToActionReference(), appearance, []));
            return;
        }

        if (existing.IsDisplayContentOnly && appearance.Text is null && appearance.ImagePath is null && appearance.BackgroundColor is null)
        {
            ApplyBinding(SelectedControl.Id, null);
            return;
        }

        ApplyBinding(SelectedControl.Id, existing.Model with { Appearance = appearance });
    }

    private bool TryGetActionItem(IDeckAction action, out ActionItemViewModel actionItem)
    {
        actionItem = _actionsByKey.Values.FirstOrDefault(candidate => ReferenceEquals(candidate.Action, action))!;
        return actionItem is not null;
    }
}
