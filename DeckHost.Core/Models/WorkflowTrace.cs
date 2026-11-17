namespace DeckHost.Core.Models;

public enum WorkflowTraceKind
{
    /// <summary>A trigger started a run (<see cref="WorkflowTrace.NodeId"/> is the trigger).</summary>
    Started,

    /// <summary>The run entered a node.</summary>
    Node,

    /// <summary>A trigger was ignored because its run is still going.</summary>
    Ignored,

    /// <summary>A node could not do its work (an action that isn't loaded), or the run hit the step limit.</summary>
    Failed,

    /// <summary>The run reached its end.</summary>
    Finished,

    /// <summary>The run was stopped before its end.</summary>
    Stopped,
}

/// <summary>One step of a workflow run, for the editor's live view and the run log.</summary>
public sealed record WorkflowTrace(
    string DeviceId,
    string ProfileId,
    string ControlId,
    int RunId,
    string? NodeId,
    WorkflowTraceKind Kind,
    string Detail,
    DateTimeOffset Time)
{
    /// <summary>For a step that waits (a Wait node, the pause between Repeat rounds): how long, so the editor can fill the node as time passes.</summary>
    public TimeSpan? Wait { get; init; }
}
