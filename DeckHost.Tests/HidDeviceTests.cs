using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Device.Hid;

namespace DeckHost.Tests;

public class HidDeviceTests : IDisposable
{
    private const string PedalId = "pedal";
    private const string PedalName = "Foot pedal";
    private const string Left = "key.0";
    private const string Right = "key.1";
    private const string Jog = "encoder.0";
    private const string Shuttle = "encoder.1";
    private const byte LeftBit = 0x01;
    private const byte JogPressBit = 0x80;
    private const byte RightValue = 7;
    private const byte JogStep = 0xFE;
    private const int ControlsOnThePedal = 4;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deckhost-hid-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static HidDeviceDescription Pedal() => new(PedalId, PedalName, 0x1234, 0x5678,
    [
        new HidControl(Left, "Left", HidControlKind.Key, 0, 0, 72, 72, new HidInput(HidInputKind.Bit, 1, Mask: LeftBit)),
        new HidControl(Right, "Right", HidControlKind.Key, 86, 0, 72, 72, new HidInput(HidInputKind.Value, 2, Value: RightValue)),
        new HidControl(Jog, "Jog", HidControlKind.Knob, 172, 0, 56, 56, new HidInput(HidInputKind.Relative, 3)) { Press = new HidInput(HidInputKind.Bit, 1, Mask: JogPressBit) },
        new HidControl(Shuttle, "Shuttle", HidControlKind.Knob, 240, 0, 56, 56, new HidInput(HidInputKind.Absolute, 4, Invert: true)),
    ]);

    private static IReadOnlyList<PhysicalControl> PedalControls() => HidDeviceDescription.FromJson(Pedal().ToJson())!.PhysicalControls();

    [Test]
    public void ToJson_DescriptionRoundTrippedThroughJson_IsUnchanged()
    {
        // Arrange
        var description = Pedal();

        // Act
        var copy = HidDeviceDescription.FromJson(description.ToJson())!;

        // Assert
        Assert.That(copy.ToJson(), Is.EqualTo(description.ToJson()));
    }

    [Test]
    public void DeviceId_Description_IsHidPlusItsId()
    {
        // Arrange
        var description = Pedal();

        // Act
        var deviceId = description.DeviceId;

        // Assert
        Assert.That(deviceId, Is.EqualTo("hid." + PedalId));
    }

    [Test]
    public void PhysicalControls_KeyWithABit_IsAKeyThatCanBeLongPressed()
    {
        // Arrange
        var controls = PedalControls();

        // Act
        var key = controls[0];

        // Assert
        Assert.That(key.Type, Is.EqualTo(ControlType.Key));
        Assert.That(key.Inputs.HasFlag(InputCapabilities.LongPress), Is.True);
    }

    [Test]
    public void PhysicalControls_KnobWithAPress_CanRotatePressAndRelease()
    {
        // Arrange
        var controls = PedalControls();

        // Act
        var inputs = controls[2].Inputs;

        // Assert
        Assert.That(inputs, Is.EqualTo(InputCapabilities.Rotate | InputCapabilities.Press | InputCapabilities.Release));
    }

    [Test]
    public void PhysicalControls_KnobWithoutAPress_CanOnlyRotate()
    {
        // Arrange
        var controls = PedalControls();

        // Act
        var inputs = controls[3].Inputs;

        // Assert
        Assert.That(inputs, Is.EqualTo(InputCapabilities.Rotate));
    }

    [Test]
    public void PhysicalControls_InputOnlyDevice_HasNoOutputs()
    {
        // Arrange
        var controls = PedalControls();

        // Act
        var outputs = controls[0].Outputs;

        // Assert
        Assert.That(outputs, Is.EqualTo(OutputCapabilities.None));
    }

    [Test]
    public void FromJson_BrokenJson_ReturnsNull()
    {
        // Arrange
        const string broken = "{ nope";

        // Act
        var description = HidDeviceDescription.FromJson(broken);

        // Assert
        Assert.That(description, Is.Null);
    }

    [Test]
    public void MakeId_NameWithSpacesAndSymbols_IsALowerCaseSlug()
    {
        // Arrange
        const string name = "My foot pedal #2";

        // Act
        var id = HidDeviceDescription.MakeId(name);

        // Assert
        Assert.That(id, Is.EqualTo("my-foot-pedal-2"));
    }

    private static HidInputParser PedalParserAfterTheShuttleStarted()
    {
        var parser = new HidInputParser(Pedal());
        parser.Parse(new byte[] { 0, 0, 0, 0, 100 });
        return parser;
    }

