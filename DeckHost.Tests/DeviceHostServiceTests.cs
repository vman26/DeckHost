using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.State;

namespace DeckHost.Tests;

public class DeviceHostServiceTests
{
    private static (DeviceHostService Host, FakeDevicePlugin Plugin, RecordingAction Action, VariableStore Store) Create(RgbFeedback? light = null, bool connected = true)
    {
        var plugin = new FakeDevicePlugin(connected);
        var action = new RecordingAction("a");
        var store = new VariableStore();
        var host = new DeviceHostService([plugin], [new TestApplicationPlugin("p", action)], store);
        host.SetProfiles([
            new DeviceProfile("profile", "Profile", FakeDevicePlugin.DeviceId,
            [
                Bindings.Single("key.0", "p", "a"),
                Bindings.Single("encoder.0", "p", "a", light),
            ]),
        ]);
        return (host, plugin, action, store);
    }

    [Fact]
    public async Task Start_pushes_every_control_with_an_image_once()
    {
        var (host, plugin, _, store) = Create();
        await using var _ = host;
        using var __ = store;

        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();

        Assert.Equal(["key.0", "key.1"], plugin.PushedImages.Order());
    }

    [Fact]
    public async Task Input_runs_the_action_off_the_calling_thread_and_pushes_only_changed_controls()
    {
        var (host, plugin, action, store) = Create();
        await using var _ = host;
        using var __ = store;
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        plugin.PushedImages.Clear();

        var callingThread = Environment.CurrentManagedThreadId;
        int? executionThread = null;
        action.OnExecute = context =>
        {
            executionThread = Environment.CurrentManagedThreadId;
            context.Variables.Publish($"devices.{context.DeviceId}.{context.ControlId}.executionCount", action.Executions.Count);
        };

        plugin.Context!.ReportInputEvent(FakeDevicePlugin.DeviceId, "key.0", InputCapabilities.Press, 1);
        await host.WhenIdleAsync();

        Assert.Single(action.Executions);
        Assert.NotEqual(callingThread, executionThread);
        Assert.Equal(["key.0"], plugin.PushedImages);

        // Nothing changed visually: no further pushes.
        plugin.PushedImages.Clear();
        host.SetActiveProfile("profile");
        await host.WhenIdleAsync();
        Assert.Empty(plugin.PushedImages);
    }

    [Fact]
    public async Task Light_feedback_sets_active_color_then_returns_to_idle()
    {
        var (host, plugin, _, store) = Create(new RgbFeedback("FF0000", "0000FF", 50));
        await using var _ = host;
        using var __ = store;
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        Assert.Contains(("encoder.0", (byte)0, (byte)0, (byte)255), plugin.Lights);
        plugin.Lights.Clear();

        plugin.Context!.ReportInputEvent(FakeDevicePlugin.DeviceId, "encoder.0", InputCapabilities.Rotate, 1);
        await host.WhenIdleAsync();
        Assert.Equal([("encoder.0", (byte)255, (byte)0, (byte)0)], plugin.Lights);

        await Task.Delay(300);
        await host.WhenIdleAsync();
        Assert.Equal(("encoder.0", (byte)0, (byte)0, (byte)255), plugin.Lights.Last());
    }

    [Fact]
    public async Task Disconnected_devices_are_not_rendered_until_they_connect()
    {
        var (host, plugin, _, store) = Create(connected: false);
        await using var _ = host;
        using var __ = store;
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        Assert.Empty(plugin.PushedImages);

        var devicesChanged = new TaskCompletionSource();
        host.DevicesChanged += () => devicesChanged.TrySetResult();
        plugin.SetConnected(true);
        await devicesChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.WhenIdleAsync();

        Assert.Equal(2, plugin.PushedImages.Count);
        Assert.True(Assert.Single(host.Devices).IsConnected);
    }

    [Fact]
    public async Task Dispose_clears_the_device_and_disposes_plugins()
    {
        var (host, plugin, _, store) = Create();
        using var __ = store;
        await host.StartAsync(CancellationToken.None);

        await host.DisposeAsync();

        Assert.Equal(1, plugin.ClearCount);
        Assert.True(plugin.Disposed);
    }

    [Fact]
    public async Task Log_buffer_is_bounded()
    {
        var (host, plugin, _, store) = Create();
        await using var _ = host;
        using var __ = store;
        await host.StartAsync(CancellationToken.None);

        for (var i = 0; i < 2000; i++)
        {
            plugin.Context!.Log("Info", $"message {i}");
        }

        Assert.True(host.RuntimeLogs.Count <= 500);
        Assert.Equal("message 1999", host.RuntimeLogs.Last().Message);
    }
}
