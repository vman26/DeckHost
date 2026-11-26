using DeckHost.App.Platform;
using DeckHost.App.SystemPlugins;

namespace DeckHost.Tests;

public sealed class NewBuiltInActionTests
{
    [Test]
    public void SplitArguments_QuotedPartWithSpaces_StaysOneArgument()
    {
        // Arrange
        const string Text = "--fast \"a file.txt\"  -v";

        // Act
        var arguments = RunCommandAction.SplitArguments(Text);

        // Assert
        Assert.That(arguments, Is.EqualTo(new[] { "--fast", "a file.txt", "-v" }));
    }

    [Test]
    public void SplitArguments_EmptyQuotes_AreAnEmptyArgument()
    {
        // Arrange
        const string Text = "a \"\" b";

        // Act
        var arguments = RunCommandAction.SplitArguments(Text);

        // Assert
        Assert.That(arguments, Is.EqualTo(new[] { "a", string.Empty, "b" }));
    }

    [Test]
    public void ParseHeaders_LinesAndSeparators_ReadsEveryNameAndValue()
    {
        // Arrange
        const string Text = "Authorization: Bearer abc;;X-Mode: on\nbroken line";

        // Act
        var headers = HttpRequestAction.ParseHeaders(Text);

        // Assert
        Assert.That(headers, Is.EqualTo(new[] { ("Authorization", "Bearer abc"), ("X-Mode", "on") }));
    }

    [TestCase(299.2, true, "5:00")]
    [TestCase(0.0, true, "0:00")]
    [TestCase(3723.9, false, "1:02:03")]
    [TestCase(59.9, false, "0:59")]
    public void Format_ATime_ShowsMinutesAndSeconds(double seconds, bool roundUp, string expected)
    {
        // Arrange
        var time = TimeSpan.FromSeconds(seconds);

        // Act
        var text = TimerAction.Format(time, roundUp);

        // Assert
        Assert.That(text, Is.EqualTo(expected));
    }

    [Test]
    public void TimerState_StartedPausedAndStartedAgain_CountsOnlyRunningTime()
    {
        // Arrange
        var state = new TimerAction.TimerState();
        const long Start = 1_000;
        const long Pause = 4_000;
        const long Resume = 10_000;
        const long Now = 12_000;
        var running = TimeSpan.FromMilliseconds((Pause - Start) + (Now - Resume));

        // Act
        state.Start(Start);
        state.Pause(Pause);
        state.Start(Resume);

        // Assert
        Assert.That(state.Elapsed(Now), Is.EqualTo(running));
    }

    [Test]
    public void HotkeyPresets_EverySystemsKeys_AreValidHotkeys()
    {
        // Arrange
        var presets = HotkeyPresets.All;

        // Act
        var invalid = presets.SelectMany(static preset => new[] { preset.Keys, preset.MacKeys, preset.LinuxKeys ?? preset.Keys })
            .Where(static keys => !Hotkey.TryParse(keys, out _, out _))
            .ToList();

        // Assert
        Assert.That(invalid, Is.Empty);
    }

    [Test]
    public void PlayerCommand_EveryLinuxPlayer_PassesTheFileLast()
    {
        // Arrange
        const string File = "/sounds/airhorn.mp3";
        const int Volume = 80;

        // Act
        var commands = SoundPlayer.LinuxPlayers.Append("afplay").Select(player => SoundPlayer.PlayerCommand(player, File, Volume, outputDevice: null)).ToList();

        // Assert
        Assert.That(commands.Select(static command => command!.Value.Arguments[^1]), Is.All.EqualTo(File));
    }

    [TestCase(@"C:\Program Files\obs-studio\bin\64bit\obs64.exe", "obs64")]
    [TestCase("/Applications/OBS.app", "OBS")]
    [TestCase("\"/usr/bin/firefox\"", "firefox")]
    [TestCase("", "")]
    public void ProcessName_AProgramPath_IsItsProcessName(string path, string expected)
    {
        // Arrange (the path)

        // Act
        var name = RunningApps.ProcessName(path);

        // Assert
        Assert.That(name, Is.EqualTo(expected));
    }
}
