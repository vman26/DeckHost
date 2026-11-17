using System.Collections.Concurrent;
using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>
/// What the host draws again after a change: only the control that changed (and the display a knob is
/// shown on), or the whole device when everything may look different. The count is
/// <c>DeviceStatistics.FramesRendered</c>, so nothing here depends on timing.
/// </summary>
public sealed class ControlDirtyTrackingTests
{
    private const string PluginId = "p";
    private const string ActionId = "a";
    private const string FirstKey = "key.0";
    private const string SecondKey = "key.1";
    private const string Knob = "encoder.0";
    private const string OtherProfileId = "other";
    private const string FirstProfileId = "first";
    private const string CpuVariable = "system.cpu";
    private const string LevelVariable = "level";
    private const string UnrelatedSuffix = "custom";
    private const string BoundRed = "#FF0000";
    private const string BoundBlack = "#000000";
    private const double FullLoad = 100.0;
    private const double PartialLoad = 0.5;
    private const int KeysOnTheDevice = 2;
    private static readonly TimeSpan LiveInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan TwoIntervals = TimeSpan.FromHours(2);

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(IDeviceWithContext plugin, string deviceId, RecordingAction action, IEnumerable<DeviceProfile> profiles)
        {
            Plugin = plugin;
            DeviceId = deviceId;
            Action = action;
            Store = new VariableStore();
            Host = new DeviceHostService([plugin], [new TestApplicationPlugin(PluginId, action)], Store);
            Host.ControlFrameRendered += frame => Frames.Enqueue(frame.ControlId);
            Host.SetProfiles(profiles);
        }

        public IDeviceWithContext Plugin { get; }

        public string DeviceId { get; }

        public RecordingAction Action { get; }

        public VariableStore Store { get; }

        public DeviceHostService Host { get; }

        public ConcurrentQueue<string> Frames { get; } = new();

        public long FramesRendered => Host.DeviceStatistics.Single(statistics => statistics.DeviceId == DeviceId).FramesRendered;

        public async Task StartAsync()
        {
            await Host.StartAsync(CancellationToken.None);
            await Host.WhenIdleAsync();
        }

