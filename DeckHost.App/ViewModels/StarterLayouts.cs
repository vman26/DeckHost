using System;
using System.Collections.Generic;
using System.Linq;
using DeckHost.Contracts.Models;

namespace DeckHost.App.ViewModels;

/// <summary>An action a starter layout puts on a control.</summary>
/// <param name="ActionKey">The action, as <c>{pluginId}:{actionId}</c>.</param>
/// <param name="Label">What the preview calls it when the action's own name says too little (a Hotkey preset).</param>
/// <param name="Settings">Settings the action gets (a Hotkey's preset); null for none.</param>
public sealed record StarterAction(string ActionKey, string? Label = null, IReadOnlyDictionary<string, string>? Settings = null)
{
    public string PluginId => ActionKey.Split(':')[0];
}

/// <summary>
/// One of the welcome tour's starter layouts: actions for the empty keys (in reading order), for the
/// first knob and for the touch strip's swipe.
/// </summary>
/// <param name="RequiredPluginId">A plugin the starter is built around; when it isn't loaded, its actions are left out and <paramref name="MissingNote"/> says so.</param>
public sealed record StarterLayout(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<StarterAction> Keys,
    StarterAction? Knob,
    StarterAction? Swipe,
    string? RequiredPluginId = null,
    string? MissingNote = null)
{
    public override string ToString() => Name;
}

/// <summary>A planned starter placement: which action goes on which control.</summary>
public sealed record StarterPlacement(PhysicalControl Control, StarterAction Action);

/// <summary>A tile of the welcome tour's starter preview.</summary>
public sealed record StarterPreviewItem(string ControlId, string ControlName, string ActionName);

/// <summary>The starter layouts the welcome tour offers.</summary>
public static class StarterLayouts
{
    private const string Media = "deckhost.plugin.media";
    private const string BuiltIn = "deckhost.plugin.builtin";
    private const string System = "deckhost.plugin.system";
    private const string Obs = "deckhost.plugin.obs";
    private const string VolumeKnob = Media + ":media.volume-knob";
    private const string TrackSwipe = Media + ":media.track-swipe";

    private static StarterAction Hotkey(string preset, string label) =>
        new(System + ":system.hotkey", label, new Dictionary<string, string>(StringComparer.Ordinal) { ["preset"] = preset });

    /// <summary>Media keys, volume, clock, system info, lock (the tour's first starter).</summary>
    public static StarterLayout MediaStarter { get; } = new(
        "media",
        "Media",
        "Play / pause, previous and next track, mute, volume down and up, a clock, system info, lock, and switch profile. On a knob: the volume; on the touch strip: swipe to skip tracks.",
        [
            new(Media + ":media.playpause"),
            new(Media + ":media.previous"),
            new(Media + ":media.next"),
            new(Media + ":media.mute"),
            new(Media + ":media.volumedown"),
            new(Media + ":media.volumeup"),
            new(BuiltIn + ":builtin.clock"),
            new(System + ":system.info"),
            new(System + ":system.lock"),
            new(BuiltIn + ":builtin.switch-profile"),
        ],
        new(VolumeKnob),
        new(TrackSwipe));

    /// <summary>OBS Studio: stream, record, scenes, mute; the microphone and a timer.</summary>
    public static StarterLayout StreamingStarter { get; } = new(
        "streaming",
        "Streaming",
        "Start and stop the stream and the recording, switch scenes, mute a source and save the replay in OBS Studio, plus your microphone, a timer and a clock. Pick the scenes and sources in each key's settings afterwards.",
        [
            new(Obs + ":obs.stream"),
            new(Obs + ":obs.record"),
            new(Obs + ":obs.scene", "Scene 1"),
            new(Obs + ":obs.scene", "Scene 2"),
            new(Obs + ":obs.mute"),
            new(Obs + ":obs.replay"),
            new(Media + ":system.mic-mute"),
            new(BuiltIn + ":builtin.timer"),
            new(BuiltIn + ":builtin.clock"),
            new(System + ":system.info"),
        ],
        new(VolumeKnob),
        new(TrackSwipe),
        Obs,
        "OBS Studio isn't installed, so its keys are left out (Plugins → Store has it).");

    /// <summary>Everyday office shortcuts.</summary>
    public static StarterLayout WorkStarter { get; } = new(
        "work",
        "Work",
        "Copy, paste, undo and a screenshot, lock the computer, mute the microphone, a timer, a clock, system info, and a key to launch an app (choose which in its settings). On a knob: the volume.",
        [
            Hotkey("copy", "Copy"),
            Hotkey("paste", "Paste"),
            Hotkey("undo", "Undo"),
            Hotkey("screenshot", "Screenshot"),
            new(System + ":system.lock"),
            new(Media + ":system.mic-mute"),
            new(BuiltIn + ":builtin.timer"),
            new(BuiltIn + ":builtin.clock"),
            new(System + ":system.info"),
            new(System + ":system.launch"),
        ],
        new(VolumeKnob),
        new(TrackSwipe));

    public static IReadOnlyList<StarterLayout> All { get; } = [MediaStarter, StreamingStarter, WorkStarter];

    /// <summary>
    /// What a starter puts where (pure): each empty key gets the next action that exists and fits it,
    /// the first knob (by id) the knob action and <c>touch.swipe</c> the swipe action. Controls that
    /// already have an action keep it.
    /// </summary>
    /// <param name="controls">The device's controls, in the order the editor lists them.</param>
    /// <param name="isBound">Whether a control already has an action.</param>
    /// <param name="fits">Whether an action exists and fits a control.</param>
    public static IReadOnlyList<StarterPlacement> Plan(
        StarterLayout starter,
        IReadOnlyList<PhysicalControl> controls,
        Func<string, bool> isBound,
        Func<StarterAction, PhysicalControl, bool> fits)
    {
        var placements = new List<StarterPlacement>();
        var keyActions = starter.Keys.ToList();
        var firstKnob = controls.Where(static control => control.Type == ControlType.Encoder)
            .Select(static control => control.Id)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        foreach (var control in controls)
        {
            if (isBound(control.Id))
            {
                continue;
            }

            StarterAction? action = null;
            if (control.Type == ControlType.Key)
            {
                var index = keyActions.FindIndex(candidate => fits(candidate, control));
                if (index >= 0)
                {
                    action = keyActions[index];
                    keyActions.RemoveAt(index);
                }
            }
            else if (control.Type == ControlType.Encoder && control.Id == firstKnob)
            {
                action = starter.Knob;
            }
            else if (control.Id == "touch.swipe")
            {
                action = starter.Swipe;
            }

            if (action is not null && fits(action, control))
            {
                placements.Add(new StarterPlacement(control, action));
            }
        }

        return placements;
    }
}
