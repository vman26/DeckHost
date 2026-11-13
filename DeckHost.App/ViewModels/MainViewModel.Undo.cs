using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts.Models;

namespace DeckHost.App.ViewModels;

/// <summary>
/// Undo and redo for what controls do: every binding change (drops, moves, removes, the editor
/// below the device, workflows) is recorded with the binding before and after. Quick edits of the
/// same control (typing in a field) merge into one step; a move's two changes are one step.
/// Profiles, pages and settings pages have their own controls and aren't in the history.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Steps kept.</summary>
    public const int UndoLimit = 100;

    /// <summary>Edits of one control closer together than this are one step.</summary>
    public static readonly TimeSpan UndoMergeWindow = TimeSpan.FromSeconds(1.5);

    private readonly List<UndoStep> _undo = [];
    private readonly List<UndoStep> _redo = [];
    private List<BindingChange>? _group;
    private bool _restoring;

    /// <summary>The clock for merging quick edits (tests replace it).</summary>
    internal Func<DateTimeOffset> UndoClock { get; set; } = static () => DateTimeOffset.UtcNow;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string UndoTip => _undo.Count > 0 ? $"Undo {_undo[^1].Description} (Ctrl+Z)" : "Nothing to undo";

    public string RedoTip => _redo.Count > 0 ? $"Redo {_redo[^1].Description} (Ctrl+Y)" : "Nothing to redo";

    /// <summary>Records the change ApplyBinding is about to make (before it makes it).</summary>
    private void RecordBindingChange(ProfileItemViewModel layout, string controlId, ControlBinding? after)
    {
        if (_restoring)
        {
            return;
        }

        var before = layout.GetModel(controlId);
        if (Equals(before, after))
        {
            return;
        }

        var change = new BindingChange(layout.Id, controlId, before, after);
        if (_group is not null)
        {
            _group.Add(change);
            return;
        }

        var now = UndoClock();
        if (_undo.Count > 0 && _undo[^1] is { Changes: [var last] } previous &&
            last.LayoutId == change.LayoutId && string.Equals(last.ControlId, change.ControlId, StringComparison.OrdinalIgnoreCase) &&
            now - previous.Time < UndoMergeWindow && _redo.Count == 0)
        {
            // Typing in a field: one step from the first value to the last.
            _undo[^1] = previous with { Changes = [last with { After = after }], Time = now };
            return;
        }

        Push(new UndoStep([change], Describe(change), now));
    }

    /// <summary>Several binding changes as one step (a move, a swap, a starter layout).</summary>
    private void Grouped(string description, Action changes)
    {
        if (_group is not null || _restoring)
        {
            changes();
            return;
        }

        _group = [];
        try
        {
            changes();
        }
        finally
        {
            var group = _group;
            _group = null;
            if (group.Count > 0)
            {
                Push(new UndoStep(group, description, UndoClock()));
            }
        }
    }

    private void Push(UndoStep step)
    {
        _undo.Add(step);
        if (_undo.Count > UndoLimit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        RaiseUndoChanged();
    }

    [RelayCommand]
    public void Undo() => Restore(_undo, _redo, undo: true);

    [RelayCommand]
    public void Redo() => Restore(_redo, _undo, undo: false);

    private void Restore(List<UndoStep> from, List<UndoStep> to, bool undo)
    {
        if (from.Count == 0)
        {
            return;
        }

        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        _restoring = true;
        try
        {
            var changes = undo ? step.Changes.AsEnumerable().Reverse() : step.Changes;
            foreach (var change in changes)
            {
                ApplyTo(change.LayoutId, change.ControlId, undo ? change.Before : change.After);
            }
        }
        finally
        {
            _restoring = false;
        }

        to.Add(step with { Time = DateTimeOffset.MinValue });
        StatusMessage = (undo ? "Undid " : "Redid ") + step.Description + ".";
        RaiseUndoChanged();
    }

    /// <summary>Puts a binding back on its layout, also when another layout is shown.</summary>
    private void ApplyTo(string layoutId, string controlId, ControlBinding? model)
    {
        if (ReferenceEquals(SelectedProfile, null) || SelectedProfile.Id != layoutId)
        {
            if (AllLayouts.FirstOrDefault(candidate => candidate.Id == layoutId) is not { } layout)
            {
                return;
            }

            if (model is null)
            {
                layout.RemoveBinding(controlId);
            }
            else if (Resolve(model) is { } resolved)
            {
                layout.SetBinding(resolved);
            }
            else
            {
                layout.AddUnresolvedBinding(model);
            }

            SyncProfilesToHost();
            return;
        }

        ApplyBinding(controlId, model);
    }

    /// <summary>Forgets the history (profiles replaced, another device shown).</summary>
    private void ClearUndo()
    {
        _undo.Clear();
        _redo.Clear();
        _group = null;
        RaiseUndoChanged();
    }

    private void RaiseUndoChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoTip));
        OnPropertyChanged(nameof(RedoTip));
    }

    private string Describe(BindingChange change)
    {
        var control = _controlsById.TryGetValue(change.ControlId, out var item) ? item.DisplayName : change.ControlId;
        return (change.Before, change.After) switch
        {
            (null, { } after) => $"putting {after.Action.ActionName} on {control}",
            ({ } before, null) => $"removing {before.Action.ActionName} from {control}",
            _ => $"the change to {control}",
        };
    }

    private sealed record BindingChange(string LayoutId, string ControlId, ControlBinding? Before, ControlBinding? After);

    private sealed record UndoStep(IReadOnlyList<BindingChange> Changes, string Description, DateTimeOffset Time);
}
