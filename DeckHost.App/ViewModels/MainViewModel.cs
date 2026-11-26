using Avalonia.Threading;
using DeckHost.Contracts;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Models;
using DeckHost.Core.Plugins;
using DeckHost.Core.Profiles;
using DeckHost.Core.Workflows;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DeckHost.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDeviceLightSettings
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
    private bool _devicePickedByUser;

    /// <summary>The profiles (with their pages and folders) of the devices the editor doesn't show right now.</summary>
    private readonly List<ProfileItemViewModel> _otherDeviceProfiles = new();
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
        _workflowBreakpoints = new WorkflowBreakpoints(deviceHostService);
        _systemData = new Platform.SystemDataPublisher(deviceHostService);
        _pluginCatalog = pluginCatalog;
        _profileStore = profileStore;
        _pluginState = pluginState;
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => FlushPendingSave();

        UseDevice(_deviceHostService.Devices.FirstOrDefault()
            ?? throw new InvalidOperationException("No device plugin provided a device definition."));
        RefreshDeviceList();

        // The host raises these on its worker thread; the UI may only be touched on the UI thread.
        _deviceHostService.RuntimeLogReceived += entry => Dispatcher.UIThread.Post(() => OnRuntimeLogReceived(entry));
        _deviceHostService.ConnectionStatusChanged += status => Dispatcher.UIThread.Post(() => OnConnectionStatusChanged(status));
        _deviceHostService.DevicesChanged += () => Dispatcher.UIThread.Post(OnDevicesChanged);
        _deviceHostService.RosterChanged += () => Dispatcher.UIThread.Post(RefreshDiscovered);
        RefreshDiscovered();
        _deviceHostService.ActiveProfileChanged += (deviceId, profileId) => Dispatcher.UIThread.Post(() => OnHostActiveProfileChanged(deviceId, profileId));
        _deviceHostService.ControlFrameRendered += frame =>
        {
            if (_previewShown)
            {
                Dispatcher.UIThread.Post(() => OnControlFrameRendered(frame));
            }
        };
        _deviceHostService.InputReceived += input => Dispatcher.UIThread.Post(() => OnInputReceived(input));
        _deviceHostService.LightChanged += light => Dispatcher.UIThread.Post(() => OnLightChanged(light));
        _pluginCatalog.Changed += (_, _) => Dispatcher.UIThread.Post(OnPluginsChanged);

        RebuildActions();
        LoadProfiles();

        // Safe mode starts without an open editor: whatever the first control's editor builds (a plugin's web page) is what crashed.
        if (!Platform.SafeMode.IsOn)
        {
            SelectControl(ControlItems[0].Control);
        }

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
                OnPropertyChanged(nameof(PickedDevice));
            }
        }
    }

    /// <summary>The devices to choose from in the header (each with its own profiles).</summary>
    public ObservableCollection<DeviceChoiceViewModel> DeviceChoices { get; } = new();

    public bool HasSeveralDevices => DeviceChoices.Count > 1;

    /// <summary>The header's device picker.</summary>
    public DeviceChoiceViewModel? PickedDevice
    {
        get => DeviceChoices.FirstOrDefault(choice => SelectedDevice is not null && SameId(choice.Id, SelectedDevice.Id));
        set
        {
            if (value is not null && _deviceHostService.Devices.FirstOrDefault(device => SameId(device.Id, value.Id)) is { } device)
            {
                _devicePickedByUser = true;
                SwitchDevice(device);
            }
        }
    }

    /// <summary>Every layout of every device: what the host gets and what is saved.</summary>
    private IEnumerable<ProfileItemViewModel> EveryLayout => AllLayouts.Concat(_otherDeviceProfiles.SelectMany(static profile => profile.AllLayouts));

    private static bool SameId(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

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

    private bool _onlyCompatibleActions;

    /// <summary>The action list shows only the actions that fit the selected control (a checkbox in the action bar).</summary>
    public bool OnlyCompatibleActions
    {
        get => _onlyCompatibleActions;
        set
        {
            if (SetProperty(ref _onlyCompatibleActions, value))
            {
                RefreshActionGroups();
                OnlyCompatibleActionsChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>Raised when the user changes <see cref="OnlyCompatibleActions"/>, so the window can remember it.</summary>
    public event EventHandler<bool>? OnlyCompatibleActionsChanged;

    /// <summary>Sets <see cref="OnlyCompatibleActions"/> from the saved settings, without telling the window to save it again.</summary>
    public void LoadOnlyCompatibleActions(bool value)
    {
        _onlyCompatibleActions = value;
        OnPropertyChanged(nameof(OnlyCompatibleActions));
        RefreshActionGroups();
    }

    public ControlItemViewModel? SelectedControl
    {
        get => _selectedControl;
        set
        {
            if (SetProperty(ref _selectedControl, value))
            {
                RefreshActionGroups();
                foreach (var item in ControlItems)
                {
                    item.IsSelected = item == value;
                }

                SyncSelectedAppearanceEditors();
                RebuildEditor();
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
                    _manualProfileId = value.RootProfile.Id;
                }

                OnPropertyChanged(nameof(SelectedProfileName));
                OnPropertyChanged(nameof(SelectedProfileSummary));
                OnPropertyChanged(nameof(SelectedRootProfile));
                RefreshPageTabs();
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

    /// <summary>DeckHost started in safe mode (web views and extra plugins are off) after crashing at start.</summary>
    public bool IsSafeMode => Platform.SafeMode.IsOn;

    public string SafeModeText => $"Safe mode: the built-in browser and extra plugins (Stream Deck, OpenAction, installed ones) are off. {Platform.SafeMode.Reason}";

    private string _recoveredNotice = string.Empty;

    /// <summary>Set when an unexpected error was recovered from (a crash report was written) instead of closing DeckHost.</summary>
    public string RecoveredNotice
    {
        get => _recoveredNotice;
        private set
        {
            if (SetProperty(ref _recoveredNotice, value))
            {
                OnPropertyChanged(nameof(HasRecoveredNotice));
            }
        }
    }

    public bool HasRecoveredNotice => RecoveredNotice.Length > 0;

    /// <summary>The user asked to leave safe mode: the host clears what it remembers and starts DeckHost again.</summary>
    public event Action? RestartNormallyRequested;

    public void RestartNormally() => RestartNormallyRequested?.Invoke();

    public void ShowRecoveredNotice() =>
        RecoveredNotice = "DeckHost hit an unexpected error and carried on. A report was saved; you can send it from Help and support.";

    public void DismissRecoveredNotice() => RecoveredNotice = string.Empty;

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

    // ---------------------------------------------------------------- pages and folders

    /// <summary>Every profile with its pages and folders.</summary>
    public IEnumerable<ProfileItemViewModel> AllLayouts => Profiles.SelectMany(static profile => profile.AllLayouts);

    /// <summary>The profile the editor shows (the header's profile picker); the page or folder within it is <see cref="SelectedProfile"/>.</summary>
    public ProfileItemViewModel? SelectedRootProfile
    {
        get => SelectedProfile?.RootProfile;
        set
        {
            if (value is not null && value != SelectedProfile?.RootProfile)
            {
                SelectedProfile = value;
            }
        }
    }

    /// <summary>The page buttons under the device: one per page of the profile.</summary>
    public ObservableCollection<PageTabViewModel> PageTabs { get; } = [];

    public bool IsInFolder => SelectedProfile?.IsFolder == true;

    /// <summary>Where the open folder is: "Page 2 › Tools › Audio".</summary>
    public string FolderTrail
    {
        get
        {
            if (SelectedProfile is not { IsFolder: true } folder)
            {
                return string.Empty;
            }

            var names = new List<string>();
            for (ProfileItemViewModel? current = folder; current is not null && names.Count < 32; current = current.IsFolder ? current.ParentLayout : null)
            {
                names.Insert(0, current.IsFolder ? current.Name : PageName(current));
            }

            return string.Join("  ›  ", names);
        }
    }

    /// <summary>Adds a page to the profile and shows it.</summary>
    public void AddPage()
    {
        if (SelectedProfile?.RootProfile is not { } root)
        {
            return;
        }

        var number = root.PageLayouts.Count + 1;
        var page = new ProfileItemViewModel(NewLayoutId(root, "page"), $"Page {number}", root.DeviceId)
        {
            ParentId = root.Id,
            Kind = ProfileKinds.Page,
            Owner = root,
        };
        root.Layouts.Add(page);
        SelectedProfile = page;
        StatusMessage = $"Added page {number}. Put a Pages action on a key (or swipe the touch strip) to flip between pages on the device.";
    }

    /// <summary>Removes a page (not the first) with its folders.</summary>
    public void DeletePage(ProfileItemViewModel page)
    {
        if (page.IsRoot || page.Kind != ProfileKinds.Page)
        {
            return;
        }

        var root = page.RootProfile;
        var number = root.PageLayouts.ToList().IndexOf(page) + 1;
        RemoveLayoutTree(page);
        if (SelectedProfile is null || !root.AllLayouts.Contains(SelectedProfile))
        {
            SelectedProfile = root.PageLayouts[Math.Clamp(number - 2, 0, root.PageLayouts.Count - 1)];
        }
        else
        {
            RefreshPageTabs();
            SyncProfilesToHost();
        }

        StatusMessage = $"Removed page {number}.";
    }

    /// <summary>Leaves the open folder in the editor (and on the device).</summary>
    public void CloseFolder()
    {
        if (SelectedProfile is { IsFolder: true, ParentLayout: { } parent })
        {
            SelectedProfile = parent;
        }
    }

    /// <summary>Opens the folder of a Folder key in the editor (and on the device).</summary>
    public void OpenFolder(string folderId)
    {
        if (AllLayouts.FirstOrDefault(layout => layout.Id == folderId && layout.IsFolder) is { } folder)
        {
            SelectedProfile = folder;
        }
    }

    private static string PageName(ProfileItemViewModel layout) =>
        layout.IsRoot ? "Page 1" : $"Page {layout.RootProfile.PageLayouts.ToList().IndexOf(layout) + 1}";

    private void RefreshPageTabs()
    {
        PageTabs.Clear();
        if (SelectedProfile is { } current)
        {
            var currentPage = current.PageLayout;
            var number = 1;
            foreach (var page in current.RootProfile.PageLayouts)
            {
                PageTabs.Add(new PageTabViewModel(this, page, number++, page == currentPage));
            }
        }

        OnPropertyChanged(nameof(IsInFolder));
        OnPropertyChanged(nameof(FolderTrail));
        OnPropertyChanged(nameof(HasPages));
    }

    /// <summary>A layout is shown: the page bar shows its pages (always at least page 1) and +.</summary>
    public bool HasPages => PageTabs.Count > 0;

    private static string NewLayoutId(ProfileItemViewModel root, string kind) => $"{root.Id}.{kind}.{Guid.NewGuid():N}"[..Math.Min(80, root.Id.Length + kind.Length + 34)];

    /// <summary>Removes a page or folder and every folder inside it.</summary>
    private static void RemoveLayoutTree(ProfileItemViewModel layout)
    {
        var root = layout.RootProfile;
        var doomed = new HashSet<string>(StringComparer.Ordinal) { layout.Id };
        bool added;
        do
        {
            added = false;
            foreach (var candidate in root.Layouts.Where(candidate => candidate.ParentId is { } parent && doomed.Contains(parent) && !doomed.Contains(candidate.Id)).ToList())
            {
                added |= doomed.Add(candidate.Id);
            }
        }
        while (added);

        foreach (var removed in root.Layouts.Where(candidate => doomed.Contains(candidate.Id)).ToList())
        {
            root.Layouts.Remove(removed);
        }
    }

    /// <summary>The profile a loaded page or folder belongs to (through its parents).</summary>
    private ProfileItemViewModel? FindRoot(ProfileItemViewModel child, IReadOnlyList<ProfileItemViewModel> children)
    {
        var current = child;
        for (var depth = 0; depth < 64 && current.ParentId is { } parentId; depth++)
        {
            if (Profiles.FirstOrDefault(profile => profile.Id == parentId) is { } root)
            {
                return root;
            }

            if (children.FirstOrDefault(candidate => candidate.Id == parentId) is not { } parent)
            {
                return null;
            }

            current = parent;
        }

        return null;
    }

    /// <summary>Makes the folder a new Folder key opens: a layout with a Back key in the first key.</summary>
    private ActionReference CreateFolderFor(ActionItemViewModel folderAction, PhysicalControl control)
    {
        var parent = SelectedProfile!;
        var root = parent.RootProfile;
        var folder = new ProfileItemViewModel(NewLayoutId(root, "folder"), "Folder", root.DeviceId)
        {
            ParentId = parent.Id,
            Kind = ProfileKinds.Folder,
            Owner = root,
        };

        if (_actionsByKey.TryGetValue(EditorActionIds.Key(EditorActionIds.BuiltInPluginId, SystemPlugins.FolderBackAction.ActionId), out var back) &&
            ControlItems.FirstOrDefault(item => back.FitsControl(item.Control)) is { } backKey &&
            Resolve(new ControlBinding(backKey.Id, ProfileActionKind.Single, back.ToActionReference(back.DefaultSettings), new ControlAppearance(), [])) is { } backBinding)
        {
            folder.SetBinding(backBinding);
        }

        root.Layouts.Add(folder);
        return folderAction.ToActionReference(new ActionSettings(folderAction.DefaultSettings.Concat(
            [new KeyValuePair<string, string>(SystemPlugins.FolderAction.FolderIdKey, folder.Id)])));
    }

    /// <summary>The folder a binding's Folder key opens, if it is one.</summary>
    private static string? FolderIdOf(ControlBinding? binding) =>
        binding is { ActionKind: ProfileActionKind.Single } &&
        binding.Action.ActionId == SystemPlugins.FolderAction.ActionId &&
        binding.Action.EffectiveSettings.TryGetValue(SystemPlugins.FolderAction.FolderIdKey, out var id) ? id : null;

    /// <summary>Keeps a folder's name in step with its key's Folder name setting.</summary>
    private void SyncFolderName(ControlBinding? binding)
    {
        if (FolderIdOf(binding) is { } folderId &&
            AllLayouts.FirstOrDefault(layout => layout.Id == folderId) is { } folder &&
            binding!.Action.EffectiveSettings.TryGetValue(SystemPlugins.FolderAction.NameKey, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            folder.Name = name;
        }
    }

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
            var (red, green, blue) = Core.Rendering.ProfileBackdrop.GetColor(SelectedProfile is null ? null : ToHostModel(SelectedProfile).Background);
            return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(red, green, blue));
        }
    }

    /// <summary>Where the device's body, screens and controls are drawn (as in the device simulator).</summary>
    public Views.DeviceGeometry CanvasGeometry
    {
        get => _canvasGeometry;
        private set => SetProperty(ref _canvasGeometry, value);
    }

    public double CanvasWidth => CanvasGeometry.Width;

    public double CanvasHeight => CanvasGeometry.Height;

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

        if (actionItem.ScaffoldKind == ProfileActionKind.Workflow)
        {
            if (existing?.IsWorkflow == true)
            {
                OpenWorkflowEditor(control.Id);
                return;
            }

            // What the control did keeps working: its action or steps become the first nodes.
            var workflow = existing is null || existing.IsDisplayContentOnly ? WorkflowConverter.Starter() : WorkflowConverter.FromBinding(existing.Model);
            updated = new ControlBinding(control.Id, ProfileActionKind.Workflow, actionItem.ToActionReference(), appearance, []) { Workflow = workflow };
            ApplyBinding(control.Id, updated);
            StatusMessage = existing is null || existing.IsDisplayContentOnly
                ? $"Made a workflow on {control.DisplayName}."
                : $"Turned {control.DisplayName} into a workflow that does what it did before.";
            OpenWorkflowEditor(control.Id);
            return;
        }
        else if (existing?.IsWorkflow == true && !actionItem.IsMultiActionScaffold)
        {
            if (!actionItem.CanBeMultiActionStep)
            {
                StatusMessage = $"{actionItem.Name} cannot be part of a workflow.";
                return;
            }

            updated = existing.Model with
            {
                Workflow = WorkflowConverter.Append(existing.Model.Workflow ?? WorkflowConverter.Starter(), actionItem.ToActionReference(actionItem.DefaultSettings)),
            };
            StatusMessage = $"Added {actionItem.Name} to the workflow on {control.DisplayName}.";
        }
        else if (actionItem.IsMultiActionScaffold)
        {
            var kind = actionItem.ScaffoldKind!.Value;
            IReadOnlyList<MultiActionStep> steps = existing switch
            {
                { IsMultiAction: true } => existing.Model.Steps,
                { PrimaryAction.CanBeMultiActionStep: true } => [new MultiActionStep(0, existing.Model.Action) { Group = StepGroups.For(kind).FirstOrDefault() }],
                _ => [],
            };

            updated = new ControlBinding(control.Id, kind, actionItem.ToActionReference(), appearance, steps);
            StatusMessage = existing is null
                ? $"Created a Multi Action on {control.DisplayName}. Drop actions on it, or into its steps below."
                : $"Converted {control.DisplayName} into a Multi Action.";
        }
        else if (existing?.IsMultiAction == true)
        {
            if (!actionItem.CanBeMultiActionStep)
            {
                StatusMessage = $"{actionItem.Name} cannot be a Multi Action step.";
                return;
            }

            var step = new MultiActionStep(existing.Model.Steps.Count, actionItem.ToActionReference(actionItem.DefaultSettings))
            {
                Group = StepGroups.For(existing.Model.ActionKind).FirstOrDefault(),
            };
            updated = existing.Model with { Steps = [.. existing.Model.Steps, step] };
            StatusMessage = $"Added {actionItem.Name} as step {updated.Steps.Count} on {control.DisplayName}.";
        }
        else if (actionItem.PluginId == EditorActionIds.BuiltInPluginId && actionItem.Id == SystemPlugins.FolderAction.ActionId)
        {
            updated = new ControlBinding(control.Id, ProfileActionKind.Single, CreateFolderFor(actionItem, control), appearance, []);
            StatusMessage = $"Made a folder on {control.DisplayName}. Double-click it (or Open folder below) to fill it; its first key goes back.";
        }
        else
        {
            updated = new ControlBinding(control.Id, ProfileActionKind.Single, actionItem.ToActionReference(actionItem.DefaultSettings), appearance, []);
            StatusMessage = actionItem.HasSettings
                ? $"Assigned {actionItem.Name} to {control.DisplayName}. Set it up below."
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

        // A removed Folder key takes its folder along, unless another key still opens it.
        var folderId = FolderIdOf(SelectedProfile.GetBinding(control.Id)?.Model);
        ApplyBinding(control.Id, null);
        if (folderId is not null &&
            !AllLayouts.Any(layout => layout.ToModel().Bindings.Any(binding => FolderIdOf(binding) == folderId)) &&
            AllLayouts.FirstOrDefault(layout => layout.Id == folderId) is { } folder)
        {
            RemoveLayoutTree(folder);
            SyncProfilesToHost();
        }
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
    public void MoveBinding(string sourceControlId, string targetControlId, bool copy) =>
        Grouped(copy ? "the copy" : "the move", () => MoveBindingCore(sourceControlId, targetControlId, copy));

    private void MoveBindingCore(string sourceControlId, string targetControlId, bool copy)
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

        RecordBindingChange(SelectedProfile, controlId, model);
        if (model is null)
        {
            SelectedProfile.RemoveBinding(controlId);
            AttachBinding(controlItem, null);
        }
        else if (Resolve(model) is { } binding)
        {
            SelectedProfile.SetBinding(binding);
            AttachBinding(controlItem, binding);
            SyncFolderName(model);
        }
        else
        {
            SelectedProfile.AddUnresolvedBinding(model);
            AttachBinding(controlItem, null);
        }

        SyncProfilesToHost();
        if (!_applyingFromEditor && string.Equals(SelectedControl?.Id, controlId, StringComparison.OrdinalIgnoreCase))
        {
            // Changed by a drop, a move or a revert: show the new binding in the editor.
            RebuildEditor();
        }
    }

    // ---------------------------------------------------------------- inline editor

    private ControlCustomizationViewModel? _editor;
    private bool _applyingFromEditor;
    private bool _showDebugPanel;
    private bool _showDeviceSimulator;
    private Views.DeviceGeometry _canvasGeometry = Views.DeviceGeometry.Empty;

    /// <summary>
    /// The editor below the device for the selected control (what the Customize window was). Edits
    /// apply right away; Revert puts back the binding the control had when it was selected.
    /// </summary>
    public ControlCustomizationViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (SetProperty(ref _editor, value))
            {
                OnPropertyChanged(nameof(HasEditor));
            }
        }
    }

    public bool HasEditor => _editor is not null;

    /// <summary>
    /// The edited layout has nothing on any control yet: the canvas says "Drag an action here" and
    /// points at the action list, until the first binding exists.
    /// </summary>
    public bool ShowEmptyDeckHint => IsEmptyDeck(ControlItems);

    internal static bool IsEmptyDeck(IReadOnlyCollection<ControlItemViewModel> controls) =>
        controls.Count > 0 && controls.All(static control => control.AssignedBinding is null);

    /// <summary>☰ → Device simulator is offered (DeckHost settings → Developer → Device simulator).</summary>
    public bool ShowDeviceSimulator
    {
        get => _showDeviceSimulator;
        set => SetProperty(ref _showDeviceSimulator, value);
    }

    /// <summary>The debug panel (the selected control's capabilities, the runtime connection and log): DeckHost settings → Show debug panel.</summary>
    public bool ShowDebugPanel
    {
        get => _showDebugPanel;
        set => SetProperty(ref _showDebugPanel, value);
    }

    private void RebuildEditor()
    {
        if (_editor is { } old)
        {
            old.Changed -= OnEditorChanged;
            old.RevertRequested -= OnEditorRevert;
            old.RemoveRequested -= OnEditorRemove;
            old.OpenFolderRequested -= OnEditorOpenFolder;
            old.OpenWorkflowRequested -= OnEditorOpenWorkflow;
            old.ConvertToWorkflowRequested -= OnEditorConvertToWorkflow;
        }

        if (SelectedControl is null || SelectedProfile is null)
        {
            Editor = null;
            return;
        }

        var editor = CreateCustomizationViewModel(SelectedControl.Id);
        editor.Changed += OnEditorChanged;
        editor.RevertRequested += OnEditorRevert;
        editor.RemoveRequested += OnEditorRemove;
        editor.OpenFolderRequested += OnEditorOpenFolder;
        editor.OpenWorkflowRequested += OnEditorOpenWorkflow;
        editor.ConvertToWorkflowRequested += OnEditorConvertToWorkflow;
        Editor = editor;
        if (editor.HasSettingsPage && SelectedDevice is { } device)
        {
            _ = LoadSettingsPageAsync(editor, device.Id);
        }
    }

    /// <summary>A plugin's own settings page (Stream Deck property inspector) for the editor.</summary>
    private async System.Threading.Tasks.Task LoadSettingsPageAsync(ControlCustomizationViewModel editor, string deviceId)
    {
        var uri = await _deviceHostService.GetActionSettingsPageAsync(deviceId, editor.ControlId);
        if (ReferenceEquals(Editor, editor))
        {
            editor.SettingsPageUri = uri;
        }
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        if (sender is not ControlCustomizationViewModel editor || !ReferenceEquals(editor, Editor))
        {
            return;
        }

        // Fields that settle when the editor opens (a choice showing its default) aren't an edit.
        var binding = editor.BuildBinding();
        if (SelectedProfile is { } layout && SameBinding(layout.GetModel(editor.ControlId), binding))
        {
            return;
        }

        _applyingFromEditor = true;
        try
        {
            ApplyBinding(editor.ControlId, binding);
        }
        finally
        {
            _applyingFromEditor = false;
        }
    }

    private void OnEditorRevert(object? sender, EventArgs e)
    {
        if (sender is ControlCustomizationViewModel editor)
        {
            ApplyBinding(editor.ControlId, editor.OriginalBinding);
            StatusMessage = $"Put {editor.DisplayName} back as it was.";
        }
    }

    private void OnEditorOpenFolder(object? sender, string folderId) => OpenFolder(folderId);

    private void OnEditorOpenWorkflow(object? sender, EventArgs e)
    {
        if (sender is ControlCustomizationViewModel editor)
        {
            OpenWorkflowEditor(editor.ControlId);
        }
    }

    private void OnEditorConvertToWorkflow(object? sender, EventArgs e)
    {
        if (sender is ControlCustomizationViewModel editor)
        {
            ConvertToWorkflow(editor.ControlId);
        }
    }

    // ---------------------------------------------------------------- workflows

    /// <summary>Raised with the editor to show (the main window opens it as a page).</summary>
    public event Action<WorkflowEditorViewModel>? WorkflowEditorRequested;

    /// <summary>Turns a Multi Action, Multi Action Switch, Key Logic or plain action into a workflow that does the same, and opens it.</summary>
    public void ConvertToWorkflow(string controlId)
    {
        if (SelectedProfile?.GetBinding(controlId) is not { IsWorkflow: false } existing ||
            !_actionsByKey.TryGetValue(EditorActionIds.Key(EditorActionIds.BuiltInPluginId, SystemPlugins.WorkflowAction.ActionId), out var workflowAction))
        {
            return;
        }

        ApplyBinding(controlId, new ControlBinding(controlId, ProfileActionKind.Workflow, workflowAction.ToActionReference(), existing.Model.Appearance, [])
        {
            Workflow = WorkflowConverter.FromBinding(existing.Model),
        });
        StatusMessage = $"Turned {existing.PrimaryAction.Name} into a workflow that does the same; add to it in the editor.";
        OpenWorkflowEditor(controlId);
    }

    /// <summary>Opens the node editor for the workflow on a control of the edited layout.</summary>
    public void OpenWorkflowEditor(string controlId)
    {
        if (CreateWorkflowEditor(controlId) is { } editor)
        {
            WorkflowEditorRequested?.Invoke(editor);
        }
    }

    public WorkflowEditorViewModel? CreateWorkflowEditor(string controlId)
    {
        if (SelectedProfile is not { } layout || SelectedDevice is not { } device ||
            !_controlsById.TryGetValue(controlId, out var controlItem) ||
            layout.GetBinding(controlId) is not { IsWorkflow: true } binding)
        {
            return null;
        }

        var layoutId = layout.Id;
        return Configure(new WorkflowEditorViewModel(
            device.Id,
            layoutId,
            controlItem.Control,
            controlItem.DisplayName,
            binding.Model.Workflow ?? WorkflowConverter.Starter(),
            _actionsByKey.Values,
            workflow => ApplyWorkflow(layoutId, controlId, workflow),
            _deviceHostService), controlId);
    }

    private readonly WorkflowBreakpoints _workflowBreakpoints;
    private readonly Platform.SystemDataPublisher _systemData;

    /// <summary>Gives an editor what it needs from the app: the shared workflows, opening one, and the shared breakpoints.</summary>
    private WorkflowEditorViewModel Configure(WorkflowEditorViewModel editor, string fromControlId)
    {
        editor.Breakpoints = _workflowBreakpoints;
        editor.SharedSource = () => _sharedWorkflows;
        editor.SaveShared = SaveSharedWorkflow;
        editor.DeleteShared = DeleteSharedWorkflow;
        editor.CallersOf = CallersOf;
        editor.OpenShared = id =>
        {
            if (CreateSharedWorkflowEditor(id, fromControlId) is { } shared)
            {
                WorkflowEditorRequested?.Invoke(shared);
            }
        };
        editor.RefreshShared();
        return editor;
    }

    // ---------------------------------------------------------------- shared workflows

    private readonly List<Workflow> _sharedWorkflows = [];

    /// <summary>The workflows Call nodes can run, by name.</summary>
    public IReadOnlyList<Workflow> SharedWorkflows => _sharedWorkflows;

    /// <summary>
    /// Keeps a workflow as a shared one (the file kind: it needs a name). A workflow that already has an
    /// id replaces that shared workflow; otherwise it gets a new id. Returns the id, or why not.
    /// </summary>
    public (string? Id, string? Problem) SaveSharedWorkflow(Workflow workflow)
    {
        if (string.IsNullOrWhiteSpace(workflow.Name))
        {
            return (null, "Give the workflow a name first: Call nodes list shared workflows by name.");
        }

        var name = workflow.Name.Trim();
        var id = workflow.Id ?? $"wf.{Guid.NewGuid():N}"[..11];
        if (_sharedWorkflows.Any(other => other.Id != id && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return (null, $"There is already a shared workflow called {name}.");
        }

        var shared = workflow with { Id = id, Name = name };
        var index = _sharedWorkflows.FindIndex(other => other.Id == id);
        if (index >= 0)
        {
            _sharedWorkflows[index] = shared;
        }
        else
        {
            _sharedWorkflows.Add(shared);
        }

        SyncProfilesToHost();
        StatusMessage = $"Saved {name} as a shared workflow.";
        return (id, null);
    }

    /// <summary>Who still calls a shared workflow: the layouts' controls and other shared workflows (names).</summary>
    public IReadOnlyList<string> CallersOf(string id) =>
    [
        .. AllLayouts.SelectMany(layout => layout.ToModels().SelectMany(model => model.Bindings)
            .Where(binding => binding.Workflow is { } workflow && Calls(workflow, id))
            .Select(binding => $"{layout.Name}: {binding.ControlId}")),
        .. _sharedWorkflows.Where(other => other.Id != id && Calls(other, id)).Select(static other => other.Name ?? other.Id ?? "a shared workflow"),
    ];

    private static bool Calls(Workflow workflow, string id) =>
        workflow.Nodes.Any(node => node.Kind == WorkflowNodeKinds.Call && string.Equals(node.EffectiveSettings.Get(WorkflowKeys.Workflow).Trim(), id, StringComparison.Ordinal));

    /// <summary>Deletes a shared workflow unless something still calls it. Returns why not.</summary>
    public string? DeleteSharedWorkflow(string id)
    {
        var callers = CallersOf(id);
        if (callers.Count > 0)
        {
            return $"It is still called by {string.Join(", ", callers.Take(3))}{(callers.Count > 3 ? ", …" : string.Empty)}.";
        }

        if (_sharedWorkflows.RemoveAll(workflow => workflow.Id == id) == 0)
        {
            return null;
        }

        SyncProfilesToHost();
        return null;
    }

    /// <summary>Opens a shared workflow in the editor (on the control the editor was opened from, whose inputs it offers).</summary>
    public WorkflowEditorViewModel? CreateSharedWorkflowEditor(string id, string fromControlId)
    {
        if (_sharedWorkflows.FirstOrDefault(workflow => workflow.Id == id) is not { } shared ||
            SelectedDevice is not { } device || !_controlsById.TryGetValue(fromControlId, out var controlItem))
        {
            return null;
        }

        return Configure(new WorkflowEditorViewModel(
            device.Id,
            SelectedProfile?.Id ?? string.Empty,
            controlItem.Control,
            controlItem.DisplayName,
            shared,
            _actionsByKey.Values,
            edited => SaveSharedWorkflow(edited with { Id = id }).Problem is null,
            _deviceHostService), fromControlId);
    }

    /// <summary>Stores an edited workflow on its control (also when another layout is shown meanwhile).</summary>
    public bool ApplyWorkflow(string layoutId, string controlId, Workflow workflow)
    {
        if (AllLayouts.FirstOrDefault(candidate => candidate.Id == layoutId) is not { } layout ||
            layout.GetBinding(controlId) is not { IsWorkflow: true } binding)
        {
            return false;
        }

        var model = binding.Model with { Workflow = workflow };
        if (ReferenceEquals(layout, SelectedProfile))
        {
            ApplyBinding(controlId, model);
            return true;
        }

        RecordBindingChange(layout, controlId, model);
        if (Resolve(model) is { } resolved)
        {
            layout.SetBinding(resolved);
        }

        SyncProfilesToHost();
        return true;
    }

    private void OnEditorRemove(object? sender, EventArgs e)
    {
        if (sender is ControlCustomizationViewModel editor)
        {
            RemoveAssignment(editor.ControlId);
        }
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
                Profiles.Concat(_otherDeviceProfiles).SelectMany(static profile => profile.ToModels()).ToList(),
                AutoSwitchEnabled,
                DefaultBackground,
                _deviceLights.Count == 0 ? null : new Dictionary<string, LightStyle>(_deviceLights, StringComparer.OrdinalIgnoreCase),
                _sharedWorkflows.Count == 0 ? null : [.. _sharedWorkflows]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Could not save profiles to {_profileStore.FilePath}: {ex.Message}";
        }
    }

    // ---------------------------------------------------------------- live preview and simulated input

    /// <summary>Sends a simulated input for a control through the normal runtime path.</summary>
    public void Simulate(ControlItemViewModel control, Contracts.Enums.InputCapabilities inputEvent, int value)
    {
        if (SelectedDevice is null)
        {
            return;
        }

        _deviceHostService.SimulateInput(new DeviceInputEvent(SelectedDevice.Id, control.Id, inputEvent, value));
    }

    private volatile bool _previewShown = true;

    /// <summary>
    /// Whether a window shows the device's pictures. While none does (DeckHost in the tray, the window
    /// minimized) the canvas skips the host's frames, so a hidden DeckHost doesn't redraw them; when it is
    /// shown again the device is redrawn, which brings the canvas up to date.
    /// </summary>
    public bool PreviewShown
    {
        get => _previewShown;
        set
        {
            if (_previewShown == value)
            {
                return;
            }

            _previewShown = value;
            if (value && SelectedDevice is { } device)
            {
                _deviceHostService.Redraw(device.Id);
            }
        }
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

    private void OnLightChanged(LightState light)
    {
        if (SelectedDevice is not null &&
            string.Equals(light.DeviceId, SelectedDevice.Id, StringComparison.OrdinalIgnoreCase) &&
            _controlsById.TryGetValue(light.ControlId, out var control))
        {
            control.SetLight(light.Red, light.Green, light.Blue);
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

    private readonly Dictionary<string, LightStyle> _deviceLights = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The default light of a device's LEDs (Device Settings → Knob lights); null for dark.</summary>
    public LightStyle? GetDeviceLight(string deviceId) => _deviceLights.GetValueOrDefault(deviceId);

    /// <summary>Sets a device's default light, shows it right away and saves it with the profiles.</summary>
    public void SetDeviceLight(string deviceId, LightStyle? style)
    {
        if (style is null)
        {
            _deviceLights.Remove(deviceId);
        }
        else
        {
            _deviceLights[deviceId] = style;
        }

        _deviceHostService.SetDeviceLight(deviceId, style);
        ScheduleSave();
    }

    private string _defaultBackgroundColor = string.Empty;
    private string _defaultBackgroundImage = string.Empty;
    private IReadOnlyList<BackgroundLayer> _defaultBackgroundLayers = [];

    /// <summary>More layers for the default background (over its color and image).</summary>
    public IReadOnlyList<BackgroundLayer> DefaultBackgroundLayers
    {
        get => _defaultBackgroundLayers;
        set
        {
            _defaultBackgroundLayers = value ?? [];
            OnPropertyChanged();
            SyncProfilesToHost();
        }
    }

    /// <summary>Background color for every profile that has none of its own (Profile Manager); empty for DeckHost's dark blue.</summary>
    public string DefaultBackgroundColor
    {
        get => _defaultBackgroundColor;
        set
        {
            if (SetProperty(ref _defaultBackgroundColor, value ?? string.Empty))
            {
                SyncProfilesToHost();
            }
        }
    }

    /// <summary>Background image for every profile that has no background of its own.</summary>
    public string DefaultBackgroundImage
    {
        get => _defaultBackgroundImage;
        set
        {
            if (SetProperty(ref _defaultBackgroundImage, value ?? string.Empty))
            {
                SyncProfilesToHost();
            }
        }
    }

    /// <summary>The background of profiles without one of their own; null when none is set.</summary>
    public ProfileBackground? DefaultBackground
    {
        get
        {
            var color = System.Text.RegularExpressions.Regex.IsMatch(_defaultBackgroundColor.Trim(), "^#?[0-9A-Fa-f]{6}$") ? _defaultBackgroundColor.Trim() : null;
            var image = string.IsNullOrWhiteSpace(_defaultBackgroundImage) ? null : _defaultBackgroundImage.Trim();
            return color is null && image is null && _defaultBackgroundLayers.Count == 0
                ? null
                : new ProfileBackground(color, image) { Layers = _defaultBackgroundLayers.Count == 0 ? null : [.. _defaultBackgroundLayers] };
        }
    }

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
                    OnForegroundAppChanged(app, _foregroundAppWatcher.CurrentTitle);
                }
                else if (!value)
                {
                    ReturnToManualProfile();
                }
            }
        }
    }

    public bool AutoSwitchSupported => Platform.ForegroundAppWatcher.IsSupported;

    public string CurrentAppName => _foregroundAppWatcher.CurrentApp is { } app
        ? string.IsNullOrWhiteSpace(_foregroundAppWatcher.CurrentTitle) ? app : $"{app} | {_foregroundAppWatcher.CurrentTitle}"
        : "—";

    public ProfileItemViewModel CreateProfile(string name, ProfileItemViewModel? copyFrom = null)
    {
        // A duplicate is a full working copy: bindings, appearance, background, app rules and plugin settings.
        var source = copyFrom?.ToModel();
        var profile = new ProfileItemViewModel($"profile.{Guid.NewGuid():N}"[..20], UniqueProfileName(name), SelectedDevice!.Id, source?.AutoSwitchApps, source?.Background, source?.OverridesBackground);
        if (copyFrom is not null && source is not null)
        {
            // Pages and folders come along with new ids; Folder keys are pointed at the copies.
            var ids = new Dictionary<string, string>(StringComparer.Ordinal) { [copyFrom.Id] = profile.Id };
            foreach (var layout in copyFrom.Layouts)
            {
                ids[layout.Id] = NewLayoutId(profile, layout.Kind ?? "page");
            }

            CopyLayout(copyFrom, profile, ids);
            foreach (var layout in copyFrom.Layouts)
            {
                var copy = new ProfileItemViewModel(ids[layout.Id], layout.Name, profile.DeviceId, null, layout.ToModel().Background, layout.OverridesBackground)
                {
                    ParentId = layout.ParentId is { } parent && ids.TryGetValue(parent, out var newParent) ? newParent : profile.Id,
                    Kind = layout.Kind,
                    Owner = profile,
                };
                CopyLayout(layout, copy, ids);
                profile.Layouts.Add(copy);
            }
        }

        Profiles.Add(profile);
        NotifyProfilesEdited();
        StatusMessage = copyFrom is null ? $"Created profile {profile.Name}." : $"Duplicated {copyFrom.Name} as {profile.Name}.";
        return profile;
    }

    /// <summary>Copies a layout's bindings and plugin settings, pointing Folder keys at the copied folders.</summary>
    private void CopyLayout(ProfileItemViewModel from, ProfileItemViewModel to, IReadOnlyDictionary<string, string> ids)
    {
        _pluginState?.CopyProfileSettings(from.Id, to.Id);
        foreach (var binding in from.ToModel().Bindings)
        {
            var copy = FolderIdOf(binding) is { } folderId && ids.TryGetValue(folderId, out var newFolderId)
                ? binding with
                {
                    Action = binding.Action with
                    {
                        Settings = new ActionSettings(binding.Action.EffectiveSettings
                            .Where(static pair => pair.Key != SystemPlugins.FolderAction.FolderIdKey)
                            .Append(new KeyValuePair<string, string>(SystemPlugins.FolderAction.FolderIdKey, newFolderId))),
                    },
                }
                : binding;
            if (Resolve(copy) is { } resolved)
            {
                to.SetBinding(resolved);
            }
            else
            {
                to.AddUnresolvedBinding(copy);
            }
        }
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
    // ---------------------------------------------------------------- export, import, backups

    /// <summary>Daily backups of all profiles (newest first); empty without a profile store.</summary>
    public IReadOnlyList<FileInfo> ProfileBackups => _profileStore?.ListBackups() ?? [];

    /// <summary>Where imported profiles keep their images.</summary>
    public static string ImportedAssetsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost", "assets");

    /// <summary>Writes a profile with its pages, folders and images to a <c>.deckhostprofile</c> file.</summary>
    public void ExportProfile(ProfileItemViewModel profile, string path)
    {
        Core.Profiles.ProfileBundle.Export([.. profile.RootProfile.ToModels()], path, _sharedWorkflows);
        StatusMessage = $"Exported {profile.RootProfile.Name} to {Path.GetFileName(path)}.";
    }

    /// <summary>The Stream Deck actions the last <see cref="ImportProfile"/> couldn't map (empty after a DeckHost profile).</summary>
    public IReadOnlyList<DeckHost.App.Profiles.StreamDeckActionUse> LastImportUnavailableActions { get; private set; } = [];

    /// <summary>
    /// Imports a DeckHost profile (<c>.deckhostprofile</c>) or a Stream Deck profile
    /// (<c>.streamDeckProfile</c>) as a new profile and shows it. Returns what happened.
    /// </summary>
    public string ImportProfile(string path)
    {
        LastImportUnavailableActions = [];
        string summary;
        IReadOnlyList<DeviceProfile> layouts;
        if (path.EndsWith(DeckHost.App.Profiles.StreamDeckProfileImporter.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var result = DeckHost.App.Profiles.StreamDeckProfileImporter.Import(path, SelectedDevice!.Id, [.. ControlItems.Select(static item => item.Control)], [.. _actionsByKey.Values], ImportedAssetsDirectory);
            layouts = result.Layouts;
            LastImportUnavailableActions = result.UnavailableActions;
            summary = $"{result.Imported} key(s) and dial(s) imported"
                + (result.NotPlaced > 0 ? $", {result.NotPlaced} had no place on this device" : string.Empty)
                + (result.Unavailable.Count > 0 ? $"; not in DeckHost (kept their look): {string.Join(", ", result.Unavailable.Take(6))}{(result.Unavailable.Count > 6 ? ", …" : string.Empty)}" : string.Empty)
                + ". Stream Deck plugin actions need their settings again.";
        }
        else
        {
            var bundle = Core.Profiles.ProfileBundle.ImportBundle(path, SelectedDevice!.Id, ImportedAssetsDirectory);
            layouts = bundle.Layouts;
            _sharedWorkflows.AddRange(bundle.SharedWorkflows);
            summary = $"{layouts.Sum(static layout => layout.Bindings.Count)} control(s) imported.";
            var controls = SelectedDevice.Controls;
            if (layouts.SelectMany(static layout => layout.Bindings).Any(binding => !controls.Any(control => SameId(control.Id, binding.ControlId))))
            {
                // Made for another device: put the keys where they fit, the rest on added pages.
                var first = layouts[0];
                var mapped = Core.Profiles.LayoutMapper.Map(layouts, controls, sourceColumns: null, page => $"{first.Id}.more{page}");
                layouts = mapped.Layouts;
                summary = $"made for another device: {mapped.Summary}.";
            }
        }

        var root = AddLayouts(layouts);
        SelectedProfile = root;
        NotifyProfilesEdited();
        StatusMessage = $"Imported {root.Name}: {summary}";
        return StatusMessage;
    }

    /// <summary>Devices other than the edited one, to copy a profile to.</summary>
    public IReadOnlyList<DeviceChoiceViewModel> OtherDevices =>
        [.. _deviceHostService.Devices.Where(device => SelectedDevice is null || !SameId(device.Id, SelectedDevice.Id)).Select(static device => new DeviceChoiceViewModel(device))];

    /// <summary>
    /// Copies a profile (with its pages and folders) to another device: keys keep their row and column
    /// where they fit, the rest go on added pages; dials and touch zones the device lacks are left out.
    /// </summary>
    public string CopyProfileToDevice(ProfileItemViewModel profile, string deviceId)
    {
        if (SelectedDevice is not { } from || _deviceHostService.Devices.FirstOrDefault(device => SameId(device.Id, deviceId)) is not { } target)
        {
            return StatusMessage = "That device isn't known any more.";
        }

        var root = profile.RootProfile;
        var copies = Core.Profiles.ProfileBundle.Copy([.. root.ToModels()], target.Id);
        var mapped = Core.Profiles.LayoutMapper.Map(copies, target.Controls, Core.Profiles.LayoutMapper.ColumnsOf(from.Controls), page => $"{copies[0].Id}.more{page}");
        var existingNames = _otherDeviceProfiles.Where(other => SameId(other.DeviceId, target.Id)).Select(static other => other.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = root.Name;
        for (var number = 2; existingNames.Contains(name); number++)
        {
            name = $"{root.Name} {number}";
        }

        ProfileItemViewModel? copy = null;
        foreach (var layout in mapped.Layouts)
        {
            var item = new ProfileItemViewModel(layout.Id, layout.IsRoot ? name : layout.Name, layout.DeviceId, layout.AutoSwitchApps, layout.Background, layout.OverridesBackground)
            {
                ParentId = layout.IsRoot ? null : layout.ParentId,
                Kind = layout.IsRoot ? null : layout.Kind,
                Owner = layout.IsRoot ? null : copy,
            };
            foreach (var binding in layout.Bindings)
            {
                if (Resolve(binding) is { } resolved)
                {
                    item.SetBinding(resolved);
                }
                else
                {
                    item.AddUnresolvedBinding(binding);
                }
            }

            if (layout.IsRoot && copy is null)
            {
                copy = item;
                _otherDeviceProfiles.Add(item);
            }
            else
            {
                copy?.Layouts.Add(item);
            }
        }

        NotifyProfilesEdited();
        return StatusMessage = $"Copied {root.Name} to {DeviceChoiceViewModel.LabelOf(target)} as {name}: {mapped.Summary}. Pick that device in the header to see it.";
    }

    /// <summary>Saves a backup of all profiles now (also done once a day automatically).</summary>
    public void BackUpProfiles()
    {
        FlushPendingSave();
        _profileStore?.BackUp(force: true);
        OnPropertyChanged(nameof(ProfileBackups));
        StatusMessage = "Backed up all profiles.";
    }

    /// <summary>Replaces all profiles with a backup (today's state is backed up first).</summary>
    public void RestoreBackup(string path)
    {
        var document = Core.Profiles.ProfileStore.LoadFrom(path) ?? throw new IOException("The backup is empty.");
        BackUpProfiles();
        LoadProfiles(document);
        ApplyProfileBindings();
        RefreshPageTabs();
        OnPropertyChanged(nameof(SelectedRootProfile));
        SyncProfilesToHost();
        StatusMessage = $"Restored the profiles from {Path.GetFileNameWithoutExtension(path)}.";
    }

    /// <summary>Adds a profile with its pages and folders (models from an import).</summary>
    private ProfileItemViewModel AddLayouts(IReadOnlyList<DeviceProfile> layouts)
    {
        ProfileItemViewModel? root = null;
        foreach (var layout in layouts)
        {
            var item = new ProfileItemViewModel(layout.Id, layout.IsRoot ? UniqueProfileName(layout.Name) : layout.Name, layout.DeviceId, layout.AutoSwitchApps, layout.Background, layout.OverridesBackground)
            {
                ParentId = layout.IsRoot ? null : layout.ParentId,
                Kind = layout.IsRoot ? null : layout.Kind,
                Owner = layout.IsRoot ? null : root,
            };
            foreach (var binding in layout.Bindings)
            {
                if (Resolve(binding) is { } resolved)
                {
                    item.SetBinding(resolved);
                }
                else
                {
                    item.AddUnresolvedBinding(binding);
                }
            }

            if (layout.IsRoot && root is null)
            {
                root = item;
                Profiles.Add(item);
            }
            else if (root is not null)
            {
                root.Layouts.Add(item);
            }
        }

        return root ?? throw new IOException("The file has no profile.");
    }

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

    private void OnForegroundAppChanged(string appName, string? windowTitle)
    {
        OnPropertyChanged(nameof(CurrentAppName));

        // Workflows can check which app is in front, and its window title.
        _deviceHostService.SetVariable(VariableKeys.ForegroundApp, appName);
        _deviceHostService.SetVariable(VariableKeys.ForegroundTitle, windowTitle ?? string.Empty);
        if (!AutoSwitchEnabled || SelectedDevice is null)
        {
            return;
        }

        var match = AutoProfileSelector.FindProfileForApp(Profiles.Select(static profile => profile.ToModel()), SelectedDevice.Id, appName, windowTitle);
        if (match is null)
        {
            ReturnToManualProfile();
            return;
        }

        if (Profiles.FirstOrDefault(profile => profile.Id == match.Id) is { } profile && profile != SelectedProfile?.RootProfile)
        {
            ActivateAutomatically(profile);
            StatusMessage = $"Switched to {profile.Name} because {appName} is active.";
        }
    }

    private void ReturnToManualProfile()
    {
        if (_manualProfileId is not null &&
            SelectedProfile?.RootProfile.Id != _manualProfileId &&
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
        foreach (var profile in EveryLayout.ToList())
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

    private void LoadProfiles(ProfileDocument? replacement = null)
    {
        ClearUndo();
        _suppressHostSync = true;
        try
        {
            var document = replacement ?? TryLoadDocument();
            var unresolved = 0;
            if (replacement is not null)
            {
                // A restored backup replaces everything.
                Profiles.Clear();
                _otherDeviceProfiles.Clear();
                _selectedProfile = null;
            }

            var children = new List<ProfileItemViewModel>();
            foreach (var profile in document?.Profiles ?? [])
            {
                var item = new ProfileItemViewModel(profile.Id, profile.Name, profile.DeviceId, profile.AutoSwitchApps, profile.Background, profile.OverridesBackground)
                {
                    ParentId = profile.ParentId,
                    Kind = profile.Kind,
                };
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

                if (item.IsRoot)
                {
                    Profiles.Add(item);
                }
                else
                {
                    children.Add(item);
                }
            }

            // Pages and folders join the profile they belong to (a folder through its page or folder).
            foreach (var child in children)
            {
                var root = FindRoot(child, children);
                if (root is not null)
                {
                    child.Owner = root;
                    root.Layouts.Add(child);
                }
            }

            // The editor opens on the device of the profile chosen last; the other devices' profiles wait aside.
            if (replacement is null &&
                Profiles.Concat(children).FirstOrDefault(profile => profile.Id == document?.SelectedProfileId) is { } last &&
                !SameId(last.DeviceId, SelectedDevice!.Id) &&
                _deviceHostService.Devices.FirstOrDefault(device => SameId(device.Id, last.DeviceId)) is { } lastDevice)
            {
                UseDevice(lastDevice);
            }

            foreach (var other in Profiles.Where(profile => !SameId(profile.DeviceId, SelectedDevice!.Id)).ToList())
            {
                Profiles.Remove(other);
                _otherDeviceProfiles.Add(other);
            }

            if (Profiles.Count == 0 && _otherDeviceProfiles.Count > 0)
            {
                Profiles.Add(new ProfileItemViewModel($"profile.{Guid.NewGuid():N}"[..20], "Default", SelectedDevice!.Id));
            }

            if (Profiles.Count == 0)
            {
                Profiles.Add(new ProfileItemViewModel("profile.default", "Default", SelectedDevice!.Id));
                Profiles.Add(new ProfileItemViewModel("profile.streaming", "Streaming", SelectedDevice.Id));
                Profiles.Add(new ProfileItemViewModel("profile.media", "Media", SelectedDevice.Id));
            }

            SelectedProfile = AllLayouts.FirstOrDefault(profile => profile.Id == document?.SelectedProfileId) ?? Profiles[0];
            _autoSwitchEnabled = document?.AutoSwitchEnabled ?? true;
            _defaultBackgroundColor = document?.DefaultBackground?.Color ?? string.Empty;
            _defaultBackgroundImage = document?.DefaultBackground?.ImagePath ?? string.Empty;
            _defaultBackgroundLayers = document?.DefaultBackground?.Layers ?? [];
            _deviceLights.Clear();
            foreach (var (deviceId, style) in document?.DeviceLights ?? new Dictionary<string, LightStyle>())
            {
                _deviceLights[deviceId] = style;
            }

            _sharedWorkflows.Clear();
            _sharedWorkflows.AddRange((document?.SharedWorkflows ?? []).Where(static workflow => !string.IsNullOrWhiteSpace(workflow.Id)));

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
        _deviceHostService.SetSharedWorkflows([.. _sharedWorkflows]);
        var hostModels = EveryLayout.Select(ToHostModel).ToList();
        _deviceHostService.SetProfiles(hostModels);
        WantBackgroundVariables(hostModels);
        if (SelectedProfile is not null)
        {
            _deviceHostService.SetActiveProfile(SelectedProfile.Id);
        }

        foreach (var (deviceId, style) in _deviceLights)
        {
            _deviceHostService.SetDeviceLight(deviceId, style);
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
            var document = _profileStore.Load();
            if (_profileStore.NewerVersionLoaded is { } newer)
            {
                StatusMessage = $"These profiles were saved by a newer DeckHost (format {newer}). Anything this version doesn't know is dropped when it saves; the original is kept in the backups folder.";
            }
            else if (_profileStore.LastMigration.Count > 0)
            {
                StatusMessage = "Profiles were updated to this version's format. The old file is in the backups folder.";
            }

            return document;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
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

        // The steps, or a workflow's action nodes (a workflow keeps working without a node whose
        // plugin is gone: the node is skipped, and the editor marks it).
        var steps = new List<StepItemViewModel>();
        foreach (var step in binding.StepActions)
        {
            if (!_actionsByKey.TryGetValue(EditorActionIds.Key(step.PluginId, step.ActionId), out var stepAction))
            {
                if (binding.ActionKind == ProfileActionKind.Workflow)
                {
                    continue;
                }

                return null;
            }

            steps.Add(new StepItemViewModel(stepAction, step.EffectiveSettings));
        }

        return new ControlBindingViewModel(binding, primary, steps);
    }

    /// <summary>The profile as the host draws it: its own background, else the default for all profiles.</summary>
    private DeviceProfile ToHostModel(ProfileItemViewModel profile)
    {
        // Pages and folders look like their profile unless they have a background of their own.
        var model = profile.ToModel();
        var inherited = profile.IsRoot ? null : profile.RootProfile.ToModel().Background;
        var effective = DeckHost.App.Profiles.ProfileBackgrounds.Effective(model.Background, inherited, DefaultBackground, model.OverridesBackground ?? true);
        return ReferenceEquals(effective, model.Background) ? model : model with { Background = effective };
    }

    /// <summary>The background the profile is drawn with (its own, its root's or the default), as the host gets it.</summary>
    public ProfileBackground? EffectiveBackground(ProfileItemViewModel profile) => ToHostModel(profile).Background;

    /// <summary>The device with this id as the host lists it, else the one being edited when that is it.</summary>
    internal DeviceDefinition? DeviceOf(string deviceId) =>
        _deviceHostService.Devices.FirstOrDefault(device => SameId(device.Id, deviceId))
        ?? (SelectedDevice is { } selected && SameId(selected.Id, deviceId) ? selected : null);

    /// <summary>Starts publishing the system values (CPU, memory) the backgrounds follow: the profiles' and the default's (the Studio previews it).</summary>
    private void WantBackgroundVariables(IEnumerable<DeviceProfile> hostModels) =>
        _systemData.Want(hostModels.SelectMany(static profile => Core.Rendering.BackgroundLayers.BoundVariables(profile.Background))
            .Concat(Core.Rendering.BackgroundLayers.BoundVariables(DefaultBackground)));

    /// <summary>The Variables page is shown (or closed): CPU and memory are published for it.</summary>
    public void WantSystemValues(bool shown) => _systemData.WantWhileShown(shown);

    private void SyncProfilesToHost()
    {
        var hostModels = EveryLayout.Select(ToHostModel).ToList();
        _deviceHostService.SetProfiles(hostModels);
        WantBackgroundVariables(hostModels);

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
            AllLayouts.FirstOrDefault(profile => profile.Id == profileId) is not { } profile)
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

    private int _discoveredDevices;

    /// <summary>Devices DeckHost found that wait for Add or Keep ignoring (the Devices page).</summary>
    public bool HasDiscoveredDevices => _discoveredDevices > 0;

    public string DiscoveredDevicesText => _discoveredDevices == 1 ? "1 new device" : $"{_discoveredDevices} new devices";

    public string DevicesMenuText => _discoveredDevices > 0 ? $"Devices ({_discoveredDevices} new)" : "Devices";

    private void RefreshDiscovered()
    {
        var count = _deviceHostService.DeviceRoster().Count(static entry => entry.Listing == Core.Devices.DeviceListing.Discovered);
        if (count == _discoveredDevices)
        {
            return;
        }

        _discoveredDevices = count;
        OnPropertyChanged(nameof(HasDiscoveredDevices));
        OnPropertyChanged(nameof(DiscoveredDevicesText));
        OnPropertyChanged(nameof(DevicesMenuText));
    }

    private void OnDevicesChanged()
    {
        RefreshDeviceList();
        if (SelectedDevice is null)
        {
            return;
        }

        var updated = _deviceHostService.Devices.FirstOrDefault(device => SameId(device.Id, SelectedDevice.Id));
        if (!_devicePickedByUser && SelectedDevice.IsConnected is false && updated is not { IsConnected: true } &&
            _deviceHostService.Devices.FirstOrDefault(static device => device.IsConnected && !device.Id.StartsWith("web.", StringComparison.Ordinal)) is { } plugged)
        {
            // Nothing picked by hand and the shown device isn't there: show the one that is plugged in.
            SwitchDevice(plugged);
        }
        else if (updated is not null)
        {
            var layoutChanged = !updated.Controls.Select(static control => (control.Id, control.Bounds)).SequenceEqual(SelectedDevice.Controls.Select(static control => (control.Id, control.Bounds)));
            if (layoutChanged)
            {
                // The device's controls changed (the web deck's grid): rebuild the canvas.
                var controlId = SelectedControl?.Id;
                UseDevice(updated);
                ApplyProfileBindings();
                SelectControl((_controlsById.GetValueOrDefault(controlId ?? string.Empty) ?? ControlItems[0]).Control);
                _deviceHostService.Redraw(updated.Id);
            }
            else
            {
                SelectedDevice = updated;
            }

            OnPropertyChanged(nameof(DeviceName));
        }
        else if ((_deviceHostService.Devices.FirstOrDefault(static device => device.IsConnected) ?? _deviceHostService.Devices.FirstOrDefault()) is { } remaining)
        {
            // The shown device was taken out of the list (the Devices page): show another one.
            SwitchDevice(remaining);
        }

        RefreshConnectionStatus();
    }

    private void RefreshDeviceList()
    {
        var devices = _deviceHostService.Devices.ToList();
        if (DeviceChoices.Select(static choice => (choice.Id, choice.Label)).SequenceEqual(devices.Select(static device => (device.Id, DeviceChoiceViewModel.LabelOf(device)))))
        {
            return;
        }

        DeviceChoices.Clear();
        foreach (var device in devices)
        {
            DeviceChoices.Add(new DeviceChoiceViewModel(device));
        }

        OnPropertyChanged(nameof(HasSeveralDevices));
        OnPropertyChanged(nameof(PickedDevice));
    }

    /// <summary>Shows <paramref name="device"/>'s controls on the canvas (no profile change).</summary>
    private void UseDevice(DeviceDefinition device)
    {
        SelectedDevice = device;
        ControlItems.Clear();
        _controlsById.Clear();
        var geometry = Views.DeviceGeometry.Of(device);
        CanvasGeometry = geometry;
        foreach (var control in device.Controls.Select(control => new ControlItemViewModel(control, geometry.Place(control))))
        {
            ControlItems.Add(control);
            _controlsById[control.Id] = control;
        }

        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(ShowEmptyDeckHint));
    }

    /// <summary>
    /// Edits another device: its controls on the canvas and its profiles in the pickers. The
    /// profiles of the device left behind stay active on it.
    /// </summary>
    public void SwitchDevice(DeviceDefinition device)
    {
        if (SelectedDevice is not null && SameId(device.Id, SelectedDevice.Id))
        {
            return;
        }

        FlushPendingSave();
        ClearUndo();
        _suppressHostSync = true;
        try
        {
            _otherDeviceProfiles.AddRange(Profiles);
            Profiles.Clear();
            foreach (var mine in _otherDeviceProfiles.Where(profile => SameId(profile.DeviceId, device.Id)).ToList())
            {
                _otherDeviceProfiles.Remove(mine);
                Profiles.Add(mine);
            }

            if (Profiles.Count == 0)
            {
                Profiles.Add(new ProfileItemViewModel($"profile.{Guid.NewGuid():N}"[..20], "Default", device.Id));
            }

            UseDevice(device);
            _selectedProfile = null;
            SelectedProfile = Profiles[0];
        }
        finally
        {
            _suppressHostSync = false;
        }

        // The host keeps each device's active profile; only a new profile list goes out.
        _deviceHostService.SetProfiles(EveryLayout.Select(ToHostModel).ToList());
        _deviceHostService.SetActiveProfile(SelectedProfile!.Id);
        SelectControl(ControlItems[0].Control);
        _deviceHostService.Redraw(device.Id);
        RefreshConnectionStatus();
        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(CanvasBackground));
        ScheduleSave();
    }

    // ---------------------------------------------------------------- editor helpers

    private void RefreshActionGroups()
    {
        // Actions that don't fit the selected control are dimmed (and hidden when the filter is on).
        var selected = SelectedControl?.Control;
        foreach (var action in _actionsByKey.Values)
        {
            action.UpdateFit(selected);
        }

        var filteredActions = _actionsByKey.Values
            .Where(action => action.ShowInPalette)
            .Where(action => !_onlyCompatibleActions || action.IsCompatible)
            .Where(action => string.IsNullOrWhiteSpace(SearchText)
                || action.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || action.PluginName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || (action.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(static action => action.PluginName)
            .ThenBy(static action => action.Name)
            .ToList();

        foreach (var existing in ActionGroups)
        {
            _expandedGroups[existing.PluginId] = existing.IsExpanded;
        }

        ActionGroups.Clear();
        var searching = !string.IsNullOrWhiteSpace(SearchText);
        foreach (var group in filteredActions.GroupBy(static action => new { action.PluginId, action.PluginName }))
        {
            // Collapsed like the Stream Deck app, unless opened before; a search opens every match.
            ActionGroups.Add(new PluginActionGroupViewModel(
                group.Key.PluginId,
                group.Key.PluginName,
                group,
                PluginIcon(group.Key.PluginId),
                searching || _expandedGroups.GetValueOrDefault(group.Key.PluginId)));
        }
    }

    private readonly Dictionary<string, bool> _expandedGroups = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A plugin's icon: its own (installed plugins), else one for DeckHost's system plugins.</summary>
    private string? PluginIcon(string pluginId) => pluginId switch
    {
        SystemPlugins.SystemPluginSet.BuiltInPluginId => Contracts.Icons.Apps,
        SystemPlugins.SystemPluginSet.MediaPluginId => Contracts.Icons.PlayPause,
        SystemPlugins.SystemPluginSet.SystemControlsPluginId => Contracts.Icons.Keyboard,
        _ => _pluginCatalog.Entries.FirstOrDefault(entry => string.Equals(entry.Id, pluginId, StringComparison.OrdinalIgnoreCase))?.IconPath,
    };

    private void ApplyProfileBindings()
    {
        foreach (var controlItem in ControlItems)
        {
            AttachBinding(controlItem, SelectedProfile?.GetBinding(controlItem.Id));
        }

        SyncSelectedAppearanceEditors();
        RaiseSelectedAssignmentChanged();
        RebuildEditor();
        OnPropertyChanged(nameof(CurrentPageName));
        OnPropertyChanged(nameof(SelectedProfileSummary));
        OnPropertyChanged(nameof(CanvasBackground));
        StatusMessage = SelectedProfile switch
        {
            null => "No profile selected.",
            { IsFolder: true } folder => $"Editing the folder {folder.Name} of {folder.RootProfile.Name}.",
            { IsRoot: false } page => $"Editing {PageName(page).ToLowerInvariant()} of {page.RootProfile.Name}.",
            { } profile => $"Editing profile {profile.Name}.",
        };
    }

    private void AttachBinding(ControlItemViewModel controlItem, ControlBindingViewModel? binding)
    {
        controlItem.AssignedBinding = binding;
        OnPropertyChanged(nameof(ShowEmptyDeckHint));

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

    /// <summary>The action list's entry for an action (its name and icon), or null.</summary>
    /// <summary>The actions the welcome tour's starter layout puts on empty keys, in reading order.</summary>
    internal static readonly string[] StarterKeyActions =
    [
        "deckhost.plugin.media:media.playpause",
        "deckhost.plugin.media:media.previous",
        "deckhost.plugin.media:media.next",
        "deckhost.plugin.media:media.mute",
        "deckhost.plugin.media:media.volumedown",
        "deckhost.plugin.media:media.volumeup",
        "deckhost.plugin.builtin:builtin.clock",
        "deckhost.plugin.system:system.info",
        "deckhost.plugin.system:system.lock",
        "deckhost.plugin.builtin:builtin.switch-profile",
    ];

    /// <summary>
    /// Fills the edited layout's empty controls with everyday actions (media keys, clock, system info,
    /// lock; the first knob sets the volume, a swipe skips tracks). Returns how many it filled.
    /// </summary>
    public int ApplyStarterLayout()
    {
        var filled = 0;
        Grouped("the starter layout", () => filled = ApplyStarterLayoutCore());
        return filled;
    }

    private int ApplyStarterLayoutCore()
    {
        if (SelectedProfile is null)
        {
            return 0;
        }

        var filled = 0;
        var keyActions = new Queue<string>(StarterKeyActions);
        foreach (var item in ControlItems.ToList())
        {
            if (SelectedProfile.GetBinding(item.Id) is not null)
            {
                continue;
            }

            var key = item.Control.Type switch
            {
                Contracts.Models.ControlType.Key when keyActions.Count > 0 => keyActions.Dequeue(),
                Contracts.Models.ControlType.Encoder when !ControlItems.Any(other => other.Control.Type == Contracts.Models.ControlType.Encoder && other.Id.CompareTo(item.Id) < 0) => "deckhost.plugin.media:media.volume-knob",
                _ when item.Id == "touch.swipe" => "deckhost.plugin.media:media.track-swipe",
                _ => null,
            };
            if (key is not null && _actionsByKey.TryGetValue(key, out var action) && action.FitsControl(item.Control))
            {
                AssignActionToControl(item.Control, action.Action);
                filled++;
            }
        }

        SelectControl(ControlItems[0].Control);
        StatusMessage = filled == 0 ? "Every control already had an action." : $"Put {filled} everyday actions on the empty controls. Drag others from the list on the right.";
        return filled;
    }

    public ActionItemViewModel? FindActionItem(IDeckAction action) => TryGetActionItem(action, out var item) ? item : null;

    private bool TryGetActionItem(IDeckAction action, out ActionItemViewModel actionItem)
    {
        actionItem = _actionsByKey.Values.FirstOrDefault(candidate => ReferenceEquals(candidate.Action, action))!;
        return actionItem is not null;
    }
}

/// <summary>A page button under the device.</summary>
/// <summary>A device in the header's device picker.</summary>
public sealed class DeviceChoiceViewModel(DeviceDefinition device)
{
    public string Id { get; } = device.Id;

    public string Label { get; } = LabelOf(device);

    public static string LabelOf(DeviceDefinition device) => device.IsConnected ? device.Name
        : device.Id.StartsWith("web.", StringComparison.Ordinal) ? $"{device.Name} (off)"
        : $"{device.Name} (offline)";

    public override string ToString() => Label;
}

public sealed class PageTabViewModel(MainViewModel owner, ProfileItemViewModel page, int number, bool isCurrent)
{
    public ProfileItemViewModel Page { get; } = page;

    public int Number { get; } = number;

    public bool IsCurrent { get; } = isCurrent;

    public bool CanDelete => !Page.IsRoot;

    public CommunityToolkit.Mvvm.Input.RelayCommand SelectCommand { get; } = new(() => owner.SelectedProfile = page);

    public CommunityToolkit.Mvvm.Input.RelayCommand DeleteCommand { get; } = new(() => owner.DeletePage(page));
}
