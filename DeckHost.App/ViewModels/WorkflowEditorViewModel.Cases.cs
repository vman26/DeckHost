using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts.Models;

namespace DeckHost.App.ViewModels;

/// <summary>One case of the selected Switch node, as a row in the inspector.</summary>
public sealed class CaseRow(WorkflowEditorViewModel owner, string id, string text) : ObservableObject
{
    private string _text = text;

    public string Id { get; } = id;

    /// <summary>What the value is compared with (numbers compare as numbers, text ignores case).</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? string.Empty))
            {
                owner.SetCaseText(Id, _text);
            }
        }
    }
}

// The cases of a Switch node: rows to add, remove, reorder and edit. A case keeps its id (and so its links) through all of that.
public sealed partial class WorkflowEditorViewModel
{
    private bool _syncingCases;

    public ObservableCollection<CaseRow> Cases { get; } = [];

    public bool SelectedIsSwitch => SelectedNode is { Kind: WorkflowNodeKinds.Switch };

    public bool CanAddCase => Cases.Count < WorkflowCases.Max;

    private static IReadOnlyList<WorkflowCase> CasesOf(WorkflowNode node) => WorkflowCases.Parse(node.EffectiveSettings.Get(WorkflowKeys.Cases));

    private static WorkflowNode WithCases(WorkflowNode node, IEnumerable<WorkflowCase> cases) =>
        node with
        {
            Settings = new ActionSettings(node.EffectiveSettings.Where(static pair => pair.Key != WorkflowKeys.Cases)
                .Append(new KeyValuePair<string, string>(WorkflowKeys.Cases, WorkflowCases.Serialize(cases)))),
        };

    /// <summary>The rows from the selected node (after a selection, an undo or a structural edit).</summary>
    private void RebuildCases()
    {
        _syncingCases = true;
        try
        {
            Cases.Clear();
            if (SelectedNode is { Kind: WorkflowNodeKinds.Switch } node)
            {
                foreach (var one in CasesOf(node))
                {
                    Cases.Add(new CaseRow(this, one.Id, one.Text));
                }
            }
        }
        finally
        {
            _syncingCases = false;
        }

        OnPropertyChanged(nameof(SelectedIsSwitch));
        OnPropertyChanged(nameof(CanAddCase));
    }

    [RelayCommand]
    private void AddCase()
    {
        if (SelectedNode is not { Kind: WorkflowNodeKinds.Switch } node || CasesOf(node).Count >= WorkflowCases.Max)
        {
            return;
        }

        var cases = CasesOf(node).ToList();
        cases.Add(new WorkflowCase(WorkflowCases.NextId(cases), string.Empty));
        ReplaceNode(WithCases(node, cases), "Added a case");
        RebuildCases();
        RaiseChanged();
    }

    /// <summary>Takes a case away with the links that left through it.</summary>
    public void RemoveCase(CaseRow row)
    {
        if (SelectedNode is not { Kind: WorkflowNodeKinds.Switch } node)
        {
            return;
        }

        var port = WorkflowCases.PortOf(row.Id);
        var next = WithCases(node, CasesOf(node).Where(one => one.Id != row.Id));
        Commit(_workflow with
        {
            Nodes = [.. _workflow.Nodes.Select(candidate => candidate.Id == node.Id ? next : candidate)],
            Links = [.. _workflow.Links.Where(link => !(link.From == node.Id && link.Port == port))],
        }, "Removed a case");
        RebuildCases();
        RaiseChanged();
    }

    /// <summary>Moves a case up or down (its links go with it).</summary>
    public void MoveCase(CaseRow row, int direction)
    {
        if (SelectedNode is not { Kind: WorkflowNodeKinds.Switch } node)
        {
            return;
        }

        var cases = CasesOf(node).ToList();
        var index = cases.FindIndex(one => one.Id == row.Id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= cases.Count)
        {
            return;
        }

        (cases[index], cases[target]) = (cases[target], cases[index]);
        ReplaceNode(WithCases(node, cases), "Moved a case");
        RebuildCases();
        RaiseChanged();
    }

    internal void SetCaseText(string id, string text)
    {
        if (_syncingCases || SelectedNode is not { Kind: WorkflowNodeKinds.Switch } node)
        {
            return;
        }

        var cases = CasesOf(node).Select(one => one.Id == id ? one with { Text = text } : one).ToList();
        ReplaceNode(WithCases(node, cases), "Changed a case", mergeKey: node.Id + ":case:" + id);
        RaiseChanged();
    }
}
