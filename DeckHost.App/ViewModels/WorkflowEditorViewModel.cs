using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Models;
using DeckHost.Core.Workflows;

namespace DeckHost.App.ViewModels;

/// <summary>
/// The node editor of one control's workflow. The workflow is immutable: every edit makes a new
/// one, which goes onto the undo stack and to the control right away (<see cref="_apply"/>), so the
/// device runs what the canvas shows. Runs on the device light up the nodes they pass.
/// </summary>
public sealed partial class WorkflowEditorViewModel : ViewModelBase, IDisposable
{
    public const double NodeWidth = 210;
    public const double HeaderHeight = 30;
    public const double SummaryHeight = 24;
    public const double PortRowHeight = 22;

    private const int MaxUndo = 200;
    private static readonly TimeSpan ActiveFor = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan SettingsMergeWindow = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan FadeFrame = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan WaitFrame = TimeSpan.FromMilliseconds(33);

    private readonly Func<Workflow, bool> _apply;
    private readonly IDeviceHostService? _host;
    private readonly Dictionary<string, ActionItemViewModel> _actionsByKey;
    private readonly List<Workflow> _undo = [];
    private readonly List<Workflow> _redo = [];
    private readonly Dictionary<string, DateTime> _activeUntil = [];
    private readonly Dictionary<string, DateTime> _failedUntil = [];
    private readonly Dictionary<(string NodeId, int RunId), (DateTime Start, TimeSpan Length)> _waits = [];
    private readonly DispatcherTimer? _fadeTimer;
    private readonly Workflow _original;
    private Workflow _workflow;
    private string? _selectedNodeId;
    private WorkflowLink? _selectedLink;
    private string? _lastSettingsEdit;
    private DateTime _lastSettingsEditAt;
    private SettingsEditorViewModel? _nodeSettings;
    private SettingsEditorViewModel? _failureOptions;
    private string _nodeTitle = string.Empty;
    private string _status = string.Empty;
    private bool _syncingInspector;

