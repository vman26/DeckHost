using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Plugins;
using DeckHost.Core.StreamDeck;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace DeckHost.Tests;

/// <summary>Devices of OpenAction (OpenDeck) device plugins.</summary>
public sealed class OpenActionDeviceTests : IDisposable
{
    private const string PluginUuid = "com.example.opendeck-test";
    private const string DeviceId = "openaction.xl-ABC123";
    private const string OpenActionId = "xl-ABC123";
    private const int KeysOnTheXl = 32;
    private const int KnobsOnTheXl = 2;
    private const int ImageSize = 144;
    private const int ZeroBrightness = 100;
    private const int NewBrightness = 35;
    private const string ForgetLabel = "Forget this device";
    private const string JpegPrefix = "data:image/jpeg;base64,";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deckhost-oa-{Guid.NewGuid():N}");
    private readonly RecordingContext _context = new();
    private readonly ConcurrentQueue<(string Plugin, JsonObject Message)> _sent = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private OpenActionDevicePlugin Create(StreamDeckRuntime runtime)
    {
        var plugin = new OpenActionDevicePlugin(runtime, Path.Combine(_root, "devices"), (uuid, message) => _sent.Enqueue((uuid, message)));
        plugin.InitializeAsync(_context, CancellationToken.None).GetAwaiter().GetResult();
        return plugin;
    }

    private StreamDeckRuntime Runtime() => new(Path.Combine(_root, "data"), Path.Combine(_root, "logs"), (_, _) => { });

    private static JsonObject Message(string json) => JsonNode.Parse(json)!.AsObject();

    private const string XlRegistration = """{"event":"registerDevice","payload":{"id":"xl-ABC123","name":"Mirabox XL","rows":4,"columns":8,"encoders":2,"type":0}}""";

