using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DeckHost.App.Platform;

public enum MediaKey
{
    PlayPause,
    Next,
    Previous,
    Stop,
    VolumeUp,
    VolumeDown,
    Mute,
}

/// <summary>
/// Simulated input for the system actions on every platform:
/// <list type="bullet">
/// <item>Windows: SendInput (<see cref="WindowsInput"/>).</item>
/// <item>Linux: playerctl (MPRIS) for media, wpctl / pactl / amixer for volume, xdotool (X11) or
/// wtype (Wayland) for keys and text, loginctl to lock.</item>
/// <item>macOS: system-defined key events for media and volume, System Events (osascript) for keys,
/// text and locking. macOS asks once to allow DeckHost under Privacy &amp; Security → Accessibility.</item>
/// </list>
/// Requests run one after another on a background queue: some platforms start a process per
/// request, which must not hold up the host's worker, and steps of a Multi Action keep their order.
/// </summary>
internal static class SystemControl
{
    /// <summary>One volume step on Linux, like a volume key press on Windows.</summary>
    private const int LinuxVolumeStepPercent = 2;

    private static readonly Channel<(Action Work, TaskCompletionSource Done)> Queue =
        Channel.CreateUnbounded<(Action, TaskCompletionSource)>(new UnboundedChannelOptions { SingleReader = true });