    [Test]
    public void Parse_FirstReport_ReportsNothingAsTheShuttlesFirstPositionIsTheStart()
    {
        // Arrange
        var parser = new HidInputParser(Pedal());

        // Act
        var events = parser.Parse(new byte[] { 0, 0, 0, 0, 100 });

        // Assert
        Assert.That(events, Is.Empty);
    }

    [Test]
    public void Parse_ReportWithKeysDownAndKnobsTurned_ReportsPressesAndTurns()
    {
        // Arrange
        var parser = PedalParserAfterTheShuttleStarted();

        // Act
        var events = parser.Parse(new byte[] { 0, LeftBit, RightValue, JogStep, 103 });

        // Assert
        Assert.That(events, Is.EqualTo(new HidInputEvent[]
        {
            new(Left, InputCapabilities.Press, 1), new(Right, InputCapabilities.Press, 1),
            new(Jog, InputCapabilities.Rotate, -2), new(Shuttle, InputCapabilities.Rotate, -3),
        }));
    }

    [Test]
    public void Parse_KeysLetGoAndKnobPushed_ReportsReleasesAndThePress()
    {
        // Arrange
        var parser = PedalParserAfterTheShuttleStarted();
        parser.Parse(new byte[] { 0, LeftBit, RightValue, JogStep, 103 });

        // Act
        var events = parser.Parse(new byte[] { 0, JogPressBit, 0, 0, 103 });

        // Assert
        Assert.That(events, Is.EqualTo(new HidInputEvent[]
        {
            new(Left, InputCapabilities.Release, 0), new(Right, InputCapabilities.Release, 0), new(Jog, InputCapabilities.Press, 1),
        }));
    }

    [Test]
    public void Parse_ShuttleWrappingFrom255To1_IsTwoStepsForwardReversedHere()
    {
        // Arrange
        var parser = PedalParserAfterTheShuttleStarted();
        parser.Parse(new byte[] { 0, JogPressBit, 0, 0, 255 });

        // Act
        var events = parser.Parse(new byte[] { 0, JogPressBit, 0, 0, 1 });

        // Assert
        Assert.That(events, Does.Contain(new HidInputEvent(Shuttle, InputCapabilities.Rotate, -2)));
    }

    private static HidInputParser ParserOfAKeyOnReport2()
    {
        var description = new HidDeviceDescription("x", "X", 1, 2, [new HidControl(Left, "A", HidControlKind.Key, 0, 0, 72, 72, new HidInput(HidInputKind.Bit, 1, Mask: 1, ReportId: 2))]);
        return new HidInputParser(description);
    }

    [Test]
    public void Parse_ReportOfAnotherId_IsIgnored()
    {
        // Arrange
        var parser = ParserOfAKeyOnReport2();

        // Act
        var events = parser.Parse(new byte[] { 1, 1 });

        // Assert
        Assert.That(events, Is.Empty);
    }

    [Test]
    public void Parse_ReportOfTheDescribedId_ReportsThePress()
    {
        // Arrange
        var parser = ParserOfAKeyOnReport2();

        // Act
        var events = parser.Parse(new byte[] { 2, 1 });

        // Assert
        Assert.That(events, Has.Exactly(1).Items);
    }

    [Test]
    public void Parse_ReportOfAnotherIdWhileTheKeyIsHeld_StillCountsAsPressed()
    {
        // Arrange
        var parser = ParserOfAKeyOnReport2();
        parser.Parse(new byte[] { 2, 1 });

        // Act
        var events = parser.Parse(new byte[] { 1, 0 });

        // Assert
        Assert.That(events, Is.Empty);
    }

    // Byte 4 is a sensor that changes all the time.
    private static byte[][] IdleWithANoisySensor() => [[0, 0, 0, 0, 10], [0, 0, 0, 0, 11], [0, 0, 0, 0, 12]];

    [Test]
    public void LearnKey_ABitThatSetsWhilePressed_IsFoundByItsBit()
    {
        // Arrange
        var idle = IdleWithANoisySensor();

        // Act
        var input = HidLearner.LearnKey(idle, [[0, 0, 0, 0, 13], [0, 0, 0x04, 0, 14]]);

        // Assert
        Assert.That(input, Is.EqualTo(new HidInput(HidInputKind.Bit, 2, Mask: 0x04)));
    }

    [Test]
    public void LearnKey_AByteThatTakesAValueWhilePressed_IsFoundByItsValue()
    {
        // Arrange
        var idle = IdleWithANoisySensor();

        // Act
        var input = HidLearner.LearnKey(idle, [[0, 0, 0, 0x1E, 13]]);

        // Assert
        Assert.That(input, Is.EqualTo(new HidInput(HidInputKind.Value, 3, Value: 0x1E)));
    }

