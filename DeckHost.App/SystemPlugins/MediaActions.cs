using DeckHost.App.Platform;
using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace DeckHost.App.SystemPlugins;

/// <summary>Sends a media key, which the active media app (Spotify, a browser, ...) responds to.</summary>
internal sealed class MediaKeyAction(string id, string name, MediaKey key) : DeckActionBase(
    id,
    name,
    InputCapabilities.Press,
    OutputCapabilities.None,
    $"Sends the {name} media key to the app that is playing.")
{
    protected override void Run(ActionContext context) => RunQueued(context, () => SystemControl.SendMediaKey(key));

    protected override string? Subtitle(IRenderContext context) => null;
}

/// <summary>Knob: turn for volume, press to mute. Where it has a display it shows the level.</summary>
internal sealed class VolumeKnobAction() : DeckActionBase(
    "media.volume-knob",
    "Volume Knob",
    InputCapabilities.Rotate | InputCapabilities.Press,
    OutputCapabilities.None,
    "Turn to change the system volume, press to mute. Shows the volume on the touch strip.")
{
    private const string StepKey = "step";
    private const uint LevelColor = 0xFF38BDF8;
    private const uint MutedColor = 0xFF64748B;

    /// <summary>The media key reaches Windows a moment after it is sent; the level is read again after this.</summary>
    private static readonly TimeSpan LevelSettleDelay = TimeSpan.FromMilliseconds(80);

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(StepKey, "Volume steps per click", ActionSettingKind.Number, "1", Description: "One step is one press of a volume key: 2% on Windows and Linux, 1/16 on macOS.") { Range = new(1, 10) },
    ];

    public override bool CanBeMultiActionStep => false;

    /// <summary>Also follows volume changes made elsewhere (the keyboard, the Windows mixer).</summary>
    public override TimeSpan? RefreshInterval => TimeSpan.FromSeconds(1);

    protected override void Run(ActionContext context)
    {
        var steps = Math.Clamp(context.Settings.GetInt(StepKey, 1), 1, 10) * Math.Max(1, Math.Abs(context.Value));
        var key = context.EventType == InputCapabilities.Press ? MediaKey.Mute
            : context.Value < 0 ? MediaKey.VolumeDown
            : MediaKey.VolumeUp;

        // After the change, read the level again so the display follows right away.
        RunQueued(
            context,
            () => SystemControl.SendMediaKey(key, key == MediaKey.Mute ? 1 : steps),
            () =>
            {
                Thread.Sleep(LevelSettleDelay);
                SystemVolume.Refresh();
            });
    }

    protected override string Title(IRenderContext context) => "Volume";

    protected override string? Subtitle(IRenderContext context) => null;

    public override void Render(IRenderContext context)
    {
        var failed = context.Variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastError), out var error) &&
                     !string.IsNullOrWhiteSpace(error);
        if (failed || SystemVolume.Read() is not { } volume)
        {
            // No level to show (not on Windows, no playback device): the plain title, or the error.
            base.Render(context);
            return;
        }

        context.DrawReading(
            ResolveDisplayText(context, Title(context)),
            volume.Muted ? "Muted" : volume.Level.ToString("P0", CultureInfo.CurrentCulture),
            volume.Level,
            volume.Muted ? MutedColor : LevelColor);
    }
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

    protected override void Run(ActionContext context) =>
        RunQueued(context, () => SystemControl.SendMediaKey(context.Value < 0 ? MediaKey.Previous : MediaKey.Next));
}
