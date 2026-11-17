using System.Collections.Concurrent;
using System.Text.Json;
using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Plugins;
using DeckHost.Core.Plugins.Isolation;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>
/// Plugins in their own process. The "process" here is <see cref="PluginProcessServer"/> on a task in
/// the test process, talking over the same loopback socket and protocol as the real one.
/// </summary>
public sealed class PluginProcessTests : IDisposable
{
    private const string ObsId = "deckhost.plugin.obs";
    private const string SceneAction = "obs.scene";
    private const string Key = "key.0";
    private const string Device = "dev";
    private const string Profile = "profile";
    private const string ObsLabel = "OBS";
    private const uint ErrorColor = 0xFFFCA5A5;
    private const int PatienceMilliseconds = 10_000;
    private const int RefreshMilliseconds = 1000;
    private const uint DrawnColor = 0xFF112233;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deckhost-isolated-{Guid.NewGuid():N}");
    private readonly VariableStore _variables = new();

    public void Dispose()
    {
        _variables.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    internal sealed class InProcessLauncher : IPluginProcessLauncher
    {
        public ConcurrentQueue<Handle> Started { get; } = new();

        public IPluginProcess Start(int port, string token, string pluginDirectory, string pluginId, Action<string, string> log)
        {
            var handle = new Handle(port, token, pluginDirectory);
            Started.Enqueue(handle);
            return handle;
        }

        internal sealed class Handle : IPluginProcess
        {
            private readonly CancellationTokenSource _stop = new();
            private readonly PluginProcessServer _server = new();

            public Handle(int port, string token, string directory) =>
                Task.Run(async () =>
                {
                    try
                    {
                        await _server.RunAsync(port, token, directory, _stop.Token);
                    }
                    catch (Exception ex) when (ex is OperationCanceledException or IOException or System.Net.Sockets.SocketException)
                    {
                    }
                    finally
                    {
                        _server.Dispose();
                    }
                });

            public int? ProcessId => null;

            /// <summary>As if the process crashed.</summary>
            public void Kill() => _stop.Cancel();

            public void Dispose() => _stop.Cancel();
        }
    }

    private sealed class RecordingHost : IActionHost
    {
        public SemaphoreSlim Renders { get; } = new(0);

        public void RequestRender(string deviceId) => Renders.Release();

        public void ActivateProfile(string deviceId, string profileIdOrName)
        {
        }

        public void ActivateRelativeProfile(string deviceId, int offset)
        {
        }
    }

    private static string ObsPackagePath
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeckHost.slnx")))
            {
                directory = directory.Parent;
            }

            return Path.Combine(directory!.FullName, "artifacts", "plugins", "deckhost.plugin.obs.zip");
        }
    }

    private PluginCatalog CreateCatalog(InProcessLauncher launcher)
    {
        var catalog = new PluginCatalog(_root, [new FakeDevicePlugin()], [], []) { ProcessLauncher = launcher };
        catalog.Load();
        return catalog;
    }

    private RecordingRenderContext Surface(string controlId = "key.0") =>
        new(new WireContext("dev", controlId, "profile", new ActionSettings([new("scene", "Live")])) { Width = 72, Height = 72 }, _variables);

    private static async Task<bool> Until(Func<bool> condition, int milliseconds = 10_000)
    {
        var end = Environment.TickCount64 + milliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 > end)
            {
                return false;
            }

            await Task.Delay(20);
        }

        return true;
    }

    private static ActionSettingDefinition SpeedSetting() =>
        new("speed", "Speed", ActionSettingKind.Number, Placeholder: "5") { Range = new NumberRange(1, 10) { Unit = "x" } };

    private static Message ReadyMessage(ActionSettingDefinition setting) => new(Message.Ready)
    {
        Plugin = new PluginDescriptor("p", "P", "1.0", null, null, [new ActionDescriptor("a", "A", InputCapabilities.Press, OutputCapabilities.ColorImage, "d", [setting], true, null, true, RefreshMilliseconds, false, false)]),
        Commands = [new DrawCommand("rect") { X = 1, W = 2, Color = DrawnColor }],
    };

    private static Message AcrossTheWire(Message message) => Wire.Decode(Wire.Encode(message))!;

    public static IEnumerable<object?> WireValues =>
    [
        "text", true, 42, 42L, 1.5, 2.5f, DateTimeOffset.UnixEpoch, null,
    ];

    [Test]
    public void Decode_ActionSettingWithARange_KeepsTheRange()
    {
        // Arrange
        var setting = SpeedSetting();

        // Act
        var back = AcrossTheWire(ReadyMessage(setting));

        // Assert
        Assert.That(back.Plugin!.Actions.Single().Settings.Single().Range, Is.EqualTo(setting.Range));
    }

    [Test]
    public void Decode_ActionWithARefreshInterval_KeepsIt()
    {
        // Arrange
        var message = ReadyMessage(SpeedSetting());

        // Act
        var back = AcrossTheWire(message);

        // Assert
        Assert.That(back.Plugin!.Actions.Single().RefreshIntervalMs, Is.EqualTo(RefreshMilliseconds));
    }

    [Test]
    public void Decode_DrawCommand_KeepsItsColor()
    {
        // Arrange
        var message = ReadyMessage(SpeedSetting());

        // Act
        var back = AcrossTheWire(message);

        // Assert
        Assert.That(back.Commands!.Single().Color, Is.EqualTo(DrawnColor));
    }

    [TestCaseSource(nameof(WireValues))]
    public void ToObject_ValueSentThroughWireValue_IsTheSameValue(object? value)
    {
        // Arrange
        var wire = WireValue.From(value)!;

        // Act
        var back = wire.ToObject();

        // Assert
        Assert.That(back, Is.EqualTo(value));
    }

    [Test]
    public void From_ObjectThatCannotBeSent_IsNull()
    {
        // Arrange
        var value = new object();

        // Act
        var wire = WireValue.From(value);

        // Assert
        Assert.That(wire, Is.Null);
    }

    [Test]
    public void Replay_RecordedDrawing_ProducesTheSameCommands()
    {
        // Arrange
        var recorded = Surface();
        recorded.FillRoundedRect(1, 2, 3, 4, 5, 0xFF000000);
        recorded.DrawLabel("Hi");
        recorded.DrawIcon("M0 0h1v1Z", 10, 10, 12);
        var copy = Surface();

        // Act
        DisplayList.Replay(copy, recorded.Commands);

        // Assert
        Assert.That(JsonSerializer.Serialize(copy.Commands, Wire.Options), Is.EqualTo(JsonSerializer.Serialize(recorded.Commands, Wire.Options)));
    }

    private (PluginCatalog Catalog, IDeckAction Scene, RecordingHost Host, ActionContext Context) InstalledScene(InProcessLauncher launcher)
    {
        var catalog = CreateCatalog(launcher);
        catalog.Install(ObsPackagePath);
        var plugin = Expect.IsType<IsolatedApplicationPlugin>(Expect.Single(catalog.EnabledApplicationPlugins, candidate => candidate.Id == ObsId));
        var scene = plugin.Actions.Single(action => action.Id == SceneAction);
        var host = new RecordingHost();
        var context = new ActionContext(Device, Key, _variables) { Host = host, ProfileId = Profile, Settings = new ActionSettings([new("scene", "Live"), new("port", "1")]) };
        scene.OnAppear(context);
        return (catalog, scene, host, context);
    }

    [Test]
    public void Install_ObsPackage_RunsInItsOwnProcess()
    {
        // Arrange
        using var catalog = CreateCatalog(new InProcessLauncher());

        // Act
        var entry = catalog.Install(ObsPackagePath);

        // Assert
        Assert.That(entry.RunsInOwnProcess, Is.True);
        Assert.That(entry.ProcessStatus, Does.Contain("own process"));
    }

    [Test]
    public void Actions_IsolatedObsPlugin_DescribesTheSceneSetting()
    {
        // Arrange
        var (catalog, scene, _, _) = InstalledScene(new InProcessLauncher());
        using var _ = catalog;

        // Act
        var settings = scene.Settings;

        // Assert
        Assert.That(settings.Any(setting => setting.Key == "scene"), Is.True);
    }

    [Test]
    public void Render_FirstTime_DrawsNothingYetAsThePictureIsRequested()
    {
        // Arrange
        var (catalog, scene, _, _) = InstalledScene(new InProcessLauncher());
        using var _ = catalog;
        var first = Surface();

        // Act
        scene.Render(first);

        // Assert
        Assert.That(first.Commands, Is.Empty);
    }

    [Test]
    public async Task Render_FirstTime_AsksDeckHostToDrawAgainWhenThePictureArrives()
    {
        // Arrange
        var (catalog, scene, host, _) = InstalledScene(new InProcessLauncher());
        using var _ = catalog;
        scene.Render(Surface());

        // Act
        var asked = await host.Renders.WaitAsync(PatienceMilliseconds);

        // Assert
        Assert.That(asked, Is.True);
    }

    [Test]
    public async Task Render_AfterThePictureArrived_DrawsTheObsLabel()
    {
        // Arrange
        var (catalog, scene, host, _) = InstalledScene(new InProcessLauncher());
        using var _ = catalog;
        scene.Render(Surface());
        await host.Renders.WaitAsync(PatienceMilliseconds);
        var second = Surface();

        // Act
        scene.Render(second);

        // Assert
        Assert.That(second.Commands.Any(command => command.Text == ObsLabel), Is.True);
    }

    [Test]
    public async Task Execute_PressWithoutAReachableObs_PublishesTheError()
    {
        // Arrange
        var (catalog, scene, _, context) = InstalledScene(new InProcessLauncher());
        using var _ = catalog;
        var errorKey = VariableKeys.For(Device, Key, VariableKeys.LastError);

        // Act
        scene.Execute(context with { EventType = InputCapabilities.Press, Value = 1 });
        var published = await Until(() => _variables.TryGetValue<string>(errorKey, out var error) && !string.IsNullOrEmpty(error));

        // Assert
        Assert.That(published, Is.True);
    }

    [Test]
    public async Task Render_AfterTheErrorWasPublished_DrawsInTheErrorColor()
    {
        // Arrange
        var (catalog, scene, _, context) = InstalledScene(new InProcessLauncher());
        using var _ = catalog;
        scene.Execute(context with { EventType = InputCapabilities.Press, Value = 1 });

        // Act
        var drawnInRed = await Until(() => DrawsAnythingIn(scene, ErrorColor));

        // Assert
        Assert.That(drawnInRed, Is.True);
    }

    private bool DrawsAnythingIn(IDeckAction scene, uint color)
    {
        var surface = Surface();
        scene.Render(surface);
        return surface.Commands.Any(command => command.Color == color);
    }

    private static void DrainRenderRequests(RecordingHost host)
    {
        while (host.Renders.CurrentCount > 0)
        {
            host.Renders.Wait();
        }
    }

    private static string ObsProcessStatus(PluginCatalog catalog) => catalog.Entries.Single(entry => entry.Id == ObsId).ProcessStatus ?? string.Empty;

    [Test]
    public async Task Kill_PluginProcessThatCrashed_IsRestarted()
    {
        // Arrange
        var launcher = new InProcessLauncher();
        var (catalog, _, _, _) = InstalledScene(launcher);
        using var _ = catalog;

        // Act (installing ran a trial process first; the last one started is the plugin's)
        launcher.Started.Last().Kill();
        var restarted = await Until(() => ObsProcessStatus(catalog).Contains("restarted 1", StringComparison.Ordinal));

        // Assert
        Assert.That(restarted, Is.True);
    }

    [Test]
    public async Task Changed_PluginProcessThatCrashed_ArrivesForTheStatusChange()
    {
        // Arrange
        var launcher = new InProcessLauncher();
        var (catalog, _, _, _) = InstalledScene(launcher);
        using var _ = catalog;
        var changes = 0;
        catalog.Changed += (_, _) => Interlocked.Increment(ref changes);

        // Act (the status is read live; the Changed events for it arrive a moment later)
        launcher.Started.Last().Kill();
        var changed = await Until(() => Volatile.Read(ref changes) >= 2);

        // Assert
        Assert.That(changed, Is.True);
    }

    [Test]
    public async Task Render_AfterTheProcessWasRestarted_DrawsTheObsLabelAgain()
    {
        // Arrange
        var launcher = new InProcessLauncher();
        var (catalog, scene, host, _) = InstalledScene(launcher);
        using var _ = catalog;
        launcher.Started.Last().Kill();
        await Until(() => ObsProcessStatus(catalog).Contains("restarted 1", StringComparison.Ordinal));
        DrainRenderRequests(host);
        scene.Render(Surface());
        await host.Renders.WaitAsync(PatienceMilliseconds);
        var surface = Surface();

        // Act
        scene.Render(surface);

        // Assert
        Assert.That(surface.Commands.Any(command => command.Text == ObsLabel), Is.True);
    }

    [Test]
    public void SetRunInOwnProcess_False_RunsThePluginInsideDeckHost()
    {
        // Arrange
        using var catalog = CreateCatalog(new InProcessLauncher());
        catalog.Install(ObsPackagePath);

        // Act
        catalog.SetRunInOwnProcess(ObsId, ownProcess: false);

        // Assert
        var entry = catalog.Entries.Single(candidate => candidate.Id == ObsId);
        Assert.That(entry.RunsInOwnProcess, Is.False);
        Assert.That(entry.ProcessStatus, Is.EqualTo("Runs inside DeckHost"));
        Assert.That(catalog.EnabledApplicationPlugins.Single(plugin => plugin.Id == ObsId), Is.Not.TypeOf<IsolatedApplicationPlugin>());
    }

    [Test]
    public void Load_AfterMovingThePluginIntoDeckHost_RemembersIt()
    {
        // Arrange
        var launcher = new InProcessLauncher();
        using (var catalog = CreateCatalog(launcher))
        {
            catalog.Install(ObsPackagePath);
            catalog.SetRunInOwnProcess(ObsId, ownProcess: false);
        }

        // Act
        using var reloaded = CreateCatalog(launcher);

        // Assert
        Assert.That(reloaded.Entries.Single(candidate => candidate.Id == ObsId).RunsInOwnProcess, Is.False);
    }

    [Test]
    public void SetRunInOwnProcess_True_MovesThePluginBackToItsOwnProcess()
    {
        // Arrange
        var launcher = new InProcessLauncher();
        using var catalog = CreateCatalog(launcher);
        catalog.Install(ObsPackagePath);
        catalog.SetRunInOwnProcess(ObsId, ownProcess: false);

        // Act
        catalog.SetRunInOwnProcess(ObsId, ownProcess: true);

        // Assert
        Assert.That(catalog.EnabledApplicationPlugins.Single(plugin => plugin.Id == ObsId), Is.TypeOf<IsolatedApplicationPlugin>());
    }
}
