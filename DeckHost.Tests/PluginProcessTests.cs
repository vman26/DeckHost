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

    [Test]
    public void Descriptors_values_and_drawings_survive_the_wire()
    {
        var setting = new ActionSettingDefinition("speed", "Speed", ActionSettingKind.Number, Placeholder: "5") { Range = new NumberRange(1, 10) { Unit = "x" } };
        var message = new Message(Message.Ready)
        {
            Plugin = new PluginDescriptor("p", "P", "1.0", null, null, [new ActionDescriptor("a", "A", InputCapabilities.Press, OutputCapabilities.ColorImage, "d", [setting], true, null, true, 1000, false, false)]),
            Commands = [new DrawCommand("rect") { X = 1, W = 2, Color = 0xFF112233 }],
        };

        var back = Wire.Decode(Wire.Encode(message))!;
        var action = back.Plugin!.Actions.Single();
        Assert.That(action.Settings.Single().Range, Is.EqualTo(setting.Range));
        Assert.That(action.RefreshIntervalMs, Is.EqualTo(1000));
        Assert.That(back.Commands!.Single().Color, Is.EqualTo(0xFF112233u));

        foreach (var value in new object?[] { "text", true, 42, 42L, 1.5, 2.5f, DateTimeOffset.UnixEpoch, null })
        {
            Assert.That(WireValue.From(value)!.ToObject(), Is.EqualTo(value));
        }

        Assert.That(WireValue.From(new object()), Is.Null);

        var recorded = Surface();
        recorded.FillRoundedRect(1, 2, 3, 4, 5, 0xFF000000);
        recorded.DrawLabel("Hi");
        recorded.DrawIcon("M0 0h1v1Z", 10, 10, 12);
        var copy = Surface();
        DisplayList.Replay(copy, recorded.Commands);
        Assert.That(JsonSerializer.Serialize(copy.Commands, Wire.Options), Is.EqualTo(JsonSerializer.Serialize(recorded.Commands, Wire.Options)));
    }

    [Test]
    public async Task A_plugin_in_its_own_process_draws_runs_and_publishes_through_DeckHost()
    {
        var launcher = new InProcessLauncher();
        using var catalog = CreateCatalog(launcher);
        var entry = catalog.Install(ObsPackagePath);

        Assert.That(entry.RunsInOwnProcess, Is.True);
        Assert.That(entry.ProcessStatus, Does.Contain("own process"));
        var plugin = Expect.IsType<IsolatedApplicationPlugin>(Expect.Single(catalog.EnabledApplicationPlugins, candidate => candidate.Id == "deckhost.plugin.obs"));
        var scene = plugin.Actions.Single(action => action.Id == "obs.scene");
        Assert.That(scene.Settings.Any(setting => setting.Key == "scene"), Is.True);

        var host = new RecordingHost();
        var context = new ActionContext("dev", "key.0", _variables) { Host = host, ProfileId = "profile", Settings = new ActionSettings([new("scene", "Live"), new("port", "1")]) };
        scene.OnAppear(context);

        // The first render asks for a picture; when it arrives, DeckHost is asked to draw again.
        var first = Surface();
        scene.Render(first);
        Assert.That(first.Commands, Is.Empty);
        Assert.That(await host.Renders.WaitAsync(10_000), Is.True);
        var second = Surface();
        scene.Render(second);
        Assert.That(second.Commands.Any(command => command.Text == "OBS"), Is.True);

        // A press: the plugin can't reach OBS, publishes the error and asks for a redraw.
        scene.Execute(context with { EventType = InputCapabilities.Press, Value = 1 });
        var errorKey = VariableKeys.For("dev", "key.0", VariableKeys.LastError);
        Assert.That(await Until(() => _variables.TryGetValue<string>(errorKey, out var error) && !string.IsNullOrEmpty(error)), Is.True);
        string? drawn = null;
        Assert.That(
            await Until(() =>
        {
            var surface = Surface();
            scene.Render(surface);
            drawn = surface.Commands.LastOrDefault()?.Text;
            return surface.Commands.Any(command => command.Color == 0xFFFCA5A5);
        }),
            Is.True,
            $"last drawn: {drawn}");
    }

    [Test]
    public async Task A_crashed_plugin_process_is_restarted_and_draws_again()
    {
        var launcher = new InProcessLauncher();
        using var catalog = CreateCatalog(launcher);
        catalog.Install(ObsPackagePath);
        var scene = catalog.EnabledApplicationPlugins.Single(candidate => candidate.Id == "deckhost.plugin.obs").Actions.Single(action => action.Id == "obs.scene");
        var host = new RecordingHost();
        scene.OnAppear(new ActionContext("dev", "key.0", _variables) { Host = host, ProfileId = "profile" });
        var changes = 0;
        catalog.Changed += (_, _) => Interlocked.Increment(ref changes);

        // Installing ran a trial process first; the last one started is the plugin's.
        launcher.Started.Last().Kill();

        Assert.That(await Until(() => catalog.Entries.Single(entry => entry.Id == "deckhost.plugin.obs").ProcessStatus?.Contains("restarted 1", StringComparison.Ordinal) == true), Is.True);
        // The status is read live; the Changed events for it arrive a moment later.
        Assert.That(await Until(() => Volatile.Read(ref changes) >= 2), Is.True);
        while (host.Renders.CurrentCount > 0)
        {
            host.Renders.Wait();
        }

        scene.Render(Surface());
        Assert.That(await host.Renders.WaitAsync(10_000), Is.True);
        var surface = Surface();
        scene.Render(surface);
        Assert.That(surface.Commands.Any(command => command.Text == "OBS"), Is.True);
    }

    [Test]
    public void A_plugin_can_be_moved_into_DeckHost_and_back_and_that_is_remembered()
    {
        var launcher = new InProcessLauncher();
        using (var catalog = CreateCatalog(launcher))
        {
            catalog.Install(ObsPackagePath);
            catalog.SetRunInOwnProcess("deckhost.plugin.obs", ownProcess: false);

            var entry = catalog.Entries.Single(candidate => candidate.Id == "deckhost.plugin.obs");
            Assert.That(entry.RunsInOwnProcess, Is.False);
            Assert.That(entry.ProcessStatus, Is.EqualTo("Runs inside DeckHost"));
            Assert.That(catalog.EnabledApplicationPlugins.Single(plugin => plugin.Id == "deckhost.plugin.obs"), Is.Not.TypeOf<IsolatedApplicationPlugin>());
        }

        using (var reloaded = CreateCatalog(launcher))
        {
            Assert.That(reloaded.Entries.Single(candidate => candidate.Id == "deckhost.plugin.obs").RunsInOwnProcess, Is.False);
            reloaded.SetRunInOwnProcess("deckhost.plugin.obs", ownProcess: true);
            Assert.That(reloaded.EnabledApplicationPlugins.Single(plugin => plugin.Id == "deckhost.plugin.obs"), Is.TypeOf<IsolatedApplicationPlugin>());
        }
    }
}