    /// <param name="apply">Stores an edited workflow on the control; false when the control no longer has it.</param>
    public WorkflowEditorViewModel(
        string deviceId,
        string layoutId,
        PhysicalControl control,
        string controlName,
        Workflow workflow,
        IEnumerable<ActionItemViewModel> actions,
        Func<Workflow, bool> apply,
        IDeviceHostService? host = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(apply);
        DeviceId = deviceId;
        LayoutId = layoutId;
        Control = control;
        ControlName = controlName;
        _workflow = workflow;
        _original = workflow;
        _apply = apply;
        _host = host;

        var all = actions.ToList();
        _actionsByKey = all.GroupBy(static action => action.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        AvailableActions = [.. all
            .Where(action => action.CanBeMultiActionStep && action.ShowInPalette && action.FitsControl(control))
            .OrderBy(static action => action.PluginName)
            .ThenBy(static action => action.Name)];
        RefreshActionGroups();
        NodePalette = [.. WorkflowNodeTypes.All.Where(static type => type.Kind != WorkflowNodeKinds.Action).Select(static type => new WorkflowPaletteItem(type))];
        WhileRunningOptions =
        [
            new(WorkflowConcurrency.Restart, "Start over: stop the run that is going and start again (a wait begins again)"),
            new(WorkflowConcurrency.Parallel, "Run in parallel: every press starts its own run (each wait shows)"),
            new(WorkflowConcurrency.Ignore, "Ignore the press until the run is done (the log says so)"),
            new(WorkflowConcurrency.Stop, "Stop it (the input works as a stop button)"),
        ];

        if (_host is not null)
        {
            _host.WorkflowTraced += OnTraced;
            _fadeTimer = new DispatcherTimer { Interval = FadeFrame };
            _fadeTimer.Tick += (_, _) => FadeHighlights();
        }

        SelectNode(_workflow.Nodes.FirstOrDefault()?.Id);
        RefreshVariables();
        RefreshShared();
    }

    /// <summary>The time the live view goes by (tests set it to hold a wait half way).</summary>
    internal Func<DateTime> Clock { get; set; } = static () => DateTime.UtcNow;

    private DateTime Now() => Clock();

    public string DeviceId { get; }

    public string LayoutId { get; }

    public PhysicalControl Control { get; }

    public string ControlName { get; }

    public string Title => string.IsNullOrWhiteSpace(_workflow.Name) ? $"Workflow · {ControlName}" : $"{_workflow.Name} · {ControlName}";

    /// <summary>The name the user gave the workflow (empty: it has none).</summary>
    public string WorkflowName
    {
        get => _workflow.Name ?? string.Empty;
        set
        {
            var name = (value ?? string.Empty).Trim();
            if (name.Length > 80)
            {
                name = name[..80];
            }

            if (name == (_workflow.Name ?? string.Empty))
            {
                return;
            }

            Commit(_workflow with { Name = name.Length == 0 ? null : name }, name.Length == 0 ? "Removed the workflow's name" : $"Named the workflow {name}", "workflow-name");
        }
    }

    /// <summary>The workflow as a file to share (<see cref="WorkflowFile"/>).</summary>
    public string ExportText() => WorkflowFile.Serialize(_workflow);

    public string ExportFileName => WorkflowFile.FileNameOf(_workflow, ControlName);

    /// <summary>
    /// Replaces the workflow with one read from a file (one undo step brings the old one back).
    /// Returns why not when the file can't be used.
    /// </summary>
    public string? Import(string text)
    {
        var imported = WorkflowFile.TryRead(text, out var error);
        if (imported is null)
        {
            return error;
        }

        Commit(imported with { WhileRunning = WorkflowConcurrency.All.Contains(imported.WhileRunning) ? imported.WhileRunning : WorkflowConcurrency.Parallel }, string.IsNullOrWhiteSpace(imported.Name) ? "Imported a workflow" : $"Imported {imported.Name}");
        SelectNode(_workflow.Nodes.FirstOrDefault(static node => node.Kind == WorkflowNodeKinds.Trigger)?.Id ?? _workflow.Nodes.FirstOrDefault()?.Id);
        OnPropertyChanged(nameof(WorkflowName));
        OnPropertyChanged(nameof(Title));
        return null;
    }

    /// <summary>The workflow as it is now (what the canvas draws).</summary>
    public Workflow Workflow => _workflow;

    /// <summary>Raised whenever the canvas must be drawn again (an edit, a selection, a run).</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<ActionItemViewModel> AvailableActions { get; }

    /// <summary>The actions grouped by plugin, like the main window's list (collapsed until opened; a search opens every match).</summary>
    public ObservableCollection<PluginActionGroupViewModel> ActionGroups { get; } = [];

    private string _actionSearch = string.Empty;
    private readonly Dictionary<string, bool> _openGroups = new(StringComparer.OrdinalIgnoreCase);

    public string ActionSearch
    {
        get => _actionSearch;
        set
        {
            if (SetProperty(ref _actionSearch, value ?? string.Empty))
            {
                RefreshActionGroups();
            }
        }
    }

    private void RefreshActionGroups()
    {
        var query = _actionSearch.Trim();
        foreach (var existing in ActionGroups)
        {
            _openGroups[existing.PluginId] = existing.IsExpanded;
        }

        var shown = query.Length == 0
            ? AvailableActions
            : [.. AvailableActions.Where(action =>
                action.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                action.PluginName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (action.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))];
        ActionGroups.Clear();
        foreach (var group in shown.GroupBy(static action => (action.PluginId, action.PluginName)))
        {
            ActionGroups.Add(new PluginActionGroupViewModel(group.Key.PluginId, group.Key.PluginName, group, null, query.Length > 0 || _openGroups.GetValueOrDefault(group.Key.PluginId)));
        }
    }

    public IReadOnlyList<WorkflowPaletteItem> NodePalette { get; }

    public IReadOnlyList<ActionSettingOption> WhileRunningOptions { get; }

    public ActionSettingOption WhileRunning
    {
        get => WhileRunningOptions.FirstOrDefault(option => option.Value == _workflow.WhileRunning) ?? WhileRunningOptions[0];
        set
        {
            if (value is not null && value.Value != _workflow.WhileRunning)
            {
                Commit(_workflow with { WhileRunning = value.Value }, "While running: " + value.Label.ToLowerInvariant());
            }
        }
    }

    /// <summary>The variables that are set right now (refreshed as runs go and by Refresh), for finding out why a workflow went the way it did.</summary>
    public ObservableCollection<VariableRow> Variables { get; } = [];

    public bool HasVariables => Variables.Count > 0;

    private string _newVariableName = string.Empty;
    private string _newVariableValue = string.Empty;

    public string NewVariableName
    {
        get => _newVariableName;
        set => SetProperty(ref _newVariableName, value ?? string.Empty);
    }

    public string NewVariableValue
    {
        get => _newVariableValue;
        set => SetProperty(ref _newVariableValue, value ?? string.Empty);
    }

    /// <summary>Reads the variables from the host again.</summary>
    [RelayCommand]
    public void RefreshVariables()
    {
        var now = _host?.UserVariables() ?? [];
        if (now.Count == Variables.Count && now.Select(static pair => (pair.Key, pair.Value)).SequenceEqual(Variables.Select(static row => (row.Name, row.Value))))
        {
            return;
        }

        Variables.Clear();
        foreach (var (name, value) in now)
        {
            Variables.Add(new VariableRow(name, value));
        }

        OnPropertyChanged(nameof(HasVariables));
    }

    /// <summary>Sets a variable from the editor (to try a workflow with it); an empty value clears it.</summary>
    [RelayCommand]
    public void SetVariableFromEditor()
    {
        var name = _newVariableName.Trim();
        if (name.Length == 0)
        {
            Status = "Type the variable's name first.";
            return;
        }

        _host?.SetVariable(DeckHost.Contracts.VariableKeys.UserVariable(name), _newVariableValue);
        Status = _newVariableValue.Length == 0 ? $"{name} cleared." : $"{name} = {_newVariableValue}";
        RefreshVariables();
    }

    /// <summary>The run log: newest first.</summary>
    public ObservableCollection<string> RunLog { get; } = [];

    public bool HasRunLog => RunLog.Count > 0;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public bool IsEmpty => _workflow.Nodes.Count == 0;

    public string Overview
    {
        get
        {
            var triggers = WorkflowEngine.TriggersOf(_workflow).Select(WorkflowTriggers.Describe).ToList();
            var actions = _workflow.Nodes.Count(static node => node.Kind == WorkflowNodeKinds.Action);
            return triggers.Count == 0
                ? "Add a When node: it decides what starts the workflow."
                : $"Starts when {string.Join(" / ", triggers).ToLowerInvariant()} · {actions} action{(actions == 1 ? string.Empty : "s")} · {_workflow.Nodes.Count} nodes";
        }
    }

    // ---------------------------------------------------------------- drawing helpers (the canvas)

    public static WorkflowNodeType TypeOf(WorkflowNode node) => WorkflowNodeTypes.Of(node.Kind);

    public string HeaderOf(WorkflowNode node) =>
        node.Kind == WorkflowNodeKinds.Action && node.Action is { } action
            ? _actionsByKey.TryGetValue(EditorActionIds.Key(action.PluginId, action.ActionId), out var item) ? item.PluginName : action.PluginName
            : TypeOf(node).Name;

    public string SummaryOf(WorkflowNode node) => WorkflowNodeTypes.Summarize(node);

    /// <summary>Whether an action node's action isn't loaded (its plugin is disabled or uninstalled).</summary>
    public bool IsMissing(WorkflowNode node) =>
        node.Kind == WorkflowNodeKinds.Action &&
        (node.Action is null || !_actionsByKey.ContainsKey(EditorActionIds.Key(node.Action.PluginId, node.Action.ActionId)));

    public static double HeightOf(WorkflowNode node) =>
        HeaderHeight + SummaryHeight + (Math.Max(1, WorkflowNodeKinds.OutputsOf(node).Count) * PortRowHeight) + 8;

    /// <summary>Where links leave a node's output port (canvas coordinates).</summary>
    public static (double X, double Y) OutputPoint(WorkflowNode node, string port)
    {
        var index = Math.Max(0, WorkflowNodeKinds.OutputsOf(node).ToList().IndexOf(port));
        return (node.X + NodeWidth, node.Y + HeaderHeight + SummaryHeight + (index * PortRowHeight) + (PortRowHeight / 2) + 2);
    }

    /// <summary>Where links enter a node.</summary>
    public static (double X, double Y) InputPoint(WorkflowNode node) => (node.X, node.Y + (HeaderHeight / 2));

    public bool IsSelected(WorkflowNode node) => node.Id == _selectedNodeId;

    public bool IsSelected(WorkflowLink link) => link == _selectedLink;

    /// <summary>How far a waiting node is (0 to 1) while a run waits at it (the oldest run when several wait); null when nothing waits there.</summary>
    public double? WaitProgress(WorkflowNode node) => WaitProgresses(node).Cast<double?>().FirstOrDefault();

    /// <summary>How far each run waiting at the node is (0 to 1), oldest first: runs in parallel each show their own wait.</summary>
    public IReadOnlyList<double> WaitProgresses(WorkflowNode node)
    {
        var now = Now();
        return
        [
            .. _waits
                .Where(pair => pair.Key.NodeId == node.Id && pair.Value.Length > TimeSpan.Zero && now - pair.Value.Start < pair.Value.Length)
                .OrderBy(static pair => pair.Key.RunId)
                .Select(pair => Math.Clamp((now - pair.Value.Start) / pair.Value.Length, 0d, 1d)),
        ];
    }

    public bool IsActive(WorkflowNode node) => _activeUntil.TryGetValue(node.Id, out var until) && until > Now();

    public bool HasFailed(WorkflowNode node) => _failedUntil.TryGetValue(node.Id, out var until) && until > Now();

    /// <summary>Whether a link was just taken by a run (both ends lit).</summary>
    public bool IsActive(WorkflowLink link) =>
        _activeUntil.TryGetValue(link.From, out var from) && from > Now() &&
        _activeUntil.TryGetValue(link.To, out var to) && to > Now();

    // ---------------------------------------------------------------- selection and inspector

    public WorkflowNode? SelectedNode => _selectedNodeId is { } id ? _workflow.Find(id) : null;

    public bool HasSelectedNode => SelectedNode is not null;

    public bool HasSelectedLink => _selectedLink is not null;

    public bool HasSelection => HasSelectedNode || HasSelectedLink;

    public bool ShowNoSelection => !HasSelection;

    public string SelectedKindName => SelectedNode is { } node ? TypeOf(node).Name : string.Empty;

    public string SelectedDescription => SelectedNode is { } node
        ? node.Kind == WorkflowNodeKinds.Action && node.Action is { } action && _actionsByKey.TryGetValue(EditorActionIds.Key(action.PluginId, action.ActionId), out var item)
            ? item.Description ?? string.Empty
            : TypeOf(node).Description
        : string.Empty;

    public string SelectedLinkText => _selectedLink is { } link && _workflow.Find(link.From) is { } from && _workflow.Find(link.To) is { } to
        ? $"{SummaryOf(from)} ({WorkflowPorts.Describe(link.Port)}) → {SummaryOf(to)}"
        : string.Empty;

    public bool SelectedIsAction => SelectedNode?.Kind == WorkflowNodeKinds.Action;

    public bool SelectedIsTrigger => SelectedNode?.Kind == WorkflowNodeKinds.Trigger;

    public bool SelectedIsMissing => SelectedNode is { } node && IsMissing(node);

    /// <summary>The action of the selected action node; choosing another one replaces it (settings start over).</summary>
    public ActionItemViewModel? SelectedAction
    {
        get => SelectedNode?.Action is { } action && _actionsByKey.TryGetValue(EditorActionIds.Key(action.PluginId, action.ActionId), out var item) ? item : null;
        set
        {
            if (_syncingInspector || value is null || SelectedNode is not { Kind: WorkflowNodeKinds.Action } node || ReferenceEquals(value, SelectedAction))
            {
                return;
            }

            ReplaceNode(node with { Action = value.ToActionReference(value.DefaultSettings) }, $"Action: {value.Name}");
            RebuildInspector();
        }
    }

    /// <summary>The selected node's own settings (a wait's length, a condition) or its action's settings.</summary>
    public SettingsEditorViewModel? NodeSettings
    {
        get => _nodeSettings;
        private set
        {
            if (SetProperty(ref _nodeSettings, value))
            {
                OnPropertyChanged(nameof(HasNodeSettings));
            }
        }
    }

    public bool HasNodeSettings => NodeSettings?.HasFields == true;

    /// <summary>The selected node's own name (empty: the generated summary).</summary>
    public string NodeTitle
    {
        get => _nodeTitle;
        set
        {
            if (!SetProperty(ref _nodeTitle, value) || _syncingInspector || SelectedNode is not { } node)
            {
                return;
            }

            var title = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (title != node.Title)
            {
                ReplaceNode(node with { Title = title }, "Renamed", mergeKey: node.Id + ":title");
            }
        }
    }

    public string NodeTitlePlaceholder => SelectedNode is { } node ? WorkflowNodeTypes.Summarize(node with { Title = null }) : string.Empty;

    public void SelectNode(string? nodeId)
    {
        DeleteChoiceNodeId = null;
        _selectedLink = null;
        _selectedNodeId = nodeId is not null && _workflow.Find(nodeId) is not null ? nodeId : null;
        RebuildInspector();
        RaiseChanged();
    }

    public void SelectLink(WorkflowLink? link)
    {
        DeleteChoiceNodeId = null;
        _selectedNodeId = null;
        _selectedLink = link is not null && _workflow.Links.Contains(link) ? link : null;
        RebuildInspector();
        RaiseChanged();
    }

    private void RebuildInspector()
    {
        _syncingInspector = true;
        try
        {
            if (_nodeSettings is not null)
            {
                _nodeSettings.Changed -= OnNodeSettingsChanged;
            }

            if (_failureOptions is not null)
            {
                _failureOptions.Changed -= OnFailureOptionsChanged;
            }

            var node = SelectedNode;
            NodeSettings = node switch
            {
                null => null,
                { Kind: WorkflowNodeKinds.Action, Action: { } action } when _actionsByKey.TryGetValue(EditorActionIds.Key(action.PluginId, action.ActionId), out var item) =>
                    new SettingsEditorViewModel(item.SettingDefinitions, action.EffectiveSettings, Control.Inputs),
                { Kind: WorkflowNodeKinds.Action } => null,
                _ => new SettingsEditorViewModel(SettingsOf(node), node.EffectiveSettings, Control.Inputs),
            };
            if (_nodeSettings is not null)
            {
                _nodeSettings.Changed += OnNodeSettingsChanged;
            }

            RebuildCases();
            FailureOptions = node is { Kind: WorkflowNodeKinds.Action }
                ? new SettingsEditorViewModel(WorkflowNodeTypes.ActionFailureSettings, node.EffectiveSettings, Control.Inputs)
                : null;
            if (_failureOptions is not null)
            {
                _failureOptions.Changed += OnFailureOptionsChanged;
            }

            NodeTitle = node?.Title ?? string.Empty;
            foreach (var name in new[]
                     {
                         nameof(SelectedNode), nameof(HasSelectedNode), nameof(HasSelectedLink), nameof(HasSelection), nameof(ShowNoSelection),
                         nameof(SelectedKindName), nameof(SelectedDescription), nameof(SelectedLinkText), nameof(SelectedIsAction),
                         nameof(SelectedIsTrigger), nameof(SelectedIsCall), nameof(SelectedIsMissing), nameof(SelectedAction), nameof(NodeTitlePlaceholder),
                     })
            {
                OnPropertyChanged(name);
            }
        }
        finally
        {
            _syncingInspector = false;
        }
    }

    /// <summary>For an action node: what happens when the action did not run (tries and the wait between them).</summary>
    public SettingsEditorViewModel? FailureOptions
    {
        get => _failureOptions;
        private set
        {
            if (SetProperty(ref _failureOptions, value))
            {
                OnPropertyChanged(nameof(HasFailureOptions));
            }
        }
    }

    public bool HasFailureOptions => _failureOptions is not null;

    private void OnFailureOptionsChanged(object? sender, EventArgs e)
    {
        if (_syncingInspector || SelectedNode is not { Kind: WorkflowNodeKinds.Action } node || _failureOptions is null)
        {
            return;
        }

        ReplaceNode(node with { Settings = _failureOptions.ToSettings() }, "Changed what happens when " + SummaryOf(node) + " fails", mergeKey: node.Id + ":failure");
    }

    private void OnNodeSettingsChanged(object? sender, EventArgs e)
    {
        if (_syncingInspector || SelectedNode is not { } node || _nodeSettings is null)
        {
            return;
        }

        var settings = _nodeSettings.ToSettings();
        var updated = node.Kind == WorkflowNodeKinds.Action && node.Action is { } action
            ? node with { Action = action with { Settings = settings } }
            : node with { Settings = settings };
        ReplaceNode(updated, "Changed " + TypeOf(node).Name.ToLowerInvariant(), mergeKey: node.Id + ":settings");
        OnPropertyChanged(nameof(NodeTitlePlaceholder));
    }

    // ---------------------------------------------------------------- edits

    /// <summary>Adds a node of a kind at a canvas position (snapped to the grid) and selects it.</summary>
    /// <param name="into">A link to put the node into (dropped onto it): one undo step adds the node and splits the link.</param>
    public WorkflowNode AddNode(string kind, double x, double y, WorkflowLink? into = null)
    {
        var type = WorkflowNodeTypes.Of(kind);
        var node = new WorkflowNode(WorkflowConverter.NextId(_workflow), type.Kind, Snap(x), Snap(y))
        {
            Settings = type.Settings.Count == 0
                ? null
                : new ActionSettings(type.Settings.Where(static setting => setting.DefaultValue.Length > 0)
                    .Select(static setting => new KeyValuePair<string, string>(setting.Key, setting.DefaultValue))),
        };
        if (node.Kind == WorkflowNodeKinds.Switch)
        {
            // A new Switch starts with two cases to fill in, so its outputs are there to link.
            node = WithCases(node, [new WorkflowCase("c1", "1"), new WorkflowCase("c2", "2")]);
        }

        var split = into is not null && CanBeInserted(node.Kind) && _workflow.Links.Contains(into);
        var added = _workflow with { Nodes = [.. _workflow.Nodes, node], Links = split ? Splice(_workflow.Links, into!, node) : _workflow.Links };
        Commit(split ? WorkflowSpacing.MakeRoom(added, node.Id, into!.To, NodeWidth) : added, split ? $"Added {type.Name} into a link" : $"Added {type.Name}");
        SelectNode(node.Id);
        return node;
    }

    /// <summary>The outputs a new node is linked from automatically: the error output is only ever linked by hand.</summary>
    private static IEnumerable<string> FlowOutputsOf(WorkflowNode node) => WorkflowNodeKinds.OutputsOf(node).Where(static port => port != WorkflowPorts.Error);

    /// <summary>Whether a node of the kind can go into the middle of a link: it has an input and an output.</summary>
    public static bool CanBeInserted(string kind) => WorkflowNodeKinds.HasInput(kind) && WorkflowNodeKinds.OutputsOf(kind).Count > 0;

    /// <summary>The output that carries on to what the link led to: Done for a Repeat (Each is its body), else the first.</summary>
    internal static string ContinuationPort(WorkflowNode node) =>
        node.Kind == WorkflowNodeKinds.Repeat ? WorkflowPorts.Done
            : node.Kind == WorkflowNodeKinds.Switch ? WorkflowPorts.Default
            : WorkflowNodeKinds.OutputsOf(node.Kind)[0];

    /// <summary>The links with <paramref name="into"/> cut in two: from its start into the node, and from the node on to its end.</summary>
    private static IReadOnlyList<WorkflowLink> Splice(IReadOnlyList<WorkflowLink> links, WorkflowLink into, WorkflowNode node) =>
    [
        .. links.Where(link => link != into),
        new WorkflowLink(into.From, into.Port, node.Id),
        new WorkflowLink(node.Id, ContinuationPort(node), into.To),
    ];

    /// <summary>Whether a node that is already on the canvas can be dropped into a link: it isn't linked to anything yet.</summary>
    public bool CanInsertIntoLink(string nodeId) =>
        _workflow.Find(nodeId) is { } node && CanBeInserted(node.Kind) &&
        !_workflow.Links.Any(link => link.From == nodeId || link.To == nodeId);

    /// <summary>Puts a node that isn't linked yet into the middle of a link, as one undo step (optionally moving it there). Returns why not when it can't.</summary>
    public string? InsertIntoLink(string nodeId, WorkflowLink link, double? x = null, double? y = null)
    {
        if (_workflow.Find(nodeId) is not { } node)
        {
            return "That node is gone.";
        }

        if (!_workflow.Links.Contains(link))
        {
            return "That link is gone.";
        }

        if (!CanBeInserted(node.Kind))
        {
            return node.Kind == WorkflowNodeKinds.Trigger ? "A When node starts a workflow; it can't go into a link." : "A Stop node ends the run; it can't pass the link on.";
        }

        if (!CanInsertIntoLink(nodeId))
        {
            return "Only a node that isn't linked yet can be dropped onto a link.";
        }

        var placed = x is { } px && y is { } py ? node with { X = Math.Max(0, Snap(px)), Y = Math.Max(0, Snap(py)) } : node;
        var spliced = _workflow with
        {
            Nodes = [.. _workflow.Nodes.Select(candidate => candidate.Id == nodeId ? placed : candidate)],
            Links = Splice(_workflow.Links, link, node),
        };
        Commit(WorkflowSpacing.MakeRoom(spliced, nodeId, link.To, NodeWidth), $"Put {SummaryOf(node)} into a link");
        SelectNode(nodeId);
        return null;
    }

    /// <summary>Adds an action node; when a node is selected and its first output is free, links it from there.</summary>
    public WorkflowNode? AddAction(ActionItemViewModel action, double? x = null, double? y = null, WorkflowLink? into = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!action.CanBeMultiActionStep)
        {
            Status = $"{action.Name} can't be part of a workflow.";
            return null;
        }

        var after = SelectedNode;
        var freePort = after is null ? null : FlowOutputsOf(after).FirstOrDefault(port => !_workflow.LinksFrom(after.Id, port).Any());
        var (px, py) = (x, y) switch
        {
            ({ } ax, { } ay) => (ax, ay),
            _ when after is not null => (after.X + NodeWidth + 40, after.Y),
            _ => NextFreeSpot(),
        };

        var node = new WorkflowNode(WorkflowConverter.NextId(_workflow), WorkflowNodeKinds.Action, Snap(px), Snap(py))
        {
            Action = action.ToActionReference(action.DefaultSettings),
        };
        var split = into is not null && _workflow.Links.Contains(into);
        var links = split
            ? Splice(_workflow.Links, into!, node)
            : x is null && after is not null && freePort is not null
                ? [.. _workflow.Links, new WorkflowLink(after.Id, freePort, node.Id)]
                : _workflow.Links;
        var withAction = _workflow with { Nodes = [.. _workflow.Nodes, node], Links = links };
        Commit(split ? WorkflowSpacing.MakeRoom(withAction, node.Id, into!.To, NodeWidth) : withAction, split ? $"Added {action.Name} into a link" : $"Added {action.Name}");
        SelectNode(node.Id);
        return node;
    }

