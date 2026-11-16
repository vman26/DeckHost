using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Models;
using DeckHost.Core.Plugins;
using DeckHost.Core.State;
using DeckHost.Core.StreamDeck;
using System.Diagnostics;
using System.IO.Compression;

namespace DeckHost.Tests;

public sealed class StreamDeckTests : IDisposable
{
    private const string PluginUuid = "com.deckhost.test";
    private const string ActionUuid = "com.deckhost.test.action";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deckhost-sd-{Guid.NewGuid():N}");

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

    [Fact]
    public void Parses_the_OpenAction_and_Elgato_archive_catalogs()
    {
        var openAction = PluginStore.ParseOpenActionCatalog("""
            { "com.elgato.counter": { "name": "Counter", "author": "Elgato", "repository": "https://github.com/elgatosf/streamdeck-counter",
              "download_url": "https://example.com/counter.streamDeckPlugin", "description": "Sample" } }
            """);
        var elgato = PluginStore.ParseElgatoArchiveCatalog("""
            [ { "id": "com.elgato.counter", "name": "Counter", "author": "Elgato", "link": "https://developer.elgato.com", "version": "1.4" },
              { "name": "no id" } ]
            """);

        var open = Assert.Single(openAction);
        Assert.Equal(("com.elgato.counter", "Counter", PluginStore.OpenActionSource, "https://example.com/counter.streamDeckPlugin"), (open.Id, open.Name, open.Source, open.DownloadUrl));
        var archived = Assert.Single(elgato);
        Assert.Equal("https://plugins.amankhanna.me/rezipped/com.elgato.counter.zip", archived.DownloadUrl);
        Assert.Equal("1.4", archived.Version);
    }

    [Fact]
    public void Merges_dial_feedback_updates()
    {
        static System.Text.Json.Nodes.JsonObject Json(string json) => System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();

        // Plain values, as most plugins send them.
        var feedback = DialFeedback.Merge(null, Json("""{ "title": "Volume", "value": "42%", "indicator": 42 }"""));
        Assert.Equal(new DialFeedback("Volume", "42%", 0.42), feedback);

        // A partial update keeps the rest; items may also be objects with a value and layout details.
        feedback = DialFeedback.Merge(feedback, Json("""{ "value": { "value": 55, "color": "#fff" }, "indicator": { "value": "130" }, "icon": "ignored" }"""));
        Assert.Equal(new DialFeedback("Volume", "55", 1d), feedback);

        // An item set to nothing is cleared.
        feedback = DialFeedback.Merge(feedback, Json("""{ "indicator": null }"""));
        Assert.Equal(new DialFeedback("Volume", "55", null), feedback);
        Assert.Equal(feedback, DialFeedback.Merge(feedback, null));
    }

    [Fact]
    public void Reads_the_manifest_and_recognizes_html_plugins()
    {
        var directory = WritePlugin("plugin.js");
        var manifest = StreamDeckManifest.Load(directory);
        Assert.Equal(PluginUuid, manifest.Uuid);
        Assert.Equal("plugin.js", manifest.CodePath);
        Assert.Null(manifest.UnsupportedReason);
        Assert.True(Assert.Single(manifest.Actions).SupportsKeypad);

        var html = StreamDeckManifest.Load(WritePlugin("index.html", uuid: "com.deckhost.html"));
        Assert.Null(html.UnsupportedReason);
        Assert.True(html.IsHtml);
        Assert.False(manifest.IsHtml);
    }

