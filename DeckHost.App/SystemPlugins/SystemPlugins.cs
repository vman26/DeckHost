using DeckHost.Contracts.Interfaces;
using System.Collections.Generic;

namespace DeckHost.App.SystemPlugins;

/// <summary>
/// The plugins that ship with DeckHost. They can be disabled in the Plugin Manager (except the
/// Built-ins, which the editor needs) but not uninstalled. Everything else, such as OBS, is an
/// installable plugin package.
/// </summary>
public static class SystemPluginSet
{
    public const string BuiltInPluginId = "deckhost.plugin.builtin";
    public const string MediaPluginId = "deckhost.plugin.media";
    public const string SystemControlsPluginId = "deckhost.plugin.system";

    /// <summary>Plugins that can't be disabled.</summary>
    public static IReadOnlyList<string> RequiredPluginIds { get; } = [BuiltInPluginId];

    public static IReadOnlyList<IApplicationPlugin> Create() =>
    [
        new SystemPlugin(
            BuiltInPluginId,
            "DeckHost Built-ins",
            "Multi Action, profile switching, clock, counter and diagnostics.",
            new MultiActionAction(),
            new SwitchProfileAction(),
            new ClockAction(),
            new CounterAction(),
            new KeyIdDiagnosticAction(),
            new DisplayContentAction()),
        new SystemPlugin(
            MediaPluginId,
            "Media Controls",
            "Play/pause, track skipping and volume for whatever app is playing.",
            new MediaKeyAction("media.playpause", "Play / Pause", Platform.VirtualKeys.MediaPlayPause),
            new MediaKeyAction("media.next", "Next Track", Platform.VirtualKeys.MediaNextTrack),
            new MediaKeyAction("media.previous", "Previous Track", Platform.VirtualKeys.MediaPrevTrack),
            new MediaKeyAction("media.stop", "Stop", Platform.VirtualKeys.MediaStop),
            new MediaKeyAction("media.volumeup", "Volume Up", Platform.VirtualKeys.VolumeUp),
            new MediaKeyAction("media.volumedown", "Volume Down", Platform.VirtualKeys.VolumeDown),
            new MediaKeyAction("media.mute", "Mute", Platform.VirtualKeys.VolumeMute),
            new VolumeKnobAction(),
            new TrackSwipeAction()),
        new SystemPlugin(
            SystemControlsPluginId,
            "System Controls",
            "Launch apps, open websites, send hotkeys and text, lock the PC and show system info.",
            new LaunchAppAction(),
            new OpenUrlAction(),
            new HotkeyAction(),
            new TypeTextAction(),
            new LockComputerAction(),
            new SystemInfoAction()),
    ];
}
