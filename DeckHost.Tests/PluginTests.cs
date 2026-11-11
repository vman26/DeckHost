using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Plugins;
using DeckHost.Core.Profiles;
using DeckHost.Core.State;
using System.IO.Compression;
using System.Text.Json;

namespace DeckHost.Tests;

public sealed class PluginCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"deckhost-plugins-{Guid.NewGuid():N}");

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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private PluginCatalog CreateCatalog()
    {
        var catalog = new PluginCatalog(
            _root,
            [new FakeDevicePlugin()],
            [new TestApplicationPlugin("system.required", new RecordingAction("a")), new TestApplicationPlugin("system.optional", new RecordingAction("b"))],
            ["system.required"]);
        catalog.Load();
        return catalog;
    }

    [Fact]
    public void System_plugins_can_be_disabled_unless_required_but_never_uninstalled()
    {
        using var catalog = CreateCatalog();

        catalog.SetEnabled("system.optional", false);
        Assert.DoesNotContain(catalog.EnabledApplicationPlugins, plugin => plugin.Id == "system.optional");
        Assert.Throws<PluginException>(() => catalog.SetEnabled("system.required", false));
        Assert.Throws<PluginException>(() => catalog.Uninstall("system.optional"));

        // The disabled state survives a restart.
        using var reloaded = CreateCatalog();
        Assert.DoesNotContain(reloaded.EnabledApplicationPlugins, plugin => plugin.Id == "system.optional");
    }

    [Fact]
    public void Installs_disables_and_uninstalls_a_plugin_package()
    {
        Assert.True(File.Exists(ObsPackagePath), $"Build the OBS plugin first: {ObsPackagePath}");
        using var catalog = CreateCatalog();
        var changes = 0;
        catalog.Changed += (_, _) => changes++;

        var entry = catalog.Install(ObsPackagePath);

        Assert.Equal("deckhost.plugin.obs", entry.Id);
        Assert.True(entry.CanUninstall);
        var plugin = Assert.Single(catalog.EnabledApplicationPlugins, candidate => candidate.Id == "deckhost.plugin.obs");
        Assert.Contains(plugin.Actions, action => action.Id == "obs.scene" && action.Settings.Any(setting => setting.Key == "scene"));

        catalog.SetEnabled(entry.Id, false);
        Assert.DoesNotContain(catalog.EnabledApplicationPlugins, candidate => candidate.Id == entry.Id);
        catalog.SetEnabled(entry.Id, true);
        Assert.Contains(catalog.EnabledApplicationPlugins, candidate => candidate.Id == entry.Id);

        // Assemblies are loaded from memory, so the files can be deleted right away.
        catalog.Uninstall(entry.Id);
        Assert.False(Directory.Exists(Path.Combine(catalog.InstallDirectory, entry.Id)));
        Assert.DoesNotContain(catalog.Entries, candidate => candidate.Id == entry.Id);
        Assert.Equal(4, changes);
    }

    [Fact]
    public void Installed_plugins_load_again_after_a_restart()
    {
        using (var catalog = CreateCatalog())
        {
            catalog.Install(ObsPackagePath);
        }

        using var reloaded = CreateCatalog();
        Assert.Contains(reloaded.EnabledApplicationPlugins, plugin => plugin.Id == "deckhost.plugin.obs");
    }

    [Theory]
    [InlineData("""{"id":"Bad Id!","name":"x","version":"1","assembly":"x.dll"}""", "plugin id")]
    [InlineData("""{"id":"good.id","name":"x","version":"1","assembly":"../evil.dll"}""", "assembly")]
    [InlineData("""{"id":"good.id","name":"x","version":"1","assembly":"x.dll","hostApiVersion":99}""", "host API")]
    [InlineData("""{"id":"system.optional","name":"x","version":"1","assembly":"x.dll"}""", "system plugin")]
    public void Rejects_invalid_packages(string manifest, string expectedMessage)
    {
        using var catalog = CreateCatalog();
        var package = CreatePackage(("plugin.json", manifest));

        var error = Assert.Throws<PluginException>(() => catalog.Install(package));
        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(catalog.InstallDirectory, "good.id")));
    }

    [Fact]
    public void Rejects_packages_that_escape_the_install_folder()
    {
        using var catalog = CreateCatalog();
        var package = CreatePackage(
            ("plugin.json", """{"id":"good.id","name":"x","version":"1","assembly":"x.dll"}"""),
            ("../outside.dll", "x"));

        Assert.Throws<PluginException>(() => catalog.Install(package));
        Assert.False(File.Exists(Path.Combine(_root, "outside.dll")));
    }

    [Fact]
    public void Rejects_packages_without_a_plugin_assembly()
    {
        using var catalog = CreateCatalog();
        var package = CreatePackage(("plugin.json", """{"id":"good.id","name":"x","version":"1","assembly":"x.dll"}"""));

        Assert.Throws<PluginException>(() => catalog.Install(package));
        Assert.DoesNotContain(catalog.Entries, entry => entry.Id == "good.id");
    }

    private string CreatePackage(params (string Name, string Content)[] entries)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }

        return path;
    }
}

