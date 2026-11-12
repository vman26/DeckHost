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

    /// <summary>
    /// True for an action that does nothing but show the control's own text, image and background
    /// (DeckHost's hidden "Display Content"). A control bound to it with only a background color
    /// counts as free: a linked control (a knob under a touch zone) may then use its display.
    /// </summary>
    bool ShowsAppearanceOnly => false;

    /// <summary>Whether the action has its own settings page (for example a Stream Deck property inspector).</summary>
    bool HasSettingsPage => false;

    /// <summary>Address of the action's own settings page for one binding; DeckHost shows it in its built-in browser.</summary>
    Uri? GetSettingsPageUri(ActionContext context) => null;

    /// <summary>Called (on the host worker) when the action becomes visible on a device: its profile became active.</summary>
    void OnAppear(ActionContext context)
    {
    }

    /// <summary>Called when the action is no longer visible (profile switched, binding removed, plugin disabled).</summary>
    void OnDisappear(ActionContext context)
    {
    }

    void Execute(ActionContext context);

    /// <summary>Draws the action's content on top of the control's background and appearance image.</summary>
    void Render(IRenderContext context);
}