    public bool AddActionByKey(string key, double x, double y) =>
        _actionsByKey.TryGetValue(key, out var action) && AddAction(action, x, y) is not null;

    /// <summary>Moves a node (after a drag), snapped to the grid.</summary>
    public void MoveNode(string nodeId, double x, double y)
    {
        if (_workflow.Find(nodeId) is not { } node)
        {
            return;
        }

        x = Math.Max(0, Snap(x));
        y = Math.Max(0, Snap(y));
        if (Math.Abs(node.X - x) < 0.5 && Math.Abs(node.Y - y) < 0.5)
        {
            return;
        }

        ReplaceNode(node with { X = x, Y = y }, "Moved");
    }

    /// <summary>Links an output port to a node. Returns why not when it can't be made.</summary>
    public string? Connect(string fromId, string port, string toId)
    {
        if (_workflow.Find(fromId) is not { } from || _workflow.Find(toId) is not { } to)
        {
            return "That node is gone.";
        }

        if (!WorkflowNodeKinds.HasInput(to.Kind))
        {
            return "A When node starts a workflow; nothing can lead into it.";
        }

        if (!WorkflowNodeKinds.OutputsOf(from).Contains(port))
        {
            return "That node has no such output.";
        }

        var link = new WorkflowLink(fromId, port, toId);
        if (_workflow.Links.Contains(link))
        {
            return "Those are already linked.";
        }

        if (_workflow.WouldCycle(fromId, toId))
        {
            return "That would make a loop. Use a Repeat node to run something several times.";
        }

        Commit(_workflow with { Links = [.. _workflow.Links, link] }, $"Linked {SummaryOf(from)} → {SummaryOf(to)}");
        SelectLink(link);
        return null;
    }