    private OpenActionDevicePlugin RegisteredXl(StreamDeckRuntime runtime)
    {
        var plugin = Create(runtime);
        plugin.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));
        return plugin;
    }

    private static byte[] RedImage() => [.. Enumerable.Range(0, ImageSize * ImageSize).SelectMany(static _ => new byte[] { 255, 0, 0, 255 })];

    private IEnumerable<(string DeviceId, string ControlId, InputCapabilities Input, int Value)> Presses() =>
        _context.Inputs.Where(static input => input.Input != InputCapabilities.LongPress);

    [Test]
    public void OnMessage_RegisterDevice_ListsAConnectedDeviceWithItsName()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = Create(runtime);

        // Act
        plugin.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));

        // Assert
        var device = Expect.Single(plugin.Devices);
        Assert.That((device.Id, device.Name, device.IsConnected), Is.EqualTo((DeviceId, "Mirabox XL", true)));
    }

    [Test]
    public void OnMessage_RegisterDevice_GivesItKeysAndKnobs()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = Create(runtime);

        // Act
        plugin.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));

        // Assert
        var device = Expect.Single(plugin.Devices);
        Assert.That(device.Controls.Count(static control => control.Type == ControlType.Key), Is.EqualTo(KeysOnTheXl));
        Assert.That(device.Controls.Count(static control => control.Type == ControlType.Encoder), Is.EqualTo(KnobsOnTheXl));
    }

    [Test]
    public void OnMessage_RegisterDevice_PutsKeyNineOnTheSecondRow()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = Create(runtime);

        // Act
        plugin.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));

        // Assert
        var device = Expect.Single(plugin.Devices);
        Assert.That(device.FindControl("key.9")!.Bounds.Y, Is.GreaterThan(device.FindControl("key.0")!.Bounds.Y));
    }

    [Test]
    public void OnMessage_RegisterDevice_ListsAConsistentDescriptor()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = Create(runtime);

        // Act
        plugin.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));

        // Assert
        Assert.That(Expect.Single(plugin.Devices).FindDescriptorProblems(), Is.Empty);
    }

    [Test]
    public void OnMessage_RegisterDevice_RaisesOneDevicesChanged()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = Create(runtime);
        var changed = 0;
        plugin.DevicesChanged += (_, _) => changed++;

        // Act
        plugin.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));

        // Assert
        Assert.That(changed, Is.EqualTo(1));
    }

    [Test]
    public void OnMessage_RegisterDevice_ReportsItConnectedAndAsksForARedraw()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = Create(runtime);

        // Act
        plugin.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));

        // Assert
        Assert.That(_context.Statuses, Does.Contain((DeviceId, "Connected")));
        Assert.That(_context.Redraws, Does.Contain(DeviceId));
    }

    [Test]
    public void OnMessage_RegisterDevice_SendsTheBrightnessAtOnce()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = Create(runtime);

        // Act
        plugin.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));

        // Assert (100 % until set)
        Assert.That(_sent.Any(sent => sent.Message["event"]?.GetValue<string>() == "setBrightness" && sent.Message["brightness"]?.GetValue<int>() == ZeroBrightness), Is.True);
    }

    [Test]
    public void OnMessage_KeyAndEncoderEvents_BecomeInputOnThatDevice()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = RegisteredXl(runtime);

        // Act
        plugin.OnMessage(PluginUuid, "keyDown", Message("""{"event":"keyDown","payload":{"device":"xl-ABC123","position":5}}"""));
        plugin.OnMessage(PluginUuid, "keyUp", Message("""{"event":"keyUp","payload":{"device":"xl-ABC123","position":5}}"""));
        plugin.OnMessage(PluginUuid, "encoderChange", Message("""{"event":"encoderChange","payload":{"device":"xl-ABC123","position":1,"ticks":-3}}"""));
        plugin.OnMessage(PluginUuid, "encoderDown", Message("""{"event":"encoderDown","payload":{"device":"xl-ABC123","position":0}}"""));
        plugin.OnMessage(PluginUuid, "encoderUp", Message("""{"event":"encoderUp","payload":{"device":"xl-ABC123","position":0}}"""));

        // Assert
        Assert.That(Presses().Select(static input => (input.DeviceId, input.ControlId, input.Input, input.Value)), Is.EqualTo(new[]
        {
            (DeviceId, "key.5", InputCapabilities.Press, 1),
            (DeviceId, "key.5", InputCapabilities.Release, 0),
            (DeviceId, "encoder.1", InputCapabilities.Rotate, -1),
            (DeviceId, "encoder.1", InputCapabilities.Rotate, -1),
            (DeviceId, "encoder.1", InputCapabilities.Rotate, -1),
            (DeviceId, "encoder.0", InputCapabilities.Press, 1),
            (DeviceId, "encoder.0", InputCapabilities.Release, 0),
        }));
    }

    [Test]
    public void OnMessage_KeyDownOfAnotherDevice_IsIgnored()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = RegisteredXl(runtime);

        // Act
        plugin.OnMessage(PluginUuid, "keyDown", Message("""{"event":"keyDown","payload":{"device":"someone-else","position":1}}"""));

        // Assert
        Assert.That(Presses(), Is.Empty);
    }

    private JsonObject ImageSentForKeyThree(StreamDeckRuntime runtime, out OpenActionDevicePlugin plugin)
    {
        plugin = RegisteredXl(runtime);
        _sent.Clear();
        plugin.TryPushImage(DeviceId, "key.3", RedImage(), ImageSize, ImageSize, out _);
        return _sent.First().Message;
    }

    [Test]
    public void TryPushImage_Key_SendsASetImageForThatKeypadPosition()
    {
        // Arrange
        using var runtime = Runtime();

        // Act
        var image = ImageSentForKeyThree(runtime, out var plugin);
        using var _ = plugin;

        // Assert
        Assert.That(image["event"]!.GetValue<string>(), Is.EqualTo("setImage"));
        Assert.That((image["device"]!.GetValue<string>(), image["controller"]!.GetValue<string>(), image["position"]!.GetValue<int>()), Is.EqualTo((OpenActionId, "Keypad", 3)));
    }

    [Test]
    public void TryPushImage_Key_SendsAJpegDataUrl()
    {
        // Arrange
        using var runtime = Runtime();

        // Act
        var image = ImageSentForKeyThree(runtime, out var plugin);
        using var _ = plugin;

        // Assert
        var url = image["image"]!.GetValue<string>();
        Assert.That(url, Does.StartWith(JpegPrefix));
        Assert.That(Convert.FromBase64String(url[JpegPrefix.Length..])[..2], Is.EqualTo(new byte[] { 0xFF, 0xD8 }));
    }

    [Test]
    public void TryPushImage_RedKey_DecodesAsRed()
    {
        // Arrange
        using var runtime = Runtime();
        var image = ImageSentForKeyThree(runtime, out var plugin);
        using var _ = plugin;
        var jpeg = Convert.FromBase64String(image["image"]!.GetValue<string>()[JpegPrefix.Length..]);

        // Act
        using var decoded = SkiaSharp.SKBitmap.Decode(jpeg);
        var pixel = decoded.GetPixel(70, 70);

        // Assert
        Assert.That(pixel.Red, Is.GreaterThan(200));
        Assert.That(pixel.Green, Is.LessThan(60));
    }

    [Test]
    public void TryPushImage_ValueOfAKnob_IsRefused()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = RegisteredXl(runtime);

        // Act
        var pushed = plugin.TryPushImage(DeviceId, "encoder.0", RedImage(), ImageSize, ImageSize, out _);

        // Assert
        Assert.That(pushed, Is.False);
    }

    [Test]
    public void Clear_RegisteredDevice_SendsASetImageWithoutPositionOrImage()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = RegisteredXl(runtime);
        _sent.Clear();

        // Act
        plugin.Clear(DeviceId);

        // Assert
        var clear = _sent.Last().Message;
        Assert.That(clear["event"]!.GetValue<string>(), Is.EqualTo("setImage"));
        Assert.That(clear["position"], Is.Null);
        Assert.That(clear["image"], Is.Null);
    }

    [Test]
    public void GetSettingActions_ConnectedDevice_OffersNothing()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = RegisteredXl(runtime);

        // Act
        var actions = plugin.GetSettingActions(DeviceId);

        // Assert
        Assert.That(actions, Is.Empty);
    }

    private void RegisterAndDeregister()
    {
        using var runtime = Runtime();
        using var plugin = RegisteredXl(runtime);
        plugin.OnMessage(PluginUuid, "deregisterDevice", Message("""{"event":"deregisterDevice","payload":"xl-ABC123"}"""));
    }

    [Test]
    public void OnMessage_DeregisterDevice_KeepsItListedButUnplugged()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = RegisteredXl(runtime);

        // Act
        plugin.OnMessage(PluginUuid, "deregisterDevice", Message("""{"event":"deregisterDevice","payload":"xl-ABC123"}"""));

        // Assert
        Assert.That(Expect.Single(plugin.Devices).IsConnected, Is.False);
    }

    [Test]
    public void TryPushImage_UnpluggedDevice_Fails()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = RegisteredXl(runtime);
        plugin.OnMessage(PluginUuid, "deregisterDevice", Message("""{"event":"deregisterDevice","payload":"xl-ABC123"}"""));

        // Act
        var pushed = plugin.TryPushImage(DeviceId, "key.0", new byte[ImageSize * ImageSize * 4], ImageSize, ImageSize, out _);

        // Assert
        Assert.That(pushed, Is.False);
    }

    [Test]
    public void Devices_AfterARestart_ListsTheRememberedDeviceUnplugged()
    {
        // Arrange
        RegisterAndDeregister();
        using var restarted = Runtime();

        // Act
        using var again = Create(restarted);

        // Assert
        Assert.That(Expect.Single(again.Devices), Has.Property("IsConnected").False.And.Property("Name").EqualTo("Mirabox XL"));
    }

    [Test]
    public void GetSettingActions_RememberedUnpluggedDevice_OffersToForgetIt()
    {
        // Arrange
        RegisterAndDeregister();
        using var restarted = Runtime();
        using var again = Create(restarted);

        // Act
        var action = Expect.Single(again.GetSettingActions(DeviceId));

        // Assert
        Assert.That(action.Label, Is.EqualTo(ForgetLabel));
    }

    [Test]
    public void OnMessage_RegisterDeviceAgain_ConnectsTheRememberedDevice()
    {
        // Arrange
        RegisterAndDeregister();
        using var restarted = Runtime();
        using var again = Create(restarted);

        // Act
        again.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));

        // Assert
        Assert.That(Expect.Single(again.Devices).IsConnected, Is.True);
    }

    [Test]
    public void OnPluginDisconnected_PluginStopped_MarksItsDevicesUnplugged()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = RegisteredXl(runtime);

        // Act
        plugin.OnPluginDisconnected(PluginUuid);

        // Assert
        Assert.That(Expect.Single(plugin.Devices).IsConnected, Is.False);
    }

    [Test]
    public void RunSettingAction_ForgetAnUnpluggedDevice_RemovesIt()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = RegisteredXl(runtime);
        plugin.OnPluginDisconnected(PluginUuid);

        // Act
        plugin.RunSettingAction(DeviceId, OpenActionDevicePlugin.ForgetActionId);

        // Assert
        Assert.That(plugin.Devices, Is.Empty);
    }

    [Test]
    public void ApplyDeviceSettings_Brightness_SendsItToThePlugin()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = RegisteredXl(runtime);
        _sent.Clear();

        // Act
        var error = plugin.ApplyDeviceSettings(DeviceId, ActionSettings.Empty.With(OpenActionDevicePlugin.BrightnessSetting, NewBrightness.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        // Assert
        Assert.That(error, Is.Null);
        var brightness = _sent.Single().Message;
        Assert.That((brightness["event"]!.GetValue<string>(), brightness["device"]!.GetValue<string>(), brightness["brightness"]!.GetValue<int>()), Is.EqualTo(("setBrightness", OpenActionId, NewBrightness)));
    }

    [Test]
    public void GetDeviceSettings_AfterApplyingABrightness_ReturnsIt()
    {
        // Arrange
        using var runtime = Runtime();
        using var plugin = RegisteredXl(runtime);
        plugin.ApplyDeviceSettings(DeviceId, ActionSettings.Empty.With(OpenActionDevicePlugin.BrightnessSetting, NewBrightness.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        // Act
        var brightness = plugin.GetDeviceSettings(DeviceId).GetInt(OpenActionDevicePlugin.BrightnessSetting, ZeroBrightness);

        // Assert
        Assert.That(brightness, Is.EqualTo(NewBrightness));
    }

    [Test]
    public void Load_ManifestWithADeviceNamespace_IsADevicePlugin()
    {
        // Arrange
        var directory = Path.Combine(_root, "src", PluginUuid + ".sdPlugin");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), """{ "Name": "Test", "Version": "1", "CodePathLin": "x", "CodePathWin": "x.exe", "CodePathMac": "x", "Actions": [], "DeviceNamespace": "xl" }""");

        // Act
        var manifest = StreamDeckManifest.Load(directory);

        // Assert
        Assert.That((manifest.IsDevicePlugin, manifest.DeviceNamespace, manifest.Actions.Count), Is.EqualTo((true, "xl", 0)));
    }

    private string DevicePluginPackage()
    {
        var directory = Path.Combine(_root, "src", PluginUuid + ".sdPlugin");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), """{ "Name": "Mirabox XL", "Version": "0.1.4", "CodePath": "missing.exe", "Actions": [], "DeviceNamespace": "xl" }""");
        var package = Path.Combine(_root, "device.streamDeckPlugin");
        using var archive = ZipFile.Open(package, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(Path.Combine(directory, "manifest.json"), PluginUuid + ".sdPlugin/manifest.json");
        return package;
    }

    [Test]
    public void Install_DevicePluginWithoutActions_IsListedAsADevicePlugin()
    {
        // Arrange
        using var runtime = Runtime();
        using var catalog = new PluginCatalog(Path.Combine(_root, "plugins"), [], [], [], runtime);
        catalog.Load();
        var package = DevicePluginPackage();

        // Act
        var entry = catalog.Install(package);

        // Assert
        Assert.That((entry.Id, entry.Kind), Is.EqualTo((PluginUuid, PluginKind.Device)));
    }

    [Test]
    public void Install_DevicePluginWithoutActions_IsListedOnce()
    {
        // Arrange
        using var runtime = Runtime();
        using var catalog = new PluginCatalog(Path.Combine(_root, "plugins"), [], [], [], runtime);
        catalog.Load();
        var package = DevicePluginPackage();

        // Act
        catalog.Install(package);

        // Assert
        Assert.That(catalog.Entries.Count(candidate => candidate.Id == PluginUuid), Is.EqualTo(1));
    }

    // ---------------------------------------------------------------- a real plugin process

    private const string FakePluginScript = """
        const args = process.argv.slice(2);
        const value = name => args[args.indexOf(name) + 1];
        const socket = new WebSocket(`ws://127.0.0.1:${value('-port')}`);
        const send = (event, payload) => socket.send(JSON.stringify({ event, payload }));
        socket.onopen = () => {
          socket.send(JSON.stringify({ event: value('-registerEvent'), uuid: value('-pluginUUID') }));
          send('registerDevice', { id: 'fake-1', name: 'Fake deck', rows: 2, columns: 3, encoders: 1, type: 0 });
        };
        socket.onmessage = message => {
          const data = JSON.parse(message.data);
          // Answer an image on key 4 with a press of key 1, a brightness with a turn of knob 0.
          if (data.event === 'setImage' && data.position === 4 && data.image.startsWith('data:image/jpeg;base64,')) send('keyDown', { device: 'fake-1', position: 1 });
          if (data.event === 'setBrightness') send('encoderChange', { device: 'fake-1', position: 0, ticks: 2 });
        };
        """;

    [Test]
    public async Task Start_PluginProcess_RegistersADeviceAndGetsItsImagesAndBrightness()
    {
        Assume.That(NodeJs.IsAvailable, Is.True, "Node.js is needed to run the fake plugin process");

        var logs = new List<string>();
        using var runtime = new StreamDeckRuntime(Path.Combine(_root, "data"), Path.Combine(_root, "logs"), (level, message) => { lock (logs) { logs.Add($"{level}: {message}"); } });
        var plugin = new OpenActionDevicePlugin(runtime, Path.Combine(_root, "devices"));
        await plugin.InitializeAsync(_context, CancellationToken.None);
        var directory = Path.Combine(_root, "src", PluginUuid + ".sdPlugin");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "plugin.js"), FakePluginScript);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), """{ "Name": "Fake", "Version": "1", "CodePath": "plugin.js", "Actions": [], "DeviceNamespace": "fake" }""");

        runtime.Start(StreamDeckManifest.Load(directory));

        // The first wait includes starting Node.js, which on a busy Windows runner has taken longer than 15 s.
        await Until(() => plugin.Devices.Count == 1, logs, PluginStartTimeout);
        var device = Expect.Single(plugin.Devices);
        Assert.That((device.Id, device.Controls.Count), Is.EqualTo(("openaction.fake-1", 7)));
        await Until(() => _context.Inputs.Any(static input => input.ControlId == "encoder.0"), logs); // setBrightness arrived

        Assert.That(plugin.TryPushImage(device.Id, "key.4", new byte[144 * 144 * 4], 144, 144, out var error), Is.True, error);
        await Until(() => _context.Inputs.Any(static input => input is { ControlId: "key.1", Input: InputCapabilities.Press }), logs);

        runtime.Stop(PluginUuid);
        await Until(() => !plugin.Devices.Single().IsConnected, logs);
        plugin.Dispose();
    }

    private static readonly TimeSpan PluginStartTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(15);

    private static async Task Until(Func<bool> condition, List<string> logs, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? MessageTimeout);
        while (!condition())
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "timed out: " + string.Join(" | ", logs));
            await Task.Delay(25);
        }
    }

    private sealed class RecordingContext : IDevicePluginContext
    {
        public ConcurrentQueue<(string DeviceId, string ControlId, InputCapabilities Input, int Value)> InputQueue { get; } = new();

        public IEnumerable<(string DeviceId, string ControlId, InputCapabilities Input, int Value)> Inputs => InputQueue.ToArray();

        public ConcurrentQueue<(string DeviceId, string State)> StatusQueue { get; } = new();

        public IEnumerable<(string DeviceId, string State)> Statuses => StatusQueue.ToArray();

        public ConcurrentQueue<string> RedrawQueue { get; } = new();

        public IEnumerable<string> Redraws => RedrawQueue.ToArray();

        public void ReportInputEvent(string deviceId, string controlId, InputCapabilities eventType, int value) => InputQueue.Enqueue((deviceId, controlId, eventType, value));

        public void ReportConnectionStatus(string deviceId, string state, string message) => StatusQueue.Enqueue((deviceId, state));

        public void Log(string level, string message)
        {
        }

        public void RequestRedraw(string deviceId) => RedrawQueue.Enqueue(deviceId);
    }
}

