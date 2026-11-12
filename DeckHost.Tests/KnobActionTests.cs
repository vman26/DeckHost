using DeckHost.App.SystemPlugins;
using DeckHost.App.ViewModels;
using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Models;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>Actions that show text or a reading on a knob (drawn on the touch zone above it).</summary>
public class KnobActionTests
{
    private static readonly PhysicalControl Knob = new("encoder.0", "Knob 1", ControlType.Encoder, new Rect(0, 0, 10, 10),
        InputCapabilities.Rotate | InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.None, DisplayControlId: "touch.0");

    private static readonly PhysicalControl KnobWithoutDisplay = Knob with { DisplayControlId = null };

    private static readonly PhysicalControl Key = new("key.0", "Key 1", ControlType.Key, new Rect(0, 0, 10, 10),
        InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress, OutputCapabilities.ColorImage | OutputCapabilities.Text);

    private static ActionItemViewModel BuiltIn(string actionId)
    {
        var plugin = SystemPluginSet.Create().First(candidate => candidate.Actions.Any(action => action.Id == actionId));
        return ActionItemViewModel.Create(plugin.Id, plugin.Name, plugin.Actions.First(action => action.Id == actionId));
    }

    [Theory]
    [InlineData("builtin.counter")]
    [InlineData("deckhost.diagnostic.key-id")]
    [InlineData("builtin.clock")]
    [InlineData("system.info")]
    public void Actions_that_show_text_fit_a_knob_with_a_touch_zone(string actionId)
    {
        var action = BuiltIn(actionId);

        Assert.True(action.FitsControl(Knob));
        Assert.True(action.FitsControl(Key));
        Assert.False(action.FitsControl(KnobWithoutDisplay));
    }

    [Fact]
    public void Key_image_actions_still_need_a_display_of_their_own()
    {
        var keyImage = ActionItemViewModel.Create("p", "P", new ImageAction());

        Assert.True(keyImage.FitsControl(Key));
        Assert.False(keyImage.FitsControl(Knob));
    }

    [Fact]
    public async Task A_counter_on_a_knob_counts_turns_and_shows_on_the_touch_zone()
    {
        var plugins = SystemPluginSet.Create();
        var device = new StripDevicePlugin();
        using var store = new VariableStore();
        await using var host = new DeviceHostService([device], plugins, store);
        var frames = new System.Collections.Concurrent.ConcurrentDictionary<string, ControlFrame>();
        host.ControlFrameRendered += frame => frames[frame.ControlId] = frame;
        var counter = new ActionReference(SystemPluginSet.BuiltInPluginId, "Built-ins", "builtin.counter", "Counter", InputCapabilities.None, OutputCapabilities.Text);
        host.SetProfiles([new DeviceProfile("p", "P", StripDevicePlugin.DeviceId,
            [new ControlBinding(StripDevicePlugin.KnobId, ProfileActionKind.Single, counter, new ControlAppearance(), [])])]);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        var before = frames[StripDevicePlugin.ZoneId].Rgba;

        host.SimulateInput(new DeviceInputEvent(StripDevicePlugin.DeviceId, StripDevicePlugin.KnobId, InputCapabilities.Rotate, 1));
        host.SimulateInput(new DeviceInputEvent(StripDevicePlugin.DeviceId, StripDevicePlugin.KnobId, InputCapabilities.Rotate, 1));
        host.SimulateInput(new DeviceInputEvent(StripDevicePlugin.DeviceId, StripDevicePlugin.KnobId, InputCapabilities.Rotate, -1));
        await host.WhenIdleAsync();

        Assert.True(store.TryGetValue<int>(VariableKeys.For(StripDevicePlugin.DeviceId, StripDevicePlugin.KnobId, VariableKeys.Counter), out var count));
        Assert.Equal(1, count);
        Assert.False(before.AsSpan().SequenceEqual(frames[StripDevicePlugin.ZoneId].Rgba), "the zone shows the new count");
    }

    private sealed class ImageAction : IDeckAction
    {
        public string Id => "image";

        public string Name => "Image";

        public InputCapabilities RequiredInputs => InputCapabilities.None;

        public OutputCapabilities RequiredOutputs => OutputCapabilities.ColorImage;

        public void Execute(ActionContext context)
        {
        }

        public void Render(IRenderContext context)
        {
        }
    }
}
