using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Device.Elgato;
using DeckHost.Device.MiraBox;
using DeckHost.Device.Simulator;
using System.Collections.Concurrent;
using System.Text.Json;

namespace DeckHost.Tests;

/// <summary>The device simulator: the plugin's loopback endpoint and the client the simulator window uses.</summary>
public sealed class SimulatorTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string _directory = Directory.CreateTempSubdirectory("deckhost-simulator").FullName;
    private readonly RecordingContext _context = new();
    private readonly MiraBoxDevicePlugin _mirabox;
    private readonly SimulatorDevicePlugin _plugin;
    private readonly List<SimulatorClient> _clients = [];

    public SimulatorTests()
    {
        _mirabox = new MiraBoxDevicePlugin(_directory);
        _plugin = new SimulatorDevicePlugin(_directory, () => [_mirabox, _plugin!]);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        _plugin.Dispose();
        _mirabox.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void Device_descriptions_survive_the_wire()
    {
        var models = MiraBoxModels.All.Select(model => model.Describe(model.DefaultDeviceId, model.ShortName, null, false, null))
            .Concat(StreamDeckModel.All.Select(model => model.Describe($"elgato.{model.ProductId:x4}", false, null, null, null)));

        foreach (var device in models)
        {
            var message = new SimulatorMessage { Type = SimulatorMessage.Attached, Device = device };
            var back = SimulatorWire.Deserialize(SimulatorWire.Serialize(message))!.Device!;

            Assert.That(back.Controls, Is.EqualTo(device.Controls), device.Id);
            Assert.That(back.Capabilities, Is.EqualTo(device.Capabilities));
            Assert.That(back.Displays.Select(static display => (display.Id, display.Width, display.Height)), Is.EqualTo(device.Displays.Select(static display => (display.Id, display.Width, display.Height))));
            Assert.That(back.Identity, Is.EqualTo(device.Identity));
        }
    }

    [Test]
    public async Task A_simulator_lists_the_models_of_every_device_plugin_and_becomes_one()
    {
        var (client, messages) = await ConnectAsync();

        var models = await messages.NextAsync(SimulatorMessage.ModelList);
        Assert.That(models.Models!.Select(static model => model.Key), Does.Contain("deckhost.device.mirabox/mirabox.n4pro").And.Contain("deckhost.device.mirabox/mirabox.xl"));

        client.Attach("deckhost.device.mirabox/mirabox.n4pro");
        var attached = await messages.NextAsync(SimulatorMessage.Attached);
        var settings = await messages.NextAsync(SimulatorMessage.Settings);

        Assert.That((attached.Model, attached.DeviceId), Is.EqualTo(("deckhost.device.mirabox/mirabox.n4pro", "simulator.mirabox.n4pro")));
        Assert.That(attached.Device!.Controls.Select(static control => control.Id), Is.EqualTo(MiraBoxModels.N4Pro.Controls.Select(static control => control.Id)));
        Assert.That((settings.Brightness, settings.Vibrate), Is.EqualTo(((int?)100, (bool?)true)));
        var device = Expect.Single(_plugin.Devices);
        Assert.That((device.Id, device.IsConnected, device.Name), Is.EqualTo(("simulator.mirabox.n4pro", true, "Simulated MiraBox N4 Pro")));
        Assert.That(_context.Statuses, Does.Contain(("simulator.mirabox.n4pro", "Connected")));
    }

    [Test]
    public async Task Images_lights_and_the_backdrop_reach_the_simulator()
    {
        var (_, messages) = await AttachAsync("deckhost.device.mirabox/mirabox.n4pro");
        var pixels = Enumerable.Range(0, 112 * 112 * 4).Select(static index => (byte)index).ToArray();

        Assert.That(_plugin.TrySetBackdrop("simulator.mirabox.n4pro", 10, 20, 30, out _), Is.True);
        Assert.That(_plugin.TryPushImage("simulator.mirabox.n4pro", "key.0", pixels, 112, 112, out _), Is.True);
        Assert.That(_plugin.TrySetLights("simulator.mirabox.n4pro", [new LightColor("encoder.1", 255, 0, 0)], out _), Is.True);

        // Images are coalesced per control and may overtake other messages.
        var received = await messages.CollectAsync(SimulatorMessage.Backdrop, SimulatorMessage.LightsChanged, SimulatorMessage.Image);
        var (backdrop, lights, image) = (received[SimulatorMessage.Backdrop], received[SimulatorMessage.LightsChanged], received[SimulatorMessage.Image]);
        Assert.That((backdrop.R, backdrop.G, backdrop.B), Is.EqualTo(((byte)10, (byte)20, (byte)30)));
        Assert.That((image.Control, image.Width, image.Height), Is.EqualTo(("key.0", 112, 112)));
        Assert.That(Convert.FromBase64String(image.Rgba!), Is.EqualTo(pixels));
        Assert.That(Expect.Single(lights.Lights!), Is.EqualTo(new SimulatorLight("encoder.1", 255, 0, 0)));
    }

    [Test]
    public async Task Input_from_the_simulator_reaches_DeckHost_like_hardware_input()
    {
        var (client, _) = await AttachAsync("deckhost.device.mirabox/mirabox.n4pro");

        client.SendInput("encoder.2", InputCapabilities.Rotate, -1);
        client.SendInput("key.3", InputCapabilities.Press, 1);
        client.SendInput("touch.swipe", InputCapabilities.Swipe, 1);
        client.SendInput("key.3", InputCapabilities.Rotate, 1); // a key can't turn: ignored
        client.SendInput("nothing", InputCapabilities.Press, 1);

        await Until(() => _context.Inputs.Count >= 4);
        await Task.Delay(100);
        Assert.That(_context.Inputs.Select(static input => (input.ControlId, input.Input)), Is.EqualTo(new[]
        {
            ("encoder.2", InputCapabilities.Rotate),
            ("key.3", InputCapabilities.Press),
            ("touch.swipe", InputCapabilities.Swipe),
            ("key.3", InputCapabilities.LongPress),
        }));
    }

    [Test]
    public async Task Closing_the_window_unplugs_the_device_and_opening_it_again_keeps_its_id()
    {
        var (client, _) = await AttachAsync("deckhost.device.mirabox/mirabox.xl");
        await client.DisposeAsync();
        _clients.Remove(client);

        await Until(() => _plugin.Devices.Single().IsConnected == false);
        Assert.That(_plugin.TryPushImage("simulator.mirabox.xl", "key.0", new byte[4], 1, 1, out var error), Is.False);
        Assert.That(error, Is.Not.Null);

        await AttachAsync("deckhost.device.mirabox/mirabox.xl");
        var device = Expect.Single(_plugin.Devices);
        Assert.That((device.Id, device.IsConnected), Is.EqualTo(("simulator.mirabox.xl", true)));
    }

    [Test]
    public async Task Two_windows_of_one_model_are_two_devices()
    {
        await AttachAsync("deckhost.device.mirabox/mirabox.mini");
        await AttachAsync("deckhost.device.mirabox/mirabox.mini");

        Assert.That(_plugin.Devices.Select(static device => device.Id), Is.EquivalentTo(new[] { "simulator.mirabox.mini", "simulator.mirabox.mini-2" }));
    }

    [Test]
    public async Task A_wrong_token_is_refused()
    {
        await _plugin.InitializeAsync(_context, CancellationToken.None);
        var endpoint = SimulatorWire.ReadEndpoint(_directory)!;
        var client = await SimulatorClient.ConnectAsync(endpoint with { Token = "guess" }, "test");
        _clients.Add(client);
        var messages = new Inbox(client);

        var refusal = await messages.NextAsync(SimulatorMessage.Failure);

        Assert.That(refusal.Error, Does.Contain("token"));
        Assert.That(_plugin.Devices, Is.Empty);
    }

    [Test]
    public async Task Settings_go_to_the_window_and_vibration_only_where_the_model_vibrates()
    {
        var (_, messages) = await AttachAsync("deckhost.device.mirabox/mirabox.n4pro");
        await messages.NextAsync(SimulatorMessage.Settings);

        Assert.That(_plugin.GetDeviceSettingDefinitions("simulator.mirabox.n4pro").Select(static setting => setting.Key), Does.Contain(SimulatorDevicePlugin.VibrateSetting));
        Assert.That(_plugin.ApplyDeviceSettings("simulator.mirabox.n4pro", ActionSettings.Empty.With(SimulatorDevicePlugin.BrightnessSetting, "40").With(SimulatorDevicePlugin.VibrateSetting, "off")), Is.Null);
        var settings = await messages.NextAsync(SimulatorMessage.Settings);
        Assert.That((settings.Brightness, settings.Vibrate), Is.EqualTo(((int?)40, (bool?)false)));

        await AttachAsync("deckhost.device.mirabox/mirabox.293");
        Assert.That(_plugin.GetDeviceSettingDefinitions("simulator.mirabox.293").Select(static setting => setting.Key), Does.Not.Contain(SimulatorDevicePlugin.VibrateSetting));
    }

    private async Task<(SimulatorClient Client, Inbox Messages)> ConnectAsync()
    {
        await _plugin.InitializeAsync(_context, CancellationToken.None);
        var endpoint = SimulatorWire.ReadEndpoint(_directory);
        Assert.That(endpoint, Is.Not.Null);
        var client = await SimulatorClient.ConnectAsync(endpoint!, "test");
        _clients.Add(client);
        return (client, new Inbox(client));
    }

    private async Task<(SimulatorClient Client, Inbox Messages)> AttachAsync(string model)
    {
        var (client, messages) = await ConnectAsync();
        await messages.NextAsync(SimulatorMessage.ModelList);
        client.Attach(model);
        await messages.NextAsync(SimulatorMessage.Attached);
        return (client, messages);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "timed out");
            await Task.Delay(10);
        }
    }

    /// <summary>The messages a client received, in order.</summary>
    private sealed class Inbox
    {
        private readonly BlockingCollection<SimulatorMessage> _messages = [];

        public Inbox(SimulatorClient client)
        {
            client.MessageReceived += (_, message) => _messages.Add(message);
            client.Start();
        }

        public Task<SimulatorMessage> NextAsync(string type) => Task.Run(() =>
        {
            while (_messages.TryTake(out var message, Timeout))
            {
                if (message.Type == type)
                {
                    return message;
                }
            }

            Assert.Fail($"no '{type}' message");
            return null!;
        });

        public Task<Dictionary<string, SimulatorMessage>> CollectAsync(params string[] types) => Task.Run(() =>
        {
            var found = new Dictionary<string, SimulatorMessage>();
            while (found.Count < types.Length && _messages.TryTake(out var message, Timeout))
            {
                if (types.Contains(message.Type))
                {
                    found.TryAdd(message.Type, message);
                }
            }

            Assert.That(found.Keys, Is.EquivalentTo(types));
            return found;
        });
    }

    private sealed class RecordingContext : IDevicePluginContext
    {
        public ConcurrentQueue<(string ControlId, InputCapabilities Input)> Inputs { get; } = new();

        public ConcurrentQueue<(string DeviceId, string State)> Statuses { get; } = new();

        public void ReportInputEvent(string deviceId, string controlId, InputCapabilities eventType, int value) => Inputs.Enqueue((controlId, eventType));

        public void ReportConnectionStatus(string deviceId, string state, string message) => Statuses.Enqueue((deviceId, state));

        public void Log(string level, string message)
        {
        }
    }
}