    static SystemControl()
    {
        _ = Task.Run(async () =>
        {
            await foreach (var (work, done) in Queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    work();
                    done.TrySetResult();
                }
                catch (Exception ex)
                {
                    done.TrySetException(ex);
                }
            }
        });
    }

    /// <summary>Runs <paramref name="work"/> after every request queued before it.</summary>
    public static Task Enqueue(Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Writer.TryWrite((work, done));
        return done.Task;
    }

    public static MediaKey? ToMediaKey(ushort virtualKey) => virtualKey switch
    {
        VirtualKeys.MediaPlayPause => MediaKey.PlayPause,
        VirtualKeys.MediaNextTrack => MediaKey.Next,
        VirtualKeys.MediaPrevTrack => MediaKey.Previous,
        VirtualKeys.MediaStop => MediaKey.Stop,
        VirtualKeys.VolumeUp => MediaKey.VolumeUp,
        VirtualKeys.VolumeDown => MediaKey.VolumeDown,
        VirtualKeys.VolumeMute => MediaKey.Mute,
        _ => null,
    };

    public static ushort ToVirtualKey(MediaKey key) => key switch
    {
        MediaKey.PlayPause => VirtualKeys.MediaPlayPause,
        MediaKey.Next => VirtualKeys.MediaNextTrack,
        MediaKey.Previous => VirtualKeys.MediaPrevTrack,
        MediaKey.Stop => VirtualKeys.MediaStop,
        MediaKey.VolumeUp => VirtualKeys.VolumeUp,
        MediaKey.VolumeDown => VirtualKeys.VolumeDown,
        _ => VirtualKeys.VolumeMute,
    };

    // ---------------------------------------------------------------- media and volume

    /// <summary>Presses a media key <paramref name="times"/> times (volume keys: one step each).</summary>
    public static void SendMediaKey(MediaKey key, int times = 1)
    {
        times = Math.Max(1, times);
        if (OperatingSystem.IsWindows())
        {
            WindowsInput.TapKey(ToVirtualKey(key), times);
        }
        else if (OperatingSystem.IsMacOS())
        {
            for (var i = 0; i < times; i++)
            {
                MacNative.PostMediaKey(key == MediaKey.Stop ? MediaKey.PlayPause : key);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            LinuxMediaKey(key, times);
        }
        else
        {
            throw new PlatformNotSupportedException("Media keys aren't supported on this system.");
        }
    }

    private static void LinuxMediaKey(MediaKey key, int times)
    {
        switch (key)
        {
            case MediaKey.VolumeUp or MediaKey.VolumeDown:
                LinuxChangeVolume((key == MediaKey.VolumeUp ? 1 : -1) * LinuxVolumeStepPercent * times);
                return;
            case MediaKey.Mute:
                LinuxToggleMute();
                return;
        }

        // Media players speak MPRIS; playerctl sends to the one that's playing (or was last).
        if (ExternalTool.Exists("playerctl"))
        {
            ExternalTool.Run("playerctl", [key switch
            {
                MediaKey.Next => "next",
                MediaKey.Previous => "previous",
                MediaKey.Stop => "stop",
                _ => "play-pause",
            }]);
            return;
        }

        // Without playerctl: the media key itself, which the desktop passes on to the player.
        if (LinuxKeyTool() is { } tool)
        {
            SendLinuxKeys(tool, [], LinuxKeyName(ToVirtualKey(key))!);
            return;
        }

        throw new PlatformNotSupportedException("Install playerctl to control media players.");
    }

    private static void LinuxChangeVolume(int percent)
    {
        var amount = $"{Math.Abs(percent)}%{(percent < 0 ? "-" : "+")}";
        switch (ExternalTool.FirstAvailable("wpctl", "pactl", "amixer"))
        {
            case "wpctl":
                // -l 1.0: don't go past 100 %, as the volume keys don't.
                ExternalTool.Run("wpctl", ["set-volume", "-l", "1.0", "@DEFAULT_AUDIO_SINK@", amount]);
                break;
            case "pactl":
                ExternalTool.Run("pactl", ["set-sink-volume", "@DEFAULT_SINK@", $"{(percent < 0 ? "-" : "+")}{Math.Abs(percent)}%"]);
                break;
            case "amixer":
                ExternalTool.Run("amixer", ["-q", "sset", "Master", amount]);
                break;
            default:
                throw new PlatformNotSupportedException("Install PipeWire (wpctl), PulseAudio (pactl) or ALSA utilities (amixer) to change the volume.");
        }
    }

    private static void LinuxToggleMute()
    {
        switch (ExternalTool.FirstAvailable("wpctl", "pactl", "amixer"))
        {
            case "wpctl":
                ExternalTool.Run("wpctl", ["set-mute", "@DEFAULT_AUDIO_SINK@", "toggle"]);
                break;
            case "pactl":
                ExternalTool.Run("pactl", ["set-sink-mute", "@DEFAULT_SINK@", "toggle"]);
                break;
            case "amixer":
                ExternalTool.Run("amixer", ["-q", "sset", "Master", "toggle"]);
                break;
            default:
                throw new PlatformNotSupportedException("Install PipeWire (wpctl), PulseAudio (pactl) or ALSA utilities (amixer) to mute.");
        }
    }

    // ---------------------------------------------------------------- keys and text

    public static void SendHotkey(Hotkey hotkey)
    {
        if (hotkey.Modifiers.Count == 0 && ToMediaKey(hotkey.Key) is { } mediaKey)
        {
            SendMediaKey(mediaKey);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            WindowsInput.SendChord(hotkey.Modifiers, hotkey.Key);
        }
        else if (OperatingSystem.IsMacOS())
        {
            var keyCode = MacKeyCode(hotkey.Key) ?? throw new NotSupportedException("macOS has no such key.");
            var modifiers = hotkey.Modifiers.Select(MacModifier).Where(static name => name is not null).ToList();
            var script = new StringBuilder($"tell application \"System Events\" to key code {keyCode}");
            if (modifiers.Count > 0)
            {
                script.Append(" using {").Append(string.Join(", ", modifiers)).Append('}');
            }

            ExternalTool.Run("osascript", ["-e", script.ToString()]);
        }
        else if (OperatingSystem.IsLinux())
        {
            var tool = LinuxKeyTool() ?? throw new PlatformNotSupportedException("Install xdotool (X11) or wtype (Wayland) to send keys.");
            var key = LinuxKeyName(hotkey.Key) ?? throw new NotSupportedException("That key can't be sent on Linux.");
            SendLinuxKeys(tool, hotkey.Modifiers.Select(LinuxModifier).OfType<string>().ToList(), key);
        }
        else
        {
            throw new PlatformNotSupportedException("Hotkeys aren't supported on this system.");
        }
    }

    public static void TypeText(string text)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsInput.TypeText(text);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Each line as a keystroke, with Return between them.
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var script = new StringBuilder("tell application \"System Events\"\n");
            for (var index = 0; index < lines.Length; index++)
            {
                if (index > 0)
                {
                    script.Append("key code 36\n");
                }

                if (lines[index].Length > 0)
                {
                    script.Append("keystroke \"").Append(lines[index].Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append("\"\n");
                }
            }

            ExternalTool.Run("osascript", ["-e", script.Append("end tell").ToString()]);
        }
        else if (OperatingSystem.IsLinux())
        {
            switch (LinuxKeyTool())
            {
                case "wtype":
                    ExternalTool.Run("wtype", ["--", text]);
                    break;
                case "xdotool":
                    ExternalTool.Run("xdotool", ["type", "--clearmodifiers", "--delay", "2", "--", text]);
                    break;
                default:
                    throw new PlatformNotSupportedException("Install xdotool (X11) or wtype (Wayland) to type text.");
            }
        }
        else
        {
            throw new PlatformNotSupportedException("Typing text isn't supported on this system.");
        }
    }

    public static void LockScreen()
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsInput.LockWorkstation();
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Control+Command+Q, the system's own lock shortcut.
            ExternalTool.Run("osascript", ["-e", "tell application \"System Events\" to keystroke \"q\" using {control down, command down}"]);
        }
        else if (OperatingSystem.IsLinux())
        {
            if (ExternalTool.Exists("loginctl") && ExternalTool.TryRun("loginctl", ["lock-session"]) is not null)
            {
                return;
            }

            if (ExternalTool.Exists("xdg-screensaver"))
            {
                ExternalTool.Run("xdg-screensaver", ["lock"]);
                return;
            }

            throw new PlatformNotSupportedException("Couldn't lock: neither loginctl nor xdg-screensaver worked.");
        }
        else
        {
            throw new PlatformNotSupportedException("Locking isn't supported on this system.");
        }
    }

    // ---------------------------------------------------------------- Linux keys

    private static bool IsWayland => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    private static bool HasX11 => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

    /// <summary>wtype on Wayland, xdotool on X11 (also for X apps under XWayland), or null.</summary>
    private static string? LinuxKeyTool()
    {
        if (IsWayland && ExternalTool.Exists("wtype"))
        {
            return "wtype";
        }

        return HasX11 && ExternalTool.Exists("xdotool") ? "xdotool" : null;
    }

    private static void SendLinuxKeys(string tool, IReadOnlyList<string> modifiers, string key)
    {
        if (tool == "wtype")
        {
            var arguments = new List<string>();
            foreach (var modifier in modifiers)
            {
                arguments.AddRange(["-M", modifier == "super" ? "logo" : modifier]);
            }

            arguments.AddRange(["-k", key]);
            foreach (var modifier in modifiers.Reverse())
            {
                arguments.AddRange(["-m", modifier == "super" ? "logo" : modifier]);
            }

            ExternalTool.Run("wtype", arguments);
            return;
        }

        ExternalTool.Run("xdotool", ["key", "--clearmodifiers", string.Join('+', [.. modifiers, key])]);
    }

    private static string? LinuxModifier(ushort virtualKey) => virtualKey switch
    {
        0x11 => "ctrl",
        0x12 => "alt",
        0x10 => "shift",
        0x5B => "super",
        _ => null,
    };

    /// <summary>The X keysym name of a Windows virtual key (the keys <see cref="Hotkey"/> can parse).</summary>
    internal static string? LinuxKeyName(ushort virtualKey) => virtualKey switch
    {
        >= 0x41 and <= 0x5A => ((char)(virtualKey + 32)).ToString(),
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
        0x0D => "Return",
        0x09 => "Tab",
        0x1B => "Escape",
        0x20 => "space",
        0x08 => "BackSpace",
        0x2E => "Delete",
        0x2D => "Insert",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "Prior",
        0x22 => "Next",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2C => "Print",
        0x13 => "Pause",
        0x14 => "Caps_Lock",
        0xBB => "equal",
        0xBD => "minus",
        0xBC => "comma",
        0xBE => "period",
        VirtualKeys.MediaPlayPause => "XF86AudioPlay",
        VirtualKeys.MediaNextTrack => "XF86AudioNext",
        VirtualKeys.MediaPrevTrack => "XF86AudioPrev",
        VirtualKeys.MediaStop => "XF86AudioStop",
        VirtualKeys.VolumeUp => "XF86AudioRaiseVolume",
        VirtualKeys.VolumeDown => "XF86AudioLowerVolume",
        VirtualKeys.VolumeMute => "XF86AudioMute",
        _ => null,
    };

    // ---------------------------------------------------------------- macOS keys

    private static string? MacModifier(ushort virtualKey) => virtualKey switch
    {
        0x11 => "control down",
        0x12 => "option down",
        0x10 => "shift down",
        0x5B => "command down",
        _ => null,
    };

    private static readonly Dictionary<char, int> MacLetterAndDigitCodes = new()
    {
        ['A'] = 0x00, ['S'] = 0x01, ['D'] = 0x02, ['F'] = 0x03, ['H'] = 0x04, ['G'] = 0x05, ['Z'] = 0x06, ['X'] = 0x07,
        ['C'] = 0x08, ['V'] = 0x09, ['B'] = 0x0B, ['Q'] = 0x0C, ['W'] = 0x0D, ['E'] = 0x0E, ['R'] = 0x0F, ['Y'] = 0x10,
        ['T'] = 0x11, ['1'] = 0x12, ['2'] = 0x13, ['3'] = 0x14, ['4'] = 0x15, ['6'] = 0x16, ['5'] = 0x17, ['9'] = 0x19,
        ['7'] = 0x1A, ['8'] = 0x1C, ['0'] = 0x1D, ['O'] = 0x1F, ['U'] = 0x20, ['I'] = 0x22, ['P'] = 0x23, ['L'] = 0x25,
        ['J'] = 0x26, ['K'] = 0x28, ['N'] = 0x2D, ['M'] = 0x2E,
    };

    private static readonly int[] MacFunctionKeyCodes =
    [
        0x7A, 0x78, 0x63, 0x76, 0x60, 0x61, 0x62, 0x64, 0x65, 0x6D, 0x67, 0x6F, // F1-F12
        0x69, 0x6B, 0x71, 0x6A, 0x40, 0x4F, 0x50, 0x5A, // F13-F20
    ];

    /// <summary>The macOS virtual key code of a Windows virtual key, or null when the Mac has no such key.</summary>
    internal static int? MacKeyCode(ushort virtualKey) => virtualKey switch
    {
        (>= 0x41 and <= 0x5A) or (>= 0x30 and <= 0x39) => MacLetterAndDigitCodes[(char)virtualKey],
        >= 0x70 and <= 0x83 => MacFunctionKeyCodes[virtualKey - 0x70],
        0x0D => 0x24,
        0x09 => 0x30,
        0x1B => 0x35,
        0x20 => 0x31,
        0x08 => 0x33,
        0x2E => 0x75,
        0x2D => 0x72,
        0x24 => 0x73,
        0x23 => 0x77,
        0x21 => 0x74,
        0x22 => 0x79,
        0x25 => 0x7B,
        0x26 => 0x7E,
        0x27 => 0x7C,
        0x28 => 0x7D,
        0x14 => 0x39,
        0xBB => 0x18,
        0xBD => 0x1B,
        0xBC => 0x2B,
        0xBE => 0x2F,
        _ => null,
    };
}