    /// <summary>Removes the selected node or link. A node is taken out of the flow: what led into it is linked to what it led to.</summary>
    [RelayCommand]
    public void DeleteSelection()
    {
        if (_selectedLink is { } link)
        {
            Commit(_workflow with { Links = [.. _workflow.Links.Where(candidate => candidate != link)] }, "Removed a link");
            SelectLink(null);
            return;
        }

        if (SelectedNode is not { } node)
        {
            return;
        }

        if (WorkflowGap.NeedsChoice(_workflow, node.Id))
        {
            // Several branches come out of it: ask which one the previous nodes should continue with.
            DeleteChoiceNodeId = node.Id;
            return;
        }

        RemoveNode(node, GapMode.Reconnect);
    }

    /// <summary>Removes the selected node and its links without linking anything across the gap (Shift+Delete).</summary>
    [RelayCommand]
    public void DeleteSelectionAndDisconnect()
    {
        if (SelectedNode is { } node)
        {
            RemoveNode(node, GapMode.Disconnect);
        }
        else
        {
            DeleteSelection();
        }
    }

    /// <summary>Answer to the question after <see cref="DeleteSelection"/>: continue with the first branch.</summary>
    [RelayCommand]
    public void DeleteAndReconnectFirstPath()
    {
        if (DeleteChoiceNodeId is { } id && _workflow.Find(id) is { } node)
        {
            RemoveNode(node, GapMode.FirstPath);
        }
    }

