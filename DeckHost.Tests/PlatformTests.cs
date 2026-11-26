using DeckHost.App.Platform;

namespace DeckHost.Tests;

/// <summary>
/// The parts of the Linux and macOS support that don't need those systems: reading what the
/// audio and window tools print, the key tables, and the sign-in entries DeckHost writes.
/// </summary>
public class PlatformTests
{
    private const double LevelTolerance = 5e-4;
    private const double FullLevel = 1.0;
    private const string PactlVolume = "Volume: front-left: 26214 /  40% / -23.88 dB,   front-right: 26214 /  40% / -23.88 dB\n        balance 0.00\n";
    private const string AmixerOutput = "Simple mixer control 'Master',0\n  Mono: Playback 63 [72%] [-5.00dB] [on]\n";
    private const string ActiveWindow = "_NET_ACTIVE_WINDOW(WINDOW): window id # 0x3e00007\n";

    [TestCase("Volume: 0.40\n", 0.40, false)]
    [TestCase("Volume: 0.55 [MUTED]\n", 0.55, true)]
    [TestCase("Volume: 1.30\n", FullLevel, false)] // PipeWire allows boosting past 100 %
    public void ParseWpctl_WpctlOutput_ReadsLevelAndMute(string output, double level, bool muted)
    {
        // Act
        var reading = SystemVolume.ParseWpctl(output);

        // Assert
        Assert.That(reading, Is.Not.Null);
        Assert.That(reading!.Value.Level, Is.EqualTo(level).Within(LevelTolerance));
        Assert.That(reading.Value.Muted, Is.EqualTo(muted));
    }

    [Test]
    public void ParseWpctl_NoOutput_IsNull()
    {
        // Act
        var reading = SystemVolume.ParseWpctl(null);

        // Assert
        Assert.That(reading, Is.Null);
    }

    [Test]
    public void ParsePercent_PactlVolumeAndMute_ReadsLevelAndMute()
    {
        // Act
        var pactl = SystemVolume.ParsePercent(PactlVolume, "Mute: yes\n");

        // Assert
        Assert.That((Math.Round(pactl!.Value.Level, 2), pactl.Value.Muted), Is.EqualTo((0.40, true)));
    }

    [Test]
    public void ParsePercent_AmixerOutput_ReadsLevelAndMute()
    {
        // Act
        var amixer = SystemVolume.ParsePercent(AmixerOutput, null);

        // Assert
        Assert.That((Math.Round(amixer!.Value.Level, 2), amixer.Value.Muted), Is.EqualTo((0.72, false)));
    }

    [Test]
    public void ParsePercent_OutputWithoutAVolume_IsNull()
    {
        // Act
        var reading = SystemVolume.ParsePercent("no device", null);

        // Assert
        Assert.That(reading, Is.Null);
    }

    [Test]
    public void WindowIds_ActiveWindowFromXprop_IsThatWindow()
    {
        // Act
        var ids = ForegroundAppWatcher.WindowIds(ActiveWindow);

        // Assert
        Assert.That(ids, Is.EqualTo(new[] { "0x3e00007" }));
    }

    [Test]
    public void WindowIds_NoActiveWindow_IsEmpty()
    {
        // Act
        var ids = ForegroundAppWatcher.WindowIds("_NET_ACTIVE_WINDOW(WINDOW): window id # 0x0\n");

        // Assert
        Assert.That(ids, Is.Empty);
    }

    [Test]
    public void WindowIds_AClientList_ListsEveryWindow()
    {
        // Act
        var ids = ForegroundAppWatcher.WindowIds("_NET_CLIENT_LIST(WINDOW): window id # 0x1a00003, 0x2200007\n");

        // Assert
        Assert.That(ids, Is.EqualTo(new[] { "0x1a00003", "0x2200007" }));
    }

