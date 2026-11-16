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

    [Test]
    public void System_plugins_can_be_disabled_unless_required_but_never_uninstalled()
    {
        using var catalog = CreateCatalog();

        catalog.SetEnabled("system.optional", false);
        Assert.That(catalog.EnabledApplicationPlugins.Any(plugin => plugin.Id == "system.optional"), Is.False);
        Assert.Throws<PluginException>(() => catalog.SetEnabled("system.required", false));
        Assert.Throws<PluginException>(() => catalog.Uninstall("system.optional"));

        // The disabled state survives a restart.
        using var reloaded = CreateCatalog();
        Assert.That(reloaded.EnabledApplicationPlugins.Any(plugin => plugin.Id == "system.optional"), Is.False);
    }

    [Test]
    public void Installs_disables_and_uninstalls_a_plugin_package()
    {
        Assert.That(File.Exists(ObsPackagePath), Is.True, $"Build the OBS plugin first: {ObsPackagePath}");
        using var catalog = CreateCatalog();
        var changes = 0;
        catalog.Changed += (_, _) => changes++;

        var entry = catalog.Install(ObsPackagePath);

        Assert.That(entry.Id, Is.EqualTo("deckhost.plugin.obs"));
        Assert.That(entry.CanUninstall, Is.True);
        var plugin = Expect.Single(catalog.EnabledApplicationPlugins, candidate => candidate.Id == "deckhost.plugin.obs");
        Assert.That(plugin.Actions.Any(action => action.Id == "obs.scene" && action.Settings.Any(setting => setting.Key == "scene")), Is.True);

        catalog.SetEnabled(entry.Id, false);
        Assert.That(catalog.EnabledApplicationPlugins.Any(candidate => candidate.Id == entry.Id), Is.False);
        catalog.SetEnabled(entry.Id, true);
        Assert.That(catalog.EnabledApplicationPlugins.Any(candidate => candidate.Id == entry.Id), Is.True);

        // Assemblies are loaded from memory, so the files can be deleted right away.
        catalog.Uninstall(entry.Id);
        Assert.That(Directory.Exists(Path.Combine(catalog.InstallDirectory, entry.Id)), Is.False);
        Assert.That(catalog.Entries.Any(candidate => candidate.Id == entry.Id), Is.False);
        Assert.That(changes, Is.EqualTo(4));
    }

    /// <summary>The OBS package with its version (and optionally more of plugin.json) changed.</summary>
    private string ObsPackageWithVersion(string version)
    {
        var copy = Path.Combine(_root, $"obs-{version}.zip");
        Directory.CreateDirectory(_root);
        File.Copy(ObsPackagePath, copy, overwrite: true);
        using var zip = ZipFile.Open(copy, ZipArchiveMode.Update);
        var entry = zip.GetEntry("plugin.json")!;
        string json;
        using (var reader = new StreamReader(entry.Open()))
        {
            json = reader.ReadToEnd();
        }

        entry.Delete();
        using var writer = new StreamWriter(zip.CreateEntry("plugin.json").Open());
        writer.Write(json.Replace("\"version\": \"1.0.0\"", $"\"version\": \"{version}\"", StringComparison.Ordinal));
        return copy;
    }

    [Test]
    public void An_update_keeps_the_previous_version_which_can_be_rolled_back_and_forth()
    {
        using var catalog = CreateCatalog();
        catalog.Install(ObsPackageWithVersion("1.0.0"));
        Assert.That(catalog.Entries.Single(entry => entry.Id == "deckhost.plugin.obs").PreviousVersion, Is.Null);

        var updated = catalog.Install(ObsPackageWithVersion("1.1.0"));
        Assert.That(updated.Version, Is.EqualTo("1.1.0"));
        Assert.That(updated.PreviousVersion, Is.EqualTo("1.0.0"));

        var back = catalog.RollBack("deckhost.plugin.obs");
        Assert.That(back.Version, Is.EqualTo("1.0.0"));
        Assert.That(back.PreviousVersion, Is.EqualTo("1.1.0"));
        Assert.That(catalog.EnabledApplicationPlugins.Any(plugin => plugin.Id == "deckhost.plugin.obs"), Is.True);

        catalog.Uninstall("deckhost.plugin.obs");
        Assert.Throws<PluginException>(() => catalog.RollBack("deckhost.plugin.obs"));
    }

    [Test]
    public void A_package_declares_its_permissions_and_diagnostics_find_what_is_wrong()
    {
        using var catalog = CreateCatalog();
        var entry = catalog.Install(ObsPackagePath);
        Assert.That(entry.Permissions, Is.EqualTo(new[] { "network" }));
        Assert.That(PluginPermissions.Describe(entry.Permissions), Is.EqualTo("It can use the network."));
        Assert.That(entry.Warnings ?? [], Is.Empty);
        Assert.That(PluginDiagnostics.Check(ObsPackagePath), Is.Empty);

        var broken = CreatePackage(
            ("plugin.json", """{"id":"good.id","name":"x","version":"one","assembly":"x.dll","icon":"icon.png","permissions":["network","telepathy"]}"""),
            ("DeckHost.Contracts.dll", "x"));
        var problems = PluginDiagnostics.Check(broken).Select(static problem => problem.ToString()).ToList();
        Assert.That(problems.Any(static problem => problem.StartsWith("error:", StringComparison.Ordinal) && problem.Contains("x.dll", StringComparison.Ordinal)), Is.True);
        Assert.That(problems.Any(static problem => problem.Contains("'one'", StringComparison.Ordinal)), Is.True);
        Assert.That(problems.Any(static problem => problem.Contains("icon.png", StringComparison.Ordinal)), Is.True);
        Assert.That(problems.Any(static problem => problem.Contains("telepathy", StringComparison.Ordinal)), Is.True);
        Assert.That(problems.Any(static problem => problem.Contains("DeckHost.Contracts.dll", StringComparison.Ordinal)), Is.True);
        Assert.That(problems.Any(static problem => problem.Contains("author", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void Installed_plugins_load_again_after_a_restart()
    {
        using (var catalog = CreateCatalog())
        {
            catalog.Install(ObsPackagePath);
        }

        using var reloaded = CreateCatalog();
        Assert.That(reloaded.EnabledApplicationPlugins.Any(plugin => plugin.Id == "deckhost.plugin.obs"), Is.True);
    }

    [TestCase("""{"id":"Bad Id!","name":"x","version":"1","assembly":"x.dll"}""", "plugin id")]
    [TestCase("""{"id":"good.id","name":"x","version":"1","assembly":"../evil.dll"}""", "assembly")]
    [TestCase("""{"id":"good.id","name":"x","version":"1","assembly":"x.dll","hostApiVersion":99}""", "host API")]
    [TestCase("""{"id":"system.optional","name":"x","version":"1","assembly":"x.dll"}""", "system plugin")]
    public void Rejects_invalid_packages(string manifest, string expectedMessage)
    {
        using var catalog = CreateCatalog();
        var package = CreatePackage(("plugin.json", manifest));

        var error = Assert.Throws<PluginException>(() => catalog.Install(package));
        Assert.That(error.Message, Does.Contain(expectedMessage).IgnoreCase);
        Assert.That(Directory.Exists(Path.Combine(catalog.InstallDirectory, "good.id")), Is.False);
    }

    [Test]
    public void Rejects_packages_that_escape_the_install_folder()
    {
        using var catalog = CreateCatalog();
        var package = CreatePackage(
            ("plugin.json", """{"id":"good.id","name":"x","version":"1","assembly":"x.dll"}"""),
            ("../outside.dll", "x"));

        Assert.Throws<PluginException>(() => catalog.Install(package));
        Assert.That(File.Exists(Path.Combine(_root, "outside.dll")), Is.False);
    }

    [Test]
    public void Rejects_packages_without_a_plugin_assembly()
    {
        using var catalog = CreateCatalog();
        var package = CreatePackage(("plugin.json", """{"id":"good.id","name":"x","version":"1","assembly":"x.dll"}"""));

        Assert.Throws<PluginException>(() => catalog.Install(package));
        Assert.That(catalog.Entries.Any(entry => entry.Id == "good.id"), Is.False);
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
    [Test]
    public void Settings_compare_by_value_and_round_trip_through_json()
    {
        var settings = new ActionSettings([new("path", @"C:\app.exe"), new("arguments", "--x")]);

        Assert.That(settings.With("other", null), Is.EqualTo(settings));
        Assert.That(settings.With("path", "y"), Is.Not.EqualTo(settings));
        Assert.That(JsonSerializer.Deserialize<ActionSettings>(JsonSerializer.Serialize(settings)), Is.EqualTo(settings));
        Assert.That(new ActionSettings([new("n", "12")]).GetInt("n", 0), Is.EqualTo(12));
        Assert.That(ActionSettings.Empty.Get("missing", "fallback"), Is.EqualTo("fallback"));
    }

    [Test]
    public void Bindings_with_settings_survive_the_profile_store()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deckhost-{Guid.NewGuid():N}.json");
        try
        {
            var reference = Bindings.Reference("p", "launch") with { Settings = new ActionSettings([new("path", @"C:\Tools\app.exe")]) };
            var binding = new ControlBinding("key.0", ProfileActionKind.Single, reference, new ControlAppearance(), []);
            var store = new ProfileStore(path);
            store.Save(new ProfileDocument(1, null, [new DeviceProfile("p1", "P", "dev", [binding])]));

            var loaded = Expect.Single(Expect.Single(store.Load()!.Profiles).Bindings);
            Assert.That(loaded.Action.EffectiveSettings.Get("path"), Is.EqualTo(@"C:\Tools\app.exe"));
            Assert.That(loaded.Action, Is.EqualTo(binding.Action));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ActionHostTests
{
    [Test]
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

        Assert.That(await switched.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo("two"));
        Assert.That(Expect.Single(action.Executions).Settings.Get("mode"), Is.EqualTo("next"));
    }

    [Test]
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

        Assert.That(action.Executions, Is.Empty);
    }
}
