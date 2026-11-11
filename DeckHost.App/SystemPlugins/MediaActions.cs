using DeckHost.App.Platform;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;

namespace DeckHost.App.SystemPlugins;

/// <summary>Sends a media key, which the active media app (Spotify, a browser, ...) responds to.</summary>
internal sealed class MediaKeyAction(string id, string name, ushort virtualKey) : DeckActionBase(
    id,
    name,
    InputCapabilities.Press,
    OutputCapabilities.None,
    $"Sends the {name} media key to the app that is playing.")
{
    protected override void Run(ActionContext context) => RunGuarded(context, () =>
    {
        RequireWindows();
        if (OperatingSystem.IsWindows())
        {
            WindowsInput.TapKey(virtualKey);
        }
    });

    protected override string? Subtitle(IRenderContext context) => null;
}

/// <summary>Knob: turn for volume, press to mute.</summary>
internal sealed class VolumeKnobAction() : DeckActionBase(
    "media.volume-knob",
    "Volume Knob",
    InputCapabilities.Rotate | InputCapabilities.Press,
    OutputCapabilities.None,
    "Turn to change the system volume, press to mute.")
{
    private const string StepKey = "step";

    public override IReadOnlyList<ActionSettingDefinition> Settings { get; } =
    [
        new(StepKey, "Volume steps per click", ActionSettingKind.Number, "1", Description: "Each step is 2% on most systems."),
    ];

    public override bool CanBeMultiActionStep => false;

    protected override void Run(ActionContext context) => RunGuarded(context, () =>
    {
        RequireWindows();
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (context.EventType == InputCapabilities.Press)
        {
            WindowsInput.TapKey(WindowsInput.VkVolumeMute);
            return;
        }

        var steps = Math.Clamp(context.Settings.GetInt(StepKey, 1), 1, 10) * Math.Abs(context.Value);
        WindowsInput.TapKey(context.Value < 0 ? WindowsInput.VkVolumeDown : WindowsInput.VkVolumeUp, steps);
    });
}

/// <summary>Touch strip: swipe left for the previous track, right for the next.</summary>
internal sealed class TrackSwipeAction() : DeckActionBase(
    "media.track-swipe",
    "Track Swipe",
    InputCapabilities.Swipe,
    OutputCapabilities.None,
    "Swipe left for the previous track, right for the next.")
{
    public override bool CanBeMultiActionStep => false;

    protected override void Run(ActionContext context) => RunGuarded(context, () =>
    {
        RequireWindows();
        if (OperatingSystem.IsWindows())
        {
            WindowsInput.TapKey(context.Value < 0 ? WindowsInput.VkMediaPrevTrack : WindowsInput.VkMediaNextTrack);
        }
    });
}
