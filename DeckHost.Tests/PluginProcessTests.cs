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

    [Fact]
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
        Assert.Equal(setting.Range, action.Settings.Single().Range);
        Assert.Equal(1000, action.RefreshIntervalMs);
        Assert.Equal(0xFF112233u, back.Commands!.Single().Color);

        foreach (var value in new object?[] { "text", true, 42, 42L, 1.5, 2.5f, DateTimeOffset.UnixEpoch, null })
        {
            Assert.Equal(value, WireValue.From(value)!.ToObject());
        }

        Assert.Null(WireValue.From(new object()));

        var recorded = Surface();
        recorded.FillRoundedRect(1, 2, 3, 4, 5, 0xFF000000);
        recorded.DrawLabel("Hi");
        recorded.DrawIcon("M0 0h1v1Z", 10, 10, 12);
        var copy = Surface();
        DisplayList.Replay(copy, recorded.Commands);
        Assert.Equal(JsonSerializer.Serialize(recorded.Commands, Wire.Options), JsonSerializer.Serialize(copy.Commands, Wire.Options));
    }

    [Fact]
    public async Task A_plugin_in_its_own_process_draws_runs_and_publishes_through_DeckHost()
    {
        var launcher = new InProcessLauncher();
        using var catalog = CreateCatalog(launcher);
        var entry = catalog.Install(ObsPackagePath);

        Assert.True(entry.RunsInOwnProcess);
        Assert.Contains("own process", entry.ProcessStatus);
        var plugin = Assert.IsType<IsolatedApplicationPlugin>(Assert.Single(catalog.EnabledApplicationPlugins, candidate => candidate.Id == "deckhost.plugin.obs"));
        var scene = plugin.Actions.Single(action => action.Id == "obs.scene");
        Assert.Contains(scene.Settings, setting => setting.Key == "scene");

        var host = new RecordingHost();
        var context = new ActionContext("dev", "key.0", _variables) { Host = host, ProfileId = "profile", Settings = new ActionSettings([new("scene", "Live"), new("port", "1")]) };
        scene.OnAppear(context);

        // The first render asks for a picture; when it arrives, DeckHost is asked to draw again.
        var first = Surface();
        scene.Render(first);
        Assert.Empty(first.Commands);
        Assert.True(await host.Renders.WaitAsync(10_000));
        var second = Surface();
        scene.Render(second);
        Assert.Contains(second.Commands, command => command.Text == "OBS");

        // A press: the plugin can't reach OBS, publishes the error and asks for a redraw.
        scene.Execute(context with { EventType = InputCapabilities.Press, Value = 1 });
        var errorKey = VariableKeys.For("dev", "key.0", VariableKeys.LastError);
        Assert.True(await Until(() => _variables.TryGetValue<string>(errorKey, out var error) && !string.IsNullOrEmpty(error)));
        string? drawn = null;
        Assert.True(await Until(() =>
        {
            var surface = Surface();
            scene.Render(surface);
            drawn = surface.Commands.LastOrDefault()?.Text;
            return surface.Commands.Any(command => command.Color == 0xFFFCA5A5);
        }), $"last drawn: {drawn}");
    }

    [Fact]
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

        Assert.True(await Until(() => catalog.Entries.Single(entry => entry.Id == "deckhost.plugin.obs").ProcessStatus?.Contains("restarted 1", StringComparison.Ordinal) == true));
        // The status is read live; the Changed events for it arrive a moment later.
        Assert.True(await Until(() => Volatile.Read(ref changes) >= 2));
        while (host.Renders.CurrentCount > 0)
        {
            host.Renders.Wait();
        }

        scene.Render(Surface());
        Assert.True(await host.Renders.WaitAsync(10_000));
        var surface = Surface();
        scene.Render(surface);
        Assert.Contains(surface.Commands, command => command.Text == "OBS");
    }

    [Fact]
    public void A_plugin_can_be_moved_into_DeckHost_and_back_and_that_is_remembered()
    {
        var launcher = new InProcessLauncher();
        using (var catalog = CreateCatalog(launcher))
        {
            catalog.Install(ObsPackagePath);
            catalog.SetRunInOwnProcess("deckhost.plugin.obs", ownProcess: false);

            var entry = catalog.Entries.Single(candidate => candidate.Id == "deckhost.plugin.obs");
            Assert.False(entry.RunsInOwnProcess);
            Assert.Equal("Runs inside DeckHost", entry.ProcessStatus);
            Assert.IsNotType<IsolatedApplicationPlugin>(catalog.EnabledApplicationPlugins.Single(plugin => plugin.Id == "deckhost.plugin.obs"));
        }

        using (var reloaded = CreateCatalog(launcher))
        {
            Assert.False(reloaded.Entries.Single(candidate => candidate.Id == "deckhost.plugin.obs").RunsInOwnProcess);
            reloaded.SetRunInOwnProcess("deckhost.plugin.obs", ownProcess: true);
            Assert.IsType<IsolatedApplicationPlugin>(reloaded.EnabledApplicationPlugins.Single(plugin => plugin.Id == "deckhost.plugin.obs"));
        }
    }
}