    [Fact]
    public void Installs_a_stream_deck_plugin_even_when_wrapped_in_another_archive()
    {
        using var runtime = new StreamDeckRuntime(Path.Combine(_root, "data"), Path.Combine(_root, "logs"), (_, _) => { });
        using var catalog = new PluginCatalog(Path.Combine(_root, "plugins"), [new FakeDevicePlugin()], [], [], runtime);
        catalog.Load();

        // A .zip that contains the real .streamDeckPlugin, as some downloads do.
        var inner = PackPlugin(WritePlugin("index.html"));
        var outer = Path.Combine(_root, "outer.zip");
        using (var archive = ZipFile.Open(outer, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(inner, "com.deckhost.test.streamDeckPlugin");
        }

        var entry = catalog.Install(outer);

        Assert.Equal(PluginUuid, entry.Id);
        Assert.Equal(PluginCatalog.StreamDeckSource, entry.Source);
        Assert.NotNull(entry.Error); // HTML plugin: installed, but can't run.
        Assert.True(Directory.Exists(Path.Combine(catalog.StreamDeckDirectory, PluginUuid + ".sdPlugin")));

        catalog.Uninstall(PluginUuid);
        Assert.False(Directory.Exists(Path.Combine(catalog.StreamDeckDirectory, PluginUuid + ".sdPlugin")));
    }

    [Fact]
    public async Task Runs_a_Node_plugin_over_the_Stream_Deck_protocol()
    {
        if (!IsNodeAvailable())
        {
            return; // Node.js isn't installed on this machine; the protocol can't be exercised end to end.
        }

        var logs = new List<string>();
        using var runtime = new StreamDeckRuntime(Path.Combine(_root, "data"), Path.Combine(_root, "logs"), (level, message) => { lock (logs) { logs.Add($"{level}: {message}"); } });
        using var catalog = new PluginCatalog(Path.Combine(_root, "plugins"), [new FakeDevicePlugin()], [], [], runtime);
        catalog.Load();
        var plugin = new FakeDevicePlugin();
        runtime.DevicesProvider = () => plugin.Devices;
        var entry = catalog.Install(PackPlugin(WritePlugin("plugin.js", script: TestPluginScript)));
        Assert.Null(entry.Error);

        using var store = new VariableStore();
        await using var host = new DeviceHostService([plugin], catalog.EnabledApplicationPlugins, store);
        var reference = new ActionReference(PluginUuid, "Test", ActionUuid, "Test action", InputCapabilities.None, OutputCapabilities.None);
        host.SetProfiles([new DeviceProfile("p1", "P", FakeDevicePlugin.DeviceId, [new ControlBinding("key.0", ProfileActionKind.Single, reference, new ControlAppearance(), [])])]);
        await host.StartAsync(CancellationToken.None);

        var action = catalog.EnabledApplicationPlugins.Single(candidate => candidate.Id == PluginUuid).Actions.Single();
        Assert.True(await WaitForTitleAsync(action, "Hello"), "willAppear → setTitle: " + string.Join(" | ", logs));

        host.SimulateInput(new DeviceInputEvent(FakeDevicePlugin.DeviceId, "key.0", InputCapabilities.Press, 1));
        Assert.True(await WaitForTitleAsync(action, "Pressed 1"), "keyDown → setTitle: " + string.Join(" | ", logs));

        // setSettings is stored and sent back with the next event.
        host.SimulateInput(new DeviceInputEvent(FakeDevicePlugin.DeviceId, "key.0", InputCapabilities.Press, 1));
        Assert.True(await WaitForTitleAsync(action, "Pressed 2"), "settings round trip: " + string.Join(" | ", logs));
    }

    private const string PidTitleScript = """
        const args = process.argv.slice(2);
        const value = name => args[args.indexOf(name) + 1];
        const socket = new WebSocket(`ws://127.0.0.1:${value('-port')}`);
        socket.onopen = () => socket.send(JSON.stringify({ event: value('-registerEvent'), uuid: value('-pluginUUID') }));
        socket.onmessage = message => {
          const data = JSON.parse(message.data);
          if (data.event === 'willAppear') socket.send(JSON.stringify({ event: 'setTitle', context: data.context, payload: { title: `Hello ${process.pid}` } }));
        };
        """;

    private static async Task<string?> WaitForTitleStartingAsync(IDeckAction action, string prefix, string? not = null)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var context = new CapturingRenderContext();
            action.Render(context);
            if (context.Texts.FirstOrDefault(text => text.StartsWith(prefix, StringComparison.Ordinal) && text != not) is { } title)
            {
                return title;
            }

            await Task.Delay(50);
        }

