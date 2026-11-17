using DeckHost.App.ViewModels;
using DeckHost.Device.Hid;

namespace DeckHost.Tests;

public sealed class DeviceCreatorTests : IDisposable
{
    private const string FirstKeyName = "Key 1";
    private const string SecondKeyName = "Key 2";
    private const byte FirstBit = 0x01;
    private const byte SecondBit = 0x02;
    private const byte ThirdBit = 0x04;
    private const byte KnobPressBit = 0x10;
    private const int FirstPause = 10;
    private const int SecondPause = 5;
    private const int CounterReports = 8;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deckhost-creator-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class FakeSource : IHidDeviceSource
    {
        public Action<byte[]>? Report { get; private set; }

        public IReadOnlyList<HidDeviceInfo> List() => [new("/dev/hidraw0", 0x16C0, 0x05DF, "Button Box", "Example", 0xFF00, 1, 4)];

        public IDisposable Listen(string path, Action<byte[]> report, Action<string> failed)
        {
            Report = report;
            return new Stop(() => Report = null);
        }

        private sealed class Stop(Action stop) : IDisposable
        {
            public void Dispose() => stop();
        }
    }

    private sealed record Creator(HidDevicePlugin Plugin, FakeSource Source, DeviceCreatorViewModel View, Func<DateTimeOffset> Now, Action<TimeSpan> Advance) : IDisposable
    {
        public void Send(params byte[] report) => Source.Report!(report);

        public void Dispose()
        {
            View.Dispose();
            Plugin.Dispose();
        }
    }

    private Creator ListeningCreator()
    {
        var now = DateTimeOffset.UtcNow;
        var plugin = new HidDevicePlugin(_folder);
        var source = new FakeSource();
        var view = new DeviceCreatorViewModel(plugin, source, static action => action(), () => now);
        view.SelectedHardware = view.Hardware.Single();
        return new Creator(plugin, source, view, () => now, by => now += by);
    }

    private static void PressAndReleaseTheFirstKey(Creator creator)
    {
        creator.Send(1, 0, 0, 0);
        creator.Send(1, FirstBit, 0, 0);
        creator.Send(1, 0, 0, 0);
    }

    [Test]
    public void AddKey_KeyPressedToWatchTheReportsAndAddedMuchLater_LearnsTheKey()
    {
        // Arrange (the user presses the key to watch what it sends, then takes a while to click Add key)
        using var creator = ListeningCreator();
        PressAndReleaseTheFirstKey(creator);
        creator.Advance(TimeSpan.FromSeconds(FirstPause));

        // Act
        creator.View.AddKeyCommand.Execute(null);
        creator.Send(1, FirstBit, 0, 0);

        // Assert
        Assert.That(creator.View.Controls.Select(static control => control.Name), Is.EqualTo(new[] { FirstKeyName }));
    }

    [Test]
    public void AddKey_KeyPressedToWatchTheReportsAndAddedMuchLater_SaysItAdded()
    {
        // Arrange
        using var creator = ListeningCreator();
        PressAndReleaseTheFirstKey(creator);
        creator.Advance(TimeSpan.FromSeconds(FirstPause));

        // Act
        creator.View.AddKeyCommand.Execute(null);
        creator.Send(1, FirstBit, 0, 0);

        // Assert
        Assert.That(creator.View.Status, Does.StartWith("Added " + FirstKeyName));
    }

    [Test]
    public void AddKey_KeyPressedRightBeforeAddKey_AddsNothingYet()
    {
        // Arrange
        using var creator = ListeningCreator();
        PressAndReleaseTheFirstKey(creator);

        // Act
        creator.View.AddKeyCommand.Execute(null);
        creator.Send(1, FirstBit, 0, 0);

        // Assert
        Assert.That(creator.View.Controls, Is.Empty);
    }

    [Test]
    public void AddKey_KeyPressedRightBeforeAddKey_AsksToWaitAndTryAgain()
    {
        // Arrange
        using var creator = ListeningCreator();
        PressAndReleaseTheFirstKey(creator);

        // Act
        creator.View.AddKeyCommand.Execute(null);
        creator.Send(1, FirstBit, 0, 0);

        // Assert
        Assert.That(creator.View.Status, Does.Contain("try again"));
    }

    [Test]
    public void AddKey_TheSameKeyAfterLettingGoAndAPause_IsLearned()
    {
        // Arrange
        using var creator = ListeningCreator();
        PressAndReleaseTheFirstKey(creator);
        creator.View.AddKeyCommand.Execute(null);
        creator.Send(1, FirstBit, 0, 0);
        creator.Send(1, 0, 0, 0);
        creator.Advance(TimeSpan.FromSeconds(SecondPause));
        creator.View.CancelLearningCommand.Execute(null);

        // Act
        creator.View.AddKeyCommand.Execute(null);
        creator.Send(1, FirstBit, 0, 0);

        // Assert
        Assert.That(creator.View.Controls.Select(static control => control.Name), Is.EqualTo(new[] { FirstKeyName }));
    }

    private static void StreamACounter(Creator creator)
    {
        for (byte counter = 0; counter < CounterReports; counter++)
        {
            creator.Send(1, 0, counter, 0);
        }
    }

    [Test]
    public void AddKey_DeviceStreamingChangingValues_KeepsLearningWithoutAskingToTryAgain()
    {
        // Arrange (a counter byte changes with every report)
        using var creator = ListeningCreator();
        StreamACounter(creator);

        // Act
        creator.View.AddKeyCommand.Execute(null);
        creator.Send(1, 0, 9, 0);
        creator.Send(1, 0, 10, 0);

        // Assert
        Assert.That(creator.View.IsLearning, Is.True);
        Assert.That(creator.View.Status, Does.Not.Contain("try again"));
    }

