using Avalonia.Input;
using System.Collections.Generic;

namespace DeckHost.App.Platform;

/// <summary>Turns an Avalonia key press into the text <see cref="Hotkey.TryParse"/> understands.</summary>
internal static class HotkeyRecorder
{
    private static readonly Dictionary<Key, string> KeyNames = new()
    {
        [Key.Enter] = "Enter",
        [Key.Tab] = "Tab",
        [Key.Escape] = "Esc",
        [Key.Space] = "Space",
        [Key.Back] = "Backspace",
        [Key.Delete] = "Delete",
        [Key.Insert] = "Insert",
        [Key.Home] = "Home",
        [Key.End] = "End",
        [Key.PageUp] = "PageUp",
        [Key.PageDown] = "PageDown",
        [Key.Left] = "Left",
        [Key.Up] = "Up",
        [Key.Right] = "Right",
        [Key.Down] = "Down",
        [Key.PrintScreen] = "PrintScreen",
        [Key.Pause] = "Pause",
        [Key.CapsLock] = "CapsLock",
        [Key.MediaPlayPause] = "PlayPause",
        [Key.MediaNextTrack] = "NextTrack",
        [Key.MediaPreviousTrack] = "PrevTrack",
        [Key.MediaStop] = "Stop",
        [Key.VolumeUp] = "VolumeUp",
        [Key.VolumeDown] = "VolumeDown",
        [Key.VolumeMute] = "Mute",
        [Key.OemPlus] = "Plus",
        [Key.OemMinus] = "Minus",
        [Key.OemComma] = "Comma",
        [Key.OemPeriod] = "Period",
    };

    public static bool IsModifier(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    /// <summary>"Ctrl+Shift+" style prefix for the modifiers currently held.</summary>
    public static string ModifierPrefix(KeyModifiers modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            parts.Add("Win");
        }

        return parts.Count == 0 ? string.Empty : string.Join("+", parts) + "+";
    }

    /// <summary>The full combination, or null when the key can't be sent as a hotkey.</summary>
    public static string? Describe(Key key, KeyModifiers modifiers)
    {
        string? name = key switch
        {
            >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
            >= Key.F1 and <= Key.F24 => $"F{key - Key.F1 + 1}",
            _ => KeyNames.GetValueOrDefault(key),
        };

        return name is null ? null : ModifierPrefix(modifiers) + name;
    }
}