    [RelayCommand]
    public void DeleteAndDisconnect() => DeleteSelectionAndDisconnect();

    [RelayCommand]
    public void CancelDelete() => DeleteChoiceNodeId = null;

    private string? _deleteChoiceNodeId;

    /// <summary>The node whose removal waits for the user's answer (null: nothing is asked).</summary>
    public string? DeleteChoiceNodeId
    {
        get => _deleteChoiceNodeId;
        private set
        {
            if (SetProperty(ref _deleteChoiceNodeId, value))
            {
                OnPropertyChanged(nameof(HasDeleteChoice));
            }
        }
    }

    public bool HasDeleteChoice => _deleteChoiceNodeId is not null;

    private void RemoveNode(WorkflowNode node, GapMode mode)
    {
        DeleteChoiceNodeId = null;
        Commit(WorkflowGap.Remove(_workflow, node.Id, mode), mode == GapMode.Disconnect ? $"Removed {SummaryOf(node)} and its links" : $"Removed {SummaryOf(node)}");
        SelectNode(null);
    }

    /// <summary>Whether the selected node has links to take off.</summary>
    public bool SelectedHasLinks => SelectedNode is { } node && _workflow.Links.Any(link => link.From == node.Id || link.To == node.Id);

    /// <summary>Takes every link off the selected node; the node stays.</summary>
    [RelayCommand]
    public void DisconnectSelection()
    {
        if (SelectedNode is not { } node || !SelectedHasLinks)
        {
            return;
        }

        Commit(_workflow with { Links = [.. _workflow.Links.Where(link => link.From != node.Id && link.To != node.Id)] }, $"Unlinked {SummaryOf(node)}");
    }