        return null;
    }

    [Fact]
    public async Task A_plugin_whose_process_dies_is_restarted_and_its_keys_come_back()
    {
        if (!IsNodeAvailable())
        {
            return;
        }

        using var runtime = new StreamDeckRuntime(Path.Combine(_root, "data"), Path.Combine(_root, "logs"), (_, _) => { }) { RestartDelays = [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100)] };
        using var catalog = new PluginCatalog(Path.Combine(_root, "plugins"), [new FakeDevicePlugin()], [], [], runtime);
        catalog.Load();
        var plugin = new FakeDevicePlugin();
        runtime.DevicesProvider = () => plugin.Devices;
        Assert.Null(catalog.Install(PackPlugin(WritePlugin("plugin.js", script: PidTitleScript))).Error);
        var changes = 0;
        catalog.Changed += (_, _) => Interlocked.Increment(ref changes);

        using var store = new VariableStore();
        await using var host = new DeviceHostService([plugin], catalog.EnabledApplicationPlugins, store);
        var reference = new ActionReference(PluginUuid, "Test", ActionUuid, "Test action", InputCapabilities.None, OutputCapabilities.None);
        host.SetProfiles([new DeviceProfile("p1", "P", FakeDevicePlugin.DeviceId, [new ControlBinding("key.0", ProfileActionKind.Single, reference, new ControlAppearance(), [])])]);
        await host.StartAsync(CancellationToken.None);
        var action = catalog.EnabledApplicationPlugins.Single(candidate => candidate.Id == PluginUuid).Actions.Single();

        var first = await WaitForTitleStartingAsync(action, "Hello ");
        Assert.NotNull(first);
        Assert.StartsWith("Runs in its own process (pid", catalog.Entries.Single(entry => entry.Id == PluginUuid).ProcessStatus);

        // The plugin's process dies: it's started again and told what's on the device.
        System.Diagnostics.Process.GetProcessById(int.Parse(first!["Hello ".Length..], System.Globalization.CultureInfo.InvariantCulture)).Kill();

        var second = await WaitForTitleStartingAsync(action, "Hello ", not: first);
        Assert.NotNull(second);
        Assert.Contains("restarted 1×", catalog.Entries.Single(entry => entry.Id == PluginUuid).ProcessStatus);
        Assert.True(changes >= 2);
    }

    /// <summary>MiraBox's older StreamDock Node.js template: port, uuid, register event and language by position.</summary>
    private const string PositionalScript = """
        const [port, uuid, registerEvent] = process.argv.slice(2);
        if (!/^\d+$/.test(port)) process.exit(1);
        const socket = new WebSocket(`ws://127.0.0.1:${port}`);
        socket.onopen = () => socket.send(JSON.stringify({ event: registerEvent, uuid }));
        socket.onmessage = message => {
          const data = JSON.parse(message.data);
          if (data.event === 'willAppear') socket.send(JSON.stringify({ event: 'setTitle', context: data.context, payload: { title: 'Positional' } }));
        };
        """;

    [Fact]
    public async Task A_plugin_that_reads_its_arguments_by_position_connects_on_the_second_try()
    {
        if (!IsNodeAvailable())
        {
            return;
        }

        var logs = new List<string>();
        using var runtime = new StreamDeckRuntime(Path.Combine(_root, "data"), Path.Combine(_root, "logs"), (_, message) => { lock (logs) { logs.Add(message); } });
        using var catalog = new PluginCatalog(Path.Combine(_root, "plugins"), [new FakeDevicePlugin()], [], [], runtime);
        catalog.Load();
        var plugin = new FakeDevicePlugin();
        runtime.DevicesProvider = () => plugin.Devices;
        Assert.Null(catalog.Install(PackPlugin(WritePlugin("plugin.js", script: PositionalScript))).Error);

        using var store = new VariableStore();
        await using var host = new DeviceHostService([plugin], catalog.EnabledApplicationPlugins, store);
        var reference = new ActionReference(PluginUuid, "Test", ActionUuid, "Test action", InputCapabilities.None, OutputCapabilities.None);
        host.SetProfiles([new DeviceProfile("p1", "P", FakeDevicePlugin.DeviceId, [new ControlBinding("key.0", ProfileActionKind.Single, reference, new ControlAppearance(), [])])]);
        await host.StartAsync(CancellationToken.None);

        var action = catalog.EnabledApplicationPlugins.Single(candidate => candidate.Id == PluginUuid).Actions.Single();
        Assert.True(await WaitForTitleAsync(action, "Positional"), string.Join(" | ", logs));
        lock (logs)
        {
            Assert.Contains(logs, message => message.Contains("older StreamDock", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_StreamDock_knob_action_is_a_dial()
    {
        var knob = new StreamDeckActionManifest("a", "A", null, null, ["Knob", "Information"], [], null, true, true);
        var both = new StreamDeckActionManifest("b", "B", null, null, ["Keypad", "Encoder"], [], null, true, true);

        Assert.True(knob.SupportsEncoder);
        Assert.Equal("Knob", knob.EncoderController);
        Assert.Equal("Encoder", both.EncoderController);
    }

    [Fact]
    public async Task A_plugin_that_keeps_crashing_is_stopped_with_a_message()
    {
        if (!IsNodeAvailable())
        {
            return;
        }

        using var runtime = new StreamDeckRuntime(Path.Combine(_root, "data"), Path.Combine(_root, "logs"), (_, _) => { }) { RestartDelays = [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)] };
        using var catalog = new PluginCatalog(Path.Combine(_root, "plugins"), [new FakeDevicePlugin()], [], [], runtime);
        catalog.Load();
        catalog.Install(PackPlugin(WritePlugin("plugin.js", script: "process.exit(1);")));

        for (var attempt = 0; attempt < 200 && runtime.ProblemOf(PluginUuid) is null; attempt++)
        {
            await Task.Delay(50);
        }

        Assert.Contains("stopped 3 times", runtime.ProblemOf(PluginUuid));
        Assert.Equal(runtime.ProblemOf(PluginUuid), catalog.Entries.Single(entry => entry.Id == PluginUuid).ProcessStatus);

        // Disabling and enabling starts it fresh.
        catalog.SetEnabled(PluginUuid, false);
        catalog.SetEnabled(PluginUuid, true);
        Assert.Null(runtime.ProblemOf(PluginUuid));
    }

    private const string TestPluginScript = """
        const args = process.argv.slice(2);
        const value = name => args[args.indexOf(name) + 1];
        const socket = new WebSocket(`ws://127.0.0.1:${value('-port')}`);
        socket.onopen = () => socket.send(JSON.stringify({ event: value('-registerEvent'), uuid: value('-pluginUUID') }));
        socket.onmessage = message => {
          const data = JSON.parse(message.data);
          const send = (event, payload) => socket.send(JSON.stringify({ event, context: data.context, payload }));
          if (data.event === 'willAppear') send('setTitle', { title: 'Hello' });
          if (data.event === 'keyDown') {
            const count = (data.payload.settings.count || 0) + 1;
            send('setSettings', { count });
            send('setTitle', { title: `Pressed ${count}` });
          }
        };
        """;

    private string WritePlugin(string codePath, string uuid = PluginUuid, string? script = null)
    {
        var directory = Path.Combine(_root, "src", Guid.NewGuid().ToString("N"), uuid + ".sdPlugin");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), $$"""
            {
              "Name": "Test", "Author": "DeckHost", "Version": "1.0", "CodePath": "{{codePath}}", "SDKVersion": 2,
              "Actions": [ { "Name": "Test action", "UUID": "{{ActionUuid}}", "Tooltip": "A test", "States": [ { "Title": "" } ], "Controllers": [ "Keypad" ] } ],
              "OS": [ { "Platform": "windows" }, { "Platform": "mac" }, { "Platform": "linux" } ]
            }
            """);
        if (script is not null)
        {
            File.WriteAllText(Path.Combine(directory, codePath), script);
        }

        return directory;
    }

    private string PackPlugin(string sdPluginDirectory)
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.streamDeckPlugin");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(sdPluginDirectory))
        {
            archive.CreateEntryFromFile(file, $"{Path.GetFileName(sdPluginDirectory)}/{Path.GetFileName(file)}");
        }

        return path;
    }

    private static async Task<bool> WaitForTitleAsync(IDeckAction action, string title)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            var context = new CapturingRenderContext();
            action.Render(context);
            if (context.Texts.Contains(title))
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static bool IsNodeAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, UseShellExecute = false });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private sealed class CapturingRenderContext : IRenderContext
    {
        public List<string> Texts { get; } = [];

        public string DeviceId => FakeDevicePlugin.DeviceId;

        public string ControlId => "key.0";

        public int Width => 72;

        public int Height => 72;

        public IVariableStore Variables { get; } = new VariableStore();

        public ActionSettings Settings => ActionSettings.Empty;

        public string ProfileId => "p1";

        public bool DrawEncodedImage(byte[] encodedImage) => true;

        public void Clear(uint argbColor)
        {
        }

        public void DrawText(string text, double x, double y, float size, uint argbColor = 0xFFFFFFFF) => Texts.Add(text);

        public void DrawCenteredText(string text, double centerX, double centerY, float size, uint argbColor = 0xFFFFFFFF) => Texts.Add(text);

        public void DrawLabel(string text, TextVerticalAlignment defaultVertical = TextVerticalAlignment.Middle, float? defaultSize = null, uint defaultColor = 0xFFFFFFFF) => Texts.Add(text);

        public void FillRoundedRect(double x, double y, double width, double height, double radius, uint argbColor)
        {
        }

        public void DrawImage(byte[] rgbaPixels, int width, int height)
        {
        }

        public bool DrawImageFile(string path) => true;
    }
}
