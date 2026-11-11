using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;

namespace DeckHost.App.Plugins;

public static class FirstPartyApplicationPlugins
{
    public static IReadOnlyList<IApplicationPlugin> Create() =>
    [
        new BuiltInApplicationPlugin(),
        new MediaApplicationPlugin(),
        new ObsApplicationPlugin(),
        new SystemApplicationPlugin(),
    ];
}

internal sealed class BuiltInApplicationPlugin : IApplicationPlugin
{
    public string Id => "deckhost.plugin.builtin";

    public string Name => "DeckHost Built-ins";

    public IReadOnlyCollection<IDeckAction> Actions { get; } =
    [
        new DisplayContentAction(),
        new MultiActionAction(),
        new KeyIdDiagnosticAction(),
    ];
}

internal sealed class MediaApplicationPlugin : IApplicationPlugin
{
    public string Id => "deckhost.plugin.media";

    public string Name => "Media Controls";

    public IReadOnlyCollection<IDeckAction> Actions { get; } =
    [
        new RenderLabelAction("media.playpause", "Play / Pause", InputCapabilities.Press, OutputCapabilities.Text),
        new RenderLabelAction("media.scrub", "Track Scrub", InputCapabilities.Touch | InputCapabilities.Swipe, OutputCapabilities.FullDisplay),
    ];
}

internal sealed class ObsApplicationPlugin : IApplicationPlugin
{
    public string Id => "deckhost.plugin.obs";

    public string Name => "OBS Studio";

    public IReadOnlyCollection<IDeckAction> Actions { get; } =
    [
        new RenderLabelAction("obs.scene", "Switch Scene", InputCapabilities.Press, OutputCapabilities.ColorImage | OutputCapabilities.Text),
        new RenderLabelAction("obs.mute", "Mute Microphone", InputCapabilities.Press, OutputCapabilities.Text),
    ];
}

internal sealed class SystemApplicationPlugin : IApplicationPlugin
{
    public string Id => "deckhost.plugin.system";

    public string Name => "System Controls";

    public IReadOnlyCollection<IDeckAction> Actions { get; } =
    [
        new RenderLabelAction("system.volume", "Master Volume", InputCapabilities.Rotate, OutputCapabilities.None),
        new RenderLabelAction("system.launch", "Launch App", InputCapabilities.Press, OutputCapabilities.None),
    ];
}

internal sealed class DisplayContentAction : DeckActionBase
{
    public DisplayContentAction()
        : base("builtin.display-content", "Display Content", InputCapabilities.None, OutputCapabilities.None)
    {
    }

    public override void Render(IRenderContext context)
    {
        var text = ResolveDisplayText(context, Name);
        context.DrawText(text, 24, context.Height / 2d, 28f);
    }
}

internal sealed class MultiActionAction : DeckActionBase
{
    public MultiActionAction()
        : base("builtin.multi-action", "Multi Action", InputCapabilities.None, OutputCapabilities.None)
    {
    }

    public override void Render(IRenderContext context)
    {
        var text = ResolveDisplayText(context, Name);
        context.DrawText(text, 24, context.Height / 2d, 26f);
    }
}

internal sealed class RenderLabelAction(string id, string name, InputCapabilities requiredInputs, OutputCapabilities requiredOutputs)
    : DeckActionBase(id, name, requiredInputs, requiredOutputs)
{
    public override void Render(IRenderContext context)
    {
        var text = ResolveDisplayText(context, Name);
        context.DrawText(text, 24, context.Height / 2d, 28f);
    }
}

internal abstract class DeckActionBase(string id, string name, InputCapabilities requiredInputs, OutputCapabilities requiredOutputs) : IDeckAction
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public InputCapabilities RequiredInputs { get; } = requiredInputs;

    public OutputCapabilities RequiredOutputs { get; } = requiredOutputs;

    public virtual void Execute(ActionContext context)
    {
        context.Variables.Publish($"devices.{context.DeviceId}.{context.ControlId}.lastAction", Name);
        context.Variables.Publish($"devices.{context.DeviceId}.{context.ControlId}.lastActionAt", DateTimeOffset.UtcNow.ToString("HH:mm:ss"));

        if (context.Variables.TryGetValue<int>($"devices.{context.DeviceId}.{context.ControlId}.executionCount", out var executionCount))
        {
            context.Variables.Publish($"devices.{context.DeviceId}.{context.ControlId}.executionCount", executionCount + 1);
        }
        else
        {
            context.Variables.Publish($"devices.{context.DeviceId}.{context.ControlId}.executionCount", 1);
        }
    }

    public abstract void Render(IRenderContext context);

    protected static string ResolveDisplayText(IRenderContext context, string fallback)
    {
        if (context.Variables.TryGetValue<string>($"devices.{context.DeviceId}.{context.ControlId}.appearance.text", out var displayText) &&
            !string.IsNullOrWhiteSpace(displayText))
        {
            return displayText;
        }

        return fallback;
    }
}

/// <summary>
/// Diagnostic action that displays the SDK key ID for the current control.
/// Used for debugging key mapping issues.
/// </summary>
internal sealed class KeyIdDiagnosticAction : IDeckAction
{
    public string Id => "deckhost.diagnostic.key-id";

    public string Name => "Key ID (Diagnostic)";

    public InputCapabilities RequiredInputs => InputCapabilities.Press;

    public OutputCapabilities RequiredOutputs => OutputCapabilities.ColorImage;

    public void Execute(ActionContext context)
    {
        // Just log for debugging
        System.Diagnostics.Debug.WriteLine($"Key ID Diagnostic pressed: {context.ControlId}");
    }

    public void Render(IRenderContext context)
    {
        if (!context.ControlId.StartsWith("key.", StringComparison.OrdinalIgnoreCase))
        {
            context.DrawText("Not a key", 8, 20f, 14f);
            return;
        }

        var keyIndexStr = context.ControlId.AsSpan(4).ToString();
        if (!int.TryParse(keyIndexStr, out var keyIndex))
        {
            context.DrawText("Error", 8, 20f, 14f);
            return;
        }

        var sdkKeyId = keyIndex;
        if (context.Variables.TryGetValue<int>($"devices.{context.DeviceId}.{context.ControlId}.sdkKeyId", out var mappedSdkKeyId))
        {
            sdkKeyId = mappedSdkKeyId;
        }

        context.DrawText($"L:{keyIndex}", 6, 16f, 16f);
        context.DrawText($"S:{sdkKeyId}", 6, 42f, 16f);
    }
}
