using Avalonia.Input;
using DeckHost.Contracts.Interfaces;

namespace DeckHost.App.Views;

/// <summary>What can be dragged inside DeckHost (in-process only).</summary>
internal static class DragFormats
{
    /// <summary>An action from the action list (or the action of a dragged control).</summary>
    public static readonly DataFormat<IDeckAction> Action = DataFormat.CreateInProcessFormat<IDeckAction>("ActionPayload");

    /// <summary>The id of a control whose binding is being dragged.</summary>
    public static readonly DataFormat<string> Control = DataFormat.CreateInProcessFormat<string>("AssignedControlPayload");

    /// <summary>A Multi Action step being dragged to a new place (its position, from 0).</summary>
    public static readonly DataFormat<string> Step = DataFormat.CreateInProcessFormat<string>("MultiActionStepPayload");
}
