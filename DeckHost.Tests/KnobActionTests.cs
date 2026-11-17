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

    [TestCase("builtin.counter")]
    [TestCase("deckhost.diagnostic.key-id")]
    [TestCase("builtin.clock")]
    [TestCase("system.info")]
    public void FitsControl_ATextActionOnAKnobWithATouchZone_IsTrue(string actionId)
    {
        // Arrange
        var action = BuiltIn(actionId);

        // Act
        var fitsKnob = action.FitsControl(Knob);

        // Assert
        Assert.That(fitsKnob, Is.True);
    }

    [TestCase("builtin.counter")]
    [TestCase("deckhost.diagnostic.key-id")]
    [TestCase("builtin.clock")]
    [TestCase("system.info")]
    public void FitsControl_ATextActionOnAKey_IsTrue(string actionId)
    {
        // Arrange
        var action = BuiltIn(actionId);

        // Act
        var fitsKey = action.FitsControl(Key);

        // Assert
        Assert.That(fitsKey, Is.True);
    }

    [TestCase("builtin.counter")]
    [TestCase("deckhost.diagnostic.key-id")]
    [TestCase("builtin.clock")]
    [TestCase("system.info")]
    public void FitsControl_ATextActionOnAKnobWithoutADisplay_IsFalse(string actionId)
    {
        // Arrange
        var action = BuiltIn(actionId);

        // Act
        var fitsKnob = action.FitsControl(KnobWithoutDisplay);

        // Assert
        Assert.That(fitsKnob, Is.False);
    }

    [Test]
    public void FitsControl_AKeyImageActionOnAKey_IsTrue()
    {
        // Arrange
        var keyImage = ActionItemViewModel.Create("p", "P", new ImageAction());

        // Act
        var fits = keyImage.FitsControl(Key);

        // Assert
        Assert.That(fits, Is.True);
    }

    [Test]
    public void FitsControl_AKeyImageActionOnAKnobWithATouchZone_IsFalseBecauseItNeedsADisplayOfItsOwn()
    {
        // Arrange
        var keyImage = ActionItemViewModel.Create("p", "P", new ImageAction());

        // Act
        var fits = keyImage.FitsControl(Knob);

        // Assert
        Assert.That(fits, Is.False);
    }

    private async Task<(DeviceHostService Host, VariableStore Store, System.Collections.Concurrent.ConcurrentDictionary<string, ControlFrame> Frames)> StartWithACounterOnTheKnob()
    {
        var store = new VariableStore();
        var host = new DeviceHostService([new StripDevicePlugin()], SystemPluginSet.Create(), store);
        var frames = new System.Collections.Concurrent.ConcurrentDictionary<string, ControlFrame>();
        host.ControlFrameRendered += frame => frames[frame.ControlId] = frame;
        var counter = new ActionReference(SystemPluginSet.BuiltInPluginId, "Built-ins", "builtin.counter", "Counter", InputCapabilities.None, OutputCapabilities.Text);
        host.SetProfiles([new DeviceProfile("p", "P", StripDevicePlugin.DeviceId,
            [new ControlBinding(StripDevicePlugin.KnobId, ProfileActionKind.Single, counter, new ControlAppearance(), [])])]);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        return (host, store, frames);
    }

    private static void TurnKnob(DeviceHostService host, int steps) =>
        host.SimulateInput(new DeviceInputEvent(StripDevicePlugin.DeviceId, StripDevicePlugin.KnobId, InputCapabilities.Rotate, steps));

    [Test]
    public async Task Rotate_ACounterOnAKnob_CountsTheTurns()
    {
        // Arrange
        var (host, store, _) = await StartWithACounterOnTheKnob();
        await using var _ = host;
        using var __ = store;

        // Act
        TurnKnob(host, 1);
        TurnKnob(host, 1);
        TurnKnob(host, -1);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(store.TryGetValue<int>(VariableKeys.For(StripDevicePlugin.DeviceId, StripDevicePlugin.KnobId, VariableKeys.Counter), out var count), Is.True);
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task Rotate_ACounterOnAKnob_RedrawsTheTouchZoneWithTheNewCount()
    {
        // Arrange
        var (host, store, frames) = await StartWithACounterOnTheKnob();
        await using var _ = host;
        using var __ = store;
        var before = frames[StripDevicePlugin.ZoneId].Rgba;

        // Act
        TurnKnob(host, 1);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(before.AsSpan().SequenceEqual(frames[StripDevicePlugin.ZoneId].Rgba), Is.False, "the zone shows the new count");
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
