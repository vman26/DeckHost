using DeckHost.Contracts;
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
        new RenderLabelAction("media.scrub", "Track Scrub", InputCapabilities.Swipe, OutputCapabilities.None),
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

internal sealed class DisplayContentAction()
    : DeckActionBase("builtin.display-content", "Display Content", InputCapabilities.None, OutputCapabilities.None)
{
    // Pure content: pressing the control doesn't count as running an action.
    public override void Execute(ActionContext context)
    {
    }

    public override void Render(IRenderContext context) => DrawTitle(context, ResolveDisplayText(context, string.Empty));
}

internal sealed class MultiActionAction()
    : DeckActionBase("builtin.multi-action", "Multi Action", InputCapabilities.None, OutputCapabilities.None);

internal sealed class RenderLabelAction(string id, string name, InputCapabilities requiredInputs, OutputCapabilities requiredOutputs)
    : DeckActionBase(id, name, requiredInputs, requiredOutputs);

/// <summary>
/// Default action behaviour: record that it ran, and draw its label plus the time it last ran.
/// Runs on the host's worker thread.
/// </summary>
internal abstract class DeckActionBase(string id, string name, InputCapabilities requiredInputs, OutputCapabilities requiredOutputs) : IDeckAction
{
    private const uint SecondaryTextColor = 0xFF93C5FD;

    public string Id { get; } = id;

    public string Name { get; } = name;

    public InputCapabilities RequiredInputs { get; } = requiredInputs;

    public OutputCapabilities RequiredOutputs { get; } = requiredOutputs;

    public virtual void Execute(ActionContext context)
    {
        // Count each physical activation once: a press/release pair is one activation.
        if (context.EventType == InputCapabilities.Release)
        {
            return;
        }

        var variables = context.Variables;
        variables.Publish(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastAction), Name);
        variables.Publish(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastActionAt), DateTimeOffset.Now.ToString("HH:mm:ss"));

        var countKey = VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.ExecutionCount);
        variables.Publish(countKey, variables.TryGetValue<int>(countKey, out var executionCount) ? executionCount + 1 : 1);
    }

    public virtual void Render(IRenderContext context)
    {
        DrawTitle(context, ResolveDisplayText(context, Name));

        if (context.Variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastActionAt), out var lastRun) &&
            !string.IsNullOrWhiteSpace(lastRun))
        {
            context.DrawCenteredText($"✓ {lastRun}", context.Width / 2d, context.Height * 0.78d, Math.Max(10f, context.Height * 0.12f), SecondaryTextColor);
        }
    }

    protected static void DrawTitle(IRenderContext context, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            context.DrawCenteredText(text, context.Width / 2d, context.Height * 0.45d, Math.Max(12f, context.Height * 0.18f));
        }
    }

    /// <summary>The binding's custom text; no text when it only shows an image; else the fallback.</summary>
    protected static string ResolveDisplayText(IRenderContext context, string fallback)
    {
        var variables = context.Variables;
        if (variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceText), out var displayText) &&
            !string.IsNullOrWhiteSpace(displayText))
        {
            return displayText;
        }

        if (variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceImage), out var imagePath) &&
            !string.IsNullOrWhiteSpace(imagePath))
        {
            return string.Empty;
        }

        return fallback;
    }
}

/// <summary>
/// Shows the logical control id and the SDK/hardware id on the device, to check the key mapping.
/// </summary>
internal sealed class KeyIdDiagnosticAction : IDeckAction
{
    public string Id => "deckhost.diagnostic.key-id";

    public string Name => "Key ID (Diagnostic)";

    public InputCapabilities RequiredInputs => InputCapabilities.Press;

    public OutputCapabilities RequiredOutputs => OutputCapabilities.ColorImage;

    public void Execute(ActionContext context)
    {
    }

    public void Render(IRenderContext context)
    {
        context.Variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.HardwareId), out var hardwareId);
        var size = Math.Max(11f, context.Height * 0.16f);
        context.DrawCenteredText(context.ControlId, context.Width / 2d, context.Height * 0.38d, size);
        context.DrawCenteredText($"SDK {hardwareId ?? "?"}", context.Width / 2d, context.Height * 0.66d, size, 0xFFFDE68A);
    }
}
