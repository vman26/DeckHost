using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;

namespace DeckHost.Contracts.Interfaces;

/// <summary>
/// An action that can be bound to a control. <see cref="Execute"/> and <see cref="Render"/>
/// are called on the host's worker thread, never the UI thread.
/// </summary>
public interface IDeckAction
{
    string Id { get; }
    string Name { get; }
    InputCapabilities RequiredInputs { get; }
    OutputCapabilities RequiredOutputs { get; }

    void Execute(ActionContext context);

    /// <summary>Draws the action's content on top of the control's background and appearance image.</summary>
    void Render(IRenderContext context);
}