        public async Task PressAsync(string controlId)
        {
            Plugin.Context!.ReportInputEvent(DeviceId, controlId, InputCapabilities.Press, 1);
            await Host.WhenIdleAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            Store.Dispose();
        }
    }

    private static RecordingAction CountingAction(TimeSpan? refreshInterval = null)
    {
        var action = new RecordingAction(ActionId) { RefreshInterval = refreshInterval };
        action.OnExecute = context =>
            context.Variables.Publish(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.ExecutionCount), action.Executions.Count);
        return action;
    }

    private static DeviceProfile ProfileOf(string id, string deviceId, ProfileBackground? background = null, params string[] boundControls) =>
        new(id, id, deviceId, [.. boundControls.Select(static control => Bindings.Single(control, PluginId, ActionId))], Background: background);

    private static async Task<Harness> StartedFakeDeviceAsync(RecordingAction? action = null, ProfileBackground? background = null, params string[] boundControls)
    {
        var harness = new Harness(new FakeDevicePlugin(), FakeDevicePlugin.DeviceId, action ?? CountingAction(),
            [ProfileOf(FirstProfileId, FakeDevicePlugin.DeviceId, background, boundControls.Length == 0 ? [FirstKey, Knob] : boundControls)]);
        await harness.StartAsync();
        return harness;
    }

    private static async Task<Harness> StartedZonesDeviceAsync()
    {
        var harness = new Harness(new TwoZoneDevicePlugin(), TwoZoneDevicePlugin.DeviceId, CountingAction(),
            [ProfileOf(FirstProfileId, TwoZoneDevicePlugin.DeviceId, null, TwoZoneDevicePlugin.FirstKnob)]);
        await harness.StartAsync();
        return harness;
    }

    private static ProfileBackground BoundColorBackground() => new()
    {
        Layers = [new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = BoundBlack, Binding = new BackgroundBinding(CpuVariable) { FromColor = BoundBlack, ToColor = BoundRed } }],
    };

    private static ProfileBackground HiddenWhileBelowOneBackground() => new()
    {
        Layers = [new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = BoundRed, Binding = new BackgroundBinding(CpuVariable, BackgroundBindingTargets.Visible, Min: 1) }],
    };

    private static long Rendered(DeviceHostService host, string deviceId) =>
        host.DeviceStatistics.Single(statistics => statistics.DeviceId == deviceId).FramesRendered;

    [Test]
    public async Task ReportInput_PressOnOneKey_RendersOnlyThatKey()
    {
        // Arrange
        await using var harness = await StartedFakeDeviceAsync();
        var before = harness.FramesRendered;

        // Act
        await harness.PressAsync(FirstKey);

        // Assert
        Assert.That(harness.FramesRendered - before, Is.EqualTo(1));
    }

    [Test]
    public async Task ReportInput_PressOnOneKey_RaisesAFrameOnlyForThatKey()
    {
        // Arrange
        await using var harness = await StartedFakeDeviceAsync();
        harness.Frames.Clear();

        // Act
        await harness.PressAsync(FirstKey);

        // Assert
        Assert.That(harness.Frames, Is.EqualTo(new[] { FirstKey }));
    }

    [Test]
    public async Task ReportInput_PressOnOneKey_PushesOnlyThatKey()
    {
        // Arrange
        await using var harness = await StartedFakeDeviceAsync();
        var plugin = (FakeDevicePlugin)harness.Plugin;
        plugin.PushedImages.Clear();

        // Act
        await harness.PressAsync(FirstKey);

        // Assert
        Assert.That(plugin.PushedImages, Is.EqualTo(new[] { FirstKey }));
    }

    [Test]
    public async Task ReportInput_PressOnKnobWithLinkedDisplay_RendersOnlyItsDisplay()
    {
        // Arrange
        await using var harness = await StartedZonesDeviceAsync();
        var plugin = (TwoZoneDevicePlugin)harness.Plugin;
        plugin.PushedImages.Clear();
        var before = harness.FramesRendered;

        // Act
        await harness.PressAsync(TwoZoneDevicePlugin.FirstKnob);

        // Assert
        Assert.That(harness.FramesRendered - before, Is.EqualTo(1));
        Assert.That(plugin.PushedImages, Is.EqualTo(new[] { TwoZoneDevicePlugin.FirstZone }));
    }

    [Test]
    public async Task Publish_VariableOfTheSecondKey_RendersOnlyTheSecondKey()
    {
        // Arrange
        await using var harness = await StartedFakeDeviceAsync();
        var before = harness.FramesRendered;

        // Act
        harness.Store.Publish(VariableKeys.For(FakeDevicePlugin.DeviceId, SecondKey, UnrelatedSuffix), 1);
        await harness.Host.WhenIdleAsync();

        // Assert
        Assert.That(harness.FramesRendered - before, Is.EqualTo(1));
    }

    [Test]
    public async Task Publish_VariableOfAKnobWithADisplay_RendersTheKnobsDisplay()
    {
        // Arrange
        await using var harness = await StartedZonesDeviceAsync();
        var before = harness.FramesRendered;

        // Act
        harness.Store.Publish(VariableKeys.For(TwoZoneDevicePlugin.DeviceId, TwoZoneDevicePlugin.SecondKnob, UnrelatedSuffix), 1);
        await harness.Host.WhenIdleAsync();

        // Assert
        Assert.That(harness.FramesRendered - before, Is.EqualTo(1));
    }

    [Test]
    public async Task Publish_VariableOfTheDeviceItself_RendersNothing()
    {
        // Arrange
        await using var harness = await StartedFakeDeviceAsync();
        var before = harness.FramesRendered;

        // Act
        harness.Store.Publish(VariableKeys.Device(FakeDevicePlugin.DeviceId) + VariableKeys.LastControl, FirstKey);
        await harness.Host.WhenIdleAsync();

        // Assert
        Assert.That(harness.FramesRendered - before, Is.EqualTo(0));
    }

    [Test]
    public async Task Publish_UserVariable_RendersEveryDisplayOfTheDevice()
    {
        // Arrange
        await using var harness = await StartedFakeDeviceAsync();
        var before = harness.FramesRendered;

        // Act
        harness.Store.Publish(VariableKeys.UserVariable(LevelVariable), 1);
        await harness.Host.WhenIdleAsync();

        // Assert
        Assert.That(harness.FramesRendered - before, Is.EqualTo(KeysOnTheDevice));
    }

    [Test]
    public async Task RequestRender_WithAControlId_RendersOnlyThatControl()
    {
        // Arrange
        await using var harness = await StartedFakeDeviceAsync();
        await harness.PressAsync(FirstKey);
        var host = harness.Action.Executions[0].Host;
        var before = harness.FramesRendered;

        // Act
        host.RequestRender(FakeDevicePlugin.DeviceId, SecondKey);
        await harness.Host.WhenIdleAsync();

        // Assert
        Assert.That(harness.FramesRendered - before, Is.EqualTo(1));
    }

    [Test]
    public async Task RequestRender_WithOnlyTheDeviceId_RendersEveryDisplayOfTheDevice()
    {
        // Arrange
        await using var harness = await StartedFakeDeviceAsync();
        await harness.PressAsync(FirstKey);
        var host = harness.Action.Executions[0].Host;
        var before = harness.FramesRendered;

        // Act
        host.RequestRender(FakeDevicePlugin.DeviceId);
        await harness.Host.WhenIdleAsync();

        // Assert
        Assert.That(harness.FramesRendered - before, Is.EqualTo(KeysOnTheDevice));
    }

    [Test]
    public async Task RefreshLiveControlsAsync_DueLiveAction_RendersOnlyItsControl()
    {
        // Arrange
        var start = DateTimeOffset.UtcNow;
        await using var harness = await StartedFakeDeviceAsync(CountingAction(LiveInterval), null, FirstKey);
        await harness.Host.RefreshLiveControlsAsync(start);
        var before = harness.FramesRendered;

        // Act
        await harness.Host.RefreshLiveControlsAsync(start + TwoIntervals);

        // Assert
        Assert.That(harness.FramesRendered - before, Is.EqualTo(1));
    }

    [Test]
    public async Task SetActiveProfile_OtherProfileOfTheDevice_RendersEveryDisplay()
    {
        // Arrange
        var harness = new Harness(new FakeDevicePlugin(), FakeDevicePlugin.DeviceId, CountingAction(),
            [ProfileOf(FirstProfileId, FakeDevicePlugin.DeviceId, null, FirstKey), ProfileOf(OtherProfileId, FakeDevicePlugin.DeviceId, null, SecondKey)]);
        await using var _ = harness;
        await harness.StartAsync();
        var before = harness.FramesRendered;

        // Act
        harness.Host.SetActiveProfile(OtherProfileId);
        await harness.Host.WhenIdleAsync();

        // Assert
        Assert.That(harness.FramesRendered - before, Is.EqualTo(KeysOnTheDevice));
    }

    [Test]
    public async Task SetActiveProfile_ProfileOfAnotherDevice_RendersNothingOnThisDevice()
    {
        // Arrange
        var action = CountingAction();
        var store = new VariableStore();
        var host = new DeviceHostService([new FakeDevicePlugin(), new TwoZoneDevicePlugin()], [new TestApplicationPlugin(PluginId, action)], store);
        await using var _ = host;
        using var __ = store;
        host.SetProfiles([
            ProfileOf(FirstProfileId, FakeDevicePlugin.DeviceId, null, FirstKey),
            ProfileOf(FirstProfileId + TwoZoneDevicePlugin.DeviceId, TwoZoneDevicePlugin.DeviceId),
            ProfileOf(OtherProfileId, TwoZoneDevicePlugin.DeviceId),
        ]);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        var before = Rendered(host, FakeDevicePlugin.DeviceId);

        // Act
        host.SetActiveProfile(OtherProfileId);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(Rendered(host, FakeDevicePlugin.DeviceId) - before, Is.EqualTo(0));
    }

    [Test]
    public async Task Publish_VariableABackgroundFollows_RendersEveryDisplay()
    {
        // Arrange
        await using var harness = await StartedFakeDeviceAsync(background: BoundColorBackground());
        var before = harness.FramesRendered;

        // Act
        harness.Store.Publish(CpuVariable, FullLoad);
        await harness.Host.WhenIdleAsync();

        // Assert
        Assert.That(harness.FramesRendered - before, Is.EqualTo(KeysOnTheDevice));
    }

    [Test]
    public async Task Publish_VariableABackgroundFollowsWithoutChangingTheLayers_RendersNothing()
    {
        // Arrange
        await using var harness = await StartedFakeDeviceAsync(background: HiddenWhileBelowOneBackground());
        var before = harness.FramesRendered;

        // Act
        harness.Store.Publish(CpuVariable, PartialLoad);
        await harness.Host.WhenIdleAsync();

        // Assert
        Assert.That(harness.FramesRendered - before, Is.EqualTo(0));
    }

    [Test]
    public async Task Redraw_AfterAnImageOfOneKeyWasSent_PushesEveryDisplayAgain()
    {
        // Arrange
        await using var harness = await StartedFakeDeviceAsync();
        var plugin = (FakeDevicePlugin)harness.Plugin;
        await harness.PressAsync(FirstKey);
        plugin.PushedImages.Clear();

        // Act
        harness.Host.Redraw(FakeDevicePlugin.DeviceId);
        await harness.Host.WhenIdleAsync();

        // Assert
        Assert.That(plugin.PushedImages.Order(), Is.EqualTo(new[] { FirstKey, SecondKey }));
    }

    [Test]
    public async Task DeviceStatistics_AfterStart_CountsOneRenderForEveryDisplay()
    {
        // Arrange
        await using var harness = new Harness(new FakeDevicePlugin(), FakeDevicePlugin.DeviceId, CountingAction(), [ProfileOf(FirstProfileId, FakeDevicePlugin.DeviceId)]);

        // Act
        await harness.StartAsync();

        // Assert
        Assert.That(harness.FramesRendered, Is.EqualTo(KeysOnTheDevice));
    }
}
