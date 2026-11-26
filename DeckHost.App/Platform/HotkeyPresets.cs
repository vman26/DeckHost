using System;
using System.Collections.Generic;
using System.Linq;

namespace DeckHost.App.Platform;

/// <summary>A common shortcut with its keys on Windows/Linux and on macOS (Win = Cmd there).</summary>
internal sealed record HotkeyPreset(string Id, string Label, string Keys, string MacKeys, string? LinuxKeys = null)
{
    /// <summary>The keys on the system DeckHost runs on.</summary>
    public string KeysHere => OperatingSystem.IsMacOS() ? MacKeys : OperatingSystem.IsLinux() ? LinuxKeys ?? Keys : Keys;
}

/// <summary>The shortcut presets of the Hotkey action (like Stream Deck 7's).</summary>
internal static class HotkeyPresets
{
    public static IReadOnlyList<HotkeyPreset> All { get; } =
    [
        new("copy", "Copy", "Ctrl+C", "Win+C"),
        new("cut", "Cut", "Ctrl+X", "Win+X"),
        new("paste", "Paste", "Ctrl+V", "Win+V"),
        new("undo", "Undo", "Ctrl+Z", "Win+Z"),
        new("redo", "Redo", "Ctrl+Y", "Win+Shift+Z", "Ctrl+Shift+Z"),
        new("select-all", "Select all", "Ctrl+A", "Win+A"),
        new("save", "Save", "Ctrl+S", "Win+S"),
        new("find", "Find", "Ctrl+F", "Win+F"),
        new("screenshot", "Screenshot (area)", "Win+Shift+S", "Win+Shift+4", "PrintScreen"),
        new("new-tab", "New tab", "Ctrl+T", "Win+T"),
        new("close-tab", "Close tab", "Ctrl+W", "Win+W"),
        new("reopen-tab", "Reopen closed tab", "Ctrl+Shift+T", "Win+Shift+T"),
        new("switch-app", "Switch app", "Alt+Tab", "Win+Tab"),
        new("show-desktop", "Show desktop", "Win+D", "Win+F3", "Win+D"),
        new("emoji", "Emoji picker", "Win+Period", "Ctrl+Win+Space", "Ctrl+Period"),
    ];

    public static HotkeyPreset? Find(string id) => All.FirstOrDefault(preset => string.Equals(preset.Id, id, StringComparison.Ordinal));
}
