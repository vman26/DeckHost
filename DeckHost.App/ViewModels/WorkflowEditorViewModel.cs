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

    private readonly Func<Workflow, bool> _apply;
    private readonly IDeviceHostService? _host;
    private readonly Dictionary<string, ActionItemViewModel> _actionsByKey;
    private readonly List<Workflow> _undo = [];
    private readonly List<Workflow> _redo = [];
    private readonly Dictionary<string, DateTime> _activeUntil = [];
    private readonly Dictionary<string, DateTime> _failedUntil = [];
    private readonly DispatcherTimer? _fadeTimer;
    private readonly Workflow _original;
    private Workflow _workflow;
    private string? _selectedNodeId;
    private WorkflowLink? _selectedLink;
    private string? _lastSettingsEdit;
    private DateTime _lastSettingsEditAt;
    private SettingsEditorViewModel? _nodeSettings;
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
        NodePalette = [.. WorkflowNodeTypes.All.Where(static type => type.Kind != WorkflowNodeKinds.Action).Select(static type => new WorkflowPaletteItem(type))];
        WhileRunningOptions =
        [
            new(WorkflowConcurrency.Parallel, "Start another run next to it"),
            new(WorkflowConcurrency.Ignore, "Ignore the input until it's done"),
            new(WorkflowConcurrency.Restart, "Stop it and start again"),
            new(WorkflowConcurrency.Stop, "Stop it (the input works as a stop button)"),
        ];

        if (_host is not null)
        {
            _host.WorkflowTraced += OnTraced;
            _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _fadeTimer.Tick += (_, _) => FadeHighlights();
        }

        SelectNode(_workflow.Nodes.FirstOrDefault()?.Id);
    }

    public string DeviceId { get; }

    public string LayoutId { get; }

    public PhysicalControl Control { get; }

    public string ControlName { get; }

    public string Title => $"Workflow · {ControlName}";

    /// <summary>The workflow as it is now (what the canvas draws).</summary>
    public Workflow Workflow => _workflow;

    /// <summary>Raised whenever the canvas must be drawn again (an edit, a selection, a run).</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<ActionItemViewModel> AvailableActions { get; }

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
        HeaderHeight + SummaryHeight + (Math.Max(1, WorkflowNodeKinds.OutputsOf(node.Kind).Count) * PortRowHeight) + 8;

    /// <summary>Where links leave a node's output port (canvas coordinates).</summary>
    public static (double X, double Y) OutputPoint(WorkflowNode node, string port)
    {
        var index = Math.Max(0, WorkflowNodeKinds.OutputsOf(node.Kind).ToList().IndexOf(port));
        return (node.X + NodeWidth, node.Y + HeaderHeight + SummaryHeight + (index * PortRowHeight) + (PortRowHeight / 2) + 2);
    }

    /// <summary>Where links enter a node.</summary>
    public static (double X, double Y) InputPoint(WorkflowNode node) => (node.X, node.Y + (HeaderHeight / 2));

    public bool IsSelected(WorkflowNode node) => node.Id == _selectedNodeId;

    public bool IsSelected(WorkflowLink link) => link == _selectedLink;

    public bool IsActive(WorkflowNode node) => _activeUntil.TryGetValue(node.Id, out var until) && until > DateTime.UtcNow;

    public bool HasFailed(WorkflowNode node) => _failedUntil.TryGetValue(node.Id, out var until) && until > DateTime.UtcNow;

    /// <summary>Whether a link was just taken by a run (both ends lit).</summary>
    public bool IsActive(WorkflowLink link) =>
        _activeUntil.TryGetValue(link.From, out var from) && from > DateTime.UtcNow &&
        _activeUntil.TryGetValue(link.To, out var to) && to > DateTime.UtcNow;

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
        _selectedLink = null;
        _selectedNodeId = nodeId is not null && _workflow.Find(nodeId) is not null ? nodeId : null;
        RebuildInspector();
        RaiseChanged();
    }

    public void SelectLink(WorkflowLink? link)
    {
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

            var node = SelectedNode;
            NodeSettings = node switch
            {
                null => null,
                { Kind: WorkflowNodeKinds.Action, Action: { } action } when _actionsByKey.TryGetValue(EditorActionIds.Key(action.PluginId, action.ActionId), out var item) =>
                    new SettingsEditorViewModel(item.SettingDefinitions, action.EffectiveSettings, Control.Inputs),
                { Kind: WorkflowNodeKinds.Action } => null,
                _ => new SettingsEditorViewModel(TypeOf(node).Settings, node.EffectiveSettings, Control.Inputs),
            };
            if (_nodeSettings is not null)
            {
                _nodeSettings.Changed += OnNodeSettingsChanged;
            }

            NodeTitle = node?.Title ?? string.Empty;
            foreach (var name in new[]
                     {
                         nameof(SelectedNode), nameof(HasSelectedNode), nameof(HasSelectedLink), nameof(HasSelection), nameof(ShowNoSelection),
                         nameof(SelectedKindName), nameof(SelectedDescription), nameof(SelectedLinkText), nameof(SelectedIsAction),
                         nameof(SelectedIsTrigger), nameof(SelectedIsMissing), nameof(SelectedAction), nameof(NodeTitlePlaceholder),
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
    public WorkflowNode AddNode(string kind, double x, double y)
    {
        var type = WorkflowNodeTypes.Of(kind);
        var node = new WorkflowNode(WorkflowConverter.NextId(_workflow), type.Kind, Snap(x), Snap(y))
        {
            Settings = type.Settings.Count == 0
                ? null
                : new ActionSettings(type.Settings.Where(static setting => setting.DefaultValue.Length > 0)
                    .Select(static setting => new KeyValuePair<string, string>(setting.Key, setting.DefaultValue))),
        };
        Commit(_workflow with { Nodes = [.. _workflow.Nodes, node] }, $"Added {type.Name}");
        SelectNode(node.Id);
        return node;
    }

    /// <summary>Adds an action node; when a node is selected and its first output is free, links it from there.</summary>
    public WorkflowNode? AddAction(ActionItemViewModel action, double? x = null, double? y = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!action.CanBeMultiActionStep)
        {
            Status = $"{action.Name} can't be part of a workflow.";
            return null;
        }

        var after = SelectedNode;
        var freePort = after is null ? null : WorkflowNodeKinds.OutputsOf(after.Kind).FirstOrDefault(port => !_workflow.LinksFrom(after.Id, port).Any());
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
        var links = x is null && after is not null && freePort is not null
            ? [.. _workflow.Links, new WorkflowLink(after.Id, freePort, node.Id)]
            : _workflow.Links;
        Commit(_workflow with { Nodes = [.. _workflow.Nodes, node], Links = links }, $"Added {action.Name}");
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

        if (!WorkflowNodeKinds.OutputsOf(from.Kind).Contains(port))
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

    [RelayCommand]
    public void DeleteSelection()
    {
        if (_selectedLink is { } link)
        {
            Commit(_workflow with { Links = [.. _workflow.Links.Where(candidate => candidate != link)] }, "Removed a link");
            SelectLink(null);
            return;
        }

        if (SelectedNode is { } node)
        {
            Commit(_workflow with
            {
                Nodes = [.. _workflow.Nodes.Where(candidate => candidate.Id != node.Id)],
                Links = [.. _workflow.Links.Where(candidate => candidate.From != node.Id && candidate.To != node.Id)],
            }, $"Removed {SummaryOf(node)}");
            SelectNode(null);
        }
    }

    [RelayCommand]
    private void DuplicateSelection()
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
            WorkflowNodeKinds.OutputsOf(previous.Kind).FirstOrDefault(port => !_workflow.LinksFrom(previous.Id, port).Any()) is { } port)
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

    /// <summary>Lays the nodes out in columns by how far they are from a trigger (rows in link order).</summary>
    [RelayCommand]
    private void Arrange()
    {
        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();
        var queue = new Queue<string>(_workflow.Nodes.Where(static node => node.Kind == WorkflowNodeKinds.Trigger).Select(static node => node.Id));
        foreach (var id in queue)
        {
            depth[id] = 0;
        }

        // Longest distance from a trigger (the graph has no loops, so this ends).
        var guard = 0;
        while (queue.Count > 0 && guard++ < 100_000)
        {
            var id = queue.Dequeue();
            order.Add(id);
            foreach (var node in _workflow.Nodes.Where(node => _workflow.Links.Any(link => link.From == id && link.To == node.Id)))
            {
                var next = depth[id] + 1;
                if (!depth.TryGetValue(node.Id, out var known) || known < next)
                {
                    depth[node.Id] = next;
                    queue.Enqueue(node.Id);
                }
            }
        }

        var rows = new Dictionary<int, double>();
        var placed = new List<WorkflowNode>();
        foreach (var node in _workflow.Nodes
                     .OrderBy(node => depth.GetValueOrDefault(node.Id, int.MaxValue))
                     .ThenBy(node => order.LastIndexOf(node.Id) is var index && index >= 0 ? index : int.MaxValue)
                     .ThenBy(static node => node.Y))
        {
            var column = depth.TryGetValue(node.Id, out var d) ? d : (depth.Count == 0 ? 0 : depth.Values.Max() + 1);
            var y = rows.GetValueOrDefault(column, WorkflowConverter.Top);
            placed.Add(node with { X = WorkflowConverter.Left + (column * (NodeWidth + 50)), Y = y });
            rows[column] = y + HeightOf(node) + 30;
        }

        Commit(_workflow with { Nodes = [.. _workflow.Nodes.Select(node => placed.First(candidate => candidate.Id == node.Id))] }, "Arranged");
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
        if (trace.NodeId is { } nodeId)
        {
            var map = trace.Kind == WorkflowTraceKind.Failed ? _failedUntil : _activeUntil;
            map[nodeId] = DateTime.UtcNow + (trace.Kind == WorkflowTraceKind.Failed ? ActiveFor * 2 : ActiveFor);
            _fadeTimer?.Start();
        }

        var node = trace.NodeId is { } id ? _workflow.Find(id) : null;
        var what = trace.Kind switch
        {
            WorkflowTraceKind.Started => $"▶ {trace.Detail}",
            WorkflowTraceKind.Finished => $"■ {trace.Detail}",
            WorkflowTraceKind.Stopped => $"■ {trace.Detail}",
            WorkflowTraceKind.Ignored => $"· {trace.Detail}",
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

    private void FadeHighlights()
    {
        var now = DateTime.UtcNow;
        var changed = Prune(_activeUntil, now) | Prune(_failedUntil, now);
        if (_activeUntil.Count == 0 && _failedUntil.Count == 0)
        {
            _fadeTimer?.Stop();
        }

        if (changed)
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
        var now = DateTime.UtcNow;
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