    [RelayCommand]
    public void DuplicateSelection()
    {
        if (SelectedNode is not { } node)
        {
            return;
        }

        var copy = node with { Id = WorkflowConverter.NextId(_workflow), X = node.X + 30, Y = node.Y + 30 };
        Commit(_workflow with { Nodes = [.. _workflow.Nodes, copy] }, $"Duplicated {SummaryOf(node)}");
        SelectNode(copy.Id);
    }

    [RelayCommand]
    private void AddPaletteNode(WorkflowPaletteItem? item)
    {
        if (item is null)
        {
            return;
        }

        var (x, y) = SelectedNode is { } after ? (after.X + NodeWidth + 40, after.Y) : NextFreeSpot();
        var previous = SelectedNode;
        var node = AddNode(item.Kind, x, y);
        if (previous is not null && WorkflowNodeKinds.HasInput(node.Kind) &&
            FlowOutputsOf(previous).FirstOrDefault(port => !_workflow.LinksFrom(previous.Id, port).Any()) is { } port)
        {
            Connect(previous.Id, port, node.Id);
            SelectNode(node.Id);
        }
    }

    [RelayCommand]
    private void AddSelectedAction(ActionItemViewModel? action)
    {
        if (action is not null)
        {
            AddAction(action);
        }
    }

