using DeckHost.App.Platform;

namespace DeckHost.Tests;

/// <summary>
/// The parts of the Linux and macOS support that don't need those systems: reading what the
/// audio and window tools print, the key tables, and the sign-in entries DeckHost writes.
/// </summary>
public class PlatformTests
{
    [TestCase("Volume: 0.40\n", 0.40, false)]
    [TestCase("Volume: 0.55 [MUTED]\n", 0.55, true)]
    [TestCase("Volume: 1.30\n", 1.0, false)] // PipeWire allows boosting past 100 %
    public void Reads_wpctl(string output, double level, bool muted)
    {
        var reading = SystemVolume.ParseWpctl(output);
        Assert.That(reading, Is.Not.Null);
        Assert.That(reading!.Value.Level, Is.EqualTo(level).Within(5e-4));
        Assert.That(reading.Value.Muted, Is.EqualTo(muted));
    }

    [Test]
    public void Reads_pactl_and_amixer()
    {
        var pactl = SystemVolume.ParsePercent(
            "Volume: front-left: 26214 /  40% / -23.88 dB,   front-right: 26214 /  40% / -23.88 dB\n        balance 0.00\n",
            "Mute: yes\n");
        Assert.That((Math.Round(pactl!.Value.Level, 2), pactl.Value.Muted), Is.EqualTo((0.40, true)));

        var amixer = SystemVolume.ParsePercent("Simple mixer control 'Master',0\n  Mono: Playback 63 [72%] [-5.00dB] [on]\n", null);
        Assert.That((Math.Round(amixer!.Value.Level, 2), amixer.Value.Muted), Is.EqualTo((0.72, false)));

        Assert.That(SystemVolume.ParsePercent("no device", null), Is.Null);
        Assert.That(SystemVolume.ParseWpctl(null), Is.Null);
    }