    [Test]
    public void WindowIds_NoOutput_IsEmpty()
    {
        // Act
        var ids = ForegroundAppWatcher.WindowIds(null);

        // Assert
        Assert.That(ids, Is.Empty);
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
    public void LinuxKeyName_EveryHotkeyKey_HasAName(string key)
    {
        // Arrange
        var parsed = Hotkey.TryParse(key, out var hotkey, out var error);

        // Act
        var name = SystemControl.LinuxKeyName(hotkey.Key);

        // Assert
        Assert.That(parsed, Is.True, error);
        Assert.That(string.IsNullOrEmpty(name), Is.False);
    }

    [TestCaseSource(nameof(AllKeys))]
    public void MacKeyCode_EveryHotkeyKey_HasACodeOrIsAMediaKeyOrIsOneTheMacLacks(string key)
    {
        // Arrange
        var parsed = Hotkey.TryParse(key, out var hotkey, out var error);

        // Act
        var hasCode = SystemControl.MacKeyCode(hotkey.Key) is not null || SystemControl.ToMediaKey(hotkey.Key) is not null;

        // Assert (media keys go out as system-defined events; the Mac has no F21-F24, Print Screen or Pause)
        var macHasNoSuchKey = key is "F21" or "F22" or "F23" or "F24" or "PrintScreen" or "Pause";
        Assert.That(parsed, Is.True, error);
        Assert.That(hasCode || macHasNoSuchKey, Is.True, $"{key} has no macOS key code");
    }

    [Test]
    public void MacKeyCode_AllHotkeyKeys_AreUnique()
    {
        // Act
        var codes = AllKeyNames
            .Select(key => Hotkey.TryParse(key, out var hotkey, out _) ? SystemControl.MacKeyCode(hotkey.Key) : null)
            .OfType<int>()
            .ToList();

        // Assert
        Assert.That(codes.Distinct().Count(), Is.EqualTo(codes.Count));
    }

    [TestCaseSource(nameof(MediaKeys))]
    public void ToMediaKey_AVirtualKeyMadeFromAMediaKey_GivesTheMediaKeyBack(MediaKey key)
    {
        // Act
        var roundTripped = SystemControl.ToMediaKey(SystemControl.ToVirtualKey(key));

        // Assert
        Assert.That(roundTripped, Is.EqualTo(key));
    }

    public static IEnumerable<MediaKey> MediaKeys() => Enum.GetValues<MediaKey>();

    [Test]
    public void LinuxAutostartEntry_ForAProgram_IsADesktopEntryThatStartsItWithTheStartupFlag()
    {
        // Act
        var entry = AppSettings.LinuxAutostartEntry("/home/me/.local/share/deckhost/DeckHost.App");

        // Assert
        Assert.That(entry, Does.Contain("[Desktop Entry]"));
        Assert.That(entry, Does.Contain("Exec=\"/home/me/.local/share/deckhost/DeckHost.App\" --startup"));
        Assert.That(entry, Does.Contain("Type=Application"));
    }

    [TestCase(null)]
    [TestCase(" ")]
    [TestCase("/nowhere/DeckHost.AppImage")]
    public void Resolve_AVariableThatNamesNoFile_IsNull(string? variable)
    {
        // Act
        var resolved = AppImage.Resolve(variable);

        // Assert
        Assert.That(resolved, Is.Null);
    }

    [Test]
    public void Resolve_AVariableThatNamesAnExistingFile_IsThatFile()
    {
        // Arrange
        Assume.That(OperatingSystem.IsLinux(), "AppImages only exist on Linux");
        var file = Path.GetTempFileName();
        try
        {
            // Act
            var resolved = AppImage.Resolve(file);

            // Assert
            Assert.That(resolved, Is.EqualTo(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public void MacLaunchAgent_AnAppBundle_OpensTheBundleAtLogin()
    {
        // Act
        var bundled = AppSettings.MacLaunchAgent("/Applications/DeckHost.app/Contents/MacOS/DeckHost.App");

        // Assert
        Assert.That(bundled, Does.Contain("<string>/usr/bin/open</string>"));
        Assert.That(bundled, Does.Contain("<string>/Applications/DeckHost.app</string>"));
        Assert.That(bundled, Does.Contain("<string>--startup</string>"));
        Assert.That(bundled, Does.Contain("<key>RunAtLoad</key>"));
    }

    [Test]
    public void MacLaunchAgent_OutsideABundle_RunsTheProgramItselfWithXmlCharactersEscaped()
    {
        // Act
        var bare = AppSettings.MacLaunchAgent("/Users/me/R&D/DeckHost.App");

        // Assert
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
    public void Describe_AnInformationalVersion_ShowsPreReleasesWithTheirCommit(string? informational, string expected)
    {
        // Act
        var shown = DeckHost.App.AppInfo.Describe(informational, null);

        // Assert
        Assert.That(shown, Is.EqualTo(expected));
    }

    [Test]
    public void ActivationPipeNameFor_AShortTempPath_IsThePlainName()
    {
        // Arrange
        const string TempPath = "/tmp/";
        const string User = "someone";

        // Act
        var name = DeckHost.App.Program.ActivationPipeNameFor(TempPath, User);

        // Assert
        Assert.That(name, Is.EqualTo($"DeckHost.Activate.{User}"));
    }

    [Test]
    public void ActivationPipeNameFor_ALongTempPath_IsAShortAbsolutePath()
    {
        // Arrange
        var tempPath = "/var/folders/" + new string('x', DeckHost.App.Program.MaxSocketPath) + "/T/";
        const string User = "someone";

        // Act
        var name = DeckHost.App.Program.ActivationPipeNameFor(tempPath, User);

        // Assert
        Assert.That((Path.IsPathRooted(name), name.Length <= DeckHost.App.Program.MaxSocketPath), Is.EqualTo((true, true)));
    }
}

public sealed class WindowTitleTests
{
    [Test]
    public void ParseWindowName_XpropOutputWithEscapedQuotes_IsTheTitle()
    {
        // Arrange
        const string Output = "_NET_WM_NAME(UTF8_STRING) = \"Say \\\"hi\\\" - Mail\"";

        // Act
        var title = DeckHost.App.Platform.ForegroundAppWatcher.ParseWindowName(Output);

        // Assert
        Assert.That(title, Is.EqualTo("Say \"hi\" - Mail"));
    }
}
