using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;

namespace DeckHost.Tests;

/// <summary>The welcome tour's decisions: which devices it lists, which device the starter fills, and what the starter puts where.</summary>
public sealed class WelcomeTourTests
{
    private const string N4Pro = WelcomeViewModel.AlwaysListedDeviceId;
    private const string WebDeck = WelcomeViewModel.WebDeckId;
    private const string StreamDeck = "elgato.0080.ABC123";
    private const string Simulator = "simulator.mirabox.n4pro";
    private const string KnobAction = "deckhost.plugin.media:media.volume-knob";
    private const string SwipeAction = "deckhost.plugin.media:media.track-swipe";

    private static DeviceDefinition Device(string id, bool connected) => new(id, id + " name", [], connected);

    private static PhysicalControl Control(string id, ControlType type) =>
        new(id, id, type, new Rect(0, 0, 1, 1), InputCapabilities.Press, OutputCapabilities.ColorImage);

    private static StarterLayout Starter(params string[] keys) =>
        new("test", "Test", "A test starter.", [.. keys.Select(static key => new StarterAction(key))], new StarterAction(KnobAction), new StarterAction(SwipeAction));

    private static bool Everything(StarterAction action, PhysicalControl control) => true;

    [Test]
    public void VisibleDevices_AlwaysListedN4ProNeverUsed_IsHidden()
    {
        // Arrange
        DeviceDefinition[] devices = [Device(N4Pro, connected: false), Device(WebDeck, connected: false)];

        // Act
        var visible = WelcomeViewModel.VisibleDevices(devices, static _ => false);

        // Assert
        Assert.That(visible.Select(static device => device.Id), Is.EqualTo(new[] { WebDeck }));
    }

    [Test]
    public void VisibleDevices_AlwaysListedN4ProWithBindings_IsListed()
    {
        // Arrange
        DeviceDefinition[] devices = [Device(N4Pro, connected: false)];

        // Act
        var visible = WelcomeViewModel.VisibleDevices(devices, static id => id == N4Pro);

        // Assert
        Assert.That(visible.Select(static device => device.Id), Is.EqualTo(new[] { N4Pro }));
    }

    [Test]
    public void VisibleDevices_AlwaysListedN4ProPluggedIn_IsListed()
    {
        // Arrange
        DeviceDefinition[] devices = [Device(N4Pro, connected: true)];

        // Act
        var visible = WelcomeViewModel.VisibleDevices(devices, static _ => false);

        // Assert
        Assert.That(visible, Has.Count.EqualTo(devices.Length));
    }

    [Test]
    public void VisibleDevices_OtherUnpluggedDevice_IsListed()
    {
        // Arrange
        DeviceDefinition[] devices = [Device(StreamDeck, connected: false)];

        // Act
        var visible = WelcomeViewModel.VisibleDevices(devices, static _ => false);

        // Assert
        Assert.That(visible, Has.Count.EqualTo(devices.Length));
    }

    [Test]
    public void PickStarterTarget_EditingTheWebDeckWithAStreamDeckPluggedIn_PicksTheStreamDeck()
    {
        // Arrange
        DeviceDefinition[] devices = [Device(WebDeck, connected: true), Device(N4Pro, connected: false), Device(StreamDeck, connected: true)];

        // Act
        var target = WelcomeViewModel.PickStarterTarget(devices, WebDeck);

        // Assert
        Assert.That(target?.Id, Is.EqualTo(StreamDeck));
    }

    [Test]
    public void PickStarterTarget_EditedDeviceIsPluggedInHardware_KeepsIt()
    {
        // Arrange
        DeviceDefinition[] devices = [Device(StreamDeck, connected: true), Device(N4Pro, connected: true)];

        // Act
        var target = WelcomeViewModel.PickStarterTarget(devices, N4Pro);

        // Assert
        Assert.That(target?.Id, Is.EqualTo(N4Pro));
    }

    [Test]
    public void PickStarterTarget_OnlyVirtualDevicesConnected_IsNull()
    {
        // Arrange
        DeviceDefinition[] devices = [Device(WebDeck, connected: true), Device(Simulator, connected: true), Device(N4Pro, connected: false)];

        // Act
        var target = WelcomeViewModel.PickStarterTarget(devices, WebDeck);

        // Assert
        Assert.That(target, Is.Null);
    }

