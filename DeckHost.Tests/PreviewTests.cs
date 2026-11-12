using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Models;
using DeckHost.Core.State;
using System.Collections.Concurrent;

namespace DeckHost.Tests;

public class PreviewTests
{
    [Fact]
    public async Task Frames_are_reported_for_the_preview_even_without_a_device()
    {
        var plugin = new FakeDevicePlugin(connected: false);
        using var store = new VariableStore();
        await using var host = new DeviceHostService([plugin], [new TestApplicationPlugin("p", new RecordingAction("a"))], store);
        var frames = new ConcurrentBag<ControlFrame>();
        host.ControlFrameRendered += frames.Add;
        host.SetProfiles([new DeviceProfile("one", "One", FakeDevicePlugin.DeviceId, [Bindings.Single("key.0", "p", "a")])]);

        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();

        Assert.Equal(["key.0", "key.1"], frames.Select(frame => frame.ControlId).Order());
        Assert.Empty(plugin.PushedImages);
    }

    [Fact]
    public async Task Profile_and_control_backgrounds_are_drawn()
    {
        var plugin = new FakeDevicePlugin();
        using var store = new VariableStore();
        await using var host = new DeviceHostService([plugin], [new TestApplicationPlugin("p", new RecordingAction("a"))], store);
        var frames = new ConcurrentDictionary<string, ControlFrame>();
        host.ControlFrameRendered += frame => frames[frame.ControlId] = frame;
        var redKey = new ControlBinding("key.1", ProfileActionKind.Single, Bindings.Reference("p", "missing"), new ControlAppearance(BackgroundColor: "#FF0000"), []);
        host.SetProfiles([new DeviceProfile("one", "One", FakeDevicePlugin.DeviceId, [redKey], Background: new ProfileBackground("#00FF00"))]);

        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();

        // Corner pixels (RGBA): key.0 shows the green profile background, key.1 its own red background.
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, frames["key.0"].Rgba[..4]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, frames["key.1"].Rgba[..4]);
    }

    [Fact]
    public async Task Simulated_input_runs_actions_and_is_reported()
    {
        var plugin = new FakeDevicePlugin();
        var action = new RecordingAction("a");
        using var store = new VariableStore();
        await using var host = new DeviceHostService([plugin], [new TestApplicationPlugin("p", action)], store);
        var inputs = new ConcurrentBag<DeviceInputEvent>();
        host.InputReceived += inputs.Add;
        host.SetProfiles([new DeviceProfile("one", "One", FakeDevicePlugin.DeviceId, [Bindings.Single("key.0", "p", "a")])]);
        await host.StartAsync(CancellationToken.None);

        host.SimulateInput(new DeviceInputEvent(FakeDevicePlugin.DeviceId, "key.0", InputCapabilities.Press, 1));
        host.SimulateInput(new DeviceInputEvent(FakeDevicePlugin.DeviceId, "key.1", InputCapabilities.Press, 1));
        await host.WhenIdleAsync();

        Assert.Single(action.Executions);
        Assert.Equal(2, inputs.Count);
    }
}
