using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;

namespace DeckHost.Contracts.Interfaces;

/// <summary>
/// An action that can be bound to a control. <see cref="Execute"/> and <see cref="Render"/>
/// are called on the host's worker thread, never the UI thread, and must return quickly:
/// start long work in the background and call <see cref="IActionHost.RequestRender"/> when done.
/// </summary>
public interface IDeckAction
{
    string Id { get; }
    string Name { get; }
    InputCapabilities RequiredInputs { get; }
    OutputCapabilities RequiredOutputs { get; }

    /// <summary>One line shown in the action list and the Customize window.</summary>
    string? Description => null;

    /// <summary>Settings the user fills in per binding; values arrive in <see cref="ActionContext.Settings"/>.</summary>
    IReadOnlyList<ActionSettingDefinition> Settings => [];

    /// <summary>False for actions the editor uses internally and that shouldn't be dragged onto controls.</summary>
    bool ShowInPalette => true;

    /// <summary>Whether the action can be a step of a Multi Action.</summary>
    bool CanBeMultiActionStep => true;

    /// <summary>Set for actions whose image changes over time (clocks, system info); the host redraws them this often.</summary>
    TimeSpan? RefreshInterval => null;

    void Execute(ActionContext context);

    /// <summary>Draws the action's content on top of the control's background and appearance image.</summary>
    void Render(IRenderContext context);
}
