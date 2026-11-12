using DeckHost.App.Platform;

namespace DeckHost.Tests;

/// <summary>
/// The parts of the Linux and macOS support that don't need those systems: reading what the
/// audio and window tools print, the key tables, and the sign-in entries DeckHost writes.
/// </summary>
public class PlatformTests
{
    [Theory]
    [InlineData("Volume: 0.40\n", 0.40, false)]
    [InlineData("Volume: 0.55 [MUTED]\n", 0.55, true)]
    [InlineData("Volume: 1.30\n", 1.0, false)] // PipeWire allows boosting past 100 %
    public void Reads_wpctl(string output, double level, bool muted)
    {
        var reading = SystemVolume.ParseWpctl(output);
        Assert.NotNull(reading);
        Assert.Equal(level, reading.Value.Level, 3);
        Assert.Equal(muted, reading.Value.Muted);
    }

    [Fact]
    public void Reads_pactl_and_amixer()
    {
        var pactl = SystemVolume.ParsePercent(
            "Volume: front-left: 26214 /  40% / -23.88 dB,   front-right: 26214 /  40% / -23.88 dB\n        balance 0.00\n",
            "Mute: yes\n");
        Assert.Equal((0.40, true), (Math.Round(pactl!.Value.Level, 2), pactl.Value.Muted));

        var amixer = SystemVolume.ParsePercent("Simple mixer control 'Master',0\n  Mono: Playback 63 [72%] [-5.00dB] [on]\n", null);
        Assert.Equal((0.72, false), (Math.Round(amixer!.Value.Level, 2), amixer.Value.Muted));

        Assert.Null(SystemVolume.ParsePercent("no device", null));
        Assert.Null(SystemVolume.ParseWpctl(null));
    }

    [Fact]
    public void Reads_window_ids_from_xprop()
    {
        Assert.Equal(["0x3e00007"], ForegroundAppWatcher.WindowIds("_NET_ACTIVE_WINDOW(WINDOW): window id # 0x3e00007\n"));
        Assert.Empty(ForegroundAppWatcher.WindowIds("_NET_ACTIVE_WINDOW(WINDOW): window id # 0x0\n"));
        Assert.Equal(["0x1a00003", "0x2200007"], ForegroundAppWatcher.WindowIds("_NET_CLIENT_LIST(WINDOW): window id # 0x1a00003, 0x2200007\n"));
        Assert.Empty(ForegroundAppWatcher.WindowIds(null));
    }

    /// <summary>Every key a hotkey can name, as Hotkey.TryParse reads it.</summary>
    private static readonly string[] AllKeyNames =
    [
        .. "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Select(static key => key.ToString()),
        .. Enumerable.Range(1, 24).Select(static function => $"F{function}"),
        "Enter", "Tab", "Esc", "Space", "Backspace", "Delete", "Insert", "Home", "End", "PageUp", "PageDown",
        "Left", "Up", "Right", "Down", "PrintScreen", "Pause", "CapsLock", "PlayPause", "NextTrack", "PrevTrack",
        "Stop", "VolumeUp", "VolumeDown", "Mute", "Plus", "Minus", "Comma", "Period",
    ];

    public static TheoryData<string> AllKeys() => new(AllKeyNames);

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void Every_hotkey_key_can_be_sent_on_Linux(string key)
    {
        Assert.True(Hotkey.TryParse(key, out var hotkey, out var error), error);
        Assert.False(string.IsNullOrEmpty(SystemControl.LinuxKeyName(hotkey.Key)));
    }

    [Theory]
    [MemberData(nameof(AllKeys))]
    public void Every_hotkey_key_has_a_Mac_key_or_is_a_media_key(string key)
    {
        Assert.True(Hotkey.TryParse(key, out var hotkey, out var error), error);

        // Media keys go out as system-defined events; the Mac has no F21-F24, Print Screen or Pause.
        var macHasNoSuchKey = key is "F21" or "F22" or "F23" or "F24" or "PrintScreen" or "Pause";
        Assert.True(
            SystemControl.MacKeyCode(hotkey.Key) is not null || SystemControl.ToMediaKey(hotkey.Key) is not null || macHasNoSuchKey,
            $"{key} has no macOS key code");
    }

    [Fact]
    public void Mac_key_codes_are_unique()
    {
        var codes = AllKeyNames
            .Select(key => Hotkey.TryParse(key, out var hotkey, out _) ? SystemControl.MacKeyCode(hotkey.Key) : null)
            .OfType<int>()
            .ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Media_keys_round_trip_through_virtual_keys()
    {
        foreach (var key in Enum.GetValues<MediaKey>())
        {
            Assert.Equal(key, SystemControl.ToMediaKey(SystemControl.ToVirtualKey(key)));
        }
    }

    [Fact]
    public void Writes_an_XDG_autostart_entry()
    {
        var entry = AppSettings.LinuxAutostartEntry("/home/me/.local/share/deckhost/DeckHost.App");
        Assert.Contains("[Desktop Entry]", entry);
        Assert.Contains("Exec=\"/home/me/.local/share/deckhost/DeckHost.App\" --startup", entry);
        Assert.Contains("Type=Application", entry);
    }

    [Fact]
    public void A_Mac_launch_agent_opens_the_app_bundle()
    {
        var bundled = AppSettings.MacLaunchAgent("/Applications/DeckHost.app/Contents/MacOS/DeckHost.App");
        Assert.Contains("<string>/usr/bin/open</string>", bundled);
        Assert.Contains("<string>/Applications/DeckHost.app</string>", bundled);
        Assert.Contains("<string>--startup</string>", bundled);
        Assert.Contains("<key>RunAtLoad</key>", bundled);

        // Outside a bundle (a development build): the program itself, with XML special characters escaped.
        var bare = AppSettings.MacLaunchAgent("/Users/me/R&D/DeckHost.App");
        Assert.Contains("<string>/Users/me/R&amp;D/DeckHost.App</string>", bare);
        Assert.DoesNotContain("/usr/bin/open", bare);
    }
}

public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.0+4b09090a51839e513f29debdadc8d634e737d2f3", "1.2.0")]
    [InlineData("1.2.1-alpha.0.3+4b09090a51839e513f29debdadc8d634e737d2f3", "1.2.1-alpha.0.3+4b09090")]
    [InlineData("1.0.0-beta.1+4b09090a51839e513f29debdadc8d634e737d2f3", "1.0.0-beta.1+4b09090")]
    [InlineData("1.0.0-rc.2.5+build.4b09090a51839e513f29debdadc8d634e737d2f3", "1.0.0-rc.2.5+4b09090")]
    [InlineData("1.2.1-alpha.0.3", "1.2.1-alpha.0.3")]
    [InlineData(null, "0.0.0")]
    public void Pre_releases_show_their_commit(string? informational, string expected) =>
        Assert.Equal(expected, DeckHost.App.AppInfo.Describe(informational, null));
}