public class ActionSettingsTests
{
    [Fact]
    public void Settings_compare_by_value_and_round_trip_through_json()
    {
        var settings = new ActionSettings([new("path", @"C:\app.exe"), new("arguments", "--x")]);

        Assert.Equal(settings, settings.With("other", null));
        Assert.NotEqual(settings, settings.With("path", "y"));
        Assert.Equal(settings, JsonSerializer.Deserialize<ActionSettings>(JsonSerializer.Serialize(settings)));
        Assert.Equal(12, new ActionSettings([new("n", "12")]).GetInt("n", 0));
        Assert.Equal("fallback", ActionSettings.Empty.Get("missing", "fallback"));
    }

    [Fact]
    public void Bindings_with_settings_survive_the_profile_store()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deckhost-{Guid.NewGuid():N}.json");
        try
        {
            var reference = Bindings.Reference("p", "launch") with { Settings = new ActionSettings([new("path", @"C:\Tools\app.exe")]) };
            var binding = new ControlBinding("key.0", ProfileActionKind.Single, reference, new ControlAppearance(), []);
            var store = new ProfileStore(path);
            store.Save(new ProfileDocument(1, null, [new DeviceProfile("p1", "P", "dev", [binding])]));

            var loaded = Assert.Single(Assert.Single(store.Load()!.Profiles).Bindings);
            Assert.Equal(@"C:\Tools\app.exe", loaded.Action.EffectiveSettings.Get("path"));
            Assert.Equal(binding.Action, loaded.Action);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ActionHostTests
{
    [Fact]
    public async Task Actions_receive_their_settings_and_can_switch_profiles()
    {
        var plugin = new FakeDevicePlugin();
        var action = new RecordingAction("switch") { OnExecute = context => context.Host.ActivateRelativeProfile(context.DeviceId, 1) };
        using var store = new VariableStore();
        await using var host = new DeviceHostService([plugin], [new TestApplicationPlugin("p", action)], store);
        var reference = Bindings.Reference("p", "switch") with { Settings = new ActionSettings([new("mode", "next")]) };
        host.SetProfiles(
        [
            new DeviceProfile("one", "One", FakeDevicePlugin.DeviceId, [new ControlBinding("key.0", ProfileActionKind.Single, reference, new ControlAppearance(), [])]),
            new DeviceProfile("two", "Two", FakeDevicePlugin.DeviceId, []),
        ]);
        var switched = new TaskCompletionSource<string>();
        host.ActiveProfileChanged += (_, profileId) => switched.TrySetResult(profileId);
        await host.StartAsync(CancellationToken.None);

        plugin.Context!.ReportInputEvent(FakeDevicePlugin.DeviceId, "key.0", InputCapabilities.Press, 1);

        Assert.Equal("two", await switched.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("next", Assert.Single(action.Executions).Settings.Get("mode"));
    }

    [Fact]
    public async Task Disabling_a_plugin_unbinds_its_actions_at_runtime()
    {
        var plugin = new FakeDevicePlugin();
        var action = new RecordingAction("a");
        using var store = new VariableStore();
        await using var host = new DeviceHostService([plugin], [new TestApplicationPlugin("p", action)], store);
        host.SetProfiles([new DeviceProfile("one", "One", FakeDevicePlugin.DeviceId, [Bindings.Single("key.0", "p", "a")])]);
        await host.StartAsync(CancellationToken.None);

        host.SetApplicationPlugins([]);
        plugin.Context!.ReportInputEvent(FakeDevicePlugin.DeviceId, "key.0", InputCapabilities.Press, 1);
        await host.WhenIdleAsync();

        Assert.Empty(action.Executions);
    }
}
