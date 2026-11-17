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

    private const string OpenActionCatalog = """
        { "com.elgato.counter": { "name": "Counter", "author": "Elgato", "repository": "https://github.com/elgatosf/streamdeck-counter",
          "download_url": "https://example.com/counter.streamDeckPlugin", "description": "Sample" } }
        """;

    private const string ElgatoArchive = """
        [ { "id": "com.elgato.counter", "name": "Counter", "author": "Elgato", "link": "https://developer.elgato.com", "version": "1.4" },
          { "name": "no id" } ]
        """;

    private const string HelloTitle = "Hello";
    private const string PluginScriptName = "plugin.js";
    private const string HtmlPage = "index.html";
    private const string KeyId = "key.0";

    [Test]
    public void ParseOpenActionCatalog_AnEntry_ReadsItsFieldsAndDownload()
    {
        // Act
        var open = Expect.Single(PluginStore.ParseOpenActionCatalog(OpenActionCatalog));

        // Assert
        Assert.That((open.Id, open.Name, open.Source, open.DownloadUrl), Is.EqualTo(("com.elgato.counter", "Counter", PluginStore.OpenActionSource, "https://example.com/counter.streamDeckPlugin")));
    }

    [Test]
    public void ParseElgatoArchiveCatalog_AnEntryWithAnId_GetsTheArchivesDownloadAndVersion()
    {
        // Act
        var archived = Expect.Single(PluginStore.ParseElgatoArchiveCatalog(ElgatoArchive));

        // Assert
        Assert.That(archived.DownloadUrl, Is.EqualTo("https://plugins.amankhanna.me/rezipped/com.elgato.counter.zip"));
        Assert.That(archived.Version, Is.EqualTo("1.4"));
    }

    private static System.Text.Json.Nodes.JsonObject Json(string json) => System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();

    [Test]
    public void Merge_PlainDialFeedbackValues_AreKept()
    {
        // Act
        var feedback = DialFeedback.Merge(null, Json("""{ "title": "Volume", "value": "42%", "indicator": 42 }"""));

        // Assert
        Assert.That(feedback, Is.EqualTo(new DialFeedback("Volume", "42%", 0.42)));
    }

    [Test]
    public void Merge_APartialUpdateWithObjectItems_KeepsTheRestAndClampsTheIndicator()
    {
        // Arrange
        var feedback = DialFeedback.Merge(null, Json("""{ "title": "Volume", "value": "42%", "indicator": 42 }"""));

        // Act (items may also be objects with a value and layout details)
        feedback = DialFeedback.Merge(feedback, Json("""{ "value": { "value": 55, "color": "#fff" }, "indicator": { "value": "130" }, "icon": "ignored" }"""));

        // Assert
        Assert.That(feedback, Is.EqualTo(new DialFeedback("Volume", "55", 1d)));
    }

    [Test]
    public void Merge_AnItemSetToNothing_IsCleared()
    {
        // Arrange
        var feedback = DialFeedback.Merge(null, Json("""{ "title": "Volume", "value": "55", "indicator": 1 }"""));

        // Act
        feedback = DialFeedback.Merge(feedback, Json("""{ "indicator": null }"""));

        // Assert
        Assert.That(feedback, Is.EqualTo(new DialFeedback("Volume", "55", null)));
    }

    [Test]
    public void Merge_NoUpdate_KeepsTheFeedback()
    {
        // Arrange
        var feedback = new DialFeedback("Volume", "55", null);

        // Act
        var merged = DialFeedback.Merge(feedback, null);

        // Assert
        Assert.That(merged, Is.EqualTo(feedback));
    }

    [Test]
    public void Load_ACodePathPlugin_ReadsItsManifestAndIsNotHtml()
    {
        // Arrange
        var directory = WritePlugin(PluginScriptName);

        // Act
        var manifest = StreamDeckManifest.Load(directory);

        // Assert
        Assert.That(manifest.Uuid, Is.EqualTo(PluginUuid));
        Assert.That(manifest.CodePath, Is.EqualTo(PluginScriptName));
        Assert.That(manifest.UnsupportedReason, Is.Null);
        Assert.That(Expect.Single(manifest.Actions).SupportsKeypad, Is.True);
        Assert.That(manifest.IsHtml, Is.False);
    }

    [Test]
    public void Load_AnHtmlPlugin_IsRecognizedAsHtmlAndSupported()
    {
        // Arrange
        var directory = WritePlugin(HtmlPage, uuid: "com.deckhost.html");

        // Act
        var html = StreamDeckManifest.Load(directory);

        // Assert
        Assert.That(html.UnsupportedReason, Is.Null);
        Assert.That(html.IsHtml, Is.True);
    }

    private PluginEntry InstallWrapped(PluginCatalog catalog)
    {
        // A .zip that contains the real .streamDeckPlugin, as some downloads do.
        var inner = PackPlugin(WritePlugin(HtmlPage));
        var outer = Path.Combine(_root, "outer.zip");
        using (var archive = ZipFile.Open(outer, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(inner, "com.deckhost.test.streamDeckPlugin");
        }

        return catalog.Install(outer);
    }

    private PluginCatalog NewCatalog(StreamDeckRuntime runtime)
    {
        var catalog = new PluginCatalog(Path.Combine(_root, "plugins"), [new FakeDevicePlugin()], [], [], runtime);
        catalog.Load();
        return catalog;
    }

    private StreamDeckRuntime NewRuntime(Action<string, string>? log = null, params TimeSpan[] restartDelays)
    {
        var runtime = new StreamDeckRuntime(Path.Combine(_root, "data"), Path.Combine(_root, "logs"), log ?? ((_, _) => { }));
        if (restartDelays.Length > 0)
        {
            runtime.RestartDelays = restartDelays;
        }

        return runtime;
    }

    [Test]
    public void Install_AStreamDeckPluginWrappedInAnotherArchive_InstallsTheInnerOne()
    {
        // Arrange
        using var runtime = NewRuntime();
        using var catalog = NewCatalog(runtime);

        // Act
        var entry = InstallWrapped(catalog);

        // Assert
        Assert.That(entry.Id, Is.EqualTo(PluginUuid));
        Assert.That(entry.Source, Is.EqualTo(PluginCatalog.StreamDeckSource));
        Assert.That(entry.Error, Is.Not.Null, "an HTML plugin: installed, but can't run");
        Assert.That(Directory.Exists(Path.Combine(catalog.StreamDeckDirectory, PluginUuid + ".sdPlugin")), Is.True);
    }

    [Test]
    public void Uninstall_AnInstalledStreamDeckPlugin_RemovesItsFolder()
    {
        // Arrange
        using var runtime = NewRuntime();
        using var catalog = NewCatalog(runtime);
        InstallWrapped(catalog);

        // Act
        catalog.Uninstall(PluginUuid);

        // Assert
        Assert.That(Directory.Exists(Path.Combine(catalog.StreamDeckDirectory, PluginUuid + ".sdPlugin")), Is.False);
    }

    private sealed record RunningPlugin(StreamDeckRuntime Runtime, PluginCatalog Catalog, DeviceHostService Host, VariableStore Store, IDeckAction Action) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            Store.Dispose();
            Catalog.Dispose();
            Runtime.Dispose();
        }
    }

    /// <summary>Installs a Node plugin made from <paramref name="script"/>, puts its action on key.0 and starts the host.</summary>
    private async Task<RunningPlugin> StartNodePluginAsync(string script, StreamDeckRuntime runtime, PluginCatalog catalog)
    {
        var plugin = new FakeDevicePlugin();
        runtime.DevicesProvider = () => plugin.Devices;
        Assert.That(catalog.Install(PackPlugin(WritePlugin(PluginScriptName, script: script))).Error, Is.Null);
        var store = new VariableStore();
        var host = new DeviceHostService([plugin], catalog.EnabledApplicationPlugins, store);
        var reference = new ActionReference(PluginUuid, "Test", ActionUuid, "Test action", InputCapabilities.None, OutputCapabilities.None);
        host.SetProfiles([new DeviceProfile("p1", "P", FakeDevicePlugin.DeviceId, [new ControlBinding(KeyId, ProfileActionKind.Single, reference, new ControlAppearance(), [])])]);
        await host.StartAsync(CancellationToken.None);
        var action = catalog.EnabledApplicationPlugins.Single(candidate => candidate.Id == PluginUuid).Actions.Single();
        return new RunningPlugin(runtime, catalog, host, store, action);
    }

    private static void PressKey(DeviceHostService host) =>
        host.SimulateInput(new DeviceInputEvent(FakeDevicePlugin.DeviceId, KeyId, InputCapabilities.Press, 1));

    [Test]
    public async Task WillAppear_ANodePlugin_SetsItsTitle()
    {
        // Arrange
        Assume.That(IsNodeAvailable(), "Node.js isn't installed on this machine; the protocol can't be exercised end to end");
        var logs = new List<string>();
        using var runtime = NewRuntime((level, message) => { lock (logs) { logs.Add($"{level}: {message}"); } });
        using var catalog = NewCatalog(runtime);

        // Act
        await using var running = await StartNodePluginAsync(TestPluginScript, runtime, catalog);

        // Assert
        Assert.That(await WaitForTitleAsync(running.Action, HelloTitle), Is.True, "willAppear → setTitle: " + string.Join(" | ", logs));
    }

    [Test]
    public async Task KeyDown_ANodePlugin_AnswersWithAnUpdatedTitle()
    {
        // Arrange
        Assume.That(IsNodeAvailable(), "Node.js isn't installed on this machine; the protocol can't be exercised end to end");
        using var runtime = NewRuntime();
        using var catalog = NewCatalog(runtime);
        await using var running = await StartNodePluginAsync(TestPluginScript, runtime, catalog);
        await WaitForTitleAsync(running.Action, HelloTitle);

        // Act
        PressKey(running.Host);

        // Assert
        Assert.That(await WaitForTitleAsync(running.Action, "Pressed 1"), Is.True, "keyDown → setTitle");
    }

    [Test]
    public async Task SetSettings_ANodePlugin_GetsTheStoredSettingsBackWithTheNextEvent()
    {
        // Arrange
        Assume.That(IsNodeAvailable(), "Node.js isn't installed on this machine; the protocol can't be exercised end to end");
        using var runtime = NewRuntime();
        using var catalog = NewCatalog(runtime);
        await using var running = await StartNodePluginAsync(TestPluginScript, runtime, catalog);
        await WaitForTitleAsync(running.Action, HelloTitle);
        PressKey(running.Host);
        await WaitForTitleAsync(running.Action, "Pressed 1");

        // Act
        PressKey(running.Host);

        // Assert
        Assert.That(await WaitForTitleAsync(running.Action, "Pressed 2"), Is.True, "settings round trip");
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

    private static readonly TimeSpan ShortRestartDelay = TimeSpan.FromMilliseconds(100);

    [Test]
    public async Task OnProcessExited_APluginWhoseProcessDies_IsRestartedAndItsKeysComeBack()
    {
        // Arrange
        Assume.That(IsNodeAvailable(), "Node.js isn't installed on this machine");
        using var runtime = NewRuntime(null, ShortRestartDelay, ShortRestartDelay);
        using var catalog = NewCatalog(runtime);
        var changes = 0;
        catalog.Changed += (_, _) => Interlocked.Increment(ref changes);
        await using var running = await StartNodePluginAsync(PidTitleScript, runtime, catalog);
        var first = await WaitForTitleStartingAsync(running.Action, "Hello ");
        Assert.That(catalog.Entries.Single(entry => entry.Id == PluginUuid).ProcessStatus, Does.StartWith("Runs in its own process (pid"));

        // Act (the plugin's process dies: it's started again and told what's on the device)
        Process.GetProcessById(int.Parse(first!["Hello ".Length..], System.Globalization.CultureInfo.InvariantCulture)).Kill();
        var second = await WaitForTitleStartingAsync(running.Action, "Hello ", not: first);

        // Assert
        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(catalog.Entries.Single(entry => entry.Id == PluginUuid).ProcessStatus, Does.Contain("restarted 1×"));
        Assert.That(changes >= 2, Is.True);
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

    [Test]
    public async Task Start_APluginThatReadsItsArgumentsByPosition_ConnectsOnTheSecondTry()
    {
        // Arrange
        Assume.That(IsNodeAvailable(), "Node.js isn't installed on this machine");
        var logs = new List<string>();
        using var runtime = NewRuntime((_, message) => { lock (logs) { logs.Add(message); } });
        using var catalog = NewCatalog(runtime);

        // Act
        await using var running = await StartNodePluginAsync(PositionalScript, runtime, catalog);

        // Assert
        Assert.That(await WaitForTitleAsync(running.Action, "Positional"), Is.True, string.Join(" | ", logs));
        lock (logs)
        {
            Assert.That(logs.Any(message => message.Contains("older StreamDock", StringComparison.Ordinal)), Is.True);
        }
    }

    private static StreamDeckActionManifest ActionFor(params string[] controllers) =>
        new("a", "A", null, null, controllers, [], null, true, true);

    [Test]
    public void SupportsEncoder_AKnobAction_IsADial()
    {
        // Arrange
        var knob = ActionFor("Knob", "Information");

        // Act
        var supportsEncoder = knob.SupportsEncoder;

        // Assert
        Assert.That(supportsEncoder, Is.True);
    }

    [Test]
    public void EncoderController_AKnobAction_IsKnob()
    {
        // Arrange
        var knob = ActionFor("Knob", "Information");

        // Act
        var controller = knob.EncoderController;

        // Assert
        Assert.That(controller, Is.EqualTo("Knob"));
    }

    [Test]
    public void EncoderController_AnActionWithBothKeypadAndEncoder_IsEncoder()
    {
        // Arrange
        var both = ActionFor("Keypad", "Encoder");

        // Act
        var controller = both.EncoderController;

        // Assert
        Assert.That(controller, Is.EqualTo("Encoder"));
    }

    private static readonly TimeSpan CrashRestartDelay = TimeSpan.FromMilliseconds(50);

    private async Task<(StreamDeckRuntime Runtime, PluginCatalog Catalog)> StartAPluginThatExitsAtOnce()
    {
        var runtime = NewRuntime(null, CrashRestartDelay, CrashRestartDelay);
        var catalog = NewCatalog(runtime);
        catalog.Install(PackPlugin(WritePlugin(PluginScriptName, script: "process.exit(1);")));
        for (var attempt = 0; attempt < 200 && runtime.ProblemOf(PluginUuid) is null; attempt++)
        {
            await Task.Delay(50);
        }

        return (runtime, catalog);
    }

    [Test]
    public async Task ProblemOf_APluginThatKeepsCrashing_SaysItStoppedThreeTimes()
    {
        // Arrange
        Assume.That(IsNodeAvailable(), "Node.js isn't installed on this machine");
        var (runtime, catalog) = await StartAPluginThatExitsAtOnce();
        using var _ = runtime;
        using var __ = catalog;

        // Act
        var problem = runtime.ProblemOf(PluginUuid);

        // Assert
        Assert.That(problem, Does.Contain("stopped 3 times"));
        Assert.That(catalog.Entries.Single(entry => entry.Id == PluginUuid).ProcessStatus, Is.EqualTo(problem));
    }

    [Test]
    public async Task SetEnabled_DisablingAndEnablingACrashedPlugin_StartsItFresh()
    {
        // Arrange
        Assume.That(IsNodeAvailable(), "Node.js isn't installed on this machine");
        var (runtime, catalog) = await StartAPluginThatExitsAtOnce();
        using var _ = runtime;
        using var __ = catalog;

        // Act
        catalog.SetEnabled(PluginUuid, false);
        catalog.SetEnabled(PluginUuid, true);

        // Assert
        Assert.That(runtime.ProblemOf(PluginUuid), Is.Null);
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
