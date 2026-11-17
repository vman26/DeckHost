using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts.Models;
using DeckHost.Core.Models;
using DeckHost.Core.Workflows;

namespace DeckHost.App.ViewModels;

/// <summary>A shared workflow in the editor's list.</summary>
public sealed record SharedWorkflowRow(string Id, string Name, string UsedBy);

// Shared workflows (what Call nodes run) and the debugger: breakpoints, pausing, continue and step.
public sealed partial class WorkflowEditorViewModel
{
    private int? _pausedRunId;
    private string? _pausedKey;
    private WorkflowBreakpoints? _breakpoints;

    // ---------------------------------------------------------------- shared workflows

    /// <summary>The shared workflows (set by the main view model; empty without it).</summary>
    public Func<IReadOnlyList<Workflow>>? SharedSource { get; set; }

    /// <summary>Keeps a workflow as a shared one: (its id, or why not).</summary>
    public Func<Workflow, (string? Id, string? Problem)>? SaveShared { get; set; }

    /// <summary>Deletes a shared workflow; returns why not.</summary>
    public Func<string, string?>? DeleteShared { get; set; }

    /// <summary>Opens a shared workflow in an editor of its own.</summary>
    public Action<string>? OpenShared { get; set; }

    /// <summary>Who calls a shared workflow, for the list (names).</summary>
    public Func<string, IReadOnlyList<string>>? CallersOf { get; set; }

    public ObservableCollection<SharedWorkflowRow> SharedList { get; } = [];

    public bool HasSharedWorkflows => SharedList.Count > 0;

    /// <summary>Whether this editor shows a shared workflow rather than the control's own.</summary>
    public bool IsShared => _workflow.Id is not null;

    public bool CanSaveAsShared => SaveShared is not null && !IsShared;

    /// <summary>The shared workflows again (after a save or delete).</summary>
    public void RefreshShared()
    {
        SharedList.Clear();
        foreach (var shared in (SharedSource?.Invoke() ?? []).Where(static workflow => workflow.Id is not null).OrderBy(static workflow => workflow.Name, StringComparer.OrdinalIgnoreCase))
        {
            var callers = CallersOf?.Invoke(shared.Id!) ?? [];
            SharedList.Add(new SharedWorkflowRow(shared.Id!, shared.Name ?? shared.Id!, callers.Count == 0 ? "not used" : "used by " + string.Join(", ", callers.Take(2)) + (callers.Count > 2 ? ", …" : string.Empty)));
        }

        OnPropertyChanged(nameof(HasSharedWorkflows));
        OnPropertyChanged(nameof(IsShared));
        OnPropertyChanged(nameof(CanSaveAsShared));
    }

    /// <summary>The settings a node shows: a Call node lists the shared workflows to choose from.</summary>
    private IReadOnlyList<ActionSettingDefinition> SettingsOf(WorkflowNode node) =>
        node.Kind != WorkflowNodeKinds.Call
            ? TypeOf(node).Settings
            :
            [
                new(WorkflowKeys.Workflow, "Workflow", ActionSettingKind.Choice, string.Empty,
                    (SharedSource?.Invoke() ?? []).Count == 0 ? "No shared workflows yet: build one, name it and use Save as shared in the toolbar." : "A shared workflow runs from its When node; this node goes on when it is done.",
                    Options: [.. (SharedSource?.Invoke() ?? []).Where(static workflow => workflow.Id is not null).Select(static workflow => new ActionSettingOption(workflow.Id!, workflow.Name ?? workflow.Id!))]),
            ];

    /// <summary>Whether the selected node is a Call node that names a shared workflow.</summary>
    public bool SelectedIsCall => SelectedNode is { Kind: WorkflowNodeKinds.Call };

    [RelayCommand]
    private void SaveAsShared()
    {
        if (SaveShared is null)
        {
            return;
        }

        var (id, problem) = SaveShared(_workflow);
        if (id is null)
        {
            Status = problem ?? "Couldn't save it as a shared workflow.";
            return;
        }

        Status = $"Saved {_workflow.Name} as a shared workflow. Add a Call workflow node to run it from any workflow.";
        RefreshShared();
    }

    /// <summary>
    /// Makes a new, empty shared workflow (a When node, named "Workflow N") and points the selected Call node at it, so a
    /// workflow can be built right from the node that will call it. <see cref="OpenCall"/> then opens it.
    /// </summary>
    [RelayCommand]
    public void NewSharedWorkflow()
    {
        if (SaveShared is null || SelectedNode is not { Kind: WorkflowNodeKinds.Call } call)
        {
            return;
        }

        var name = NextSharedName();
        var created = new Workflow
        {
            Name = name,
            Nodes = [new WorkflowNode("n1", WorkflowNodeKinds.Trigger, WorkflowConverter.Left, WorkflowConverter.Top)],
            WhileRunning = WorkflowConcurrency.Restart,
        };
        var (id, problem) = SaveShared(created);
        if (id is null)
        {
            Status = problem ?? "Couldn't make a new workflow.";
            return;
        }

        var settings = (call.Settings ?? ActionSettings.Empty).With(WorkflowKeys.Workflow, id);
        Commit(_workflow with { Nodes = [.. _workflow.Nodes.Select(node => node.Id == call.Id ? node with { Settings = settings } : node)] }, $"New workflow {name}");
        Status = $"Made the shared workflow {name} and set this node to call it. Choose Open workflow to build it.";
        RefreshShared();
        SelectNode(call.Id);
    }