    [Test]
    public void AddKey_DeviceStreamingChangingValues_LearnsTheKeyThatIsPressed()
    {
        // Arrange
        using var creator = ListeningCreator();
        StreamACounter(creator);
        creator.View.AddKeyCommand.Execute(null);
        creator.Send(1, 0, 9, 0);
        creator.Send(1, 0, 10, 0);

        // Act
        creator.Send(1, ThirdBit, 11, 0);

        // Assert
        Assert.That(creator.View.Controls.Select(static control => control.Name), Is.EqualTo(new[] { FirstKeyName }));
    }

    private static void LearnTwoKeys(Creator creator)
    {
        LearnKeyOfBit(creator, FirstBit);
        LearnKeyOfBit(creator, SecondBit);
    }

    private static void LearnKeyOfBit(Creator creator, byte bit)
    {
        creator.View.AddKeyCommand.Execute(null);
        creator.Send(1, bit, 0, 0);
        creator.Send(1, 0, 0, 0);
    }

    private static void LearnAPushableKnob(Creator creator)
    {
        creator.View.AddKnobCommand.Execute(null);
        creator.Send(1, 0, 1, 0);
        creator.Send(1, 0, 1, 0);
        creator.View.DoneTurningCommand.Execute(null);
        creator.View.LearnPressCommand.Execute(null);
        creator.Send(1, 0, 0, KnobPressBit);
    }

    private static void DragKey2LeftOfKey1(Creator creator)
    {
        var second = creator.View.Controls.Single(control => control.Name == SecondKeyName);
        second.MoveTo(-50, 0);
        creator.View.Controls.Single(control => control.Name == FirstKeyName).MoveTo(100, 0);
        second.Dropped();
    }

    private Creator CreatorWithTwoKeysAndAKnobSaved()
    {
        var creator = ListeningCreator();
        LearnTwoKeys(creator);
        LearnAPushableKnob(creator);
        DragKey2LeftOfKey1(creator);
        creator.View.SaveCommand.Execute(null);
        return creator;
    }

    [Test]
    public void SelectedHardware_ListedHardware_NamesTheDeviceAfterIt()
    {
        // Arrange
        using var creator = ListeningCreator();

        // Act
        var name = creator.View.Name;

        // Assert
        Assert.That(name, Is.EqualTo("Button Box"));
    }

    [Test]
    public void AddKey_EachNewKey_StartsLearning()
    {
        // Arrange
        using var creator = ListeningCreator();

        // Act
        creator.View.AddKeyCommand.Execute(null);

        // Assert
        Assert.That(creator.View.IsLearning, Is.True);
    }

    [Test]
    public void AddKey_TheSameKeyAgain_IsRecognizedAndNotAddedTwice()
    {
        // Arrange
        using var creator = ListeningCreator();
        LearnTwoKeys(creator);

        // Act
        creator.View.AddKeyCommand.Execute(null);
        creator.Send(1, FirstBit, 0, 0);

        // Assert
        Assert.That(creator.View.Status, Does.Contain("already there"));
    }

    [Test]
    public void Save_TwoKeysAndAKnob_SavesTheHardwareIdentity()
    {
        // Arrange
        using var creator = CreatorWithTwoKeysAndAKnobSaved();

        // Act
        var description = Expect.Single(creator.Plugin.Descriptions);

        // Assert
        Assert.That((description.Id, description.VendorId, description.ProductId, description.UsagePage), Is.EqualTo(("button-box", 0x16C0, 0x05DF, 0xFF00)));
    }

    [Test]
    public void Save_KeysDraggedIntoANewOrder_NumbersThemByPosition()
    {
        // Arrange
        using var creator = CreatorWithTwoKeysAndAKnobSaved();

        // Act
        var description = Expect.Single(creator.Plugin.Descriptions);

        // Assert
        Assert.That(description.Controls.Select(static control => $"{control.Id}:{control.Name}"), Is.EqualTo(new[] { "key.0:Key 2", "key.1:Key 1", "encoder.0:Knob 1" }));
    }

    [Test]
    public void Save_PushableKnob_KeepsWhatWasLearnedForTurningAndPressing()
    {
        // Arrange
        using var creator = CreatorWithTwoKeysAndAKnobSaved();

        // Act
        var knob = Expect.Single(creator.Plugin.Descriptions).Controls[2];

        // Assert
        Assert.That(knob.Input, Is.EqualTo(new HidInput(HidInputKind.Relative, 2, ReportId: 1)));
        Assert.That(knob.Press, Is.EqualTo(new HidInput(HidInputKind.Bit, 3, Mask: KnobPressBit, ReportId: 1)));
    }

    [Test]
    public void Save_TwoKeysAndAKnob_ListsTheSavedDevice()
    {
        // Arrange
        using var creator = CreatorWithTwoKeysAndAKnobSaved();

        // Act
        var device = Expect.Single(creator.Plugin.Devices);

        // Assert
        Assert.That(device.Id, Is.EqualTo("hid.button-box"));
    }

    [Test]
    public void Send_PressOfASavedKey_LightsItUpLive()
    {
        // Arrange
        using var creator = CreatorWithTwoKeysAndAKnobSaved();

        // Act
        creator.Send(1, SecondBit, 0, 0);

        // Assert
        Assert.That(creator.View.Controls.Single(control => control.Name == SecondKeyName).IsDown, Is.True);
    }

    [Test]
    public void Dispose_Creator_StopsListening()
    {
        // Arrange
        var creator = CreatorWithTwoKeysAndAKnobSaved();

        // Act
        creator.Dispose();

        // Assert
        Assert.That(creator.Source.Report, Is.Null);
    }
}