    [Test]
    public void LearnKey_ABitThatClearsWhilePressed_IsFoundAsInverted()
    {
        // Arrange (wired the other way)
        byte[][] idle = [[0, 0xFF]];

        // Act
        var input = HidLearner.LearnKey(idle, [[0, 0xFB]]);

        // Assert
        Assert.That(input, Is.EqualTo(new HidInput(HidInputKind.Bit, 1, Mask: 0x04, Invert: true)));
    }

    [Test]
    public void LearnKey_OnlyTheNoisySensorChanges_FindsNothing()
    {
        // Arrange
        var idle = IdleWithANoisySensor();

        // Act
        var input = HidLearner.LearnKey(idle, [[0, 0, 0, 0, 99]]);

        // Assert
        Assert.That(input, Is.Null);
    }

    private static byte[][] IdleKnob() => [[1, 0, 0, 50]];

    [Test]
    public void LearnKnob_ByteThatStepsAndReturnsToZero_IsARelativeKnob()
    {
        // Arrange
        var idle = IdleKnob();

        // Act
        var input = HidLearner.LearnKnob(idle, [[1, 0, 1, 50], [1, 0, 0, 50], [1, 0, 1, 50]]);

        // Assert
        Assert.That(input, Is.EqualTo(new HidInput(HidInputKind.Relative, 2, ReportId: 1)));
    }

    [Test]
    public void LearnKnob_ClockwiseTurnThatStepsBackwards_IsInverted()
    {
        // Arrange
        var idle = IdleKnob();

        // Act
        var input = HidLearner.LearnKnob(idle, [[1, 0, 0xFF, 50], [1, 0, 0xFF, 50]]);

        // Assert
        Assert.That(input!.Invert, Is.True);
    }

    [Test]
    public void LearnKnob_BytePositionThatGrows_IsAnAbsoluteKnob()
    {
        // Arrange
        var idle = IdleKnob();

        // Act
        var input = HidLearner.LearnKnob(idle, [[1, 0, 0, 52], [1, 0, 0, 55]]);

        // Assert
        Assert.That(input, Is.EqualTo(new HidInput(HidInputKind.Absolute, 3, ReportId: 1)));
    }

    [Test]
    public void LearnKnob_ClockwiseTurnThatLowersThePosition_IsInverted()
    {
        // Arrange
        var idle = IdleKnob();

        // Act
        var input = HidLearner.LearnKnob(idle, [[1, 0, 0, 48], [1, 0, 0, 45]]);

        // Assert
        Assert.That(input!.Invert, Is.True);
    }

    [Test]
    public void Devices_NoSavedDescription_IsEmpty()
    {
        // Arrange
        using var plugin = new HidDevicePlugin(_folder);

        // Act
        var devices = plugin.Devices;

        // Assert
        Assert.That(devices, Is.Empty);
    }

    [Test]
    public void Save_Description_ListsAnOfflineDeviceWithItsControls()
    {
        // Arrange
        using var plugin = new HidDevicePlugin(_folder);

        // Act
        plugin.Save(Pedal());

        // Assert
        var device = Expect.Single(plugin.Devices);
        Assert.That((device.Id, device.Name, device.IsConnected), Is.EqualTo(("hid." + PedalId, PedalName, false)));
        Assert.That(device.Controls.Count, Is.EqualTo(ControlsOnThePedal));
    }

    [Test]
    public void Devices_PluginStartedOnAFolderWithASavedDescription_ListsIt()
    {
        // Arrange
        using (var plugin = new HidDevicePlugin(_folder))
        {
            plugin.Save(Pedal());
        }

        // Act
        using var reloaded = new HidDevicePlugin(_folder);

        // Assert
        Assert.That(reloaded.Devices, Has.Exactly(1).Items);
    }

    [Test]
    public void Delete_SavedDescription_RemovesTheDevice()
    {
        // Arrange
        using var plugin = new HidDevicePlugin(_folder);
        plugin.Save(Pedal());

        // Act
        plugin.Delete(PedalId);

        // Assert
        Assert.That(plugin.Devices, Is.Empty);
    }

    [Test]
    public void DevicesChanged_SaveThenDelete_IsRaisedTwice()
    {
        // Arrange
        using var plugin = new HidDevicePlugin(_folder);
        var changes = 0;
        plugin.DevicesChanged += (_, _) => changes++;

        // Act
        plugin.Save(Pedal());
        plugin.Delete(PedalId);

        // Assert
        Assert.That(changes, Is.EqualTo(2));
    }
}