    /// <summary>
    /// Lays the nodes out in columns by how far they are from a trigger. Inside a column the nodes keep the order of the
    /// flow (<see cref="WorkflowFlowOrder"/>): a Switch's branches in the order of its cases, Else last.
    /// </summary>
    [RelayCommand]
    private void Arrange()
    {
        var depth = WorkflowFlowOrder.Depths(_workflow);
        var order = WorkflowFlowOrder.Order(_workflow).ToList();
        var unreachedColumn = depth.Count == 0 ? 0 : depth.Values.Max() + 1;

        var rows = new Dictionary<int, double>();
        var placed = new Dictionary<string, WorkflowNode>(StringComparer.Ordinal);
        foreach (var node in _workflow.Nodes
                     .OrderBy(node => depth.GetValueOrDefault(node.Id, int.MaxValue))
                     .ThenBy(node => order.IndexOf(node.Id)))
        {
            var column = depth.GetValueOrDefault(node.Id, unreachedColumn);
            var y = rows.GetValueOrDefault(column, WorkflowConverter.Top);
            placed[node.Id] = node with { X = WorkflowConverter.Left + (column * (NodeWidth + 50)), Y = y };
            rows[column] = y + HeightOf(node) + 30;
        }

        Commit(_workflow with { Nodes = [.. _workflow.Nodes.Select(node => placed[node.Id])] }, "Arranged");
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Add(_workflow);
        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _lastSettingsEdit = null;
        Store(previous, "Undone");
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Add(_workflow);
        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _lastSettingsEdit = null;
        Store(next, "Redone");
    }

    /// <summary>Puts back the workflow as it was when the editor opened.</summary>
    [RelayCommand]
    private void RevertAll()
    {
        if (!_original.Equals(_workflow))
        {
            Commit(_original, "Reverted to how it was when you opened it");
        }
    }

    // ---------------------------------------------------------------- running

    /// <summary>The triggers the workflow has, for Run.</summary>
    public IReadOnlyList<ActionSettingOption> RunOptions =>
        [.. WorkflowEngine.TriggersOf(_workflow).Select(static trigger => new ActionSettingOption(trigger, WorkflowTriggers.Describe(trigger)))];

    [RelayCommand]
    private void Run(ActionSettingOption? trigger)
    {
        var chosen = trigger?.Value
                     ?? (SelectedNode is { Kind: WorkflowNodeKinds.Trigger } node ? node.EffectiveSettings.Get(WorkflowKeys.On, WorkflowTriggers.Down) : null)
                     ?? RunOptions.FirstOrDefault()?.Value;
        if (chosen is null)
        {
            Status = "Add a When node first.";
            return;
        }

        if (_host is null)
        {
            return;
        }

        _host.RunWorkflowTrigger(DeviceId, Control.Id, chosen);
        Status = $"Running: {WorkflowTriggers.Describe(chosen).ToLowerInvariant()} (on the device's active profile).";
    }

    [RelayCommand]
    private void Stop()
    {
        _host?.StopWorkflows(DeviceId, Control.Id);
        Status = "Stopped.";
    }

    [RelayCommand]
    private void ClearRunLog()
    {
        RunLog.Clear();
        OnPropertyChanged(nameof(HasRunLog));
    }