    [Test]
    public void Reads_window_ids_from_xprop()
    {
        Assert.That(ForegroundAppWatcher.WindowIds("_NET_ACTIVE_WINDOW(WINDOW): window id # 0x3e00007\n"), Is.EqualTo(new[] { "0x3e00007" }));
        Assert.That(ForegroundAppWatcher.WindowIds("_NET_ACTIVE_WINDOW(WINDOW): window id # 0x0\n"), Is.Empty);
        Assert.That(ForegroundAppWatcher.WindowIds("_NET_CLIENT_LIST(WINDOW): window id # 0x1a00003, 0x2200007\n"), Is.EqualTo(new[] { "0x1a00003", "0x2200007" }));
        Assert.That(ForegroundAppWatcher.WindowIds(null), Is.Empty);
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

    public static IEnumerable<string> AllKeys() => AllKeyNames;

    [TestCaseSource(nameof(AllKeys))]
    public void Every_hotkey_key_can_be_sent_on_Linux(string key)
    {
        Assert.That(Hotkey.TryParse(key, out var hotkey, out var error), Is.True, error);
        Assert.That(string.IsNullOrEmpty(SystemControl.LinuxKeyName(hotkey.Key)), Is.False);
    }

    [TestCaseSource(nameof(AllKeys))]
    public void Every_hotkey_key_has_a_Mac_key_or_is_a_media_key(string key)
    {
        Assert.That(Hotkey.TryParse(key, out var hotkey, out var error), Is.True, error);

        // Media keys go out as system-defined events; the Mac has no F21-F24, Print Screen or Pause.
        var macHasNoSuchKey = key is "F21" or "F22" or "F23" or "F24" or "PrintScreen" or "Pause";
        Assert.That(
            SystemControl.MacKeyCode(hotkey.Key) is not null || SystemControl.ToMediaKey(hotkey.Key) is not null || macHasNoSuchKey,
            Is.True,
            $"{key} has no macOS key code");
    }

    [Test]
    public void Mac_key_codes_are_unique()
    {
        var codes = AllKeyNames
            .Select(key => Hotkey.TryParse(key, out var hotkey, out _) ? SystemControl.MacKeyCode(hotkey.Key) : null)
            .OfType<int>()
            .ToList();
        Assert.That(codes.Distinct().Count(), Is.EqualTo(codes.Count));
    }

    [Test]
    public void Media_keys_round_trip_through_virtual_keys()
    {
        foreach (var key in Enum.GetValues<MediaKey>())
        {
            Assert.That(SystemControl.ToMediaKey(SystemControl.ToVirtualKey(key)), Is.EqualTo(key));
        }
    }

    [Test]
    public void Writes_an_XDG_autostart_entry()
    {
        var entry = AppSettings.LinuxAutostartEntry("/home/me/.local/share/deckhost/DeckHost.App");
        Assert.That(entry, Does.Contain("[Desktop Entry]"));
        Assert.That(entry, Does.Contain("Exec=\"/home/me/.local/share/deckhost/DeckHost.App\" --startup"));
        Assert.That(entry, Does.Contain("Type=Application"));
    }

    [Test]
    public void The_AppImage_variable_counts_only_when_it_names_a_file()
    {
        Assert.That(AppImage.Resolve(null), Is.Null);
        Assert.That(AppImage.Resolve(" "), Is.Null);
        Assert.That(AppImage.Resolve("/nowhere/DeckHost.AppImage"), Is.Null);
        if (OperatingSystem.IsLinux())
        {
            var file = Path.GetTempFileName();
            try
            {
                Assert.That(AppImage.Resolve(file), Is.EqualTo(file));
            }
            finally
            {
                File.Delete(file);
            }
        }
    }

    [Test]
    public void A_Mac_launch_agent_opens_the_app_bundle()
    {
        var bundled = AppSettings.MacLaunchAgent("/Applications/DeckHost.app/Contents/MacOS/DeckHost.App");
        Assert.That(bundled, Does.Contain("<string>/usr/bin/open</string>"));
        Assert.That(bundled, Does.Contain("<string>/Applications/DeckHost.app</string>"));
        Assert.That(bundled, Does.Contain("<string>--startup</string>"));
        Assert.That(bundled, Does.Contain("<key>RunAtLoad</key>"));

        // Outside a bundle (a development build): the program itself, with XML special characters escaped.
        var bare = AppSettings.MacLaunchAgent("/Users/me/R&D/DeckHost.App");
        Assert.That(bare, Does.Contain("<string>/Users/me/R&amp;D/DeckHost.App</string>"));
        Assert.That(bare, Does.Not.Contain("/usr/bin/open"));
    }
}

public class AppVersionTests
{
    [TestCase("1.2.0+4b09090a51839e513f29debdadc8d634e737d2f3", "1.2.0")]
    [TestCase("1.2.1-alpha.0.3+4b09090a51839e513f29debdadc8d634e737d2f3", "1.2.1-alpha.0.3+4b09090")]
    [TestCase("1.0.0-beta.1+4b09090a51839e513f29debdadc8d634e737d2f3", "1.0.0-beta.1+4b09090")]
    [TestCase("1.0.0-rc.2.5+build.4b09090a51839e513f29debdadc8d634e737d2f3", "1.0.0-rc.2.5+4b09090")]
    [TestCase("1.2.1-alpha.0.3", "1.2.1-alpha.0.3")]
    [TestCase("1.4.0-alpha.21+Branch.release-1.4.0.Sha.d3d4b5e05b1e5182023baa6a9afb50d742a0ae49.d3d4b5e", "1.4.0-alpha.21+d3d4b5e")]
    [TestCase("1.4.0+Branch.main.Sha.d3d4b5e05b1e5182023baa6a9afb50d742a0ae49.d3d4b5e", "1.4.0")]
    [TestCase(null, "0.0.0")]
    public void Pre_releases_show_their_commit(string? informational, string expected) =>
        Assert.That(DeckHost.App.AppInfo.Describe(informational, null), Is.EqualTo(expected));
}