    [Test]
    public void NewlyConnected_HardwarePluggedInSinceLastLook_IsFound()
    {
        // Arrange
        var before = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DeviceDefinition[] devices = [Device(WebDeck, connected: true), Device(StreamDeck, connected: true)];

        // Act
        var found = WelcomeViewModel.NewlyConnected(before, devices);

        // Assert
        Assert.That(found?.Id, Is.EqualTo(StreamDeck));
    }

    [Test]
    public void NewlyConnected_AlreadyConnectedBefore_IsNull()
    {
        // Arrange
        var before = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { StreamDeck };
        DeviceDefinition[] devices = [Device(StreamDeck, connected: true)];

        // Act
        var found = WelcomeViewModel.NewlyConnected(before, devices);

        // Assert
        Assert.That(found, Is.Null);
    }

    [Test]
    public void Plan_EmptyKeys_GetTheActionsInReadingOrder()
    {
        // Arrange
        const string First = "plugin:first";
        const string Second = "plugin:second";
        PhysicalControl[] controls = [Control("key.0", ControlType.Key), Control("key.1", ControlType.Key)];

        // Act
        var plan = StarterLayouts.Plan(Starter(First, Second), controls, static _ => false, Everything);

        // Assert
        Assert.That(plan.Select(static placement => (placement.Control.Id, placement.Action.ActionKey)), Is.EqualTo(new[] { ("key.0", First), ("key.1", Second) }));
    }

    [Test]
    public void Plan_BoundKey_KeepsItsActionAndTheNextKeyGetsTheFirst()
    {
        // Arrange
        const string First = "plugin:first";
        PhysicalControl[] controls = [Control("key.0", ControlType.Key), Control("key.1", ControlType.Key)];

        // Act
        var plan = StarterLayouts.Plan(Starter(First, "plugin:second"), controls, static id => id == "key.0", Everything);

        // Assert
        var placement = Expect.Single(plan);
        Assert.That((placement.Control.Id, placement.Action.ActionKey), Is.EqualTo(("key.1", First)));
    }

    [Test]
    public void Plan_MissingAction_IsSkippedWithoutLeavingAGap()
    {
        // Arrange
        const string Missing = "obs:stream";
        const string Present = "plugin:present";
        PhysicalControl[] controls = [Control("key.0", ControlType.Key)];

        // Act
        var plan = StarterLayouts.Plan(Starter(Missing, Present), controls, static _ => false, static (action, _) => action.ActionKey != Missing);

        // Assert
        Assert.That(Expect.Single(plan).Action.ActionKey, Is.EqualTo(Present));
    }

    [Test]
    public void Plan_SeveralKnobs_OnlyTheFirstGetsTheKnobAction()
    {
        // Arrange
        PhysicalControl[] controls = [Control("encoder.1", ControlType.Encoder), Control("encoder.0", ControlType.Encoder)];

        // Act
        var plan = StarterLayouts.Plan(Starter(), controls, static _ => false, Everything);

        // Assert
        var placement = Expect.Single(plan);
        Assert.That((placement.Control.Id, placement.Action.ActionKey), Is.EqualTo(("encoder.0", KnobAction)));
    }

    [Test]
    public void Plan_TouchSwipe_GetsTheSwipeAction()
    {
        // Arrange
        PhysicalControl[] controls = [Control("touch.swipe", ControlType.TouchSurface)];

        // Act
        var plan = StarterLayouts.Plan(Starter(), controls, static _ => false, Everything);

        // Assert
        Assert.That(Expect.Single(plan).Action.ActionKey, Is.EqualTo(SwipeAction));
    }

    [Test]
    public void All_EveryStarter_FillsTenKeys()
    {
        // Arrange
        const int KeysOfAnN4Pro = 10;

        // Act
        var counts = StarterLayouts.All.Select(static starter => starter.Keys.Count);

        // Assert
        Assert.That(counts, Is.All.EqualTo(KeysOfAnN4Pro));
    }

    [Test]
    public void StarterKeyActions_MediaStarter_KeepsTheEarlierList()
    {
        // Arrange
        const string FirstKey = "deckhost.plugin.media:media.playpause";

        // Act
        var first = MainViewModel.StarterKeyActions[0];

        // Assert
        Assert.That(first, Is.EqualTo(FirstKey));
    }
}
