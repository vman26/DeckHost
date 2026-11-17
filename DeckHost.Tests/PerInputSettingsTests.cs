using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core;
using DeckHost.Core.Models;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>Settings that differ per input of a control: a knob pressed, turned left, turned right.</summary>
public class PerInputSettingsTests
{
    private const string DeviceId = "dev";
    private const string KnobId = "encoder.0";
    private const InputCapabilities Knob = InputCapabilities.Rotate | InputCapabilities.Press | InputCapabilities.Release;
    private const string ModeKey = "mode";
    private const string SharedKey = "shared";

    private static readonly ActionSettingDefinition Mode = new(ModeKey, "Mode", ActionSettingKind.Choice, "a", Options: [new("a", "A"), new("b", "B"), new("c", "C")])
    {
        PerInput = true,
        InputDefaults = new Dictionary<string, string> { [ControlInputs.RotateLeft] = "b", [ControlInputs.RotateRight] = "c" },
    };

    private static readonly ActionSettingDefinition SharedSetting = new(SharedKey, "Shared", DefaultValue: "x");

    /// <summary>The plain value "c", the left turn's own "a" and a shared "y".</summary>
    private static ActionSettings StoredSettings() => new(new Dictionary<string, string>
    {
        [ModeKey] = "c",
        [$"{ModeKey}@{ControlInputs.RotateLeft}"] = "a",
        [SharedKey] = "y",
    });

    [TestCase(InputCapabilities.Press, 1, ControlInputs.Press)]
    [TestCase(InputCapabilities.Release, 0, ControlInputs.Release)]
    [TestCase(InputCapabilities.LongPress, 1, ControlInputs.LongPress)]
    [TestCase(InputCapabilities.Rotate, -2, ControlInputs.RotateLeft)]
    [TestCase(InputCapabilities.Rotate, 1, ControlInputs.RotateRight)]
    [TestCase(InputCapabilities.Swipe, -1, ControlInputs.SwipeLeft)]
    [TestCase(InputCapabilities.Swipe, 1, ControlInputs.SwipeRight)]
    [TestCase(InputCapabilities.Rotate, 0, null)]
    public void FromEvent_AnEventAndItsValue_MapsToAnInputByDirection(InputCapabilities eventType, int value, string? expected)
    {
        // Act
        var input = ControlInputs.FromEvent(eventType, value);

        // Assert
        Assert.That(input, Is.EqualTo(expected));
    }

    [Test]
    public void Of_AKnob_IsSetUpPerPressAndDirectionAndAReleaseIsNotAnInput()
    {
        // Act
        var inputs = ControlInputs.Of(Knob);

        // Assert
        Assert.That(inputs, Is.EqualTo(new[] { ControlInputs.Press, ControlInputs.RotateLeft, ControlInputs.RotateRight }));
    }

    [Test]
    public void Of_AKeyWithLongPress_HasPressAndLongPress()
    {
        // Act
        var inputs = ControlInputs.Of(InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress);

        // Assert
        Assert.That(inputs, Is.EqualTo(new[] { ControlInputs.Press, ControlInputs.LongPress }));
    }

    [Test]
    public void Of_ASwipeZone_HasBothDirections()
    {
        // Act
        var inputs = ControlInputs.Of(InputCapabilities.Swipe);

        // Assert
        Assert.That(inputs, Is.EqualTo(new[] { ControlInputs.SwipeLeft, ControlInputs.SwipeRight }));
    }

    [Test]
    public void ForInput_AValueStoredForTheInput_WinsOverTheRest()
    {
        // Arrange
        var settings = StoredSettings();

        // Act
        var resolved = settings.ForInput([Mode, SharedSetting], ControlInputs.RotateLeft);

        // Assert
        Assert.That(resolved.Get(ModeKey), Is.EqualTo("a"));
    }

    [Test]
    public void ForInput_NothingStoredForTheInput_UsesItsPerInputDefaultOverThePlainValue()
    {
        // Arrange (the defaults: right turn "c"; the plain value is also "c", so check with another plain value)
        var settings = new ActionSettings(new Dictionary<string, string> { [ModeKey] = "a" });

        // Act
        var resolved = settings.ForInput([Mode], ControlInputs.RotateRight);

        // Assert
        Assert.That(resolved.Get(ModeKey), Is.EqualTo("c"));
    }

    [Test]
    public void ForInput_AnInputWithoutADefault_FallsBackToThePlainValueOfBindingsFromBefore()
    {
        // Arrange
        var settings = StoredSettings();

        // Act
        var resolved = settings.ForInput([Mode, SharedSetting], ControlInputs.Press);

        // Assert
        Assert.That(resolved.Get(ModeKey), Is.EqualTo("c"));
    }

    [Test]
    public void ForInput_NothingStoredAtAll_UsesTheSettingsDefault()
    {
        // Act
        var resolved = ActionSettings.Empty.ForInput([Mode], ControlInputs.Press);

        // Assert
        Assert.That(resolved.Get(ModeKey, Mode.DefaultValue), Is.EqualTo("a"));
    }

    [Test]
    public void ForInput_ASettingThatIsNotPerInput_KeepsItsPlainValue()
    {
        // Arrange
        var settings = StoredSettings();

        // Act
        var resolved = settings.ForInput([Mode, SharedSetting], ControlInputs.RotateLeft);

        // Assert
        Assert.That(resolved.Get(SharedKey), Is.EqualTo("y"));
    }

    [Test]
    public void ForInput_NoPerInputSettings_ReturnsTheSameSettings()
    {
        // Arrange
        var settings = StoredSettings();

        // Act
        var resolved = settings.ForInput([SharedSetting], ControlInputs.Press);

        // Assert
        Assert.That(resolved, Is.SameAs(settings));
    }

    [Test]
    public void ForInput_NoInput_ReturnsTheSameSettings()
    {
        // Arrange
        var settings = StoredSettings();

        // Act
        var resolved = settings.ForInput([Mode], null);

        // Assert
        Assert.That(resolved, Is.SameAs(settings));
    }

    [Test]
    public void Dispatch_PressAndBothTurns_HandsTheActionTheValueForTheInputThatHappened()
    {
        // Arrange
        using var store = new VariableStore();
        var action = new RecordingAction("a") { Settings = [Mode] };
        var router = new ActionRouter(store, [new TestApplicationPlugin("p", action)]);
        var reference = Bindings.Reference("p", "a") with
        {
            Settings = new ActionSettings(new Dictionary<string, string> { [$"{ModeKey}@press"] = "b", [$"{ModeKey}@rotate-right"] = "a" }),
        };
        var profile = new DeviceProfile("profile", "Profile", DeviceId, [new ControlBinding(KnobId, ProfileActionKind.Single, reference, new ControlAppearance(), [])]);

        // Act
        router.Dispatch(profile, new DeviceInputEvent(DeviceId, KnobId, InputCapabilities.Press, 1), NullActionHost.Instance);
        router.Dispatch(profile, new DeviceInputEvent(DeviceId, KnobId, InputCapabilities.Rotate, -1), NullActionHost.Instance);
        router.Dispatch(profile, new DeviceInputEvent(DeviceId, KnobId, InputCapabilities.Rotate, 1), NullActionHost.Instance);

        // Assert
        Assert.That(action.Executions.Select(context => context.Settings.Get(ModeKey)), Is.EqualTo(new[] { "b", "b", "a" }));
    }
}
