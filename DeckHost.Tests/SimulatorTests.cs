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
    private const string N4ProModel = "deckhost.device.mirabox/mirabox.n4pro";
    private const string XlModel = "deckhost.device.mirabox/mirabox.xl";
    private const string MiniModel = "deckhost.device.mirabox/mirabox.mini";
    private const string Model293 = "deckhost.device.mirabox/mirabox.293";
    private const string N4ProDevice = "simulator.mirabox.n4pro";
    private const string XlDevice = "simulator.mirabox.xl";
    private const int KeySize = 112;
    private const int DefaultBrightness = 100;
    private const int NewBrightness = 40;
    private const int InputsThatReachDeckHost = 4;
    private const int InputSettleMilliseconds = 100;

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

    private static IReadOnlyList<DeviceDefinition> EveryModel() =>
    [
        .. MiraBoxModels.All.Select(model => model.Describe(model.DefaultDeviceId, model.ShortName, null, false, null)),
        .. StreamDeckModel.All.Select(model => model.Describe($"elgato.{model.ProductId:x4}", false, null, null, null)),
    ];

    private static DeviceDefinition AcrossTheWire(DeviceDefinition device) =>
        SimulatorWire.Deserialize(SimulatorWire.Serialize(new SimulatorMessage { Type = SimulatorMessage.Attached, Device = device }))!.Device!;

    private static IReadOnlyList<string> ModelsWhoseControlsChanged() =>
        [.. EveryModel().Where(device => !AcrossTheWire(device).Controls.SequenceEqual(device.Controls)).Select(static device => device.Id)];

    private static IReadOnlyList<string> ModelsWhoseCapabilitiesChanged() =>
        [.. EveryModel().Where(device => AcrossTheWire(device).Capabilities != device.Capabilities).Select(static device => device.Id)];

    private static IReadOnlyList<string> ModelsWhoseDisplaysChanged() =>
        [.. EveryModel().Where(device => !AcrossTheWire(device).Displays.Select(static display => (display.Id, display.Width, display.Height))
            .SequenceEqual(device.Displays.Select(static display => (display.Id, display.Width, display.Height)))).Select(static device => device.Id)];

    private static IReadOnlyList<string> ModelsWhoseIdentityChanged() =>
        [.. EveryModel().Where(device => AcrossTheWire(device).Identity != device.Identity).Select(static device => device.Id)];

    [Test]
    public void Deserialize_EveryModelsControls_SurviveTheWire()
    {
        // Arrange (every MiraBox and Stream Deck model)

        // Act
        var changed = ModelsWhoseControlsChanged();

        // Assert
        Assert.That(changed, Is.Empty);
    }

    [Test]
    public void Deserialize_EveryModelsCapabilities_SurviveTheWire()
    {
        // Arrange (every MiraBox and Stream Deck model)

        // Act
        var changed = ModelsWhoseCapabilitiesChanged();

        // Assert
        Assert.That(changed, Is.Empty);
    }

    [Test]
    public void Deserialize_EveryModelsDisplays_SurviveTheWire()
    {
        // Arrange (every MiraBox and Stream Deck model)

        // Act
        var changed = ModelsWhoseDisplaysChanged();

        // Assert
        Assert.That(changed, Is.Empty);
    }

    [Test]
    public void Deserialize_EveryModelsIdentity_SurvivesTheWire()
    {
        // Arrange (every MiraBox and Stream Deck model)

        // Act
        var changed = ModelsWhoseIdentityChanged();

        // Assert
        Assert.That(changed, Is.Empty);
    }

    [Test]
    public async Task Connect_Simulator_ListsTheModelsOfEveryDevicePlugin()
    {
        // Arrange
        var (_, messages) = await ConnectAsync();

        // Act
        var models = await messages.NextAsync(SimulatorMessage.ModelList);

        // Assert
        Assert.That(models.Models!.Select(static model => model.Key), Does.Contain(N4ProModel).And.Contain(XlModel));
    }

    private async Task<(SimulatorMessage Attached, SimulatorMessage Settings)> AttachedN4ProAsync()
    {
        var (client, messages) = await ConnectAsync();
        await messages.NextAsync(SimulatorMessage.ModelList);
        client.Attach(N4ProModel);
        return (await messages.NextAsync(SimulatorMessage.Attached), await messages.NextAsync(SimulatorMessage.Settings));
    }

    [Test]
    public async Task Attach_Model_BecomesADeviceOfThatModel()
    {
        // Arrange (a connected simulator window)

        // Act
        var (attached, _) = await AttachedN4ProAsync();

        // Assert
        Assert.That((attached.Model, attached.DeviceId), Is.EqualTo((N4ProModel, N4ProDevice)));
        Assert.That(attached.Device!.Controls.Select(static control => control.Id), Is.EqualTo(MiraBoxModels.N4Pro.Controls.Select(static control => control.Id)));
    }

    [Test]
    public async Task Attach_Model_SendsTheDefaultSettings()
    {
        // Arrange (a connected simulator window)

        // Act
        var (_, settings) = await AttachedN4ProAsync();

        // Assert
        Assert.That((settings.Brightness, settings.Vibrate), Is.EqualTo(((int?)DefaultBrightness, (bool?)true)));
    }

    [Test]
    public async Task Attach_Model_ListsAConnectedSimulatedDevice()
    {
        // Arrange (a connected simulator window)

        // Act
        await AttachedN4ProAsync();

        // Assert
        var device = Expect.Single(_plugin.Devices);
        Assert.That((device.Id, device.IsConnected, device.Name), Is.EqualTo((N4ProDevice, true, "Simulated MiraBox N4 Pro")));
    }

    [Test]
    public async Task Attach_Model_ReportsTheDeviceConnected()
    {
        // Arrange (a connected simulator window)

        // Act
        await AttachedN4ProAsync();

        // Assert
        Assert.That(_context.Statuses, Does.Contain((N4ProDevice, "Connected")));
    }

    private async Task<IReadOnlyDictionary<string, SimulatorMessage>> SendBackdropImageAndLightAsync(byte[] pixels)
    {
        var (_, messages) = await AttachAsync(N4ProModel);
        _plugin.TrySetBackdrop(N4ProDevice, 10, 20, 30, out _);
        _plugin.TryPushImage(N4ProDevice, "key.0", pixels, KeySize, KeySize, out _);
        _plugin.TrySetLights(N4ProDevice, [new LightColor("encoder.1", 255, 0, 0)], out _);

        // Images are coalesced per control and may overtake other messages.
        return await messages.CollectAsync(SimulatorMessage.Backdrop, SimulatorMessage.LightsChanged, SimulatorMessage.Image);
    }

    private static byte[] KeyPixels() => [.. Enumerable.Range(0, KeySize * KeySize * 4).Select(static index => (byte)index)];

    [Test]
    public async Task TrySetBackdrop_Color_ReachesTheSimulator()
    {
        // Arrange
        var pixels = KeyPixels();

        // Act
        var received = await SendBackdropImageAndLightAsync(pixels);

        // Assert
        var backdrop = received[SimulatorMessage.Backdrop];
        Assert.That((backdrop.R, backdrop.G, backdrop.B), Is.EqualTo(((byte)10, (byte)20, (byte)30)));
    }

    [Test]
    public async Task TryPushImage_KeyImage_ReachesTheSimulatorWithItsPixels()
    {
        // Arrange
        var pixels = KeyPixels();

        // Act
        var received = await SendBackdropImageAndLightAsync(pixels);

        // Assert
        var image = received[SimulatorMessage.Image];
        Assert.That((image.Control, image.Width, image.Height), Is.EqualTo(("key.0", KeySize, KeySize)));
        Assert.That(image.Rgba, Is.EqualTo(pixels));
    }

    [Test]
    public async Task TrySetLights_OneColor_ReachesTheSimulator()
    {
        // Arrange
        var pixels = KeyPixels();

        // Act
        var received = await SendBackdropImageAndLightAsync(pixels);

        // Assert
        Assert.That(Expect.Single(received[SimulatorMessage.LightsChanged].Lights!), Is.EqualTo(new SimulatorLight("encoder.1", 255, 0, 0)));
    }

    [Test]
    public async Task SendInput_FromTheSimulator_ReachesDeckHostLikeHardwareInput()
    {
        // Arrange
        var (client, _) = await AttachAsync(N4ProModel);

        // Act
        client.SendInput("encoder.2", InputCapabilities.Rotate, -1);
        client.SendInput("key.3", InputCapabilities.Press, 1);
        client.SendInput("touch.swipe", InputCapabilities.Swipe, 1);
        client.SendInput("key.3", InputCapabilities.Rotate, 1); // a key can't turn: ignored
        client.SendInput("nothing", InputCapabilities.Press, 1);
        await Until(() => _context.Inputs.Count >= InputsThatReachDeckHost);
        await Task.Delay(InputSettleMilliseconds);

        // Assert
        Assert.That(_context.Inputs.Select(static input => (input.ControlId, input.Input)), Is.EqualTo(new[]
        {
            ("encoder.2", InputCapabilities.Rotate),
            ("key.3", InputCapabilities.Press),
            ("touch.swipe", InputCapabilities.Swipe),
            ("key.3", InputCapabilities.LongPress),
        }));
    }

    private async Task AttachAndCloseTheXlWindowAsync()
    {
        var (client, _) = await AttachAsync(XlModel);
        await client.DisposeAsync();
        _clients.Remove(client);
        await Until(() => _plugin.Devices.Count == 0);
    }

    [Test]
    public async Task DisposeAsync_ClosingTheWindow_RemovesTheDevice()
    {
        // Arrange
        var (client, _) = await AttachAsync(XlModel);

        // Act
        await client.DisposeAsync();
        _clients.Remove(client);

        // Assert
        await Until(() => _plugin.Devices.Count == 0);
        Assert.That(_plugin.Devices, Is.Empty);
    }

    [Test]
    public async Task TryPushImage_AfterTheWindowWasClosed_FailsWithAnError()
    {
        // Arrange
        await AttachAndCloseTheXlWindowAsync();

        // Act
        var pushed = _plugin.TryPushImage(XlDevice, "key.0", new byte[4], 1, 1, out var error);

        // Assert
        Assert.That(pushed, Is.False);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public async Task Attach_SameModelAgainAfterClosing_BringsTheSameIdBack()
    {
        // Arrange
        await AttachAndCloseTheXlWindowAsync();

        // Act
        await AttachAsync(XlModel);

        // Assert
        var device = Expect.Single(_plugin.Devices);
        Assert.That((device.Id, device.IsConnected), Is.EqualTo((XlDevice, true)));
    }

    [Test]
    public async Task SetEnabled_SwitchedOff_RemovesTheDevicesAndTheEndpoint()
    {
        // Arrange
        await AttachAsync(N4ProModel);

        // Act
        _plugin.SetEnabled(false);
        await Until(() => _plugin.Devices.Count == 0);

        // Assert
        Assert.That(SimulatorWire.ReadEndpoint(_directory), Is.Null);
        Assert.That(_plugin.Port, Is.Zero);
    }

    [Test]
    public async Task SetEnabled_SwitchedOffAndOnAgain_AcceptsAWindowAgain()
    {
        // Arrange
        await AttachAsync(N4ProModel);
        _plugin.SetEnabled(false);
        await Until(() => _plugin.Devices.Count == 0);

        // Act
        _plugin.SetEnabled(true);
        await AttachAsync(N4ProModel);

        // Assert
        Assert.That(SimulatorWire.ReadEndpoint(_directory), Is.Not.Null);
        Assert.That(Expect.Single(_plugin.Devices).IsConnected, Is.True);
    }

    [Test]
    public async Task InitializeAsync_SwitchedOffFromTheStart_HasNoEndpoint()
    {
        // Arrange
        var offFolder = Path.Combine(_directory, "off");
        using var plugin = new SimulatorDevicePlugin(offFolder, () => [_mirabox], enabled: false);

        // Act
        await plugin.InitializeAsync(_context, CancellationToken.None);

        // Assert
        Assert.That(SimulatorWire.ReadEndpoint(offFolder), Is.Null);
        Assert.That(plugin.Port, Is.Zero);
    }

    [Test]
    public async Task Attach_TwoWindowsOfOneModel_AreTwoDevices()
    {
        // Arrange
        await AttachAsync(MiniModel);

        // Act
        await AttachAsync(MiniModel);

        // Assert
        Assert.That(_plugin.Devices.Select(static device => device.Id), Is.EquivalentTo(new[] { "simulator.mirabox.mini", "simulator.mirabox.mini-2" }));
    }

    [Test]
    public async Task ConnectAsync_WrongToken_IsRefusedWithAMessageAboutTheToken()
    {
        // Arrange
        await _plugin.InitializeAsync(_context, CancellationToken.None);
        var endpoint = SimulatorWire.ReadEndpoint(_directory)!;
        var client = await SimulatorClient.ConnectAsync(endpoint with { Token = "guess" }, "test");
        _clients.Add(client);
        var messages = new Inbox(client);

        // Act
        var refusal = await messages.NextAsync(SimulatorMessage.Failure);

        // Assert
        Assert.That(refusal.Error, Does.Contain("token"));
    }

    [Test]
    public async Task ConnectAsync_WrongToken_ListsNoDevice()
    {
        // Arrange
        await _plugin.InitializeAsync(_context, CancellationToken.None);
        var endpoint = SimulatorWire.ReadEndpoint(_directory)!;
        var client = await SimulatorClient.ConnectAsync(endpoint with { Token = "guess" }, "test");
        _clients.Add(client);
        var messages = new Inbox(client);
        await messages.NextAsync(SimulatorMessage.Failure);

        // Act
        var devices = _plugin.Devices;

        // Assert
        Assert.That(devices, Is.Empty);
    }

    [Test]
    public async Task GetDeviceSettingDefinitions_ModelThatVibrates_OffersVibration()
    {
        // Arrange
        await AttachAsync(N4ProModel);

        // Act
        var keys = _plugin.GetDeviceSettingDefinitions(N4ProDevice).Select(static setting => setting.Key);

        // Assert
        Assert.That(keys, Does.Contain(SimulatorDevicePlugin.VibrateSetting));
    }

    [Test]
    public async Task GetDeviceSettingDefinitions_ModelThatDoesNotVibrate_OffersNoVibration()
    {
        // Arrange
        await AttachAsync(Model293);

        // Act
        var keys = _plugin.GetDeviceSettingDefinitions("simulator.mirabox.293").Select(static setting => setting.Key);

        // Assert
        Assert.That(keys, Does.Not.Contain(SimulatorDevicePlugin.VibrateSetting));
    }

    [Test]
    public async Task ApplyDeviceSettings_BrightnessAndVibration_GoToTheWindow()
    {
        // Arrange
        var (_, messages) = await AttachAsync(N4ProModel);
        await messages.NextAsync(SimulatorMessage.Settings);
        var settings = ActionSettings.Empty.With(SimulatorDevicePlugin.BrightnessSetting, NewBrightness.ToString(System.Globalization.CultureInfo.InvariantCulture)).With(SimulatorDevicePlugin.VibrateSetting, "off");

        // Act
        var error = _plugin.ApplyDeviceSettings(N4ProDevice, settings);
        var sent = await messages.NextAsync(SimulatorMessage.Settings);

        // Assert
        Assert.That(error, Is.Null);
        Assert.That((sent.Brightness, sent.Vibrate), Is.EqualTo(((int?)NewBrightness, (bool?)false)));
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
