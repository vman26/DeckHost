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
            "Multi Action, pages, folders, profile switching, clock, counter and diagnostics.",
            new MultiActionAction(),
            new PageAction(),
            new FolderAction(),
            new FolderBackAction(),
            new SwitchProfileAction(),
            new ClockAction(),
            new CounterAction(),
            new KeyIdDiagnosticAction(),
            new DisplayContentAction()),
        new SystemPlugin(
            MediaPluginId,
            "Media Controls",
            "Play/pause, track skipping and volume for whatever app is playing.",
            new MediaKeyAction("media.playpause", "Play / Pause", Platform.MediaKey.PlayPause),
            new MediaKeyAction("media.next", "Next Track", Platform.MediaKey.Next),
            new MediaKeyAction("media.previous", "Previous Track", Platform.MediaKey.Previous),
            new MediaKeyAction("media.stop", "Stop", Platform.MediaKey.Stop),
            new MediaKeyAction("media.volumeup", "Volume Up", Platform.MediaKey.VolumeUp),
            new MediaKeyAction("media.volumedown", "Volume Down", Platform.MediaKey.VolumeDown),
            new MediaKeyAction("media.mute", "Mute", Platform.MediaKey.Mute),
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
