using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Plugins;
using DeckHost.Core.StreamDeck;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace DeckHost.Tests;

/// <summary>Devices of OpenAction (OpenDeck) device plugins.</summary>
public sealed class OpenActionDeviceTests : IDisposable
{
    private const string PluginUuid = "com.example.opendeck-test";
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

    [Test]
    public void A_registered_device_becomes_a_DeckHost_device_with_keys_and_knobs()
    {
        using var runtime = Runtime();
        using var plugin = Create(runtime);
        var changed = 0;
        plugin.DevicesChanged += (_, _) => changed++;

        plugin.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));

        var device = Expect.Single(plugin.Devices);
        Assert.That((device.Id, device.Name, device.IsConnected), Is.EqualTo(("openaction.xl-ABC123", "Mirabox XL", true)));
        Assert.That(device.Controls.Count(static control => control.Type == ControlType.Key), Is.EqualTo(32));
        Assert.That(device.Controls.Count(static control => control.Type == ControlType.Encoder), Is.EqualTo(2));
        Assert.That(device.FindControl("key.9")!.Bounds.Y, Is.GreaterThan(device.FindControl("key.0")!.Bounds.Y), "key 9 is on the second row");
        Assert.That(device.FindDescriptorProblems(), Is.Empty);
        Assert.That(changed, Is.EqualTo(1));
        Assert.That(_context.Statuses, Does.Contain(("openaction.xl-ABC123", "Connected")));
        Assert.That(_context.Redraws, Does.Contain("openaction.xl-ABC123"));
        // The brightness is sent at once (100 % until set).
        Assert.That(_sent.Any(sent => sent.Message["event"]?.GetValue<string>() == "setBrightness" && sent.Message["brightness"]?.GetValue<int>() == 100), Is.True);
    }

    [Test]
    public void Presses_and_turns_become_input_on_that_device()
    {
        using var runtime = Runtime();
        using var plugin = Create(runtime);
        plugin.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));

        plugin.OnMessage(PluginUuid, "keyDown", Message("""{"event":"keyDown","payload":{"device":"xl-ABC123","position":5}}"""));
        plugin.OnMessage(PluginUuid, "keyUp", Message("""{"event":"keyUp","payload":{"device":"xl-ABC123","position":5}}"""));
        plugin.OnMessage(PluginUuid, "encoderChange", Message("""{"event":"encoderChange","payload":{"device":"xl-ABC123","position":1,"ticks":-3}}"""));
        plugin.OnMessage(PluginUuid, "encoderDown", Message("""{"event":"encoderDown","payload":{"device":"xl-ABC123","position":0}}"""));
        plugin.OnMessage(PluginUuid, "encoderUp", Message("""{"event":"encoderUp","payload":{"device":"xl-ABC123","position":0}}"""));
        plugin.OnMessage(PluginUuid, "keyDown", Message("""{"event":"keyDown","payload":{"device":"someone-else","position":1}}"""));

        Assert.That(_context.Inputs.Where(static input => input.Input != InputCapabilities.LongPress).Select(static input => (input.DeviceId, input.ControlId, input.Input, input.Value)), Is.EqualTo(new[]
        {
            ("openaction.xl-ABC123", "key.5", InputCapabilities.Press, 1),
            ("openaction.xl-ABC123", "key.5", InputCapabilities.Release, 0),
            ("openaction.xl-ABC123", "encoder.1", InputCapabilities.Rotate, -1),
            ("openaction.xl-ABC123", "encoder.1", InputCapabilities.Rotate, -1),
            ("openaction.xl-ABC123", "encoder.1", InputCapabilities.Rotate, -1),
            ("openaction.xl-ABC123", "encoder.0", InputCapabilities.Press, 1),
            ("openaction.xl-ABC123", "encoder.0", InputCapabilities.Release, 0),
        }));
    }

    [Test]
    public void Images_go_to_the_plugin_as_JPEG_data_URLs_and_clearing_sends_none()
    {
        using var runtime = Runtime();
        using var plugin = Create(runtime);
        plugin.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));
        _sent.Clear();
        var red = Enumerable.Range(0, 144 * 144).SelectMany(static _ => new byte[] { 255, 0, 0, 255 }).ToArray();

        Assert.That(plugin.TryPushImage("openaction.xl-ABC123", "key.3", red, 144, 144, out var error), Is.True, error);
        Assert.That(plugin.TryPushImage("openaction.xl-ABC123", "encoder.0", red, 144, 144, out _), Is.False);
        plugin.Clear("openaction.xl-ABC123");

        var image = _sent.First().Message;
        Assert.That(image["event"]!.GetValue<string>(), Is.EqualTo("setImage"));
        Assert.That((image["device"]!.GetValue<string>(), image["controller"]!.GetValue<string>(), image["position"]!.GetValue<int>()), Is.EqualTo(("xl-ABC123", "Keypad", 3)));
        var url = image["image"]!.GetValue<string>();
        Assert.That(url, Does.StartWith("data:image/jpeg;base64,"));
        var jpeg = Convert.FromBase64String(url["data:image/jpeg;base64,".Length..]);
        Assert.That(jpeg[..2], Is.EqualTo(new byte[] { 0xFF, 0xD8 }));
        using (var decoded = SkiaSharp.SKBitmap.Decode(jpeg))
        {
            var pixel = decoded.GetPixel(70, 70);
            Assert.That(pixel.Red, Is.GreaterThan(200));
            Assert.That(pixel.Green, Is.LessThan(60));
        }

        var clear = _sent.Last().Message;
        Assert.That(clear["event"]!.GetValue<string>(), Is.EqualTo("setImage"));
        Assert.That(clear["position"], Is.Null);
        Assert.That(clear["image"], Is.Null);
    }

    [Test]
    public void A_device_stays_listed_unplugged_until_forgotten_and_comes_back_when_registered_again()
    {
        using (var runtime = Runtime())
        using (var plugin = Create(runtime))
        {
            plugin.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));
            Assert.That(plugin.GetSettingActions("openaction.xl-ABC123"), Is.Empty, "not while it's connected");

            plugin.OnMessage(PluginUuid, "deregisterDevice", Message("""{"event":"deregisterDevice","payload":"xl-ABC123"}"""));

            Assert.That(Expect.Single(plugin.Devices).IsConnected, Is.False);
            Assert.That(plugin.TryPushImage("openaction.xl-ABC123", "key.0", new byte[144 * 144 * 4], 144, 144, out _), Is.False);
        }

        using var restarted = Runtime();
        using var again = Create(restarted);
        Assert.That(Expect.Single(again.Devices), Has.Property("IsConnected").False.And.Property("Name").EqualTo("Mirabox XL"));
        Assert.That(Expect.Single(again.GetSettingActions("openaction.xl-ABC123")).Label, Is.EqualTo("Forget this device"));

        again.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));
        Assert.That(Expect.Single(again.Devices).IsConnected, Is.True);

        again.OnPluginDisconnected(PluginUuid);
        Assert.That(Expect.Single(again.Devices).IsConnected, Is.False, "the plugin stopped");
        again.RunSettingAction("openaction.xl-ABC123", OpenActionDevicePlugin.ForgetActionId);
        Assert.That(again.Devices, Is.Empty);
    }

    [Test]
    public void Brightness_is_a_device_setting_sent_to_the_plugin()
    {
        using var runtime = Runtime();
        using var plugin = Create(runtime);
        plugin.OnMessage(PluginUuid, "registerDevice", Message(XlRegistration));
        _sent.Clear();

        Assert.That(plugin.ApplyDeviceSettings("openaction.xl-ABC123", ActionSettings.Empty.With(OpenActionDevicePlugin.BrightnessSetting, "35")), Is.Null);

        var brightness = _sent.Single().Message;
        Assert.That((brightness["event"]!.GetValue<string>(), brightness["device"]!.GetValue<string>(), brightness["brightness"]!.GetValue<int>()), Is.EqualTo(("setBrightness", "xl-ABC123", 35)));
        Assert.That(plugin.GetDeviceSettings("openaction.xl-ABC123").GetInt(OpenActionDevicePlugin.BrightnessSetting, 100), Is.EqualTo(35));
    }

    [Test]
    public void The_manifest_tells_a_device_plugin_by_its_DeviceNamespace()
    {
        var directory = Path.Combine(_root, "src", PluginUuid + ".sdPlugin");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), """{ "Name": "Test", "Version": "1", "CodePathLin": "x", "CodePathWin": "x.exe", "CodePathMac": "x", "Actions": [], "DeviceNamespace": "xl" }""");

        var manifest = StreamDeckManifest.Load(directory);

        Assert.That((manifest.IsDevicePlugin, manifest.DeviceNamespace, manifest.Actions.Count), Is.EqualTo((true, "xl", 0)));
    }

    [Test]
    public void A_device_plugin_without_actions_installs_and_is_listed_as_a_device_plugin()
    {
        using var runtime = Runtime();
        using var catalog = new PluginCatalog(Path.Combine(_root, "plugins"), [], [], [], runtime);
        catalog.Load();
        var directory = Path.Combine(_root, "src", PluginUuid + ".sdPlugin");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), """{ "Name": "Mirabox XL", "Version": "0.1.4", "CodePath": "missing.exe", "Actions": [], "DeviceNamespace": "xl" }""");
        var package = Path.Combine(_root, "device.streamDeckPlugin");
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(Path.Combine(directory, "manifest.json"), PluginUuid + ".sdPlugin/manifest.json");
        }

        var entry = catalog.Install(package);

        Assert.That((entry.Id, entry.Kind), Is.EqualTo((PluginUuid, PluginKind.Device)));
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
    public async Task A_plugin_process_registers_a_device_and_gets_its_images_and_brightness()
    {
        if (!IsNodeAvailable())
        {
            return;
        }

        var logs = new List<string>();
        using var runtime = new StreamDeckRuntime(Path.Combine(_root, "data"), Path.Combine(_root, "logs"), (level, message) => { lock (logs) { logs.Add($"{level}: {message}"); } });
        var plugin = new OpenActionDevicePlugin(runtime, Path.Combine(_root, "devices"));
        await plugin.InitializeAsync(_context, CancellationToken.None);
        var directory = Path.Combine(_root, "src", PluginUuid + ".sdPlugin");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "plugin.js"), FakePluginScript);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), """{ "Name": "Fake", "Version": "1", "CodePath": "plugin.js", "Actions": [], "DeviceNamespace": "fake" }""");

        runtime.Start(StreamDeckManifest.Load(directory));

        await Until(() => plugin.Devices.Count == 1, logs);
        var device = Expect.Single(plugin.Devices);
        Assert.That((device.Id, device.Controls.Count), Is.EqualTo(("openaction.fake-1", 7)));
        await Until(() => _context.Inputs.Any(static input => input.ControlId == "encoder.0"), logs); // setBrightness arrived

        Assert.That(plugin.TryPushImage(device.Id, "key.4", new byte[144 * 144 * 4], 144, 144, out var error), Is.True, error);
        await Until(() => _context.Inputs.Any(static input => input is { ControlId: "key.1", Input: InputCapabilities.Press }), logs);

        runtime.Stop(PluginUuid);
        await Until(() => !plugin.Devices.Single().IsConnected, logs);
        plugin.Dispose();
    }

    private static async Task Until(Func<bool> condition, List<string> logs)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "timed out: " + string.Join(" | ", logs));
            await Task.Delay(25);
        }
    }

    private static bool IsNodeAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
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
