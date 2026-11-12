using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.Core.StreamDeck;

/// <summary>Presents an installed Stream Deck plugin as a DeckHost application plugin.</summary>
public sealed class StreamDeckApplicationPlugin : IApplicationPlugin
{
    internal StreamDeckApplicationPlugin(StreamDeckRuntime runtime, StreamDeckManifest manifest)
    {
        Manifest = manifest;
        Actions = [.. manifest.Actions.Where(static action => action.VisibleInActionsList).Select(action => new StreamDeckAction(runtime, manifest, action))];
    }

    public StreamDeckManifest Manifest { get; }

    public string Id => Manifest.Uuid;

    public string Name => Manifest.Name;

    public string Version => Manifest.Version;

    public string? Author => Manifest.Author;

    public string? Description => Manifest.Description ?? Manifest.Category;

    public IReadOnlyCollection<IDeckAction> Actions { get; }
}

internal sealed class StreamDeckAction(StreamDeckRuntime runtime, StreamDeckManifest manifest, StreamDeckActionManifest action) : IDeckAction
{
    public string Id => action.Uuid;

    public string Name => action.Name;

    public string? Description => string.IsNullOrWhiteSpace(action.Tooltip) ? "Stream Deck plugin action." : action.Tooltip;

    // Keypad actions go on anything with a display; dial-only actions need a knob.
    public InputCapabilities RequiredInputs => action.SupportsKeypad ? InputCapabilities.None : InputCapabilities.Rotate;

    public OutputCapabilities RequiredOutputs => action.SupportsKeypad && !action.SupportsEncoder ? OutputCapabilities.ColorImage : OutputCapabilities.None;

    public bool CanBeMultiActionStep => action.SupportedInMultiActions && action.SupportsKeypad;

    public bool HasSettingsPage => action.PropertyInspectorPath is not null;

    public Uri? GetSettingsPageUri(ActionContext context) => runtime.GetPropertyInspectorUri(manifest, action, context);

    public void OnAppear(ActionContext context) => runtime.Appear(manifest, action, context);

    public void OnDisappear(ActionContext context) => runtime.Disappear(manifest, action, context);

    public void Execute(ActionContext context) => runtime.Execute(manifest, action, context);

    public void Render(IRenderContext context) => runtime.Render(manifest, action, context);
}
