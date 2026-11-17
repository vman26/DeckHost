using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.State;

namespace DeckHost.Tests;

public class DeviceHostServiceTests
{
    private const string ProfileId = "profile";
    private const string PluginId = "p";
    private const string ActionId = "a";
    private const string FirstKey = "key.0";
    private const string SecondKey = "key.1";
    private const string Knob = "encoder.0";
    private const string ActiveColor = "FF0000";
    private const string IdleColor = "0000FF";
    private const int ActiveDurationMilliseconds = 50;
    private const int AfterActiveDurationMilliseconds = 300;
    private const int ImagesOnTheDevice = 2;
    private const int FloodOfMessages = 2000;
    private const int LogLimit = 500;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);
    private static readonly (string, byte, byte, byte) KnobIdle = (Knob, 0, 0, 255);
    private static readonly (string, byte, byte, byte) KnobActive = (Knob, 255, 0, 0);

    private static (DeviceHostService Host, FakeDevicePlugin Plugin, RecordingAction Action, VariableStore Store) Create(RgbFeedback? light = null, bool connected = true)
    {
        var plugin = new FakeDevicePlugin(connected);
        var action = new RecordingAction(ActionId);
        var store = new VariableStore();
        var host = new DeviceHostService([plugin], [new TestApplicationPlugin(PluginId, action)], store);
        host.SetProfiles([
            new DeviceProfile(ProfileId, "Profile", FakeDevicePlugin.DeviceId,
            [
                Bindings.Single(FirstKey, PluginId, ActionId),
                Bindings.Single(Knob, PluginId, ActionId, light),
            ]),
        ]);
        return (host, plugin, action, store);
    }

    private static async Task StartedAsync(DeviceHostService host)
    {
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
    }

    private static void ReportInput(FakeDevicePlugin plugin, string controlId, InputCapabilities eventType, int value = 1) =>
        plugin.Context!.ReportInputEvent(FakeDevicePlugin.DeviceId, controlId, eventType, value);

    [Test]
    public async Task DeviceStatistics_AfterOneInput_CountsTheInputAndTheImagesSent()
    {
        // Arrange
        var (host, plugin, _, store) = Create();
        await using var _ = host;
        using var __ = store;
        await host.StartAsync(CancellationToken.None);

        // Act
        ReportInput(plugin, FirstKey, InputCapabilities.Press);
        await host.WhenIdleAsync();

        // Assert
        var statistics = Expect.Single(host.DeviceStatistics);
        Assert.That(statistics.DeviceId, Is.EqualTo(FakeDevicePlugin.DeviceId));
        Assert.That(statistics.Inputs, Is.EqualTo(1));
        Assert.That(statistics.LastInput, Is.Not.Null);
        Assert.That(statistics.FramesSent, Is.GreaterThanOrEqualTo(2));
        Assert.That(statistics.FrameFailures, Is.EqualTo(0));
    }

    [Test]
    public async Task StartAsync_ConnectedDevice_PushesEveryControlWithAnImageOnce()
    {
        // Arrange
        var (host, plugin, _, store) = Create();
        await using var _ = host;
        using var __ = store;

        // Act
        await StartedAsync(host);

        // Assert
        Assert.That(plugin.PushedImages.Order(), Is.EqualTo(new[] { FirstKey, SecondKey }));
    }

    [Test]
    public async Task ReportInput_ActionWaitingForTheReportToReturn_RunsOnTheWorkerAfterTheReturn()
    {
        // Arrange
        var (host, plugin, action, store) = Create();
        await using var _ = host;
        using var __ = store;
        await StartedAsync(host);

        // The action can't finish until the report call has returned. A host that ran the action
        // inside ReportInputEvent (on the SDK's callback thread) would time out here. Comparing
        // thread ids instead doesn't work: this test and the worker both run on pool threads.
        using var reportReturned = new ManualResetEventSlim();
        var ranAfterReportReturned = false;
        action.OnExecute = context =>
        {
            ranAfterReportReturned = reportReturned.Wait(Patience);
            context.Variables.Publish(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.ExecutionCount), action.Executions.Count);
        };

        // Act
        ReportInput(plugin, FirstKey, InputCapabilities.Press);
        reportReturned.Set();
        await host.WhenIdleAsync();

        // Assert
        Assert.That(action.Executions, Has.Exactly(1).Items);
        Assert.That(ranAfterReportReturned, Is.True);
    }

    [Test]
    public async Task ReportInput_PressOnAKey_PushesOnlyTheChangedKey()
    {
        // Arrange
        var (host, plugin, action, store) = Create();
        await using var _ = host;
        using var __ = store;
        await StartedAsync(host);
        plugin.PushedImages.Clear();
        action.OnExecute = context =>
            context.Variables.Publish(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.ExecutionCount), action.Executions.Count);

        // Act
        ReportInput(plugin, FirstKey, InputCapabilities.Press);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(plugin.PushedImages, Is.EqualTo(new[] { FirstKey }));
    }

    [Test]
    public async Task SetActiveProfile_NothingChangedVisually_PushesNothing()
    {
        // Arrange
        var (host, plugin, _, store) = Create();
        await using var _ = host;
        using var __ = store;
        await StartedAsync(host);
        plugin.PushedImages.Clear();

        // Act
        host.SetActiveProfile(ProfileId);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(plugin.PushedImages, Is.Empty);
    }

    [Test]
    public async Task StartAsync_KnobWithAnIdleLight_ShowsTheIdleColor()
    {
        // Arrange
        var (host, plugin, _, store) = Create(new RgbFeedback(ActiveColor, IdleColor, ActiveDurationMilliseconds));
        await using var _ = host;
        using var __ = store;

        // Act
        await StartedAsync(host);

        // Assert
        Assert.That(plugin.Lights, Does.Contain(KnobIdle));
    }

    [Test]
    public async Task ReportInput_RotateKnobWithLightFeedback_ShowsTheActiveColor()
    {
        // Arrange
        var (host, plugin, _, store) = Create(new RgbFeedback(ActiveColor, IdleColor, ActiveDurationMilliseconds));
        await using var _ = host;
        using var __ = store;
        await StartedAsync(host);
        plugin.Lights.Clear();

        // Act
        ReportInput(plugin, Knob, InputCapabilities.Rotate);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(plugin.Lights, Is.EqualTo(new[] { KnobActive }));
    }

    [Test]
    public async Task ReportInput_RotateKnobWithLightFeedback_ReturnsToTheIdleColorAfterTheActiveDuration()
    {
        // Arrange
        var (host, plugin, _, store) = Create(new RgbFeedback(ActiveColor, IdleColor, ActiveDurationMilliseconds));
        await using var _ = host;
        using var __ = store;
        await StartedAsync(host);
        ReportInput(plugin, Knob, InputCapabilities.Rotate);
        await host.WhenIdleAsync();

        // Act
        await Task.Delay(AfterActiveDurationMilliseconds);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(plugin.Lights.Last(), Is.EqualTo(KnobIdle));
    }

    [Test]
    public async Task StartAsync_DisconnectedDevice_PushesNothing()
    {
        // Arrange
        var (host, plugin, _, store) = Create(connected: false);
        await using var _ = host;
        using var __ = store;

        // Act
        await StartedAsync(host);

        // Assert
        Assert.That(plugin.PushedImages, Is.Empty);
    }

    [Test]
    public async Task SetConnected_DeviceConnectsAfterStart_PushesItsImages()
    {
        // Arrange
        var (host, plugin, _, store) = Create(connected: false);
        await using var _ = host;
        using var __ = store;
        await StartedAsync(host);
        var devicesChanged = new TaskCompletionSource();
        host.DevicesChanged += () => devicesChanged.TrySetResult();

        // Act
        plugin.SetConnected(true);
        await devicesChanged.Task.WaitAsync(Patience);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(plugin.PushedImages.Count, Is.EqualTo(ImagesOnTheDevice));
    }

    [Test]
    public async Task SetConnected_DeviceConnectsAfterStart_ListsTheDeviceAsConnected()
    {
        // Arrange
        var (host, plugin, _, store) = Create(connected: false);
        await using var _ = host;
        using var __ = store;
        await StartedAsync(host);
        var devicesChanged = new TaskCompletionSource();
        host.DevicesChanged += () => devicesChanged.TrySetResult();

        // Act
        plugin.SetConnected(true);
        await devicesChanged.Task.WaitAsync(Patience);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(Expect.Single(host.Devices).IsConnected, Is.True);
    }

    [Test]
    public async Task DisposeAsync_StartedHost_ClearsTheDeviceOnce()
    {
        // Arrange
        var (host, plugin, _, store) = Create();
        using var __ = store;
        await host.StartAsync(CancellationToken.None);

        // Act
        await host.DisposeAsync();

        // Assert
        Assert.That(plugin.ClearCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DisposeAsync_StartedHost_DisposesThePlugins()
    {
        // Arrange
        var (host, plugin, _, store) = Create();
        using var __ = store;
        await host.StartAsync(CancellationToken.None);

        // Act
        await host.DisposeAsync();

        // Assert
        Assert.That(plugin.Disposed, Is.True);
    }

    [Test]
    public async Task RuntimeLogs_MoreMessagesThanTheLimit_KeepsOnlyTheLimit()
    {
        // Arrange
        var (host, plugin, _, store) = Create();
        await using var _ = host;
        using var __ = store;
        await StartedAsync(host);

        // Act
        LogMessages(plugin, FloodOfMessages);

        // Assert
        Assert.That(host.RuntimeLogs.Count, Is.LessThanOrEqualTo(LogLimit));
    }

    [Test]
    public async Task RuntimeLogs_MoreMessagesThanTheLimit_KeepsTheNewestMessage()
    {
        // Arrange
        var (host, plugin, _, store) = Create();
        await using var _ = host;
        using var __ = store;
        await StartedAsync(host);

        // Act
        LogMessages(plugin, FloodOfMessages);

        // Assert
        Assert.That(host.RuntimeLogs.Last().Message, Is.EqualTo($"message {FloodOfMessages - 1}"));
    }

    private static void LogMessages(FakeDevicePlugin plugin, int count)
    {
        for (var i = 0; i < count; i++)
        {
            plugin.Context!.Log("Info", $"message {i}");
        }
    }
}