    private void OnTraced(WorkflowTrace trace)
    {
        if (!string.Equals(trace.DeviceId, DeviceId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(trace.ControlId, Control.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dispatcher.UIThread.Post(() => ShowTrace(trace));
    }

    /// <summary>Lights up a traced node and adds the step to the run log.</summary>
    public void ShowTrace(WorkflowTrace trace)
    {
        RefreshVariables();
        TrackPause(trace);
        if (!string.Equals(trace.WorkflowId, _workflow.Id, StringComparison.Ordinal))
        {
            // A step in another workflow (a shared one this control calls, or the control's own): logged, not drawn here.
            RunLog.Insert(0, $"{trace.Time.ToLocalTime().ToString("HH:mm:ss.f", CultureInfo.InvariantCulture)}  #{trace.RunId}  ↳ {(trace.WorkflowId is null ? "the control's workflow" : SharedName(trace.WorkflowId))}: {trace.Detail}");
            while (RunLog.Count > 80)
            {
                RunLog.RemoveAt(RunLog.Count - 1);
            }

            OnPropertyChanged(nameof(HasRunLog));
            RaiseChanged();
            return;
        }

        if (trace.Kind is WorkflowTraceKind.Finished or WorkflowTraceKind.Stopped)
        {
            // A run that ended (or was restarted) no longer waits anywhere.
            foreach (var key in _waits.Keys.Where(key => key.RunId == trace.RunId).ToList())
            {
                _waits.Remove(key);
            }
        }

        if (trace.NodeId is { } nodeId)
        {
            var map = trace.Kind == WorkflowTraceKind.Failed ? _failedUntil : _activeUntil;
            map[nodeId] = Now() + (trace.Kind == WorkflowTraceKind.Failed ? ActiveFor * 2 : ActiveFor);
            if (trace.Wait is { } wait && wait > TimeSpan.Zero)
            {
                // A wait fills its node over the time it takes instead of just lighting it up.
                _waits[(nodeId, trace.RunId)] = (Now(), wait);
                map[nodeId] = Now() + wait + ActiveFor;
            }

            if (_fadeTimer is not null)
            {
                _fadeTimer.Interval = _waits.Count > 0 ? WaitFrame : FadeFrame;
                _fadeTimer.Start();
            }
        }

        var node = trace.NodeId is { } id ? _workflow.Find(id) : null;
        var what = trace.Kind switch
        {
            WorkflowTraceKind.Started => $"▶ {trace.Detail}",
            WorkflowTraceKind.Finished => $"■ {trace.Detail}",
            WorkflowTraceKind.Stopped => $"■ {trace.Detail}",
            WorkflowTraceKind.Ignored => $"· {trace.Detail}",
            WorkflowTraceKind.Paused => $"⏸ {(node is null ? string.Empty : SummaryOf(node) + ": ")}{trace.Detail}",
            WorkflowTraceKind.Resumed => $"▶ {trace.Detail}",
            WorkflowTraceKind.Failed => $"✕ {(node is null ? string.Empty : SummaryOf(node) + ": ")}{trace.Detail}",
            _ => node is null ? trace.Detail : $"{SummaryOf(node)}{(trace.Detail.Length > 0 && trace.Detail != SummaryOf(node) ? " → " + trace.Detail : string.Empty)}",
        };
        RunLog.Insert(0, $"{trace.Time.ToLocalTime().ToString("HH:mm:ss.f", CultureInfo.InvariantCulture)}  #{trace.RunId}  {what}");
        while (RunLog.Count > 80)
        {
            RunLog.RemoveAt(RunLog.Count - 1);
        }

        OnPropertyChanged(nameof(HasRunLog));
        RaiseChanged();
    }

    private string SharedName(string id) => (SharedSource?.Invoke() ?? []).FirstOrDefault(workflow => workflow.Id == id)?.Name ?? id;

    private void FadeHighlights()
    {
        var now = Now();
        var waiting = _waits.Count > 0;
        var changed = Prune(_activeUntil, now) | Prune(_failedUntil, now);
        foreach (var done in _waits.Where(pair => now - pair.Value.Start >= pair.Value.Length).Select(static pair => pair.Key).ToList())
        {
            _waits.Remove(done);
            changed = true;
        }

        if (_activeUntil.Count == 0 && _failedUntil.Count == 0 && _waits.Count == 0)
        {
            _fadeTimer?.Stop();
        }
        else if (_fadeTimer is not null)
        {
            _fadeTimer.Interval = _waits.Count > 0 ? WaitFrame : FadeFrame;
        }

        // While something waits the node fills a little more each frame.
        if (changed || waiting)
        {
            RaiseChanged();
        }
    }

    private static bool Prune(Dictionary<string, DateTime> map, DateTime now)
    {
        var expired = map.Where(pair => pair.Value <= now).Select(static pair => pair.Key).ToList();
        foreach (var key in expired)
        {
            map.Remove(key);
        }

        return expired.Count > 0;
    }

    // ---------------------------------------------------------------- storing

    private void ReplaceNode(WorkflowNode node, string what, string? mergeKey = null) =>
        Commit(_workflow with { Nodes = [.. _workflow.Nodes.Select(candidate => candidate.Id == node.Id ? node : candidate)] }, what, mergeKey);

    /// <summary>
    /// Makes <paramref name="next"/> the workflow: onto the undo stack (quick edits of the same field
    /// merge into one step) and onto the control.
    /// </summary>
    private void Commit(Workflow next, string what, string? mergeKey = null)
    {
        var now = Now();
        var merge = mergeKey is not null && mergeKey == _lastSettingsEdit && now - _lastSettingsEditAt < SettingsMergeWindow && _undo.Count > 0;
        if (!merge)
        {
            _undo.Add(_workflow);
            if (_undo.Count > MaxUndo)
            {
                _undo.RemoveAt(0);
            }
        }

        _lastSettingsEdit = mergeKey;
        _lastSettingsEditAt = now;
        _redo.Clear();
        Store(next, what);
    }

    private void Store(Workflow next, string what)
    {
        _workflow = next;
        Status = _apply(next) ? what : "This control no longer has this workflow, so the change wasn't saved. Close the editor and open it again.";
        if (_selectedNodeId is not null && _workflow.Find(_selectedNodeId) is null)
        {
            _selectedNodeId = null;
        }

        if (_selectedLink is not null && !_workflow.Links.Contains(_selectedLink))
        {
            _selectedLink = null;
        }

        if (!_syncingInspector && (what is "Undone" or "Redone" || what.StartsWith("Reverted", StringComparison.Ordinal)))
        {
            RebuildInspector();
        }

        foreach (var name in new[] { nameof(Workflow), nameof(CanUndo), nameof(CanRedo), nameof(IsEmpty), nameof(Overview), nameof(RunOptions), nameof(WhileRunning), nameof(SelectedLinkText) })
        {
            OnPropertyChanged(name);
        }

        RaiseChanged();
    }

    private (double X, double Y) NextFreeSpot()
    {
        if (_workflow.Nodes.Count == 0)
        {
            return (WorkflowConverter.Left, WorkflowConverter.Top);
        }

        var lowest = _workflow.Nodes.Max(static node => node.Y + HeightOf(node));
        return (WorkflowConverter.Left, lowest + 30);
    }

    private static double Snap(double value) => Math.Round(value / 10) * 10;

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_host is not null)
        {
            _host.WorkflowTraced -= OnTraced;
        }

        _fadeTimer?.Stop();
        if (_breakpoints is not null)
        {
            _breakpoints.Changed -= OnBreakpointsChanged;
            _breakpoints.Detach();
            _breakpoints = null;
        }
    }
}

/// <summary>A node kind in the editor's palette.</summary>
public sealed class WorkflowPaletteItem(WorkflowNodeType type)
{
    public string Kind => type.Kind;

    public string Name => type.Name;

    public string Description => type.Description;

    public ListIcon Icon { get; } = ListIcon.From(type.Icon, type.Color, Contracts.Icons.Workflow);
}

/// <summary>A variable and its value, for the editor's variable view.</summary>
public sealed record VariableRow(string Name, string Value)
{
    /// <summary>How the value reads: number, true / false, text or empty.</summary>
    public string Kind => DeckHost.Core.Workflows.WorkflowValue.Parse(Value).KindName;
}