/// <summary>Closing DeckHost's window while Windows shuts down.</summary>
public class ShutdownTests
{
    [TestCase(Avalonia.Controls.WindowCloseReason.WindowClosing, false, true, true)]
    [TestCase(Avalonia.Controls.WindowCloseReason.Undefined, false, true, true)]
    [TestCase(Avalonia.Controls.WindowCloseReason.OSShutdown, false, true, false)]
    [TestCase(Avalonia.Controls.WindowCloseReason.ApplicationShutdown, false, true, false)]
    [TestCase(Avalonia.Controls.WindowCloseReason.WindowClosing, true, true, false)]
    [TestCase(Avalonia.Controls.WindowCloseReason.WindowClosing, false, false, false)]
    public void KeepsWindowOnClose_CloseReason_KeepsDeckHostInTheTrayOnlyForTheUsersOwnClose(Avalonia.Controls.WindowCloseReason reason, bool exiting, bool tray, bool keeps)
    {
        // Arrange (the close reason, whether the app is exiting and whether there is a tray icon)

        // Act
        var kept = DeckHost.App.App.KeepsWindowOnClose(reason, exiting, tray);

        // Assert
        Assert.That(kept, Is.EqualTo(keeps));
    }

    [Test]
    public void Dispose_RuntimeWithNothingRunning_DoesNotThrow()
    {
        // Arrange (each running process is waited for up to 3 s; the processes themselves are covered by the plugin tests)
        var directory = Path.Combine(Path.GetTempPath(), "deckhost-dispose-" + Guid.NewGuid().ToString("N"));
        var runtime = new StreamDeckRuntime(Path.Combine(directory, "data"), Path.Combine(directory, "logs"), (_, _) => { });

        // Act
        var dispose = runtime.Dispose;

        // Assert
        Assert.DoesNotThrow(() => dispose());
        Directory.Delete(directory, recursive: true);
    }
}
