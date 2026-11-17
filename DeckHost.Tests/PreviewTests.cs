using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Models;
using DeckHost.Core.State;
using System.Collections.Concurrent;

namespace DeckHost.Tests;

public class PreviewTests
{
    private const string PluginId = "p";
    private const string ActionId = "a";
    private const string FirstKey = "key.0";
    private const string SecondKey = "key.1";
    private const string Green = "#00FF00";
    private const string Red = "#FF0000";
    private static readonly byte[] GreenPixel = [0, 255, 0, 255];
    private static readonly byte[] RedPixel = [255, 0, 0, 255];

    private static DeviceProfile ProfileWithKey(params ControlBinding[] bindings) => new("one", "One", FakeDevicePlugin.DeviceId, bindings);

    private static async Task<ConcurrentDictionary<string, ControlFrame>> FramesOfAGreenProfileWithARedKeyAsync()
    {
        using var store = new VariableStore();
        await using var host = new DeviceHostService([new FakeDevicePlugin()], [new TestApplicationPlugin(PluginId, new RecordingAction(ActionId))], store);
        var frames = new ConcurrentDictionary<string, ControlFrame>();
        host.ControlFrameRendered += frame => frames[frame.ControlId] = frame;
        var redKey = new ControlBinding(SecondKey, ProfileActionKind.Single, Bindings.Reference(PluginId, "missing"), new ControlAppearance(BackgroundColor: Red), []);
        host.SetProfiles([ProfileWithKey(redKey) with { Background = new ProfileBackground(Green) }]);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        return frames;
    }

    [Test]
    public async Task StartAsync_DeviceNotPlugged_ReportsAFrameForEveryControlWithAnImage()
    {
        // Arrange
        var plugin = new FakeDevicePlugin(connected: false);
        using var store = new VariableStore();
        await using var host = new DeviceHostService([plugin], [new TestApplicationPlugin(PluginId, new RecordingAction(ActionId))], store);
        var frames = new ConcurrentBag<ControlFrame>();
        host.ControlFrameRendered += frames.Add;
        host.SetProfiles([ProfileWithKey(Bindings.Single(FirstKey, PluginId, ActionId))]);

        // Act
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(frames.Select(frame => frame.ControlId).Order(), Is.EqualTo(new[] { FirstKey, SecondKey }));
    }

    [Test]
    public async Task StartAsync_DeviceNotPlugged_PushesNothingToTheDevice()
    {
        // Arrange
        var plugin = new FakeDevicePlugin(connected: false);
        using var store = new VariableStore();
        await using var host = new DeviceHostService([plugin], [new TestApplicationPlugin(PluginId, new RecordingAction(ActionId))], store);
        host.SetProfiles([ProfileWithKey(Bindings.Single(FirstKey, PluginId, ActionId))]);

        // Act
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(plugin.PushedImages, Is.Empty);
    }

    [Test]
    public async Task StartAsync_ProfileBackgroundGreen_DrawsAKeyWithoutItsOwnBackgroundInGreen()
    {
        // Arrange (a green profile background and a key with a red background of its own)

        // Act
        var frames = await FramesOfAGreenProfileWithARedKeyAsync();

        // Assert (corner pixels, RGBA)
        Assert.That(frames[FirstKey].Rgba[..4], Is.EqualTo(GreenPixel));
    }

    [Test]
    public async Task StartAsync_KeyWithABackgroundOfItsOwn_DrawsItInThatColor()
    {
        // Arrange (a green profile background and a key with a red background of its own)

        // Act
        var frames = await FramesOfAGreenProfileWithARedKeyAsync();

        // Assert (corner pixels, RGBA)
        Assert.That(frames[SecondKey].Rgba[..4], Is.EqualTo(RedPixel));
    }

    private static async Task<(DeviceHostService Host, RecordingAction Action, ConcurrentBag<DeviceInputEvent> Inputs, VariableStore Store)> StartedWithInputsReportedAsync()
    {
        var action = new RecordingAction(ActionId);
        var store = new VariableStore();
        var host = new DeviceHostService([new FakeDevicePlugin()], [new TestApplicationPlugin(PluginId, action)], store);
        var inputs = new ConcurrentBag<DeviceInputEvent>();
        host.InputReceived += inputs.Add;
        host.SetProfiles([ProfileWithKey(Bindings.Single(FirstKey, PluginId, ActionId))]);
        await host.StartAsync(CancellationToken.None);
        return (host, action, inputs, store);
    }

    private static void PressBothKeys(DeviceHostService host)
    {
        host.SimulateInput(new DeviceInputEvent(FakeDevicePlugin.DeviceId, FirstKey, InputCapabilities.Press, 1));
        host.SimulateInput(new DeviceInputEvent(FakeDevicePlugin.DeviceId, SecondKey, InputCapabilities.Press, 1));
    }

    [Test]
    public async Task SimulateInput_PressOnABoundKey_RunsItsAction()
    {
        // Arrange
        var (host, action, _, store) = await StartedWithInputsReportedAsync();
        await using var _ = host;
        using var __ = store;

        // Act
        PressBothKeys(host);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(action.Executions, Has.Exactly(1).Items);
    }

    [Test]
    public async Task SimulateInput_PressOnTwoKeys_ReportsBothInputs()
    {
        // Arrange
        var (host, _, inputs, store) = await StartedWithInputsReportedAsync();
        await using var _ = host;
        using var __ = store;

        // Act
        PressBothKeys(host);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(inputs.Count, Is.EqualTo(2));
    }
}
