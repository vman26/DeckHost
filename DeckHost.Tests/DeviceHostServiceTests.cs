using DeckHost.Contracts;
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

    [Test]
    public async Task The_host_counts_inputs_and_images_per_device()
    {
        var (host, plugin, _, store) = Create();
        await using var _ = host;
        using var __ = store;

        await host.StartAsync(CancellationToken.None);
        plugin.Context!.ReportInputEvent(FakeDevicePlugin.DeviceId, "key.0", InputCapabilities.Press, 1);
        await host.WhenIdleAsync();

        var statistics = Expect.Single(host.DeviceStatistics);
        Assert.That(statistics.DeviceId, Is.EqualTo(FakeDevicePlugin.DeviceId));
        Assert.That(statistics.Inputs, Is.EqualTo(1));
        Assert.That(statistics.LastInput, Is.Not.Null);
        Assert.That(statistics.FramesSent >= 2, Is.True);
        Assert.That(statistics.FrameFailures, Is.EqualTo(0));
    }

    [Test]
    public async Task Start_pushes_every_control_with_an_image_once()
    {
        var (host, plugin, _, store) = Create();
        await using var _ = host;
        using var __ = store;

        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();

        Assert.That(plugin.PushedImages.Order(), Is.EqualTo(new[] { "key.0", "key.1" }));
    }

    [Test]
    public async Task Input_is_queued_for_the_worker_and_pushes_only_changed_controls()
    {
        var (host, plugin, action, store) = Create();
        await using var _ = host;
        using var __ = store;
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        plugin.PushedImages.Clear();

        // The action can't finish until the report call has returned. A host that ran the action
        // inside ReportInputEvent (on the SDK's callback thread) would time out here. Comparing
        // thread ids instead doesn't work: this test and the worker both run on pool threads.
        using var reportReturned = new ManualResetEventSlim();
        var ranAfterReportReturned = false;
        action.OnExecute = context =>
        {
            ranAfterReportReturned = reportReturned.Wait(TimeSpan.FromSeconds(5));
            context.Variables.Publish(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.ExecutionCount), action.Executions.Count);
        };

        plugin.Context!.ReportInputEvent(FakeDevicePlugin.DeviceId, "key.0", InputCapabilities.Press, 1);
        reportReturned.Set();
        await host.WhenIdleAsync();

        Assert.That(action.Executions, Has.Exactly(1).Items);
        Assert.That(ranAfterReportReturned, Is.True);
        Assert.That(plugin.PushedImages, Is.EqualTo(new[] { "key.0" }));

        // Nothing changed visually: no further pushes.
        plugin.PushedImages.Clear();
        host.SetActiveProfile("profile");
        await host.WhenIdleAsync();
        Assert.That(plugin.PushedImages, Is.Empty);
    }

    [Test]
    public async Task Light_feedback_sets_active_color_then_returns_to_idle()
    {
        var (host, plugin, _, store) = Create(new RgbFeedback("FF0000", "0000FF", 50));
        await using var _ = host;
        using var __ = store;
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        Assert.That(plugin.Lights, Does.Contain(("encoder.0", (byte)0, (byte)0, (byte)255)));
        plugin.Lights.Clear();

        plugin.Context!.ReportInputEvent(FakeDevicePlugin.DeviceId, "encoder.0", InputCapabilities.Rotate, 1);
        await host.WhenIdleAsync();
        Assert.That(plugin.Lights, Is.EqualTo(new[] { ("encoder.0", (byte)255, (byte)0, (byte)0) }));

        await Task.Delay(300);
        await host.WhenIdleAsync();
        Assert.That(plugin.Lights.Last(), Is.EqualTo(("encoder.0", (byte)0, (byte)0, (byte)255)));
    }

    [Test]
    public async Task Disconnected_devices_are_not_rendered_until_they_connect()
    {
        var (host, plugin, _, store) = Create(connected: false);
        await using var _ = host;
        using var __ = store;
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();
        Assert.That(plugin.PushedImages, Is.Empty);

        var devicesChanged = new TaskCompletionSource();
        host.DevicesChanged += () => devicesChanged.TrySetResult();
        plugin.SetConnected(true);
        await devicesChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.WhenIdleAsync();

        Assert.That(plugin.PushedImages.Count, Is.EqualTo(2));
        Assert.That(Expect.Single(host.Devices).IsConnected, Is.True);
    }

    [Test]
    public async Task Dispose_clears_the_device_and_disposes_plugins()
    {
        var (host, plugin, _, store) = Create();
        using var __ = store;
        await host.StartAsync(CancellationToken.None);

        await host.DisposeAsync();

        Assert.That(plugin.ClearCount, Is.EqualTo(1));
        Assert.That(plugin.Disposed, Is.True);
    }

    [Test]
    public async Task Log_buffer_is_bounded()
    {
        var (host, plugin, _, store) = Create();
        await using var _ = host;
        using var __ = store;
        await host.StartAsync(CancellationToken.None);

        // Let the worker finish its start-up render: it writes log lines of its own.
        await host.WhenIdleAsync();

        for (var i = 0; i < 2000; i++)
        {
            plugin.Context!.Log("Info", $"message {i}");
        }

        Assert.That(host.RuntimeLogs.Count <= 500, Is.True);
        Assert.That(host.RuntimeLogs.Last().Message, Is.EqualTo("message 1999"));
    }
}