    /// <summary>The first "Workflow N" that no shared workflow has.</summary>
    internal string NextSharedName()
    {
        var names = (SharedSource?.Invoke() ?? []).Select(static workflow => workflow.Name ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var number = 1;
        while (names.Contains($"Workflow {number}"))
        {
            number++;
        }

        return $"Workflow {number}";
    }

    [RelayCommand]
    private void OpenCall()
    {
        if (SelectedNode is { Kind: WorkflowNodeKinds.Call } node && node.EffectiveSettings.Get(WorkflowKeys.Workflow).Trim() is { Length: > 0 } id)
        {
            OpenShared?.Invoke(id);
        }
    }

    [RelayCommand]
    private void OpenSharedRow(SharedWorkflowRow? row)
    {
        if (row is not null)
        {
            OpenShared?.Invoke(row.Id);
        }
    }

    [RelayCommand]
    private void DeleteSharedRow(SharedWorkflowRow? row)
    {
        if (row is null || DeleteShared is null)
        {
            return;
        }

        Status = DeleteShared(row.Id) is { } why ? $"Can't delete {row.Name}: {why}" : $"Deleted {row.Name}.";
        RefreshShared();
    }

    // ---------------------------------------------------------------- debugger

    /// <summary>Where this control's runs stop (shared by the control's editors).</summary>
    public WorkflowBreakpoints Breakpoints
    {
        get
        {
            if (_breakpoints is null)
            {
                Breakpoints = new WorkflowBreakpoints(_host);
            }

            return _breakpoints!;
        }
        set
        {
            if (_breakpoints is not null)
            {
                _breakpoints.Changed -= OnBreakpointsChanged;
                _breakpoints.Detach();
            }

            _breakpoints = value;
            _breakpoints.Changed += OnBreakpointsChanged;

            // Runs only stop at the points while an editor is open to see and continue them.
            _breakpoints.Attach();
        }
    }

    private void OnBreakpointsChanged(object? sender, EventArgs e) => RaiseChanged();

    private string KeyOf(string nodeId) => WorkflowEngine.BreakpointKey(_workflow.Id, nodeId);

    public bool HasBreakpoint(WorkflowNode node) => Breakpoints.Has(DeviceId, Control.Id, KeyOf(node.Id));

    /// <summary>Puts a breakpoint in front of a node (runs stop there), or takes it away.</summary>
    public void ToggleBreakpoint(string nodeId)
    {
        if (_workflow.Find(nodeId) is not { } node)
        {
            return;
        }

        Status = Breakpoints.Toggle(DeviceId, Control.Id, KeyOf(nodeId))
            ? $"Breakpoint in front of {SummaryOf(node)}: a run stops there until you continue."
            : $"Breakpoint removed from {SummaryOf(node)}.";
        RaiseChanged();
    }

    public bool IsPaused => _pausedRunId is not null;

    /// <summary>Whether a held run waits in front of this node (in this editor's workflow).</summary>
    public bool IsPausedAt(WorkflowNode node) => _pausedKey == KeyOf(node.Id);

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void Continue()
    {
        if (_pausedRunId is { } run)
        {
            _host?.ContinueWorkflow(run);
        }
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void Step()
    {
        if (_pausedRunId is { } run)
        {
            _host?.StepWorkflow(run);
        }
    }

    /// <summary>Follows a run that stops and goes on, for the Continue and Step buttons and the highlight.</summary>
    private void TrackPause(WorkflowTrace trace)
    {
        switch (trace.Kind)
        {
            case WorkflowTraceKind.Paused:
                _pausedRunId = trace.RunId;
                _pausedKey = trace.NodeId is { } id ? WorkflowEngine.BreakpointKey(trace.WorkflowId, id) : null;
                Status = $"Run #{trace.RunId} is held: {trace.Detail.ToLowerInvariant()}. Continue runs on, Step runs one node.";
                break;
            case WorkflowTraceKind.Resumed or WorkflowTraceKind.Finished or WorkflowTraceKind.Stopped when _pausedRunId == trace.RunId:
                _pausedRunId = null;
                _pausedKey = null;
                break;
            default:
                return;
        }

        OnPropertyChanged(nameof(IsPaused));
        ContinueCommand.NotifyCanExecuteChanged();
        StepCommand.NotifyCanExecuteChanged();
    }
}
