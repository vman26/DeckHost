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
    private const InputCapabilities Knob = InputCapabilities.Rotate | InputCapabilities.Press | InputCapabilities.Release;

    private static readonly ActionSettingDefinition Mode = new("mode", "Mode", ActionSettingKind.Choice, "a", Options: [new("a", "A"), new("b", "B"), new("c", "C")])
    {
        PerInput = true,
        InputDefaults = new Dictionary<string, string> { [ControlInputs.RotateLeft] = "b", [ControlInputs.RotateRight] = "c" },
    };



    private static readonly ActionSettingDefinition SharedSetting = new("shared", "Shared", DefaultValue: "x");

    [Theory]
    [InlineData(InputCapabilities.Press, 1, ControlInputs.Press)]
    [InlineData(InputCapabilities.Release, 0, ControlInputs.Release)]
    [InlineData(InputCapabilities.LongPress, 1, ControlInputs.LongPress)]
    [InlineData(InputCapabilities.Rotate, -2, ControlInputs.RotateLeft)]
    [InlineData(InputCapabilities.Rotate, 1, ControlInputs.RotateRight)]
    [InlineData(InputCapabilities.Swipe, -1, ControlInputs.SwipeLeft)]
    [InlineData(InputCapabilities.Swipe, 1, ControlInputs.SwipeRight)]
    [InlineData(InputCapabilities.Rotate, 0, null)]
    public void Events_map_to_inputs_by_direction(InputCapabilities eventType, int value, string? expected) =>
        Assert.Equal(expected, ControlInputs.FromEvent(eventType, value));

    [Fact]
    public void A_knob_is_set_up_per_press_and_direction_and_a_release_is_not_an_input()
    {
        Assert.Equal([ControlInputs.Press, ControlInputs.RotateLeft, ControlInputs.RotateRight], ControlInputs.Of(Knob));
        Assert.Equal([ControlInputs.Press, ControlInputs.LongPress], ControlInputs.Of(InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress));
        Assert.Equal([ControlInputs.SwipeLeft, ControlInputs.SwipeRight], ControlInputs.Of(InputCapabilities.Swipe));
    }

    [Fact]
    public void An_input_gets_its_own_value_then_its_default_then_the_plain_value()
    {
        var settings = new ActionSettings(new Dictionary<string, string>
        {
            ["mode"] = "c",
            ["mode@rotate-left"] = "a",
            ["shared"] = "y",
        });

        Assert.Equal("a", settings.ForInput([Mode, SharedSetting], ControlInputs.RotateLeft).Get("mode")); // its own value
        Assert.Equal("c", settings.ForInput([Mode, SharedSetting], ControlInputs.RotateRight).Get("mode")); // its default
        Assert.Equal("c", settings.ForInput([Mode, SharedSetting], ControlInputs.Press).Get("mode")); // the plain value (bindings from before)
        Assert.Equal("a", ActionSettings.Empty.ForInput([Mode], ControlInputs.Press).Get("mode", Mode.DefaultValue));
        Assert.Equal("y", settings.ForInput([Mode, SharedSetting], ControlInputs.RotateLeft).Get("shared"));
        Assert.Same(settings, settings.ForInput([SharedSetting], ControlInputs.Press));
        Assert.Same(settings, settings.ForInput([Mode], null));
    }

    [Fact]
    public void The_router_hands_the_action_the_value_for_the_input_that_happened()
    {
        using var store = new VariableStore();
        var action = new RecordingAction("a") { Settings = [Mode] };
        var router = new ActionRouter(store, [new TestApplicationPlugin("p", action)]);
        var reference = Bindings.Reference("p", "a") with
        {
            Settings = new ActionSettings(new Dictionary<string, string> { ["mode@press"] = "b", ["mode@rotate-right"] = "a" }),
        };
        var profile = new DeviceProfile("profile", "Profile", DeviceId, [new ControlBinding("encoder.0", ProfileActionKind.Single, reference, new ControlAppearance(), [])]);

        router.Dispatch(profile, new DeviceInputEvent(DeviceId, "encoder.0", InputCapabilities.Press, 1), NullActionHost.Instance);
        router.Dispatch(profile, new DeviceInputEvent(DeviceId, "encoder.0", InputCapabilities.Rotate, -1), NullActionHost.Instance);
        router.Dispatch(profile, new DeviceInputEvent(DeviceId, "encoder.0", InputCapabilities.Rotate, 1), NullActionHost.Instance);

        Assert.Equal(["b", "b", "a"], action.Executions.Select(context => context.Settings.Get("mode")));
    }
}
