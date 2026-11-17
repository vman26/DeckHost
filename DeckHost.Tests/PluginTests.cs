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
    private const string ObsId = "deckhost.plugin.obs";
    private const string Required = "system.required";
    private const string Optional = "system.optional";
    private const string GoodId = "good.id";
    private const string GoodManifest = """{"id":"good.id","name":"x","version":"1","assembly":"x.dll"}""";
    private const int ChangesInTheInstallLifecycle = 4;

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

    private static bool IsEnabled(PluginCatalog catalog, string id) => catalog.EnabledApplicationPlugins.Any(plugin => plugin.Id == id);

    [Test]
    public void SetEnabled_OptionalSystemPlugin_DisablesIt()
    {
        // Arrange
        using var catalog = CreateCatalog();

        // Act
        catalog.SetEnabled(Optional, false);

        // Assert
        Assert.That(IsEnabled(catalog, Optional), Is.False);
    }

    [Test]
    public void SetEnabled_RequiredSystemPlugin_Throws()
    {
        // Arrange
        using var catalog = CreateCatalog();

        // Act
        var disable = () => catalog.SetEnabled(Required, false);

        // Assert
        Assert.Throws<PluginException>(() => disable());
    }

    [Test]
    public void Uninstall_SystemPlugin_Throws()
    {
        // Arrange
        using var catalog = CreateCatalog();

        // Act
        var uninstall = () => catalog.Uninstall(Optional);

        // Assert
        Assert.Throws<PluginException>(() => uninstall());
    }

    [Test]
    public void Load_AfterDisablingAPluginAndRestarting_KeepsItDisabled()
    {
        // Arrange
        using (var catalog = CreateCatalog())
        {
            catalog.SetEnabled(Optional, false);
        }

        // Act
        using var reloaded = CreateCatalog();

        // Assert
        Assert.That(IsEnabled(reloaded, Optional), Is.False);
    }

    private static void AssertObsPackageIsBuilt() =>
        Assert.That(File.Exists(ObsPackagePath), Is.True, $"Build the OBS plugin first: {ObsPackagePath}");

    [Test]
    public void Install_ObsPackage_ListsAnUninstallablePlugin()
    {
        // Arrange
        AssertObsPackageIsBuilt();
        using var catalog = CreateCatalog();

        // Act
        var entry = catalog.Install(ObsPackagePath);

        // Assert
        Assert.That(entry.Id, Is.EqualTo(ObsId));
        Assert.That(entry.CanUninstall, Is.True);
    }

    [Test]
    public void Install_ObsPackage_EnablesItsActionsWithTheirSettings()
    {
        // Arrange
        AssertObsPackageIsBuilt();
        using var catalog = CreateCatalog();

        // Act
        catalog.Install(ObsPackagePath);

        // Assert
        var plugin = Expect.Single(catalog.EnabledApplicationPlugins, candidate => candidate.Id == ObsId);
        Assert.That(plugin.Actions.Any(action => action.Id == "obs.scene" && action.Settings.Any(setting => setting.Key == "scene")), Is.True);
    }

    [Test]
    public void SetEnabled_InstalledPlugin_DisablesAndEnablesIt()
    {
        // Arrange
        AssertObsPackageIsBuilt();
        using var catalog = CreateCatalog();
        catalog.Install(ObsPackagePath);

        // Act
        catalog.SetEnabled(ObsId, false);
        var disabled = !IsEnabled(catalog, ObsId);
        catalog.SetEnabled(ObsId, true);

        // Assert
        Assert.That(disabled, Is.True);
        Assert.That(IsEnabled(catalog, ObsId), Is.True);
    }

    [Test]
    public void Uninstall_InstalledPlugin_DeletesItsFilesAtOnceAndUnlistsIt()
    {
        // Arrange
        AssertObsPackageIsBuilt();
        using var catalog = CreateCatalog();
        catalog.Install(ObsPackagePath);

        // Act (the assemblies are loaded from memory, so the files can be deleted right away)
        catalog.Uninstall(ObsId);

        // Assert
        Assert.That(Directory.Exists(Path.Combine(catalog.InstallDirectory, ObsId)), Is.False);
        Assert.That(catalog.Entries.Any(candidate => candidate.Id == ObsId), Is.False);
    }

    [Test]
    public void Changed_InstallDisableEnableUninstall_IsRaisedForEachStep()
    {
        // Arrange
        AssertObsPackageIsBuilt();
        using var catalog = CreateCatalog();
        var changes = 0;
        catalog.Changed += (_, _) => changes++;

        // Act
        var entry = catalog.Install(ObsPackagePath);
        catalog.SetEnabled(entry.Id, false);
        catalog.SetEnabled(entry.Id, true);
        catalog.Uninstall(entry.Id);

        // Assert
        Assert.That(changes, Is.EqualTo(ChangesInTheInstallLifecycle));
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
    public void Install_FirstVersion_HasNoPreviousVersion()
    {
        // Arrange
        using var catalog = CreateCatalog();

        // Act
        catalog.Install(ObsPackageWithVersion("1.0.0"));

        // Assert
        Assert.That(catalog.Entries.Single(entry => entry.Id == ObsId).PreviousVersion, Is.Null);
    }

    [Test]
    public void Install_Update_KeepsThePreviousVersion()
    {
        // Arrange
        using var catalog = CreateCatalog();
        catalog.Install(ObsPackageWithVersion("1.0.0"));

        // Act
        var updated = catalog.Install(ObsPackageWithVersion("1.1.0"));

        // Assert
        Assert.That(updated.Version, Is.EqualTo("1.1.0"));
        Assert.That(updated.PreviousVersion, Is.EqualTo("1.0.0"));
    }

    [Test]
    public void RollBack_AfterAnUpdate_GoesBackToThePreviousVersionAndKeepsTheNewOneToRollForward()
    {
        // Arrange
        using var catalog = CreateCatalog();
        catalog.Install(ObsPackageWithVersion("1.0.0"));
        catalog.Install(ObsPackageWithVersion("1.1.0"));

        // Act
        var back = catalog.RollBack(ObsId);

        // Assert
        Assert.That(back.Version, Is.EqualTo("1.0.0"));
        Assert.That(back.PreviousVersion, Is.EqualTo("1.1.0"));
        Assert.That(IsEnabled(catalog, ObsId), Is.True);
    }

    [Test]
    public void RollBack_AfterUninstalling_Throws()
    {
        // Arrange
        using var catalog = CreateCatalog();
        catalog.Install(ObsPackageWithVersion("1.0.0"));
        catalog.Install(ObsPackageWithVersion("1.1.0"));
        catalog.Uninstall(ObsId);

        // Act
        var rollBack = () => catalog.RollBack(ObsId);

        // Assert
        Assert.Throws<PluginException>(() => rollBack());
    }

    [Test]
    public void Install_PackageThatDeclaresPermissions_ListsThem()
    {
        // Arrange
        using var catalog = CreateCatalog();

        // Act
        var entry = catalog.Install(ObsPackagePath);

        // Assert
        Assert.That(entry.Permissions, Is.EqualTo(new[] { "network" }));
        Assert.That(PluginPermissions.Describe(entry.Permissions), Is.EqualTo("It can use the network."));
    }

    [Test]
    public void Check_ObsPackage_FindsNoProblem()
    {
        // Arrange
        AssertObsPackageIsBuilt();

        // Act
        var problems = PluginDiagnostics.Check(ObsPackagePath);

        // Assert
        Assert.That(problems, Is.Empty);
    }

    [Test]
    public void Install_ObsPackage_HasNoWarnings()
    {
        // Arrange
        using var catalog = CreateCatalog();

        // Act
        var entry = catalog.Install(ObsPackagePath);

        // Assert
        Assert.That(entry.Warnings ?? [], Is.Empty);
    }

    private IReadOnlyList<string> ProblemsOfABrokenPackage() => [.. PluginDiagnostics.Check(CreatePackage(
        ("plugin.json", """{"id":"good.id","name":"x","version":"one","assembly":"x.dll","icon":"icon.png","permissions":["network","telepathy"]}"""),
        ("DeckHost.Contracts.dll", "x"))).Select(static problem => problem.ToString())];

    [Test]
    public void Check_MissingAssembly_IsAnError()
    {
        // Arrange
        var problems = ProblemsOfABrokenPackage();

        // Act
        var found = problems.Any(static problem => problem.StartsWith("error:", StringComparison.Ordinal) && problem.Contains("x.dll", StringComparison.Ordinal));

        // Assert
        Assert.That(found, Is.True);
    }

    [TestCase("'one'")]
    [TestCase("icon.png")]
    [TestCase("telepathy")]
    [TestCase("DeckHost.Contracts.dll")]
    [TestCase("author")]
    public void Check_BrokenPackage_MentionsWhatIsWrong(string mention)
    {
        // Arrange
        var problems = ProblemsOfABrokenPackage();

        // Act
        var found = problems.Any(problem => problem.Contains(mention, StringComparison.Ordinal));

        // Assert
        Assert.That(found, Is.True);
    }

    [Test]
    public void Load_AfterInstallingAndRestarting_LoadsThePluginAgain()
    {
        // Arrange
        using (var catalog = CreateCatalog())
        {
            catalog.Install(ObsPackagePath);
        }

        // Act
        using var reloaded = CreateCatalog();

        // Assert
        Assert.That(IsEnabled(reloaded, ObsId), Is.True);
    }

    [TestCase("""{"id":"Bad Id!","name":"x","version":"1","assembly":"x.dll"}""", "plugin id")]
    [TestCase("""{"id":"good.id","name":"x","version":"1","assembly":"../evil.dll"}""", "assembly")]
    [TestCase("""{"id":"good.id","name":"x","version":"1","assembly":"x.dll","hostApiVersion":99}""", "host API")]
    [TestCase("""{"id":"system.optional","name":"x","version":"1","assembly":"x.dll"}""", "system plugin")]
    public void Install_InvalidPackage_IsRejectedWithAMessage(string manifest, string expectedMessage)
    {
        // Arrange
        using var catalog = CreateCatalog();
        var package = CreatePackage(("plugin.json", manifest));

        // Act
        var install = () => catalog.Install(package);

        // Assert
        var error = Assert.Throws<PluginException>(() => install());
        Assert.That(error.Message, Does.Contain(expectedMessage).IgnoreCase);
    }

    [TestCase("""{"id":"Bad Id!","name":"x","version":"1","assembly":"x.dll"}""")]
    [TestCase("""{"id":"good.id","name":"x","version":"1","assembly":"../evil.dll"}""")]
    [TestCase("""{"id":"good.id","name":"x","version":"1","assembly":"x.dll","hostApiVersion":99}""")]
    public void Install_InvalidPackage_LeavesNoFolderBehind(string manifest)
    {
        // Arrange
        using var catalog = CreateCatalog();
        var package = CreatePackage(("plugin.json", manifest));

        // Act
        Assert.Throws<PluginException>(() => catalog.Install(package));

        // Assert
        Assert.That(Directory.Exists(Path.Combine(catalog.InstallDirectory, GoodId)), Is.False);
    }

    [Test]
    public void Install_PackageThatEscapesTheInstallFolder_IsRejected()
    {
        // Arrange
        using var catalog = CreateCatalog();
        var package = CreatePackage(("plugin.json", GoodManifest), ("../outside.dll", "x"));

        // Act
        var install = () => catalog.Install(package);

        // Assert
        Assert.Throws<PluginException>(() => install());
    }

    [Test]
    public void Install_PackageThatEscapesTheInstallFolder_WritesNothingOutside()
    {
        // Arrange
        using var catalog = CreateCatalog();
        var package = CreatePackage(("plugin.json", GoodManifest), ("../outside.dll", "x"));

        // Act
        Assert.Throws<PluginException>(() => catalog.Install(package));

        // Assert
        Assert.That(File.Exists(Path.Combine(_root, "outside.dll")), Is.False);
    }

    [Test]
    public void Install_PackageWithoutAPluginAssembly_IsRejected()
    {
        // Arrange
        using var catalog = CreateCatalog();
        var package = CreatePackage(("plugin.json", GoodManifest));

        // Act
        var install = () => catalog.Install(package);

        // Assert
        Assert.Throws<PluginException>(() => install());
    }

    [Test]
    public void Install_PackageWithoutAPluginAssembly_IsNotListed()
    {
        // Arrange
        using var catalog = CreateCatalog();
        var package = CreatePackage(("plugin.json", GoodManifest));

        // Act
        Assert.Throws<PluginException>(() => catalog.Install(package));

        // Assert
        Assert.That(catalog.Entries.Any(entry => entry.Id == GoodId), Is.False);
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
    private const string PathKey = "path";
    private const string AppPath = @"C:\app.exe";

    private static ActionSettings AppSettings() => new([new(PathKey, AppPath), new("arguments", "--x")]);

    [Test]
    public void With_NullValueForAnUnsetKey_IsEqualToTheOriginal()
    {
        // Arrange
        var settings = AppSettings();

        // Act
        var changed = settings.With("other", null);

        // Assert
        Assert.That(changed, Is.EqualTo(settings));
    }

    [Test]
    public void With_AnotherValue_IsNotEqualToTheOriginal()
    {
        // Arrange
        var settings = AppSettings();

        // Act
        var changed = settings.With(PathKey, "y");

        // Assert
        Assert.That(changed, Is.Not.EqualTo(settings));
    }

    [Test]
    public void Deserialize_SerializedSettings_AreEqualToTheOriginal()
    {
        // Arrange
        var settings = AppSettings();

        // Act
        var loaded = JsonSerializer.Deserialize<ActionSettings>(JsonSerializer.Serialize(settings));

        // Assert
        Assert.That(loaded, Is.EqualTo(settings));
    }

    [Test]
    public void GetInt_ValueWrittenAsText_IsTheNumber()
    {
        // Arrange
        var settings = new ActionSettings([new("n", "12")]);

        // Act
        var number = settings.GetInt("n", 0);

        // Assert
        Assert.That(number, Is.EqualTo(12));
    }

    [Test]
    public void Get_MissingKey_IsTheFallback()
    {
        // Arrange
        var settings = ActionSettings.Empty;

        // Act
        var value = settings.Get("missing", "fallback");

        // Assert
        Assert.That(value, Is.EqualTo("fallback"));
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"deckhost-{Guid.NewGuid():N}.json");

    private static ControlBinding LaunchBinding()
    {
        var reference = Bindings.Reference("p", "launch") with { Settings = new ActionSettings([new(PathKey, @"C:\Tools\app.exe")]) };
        return new ControlBinding("key.0", ProfileActionKind.Single, reference, new ControlAppearance(), []);
    }

    private static ControlBinding SavedAndLoaded(ControlBinding binding, string path)
    {
        var store = new ProfileStore(path);
        store.Save(new ProfileDocument(1, null, [new DeviceProfile("p1", "P", "dev", [binding])]));
        return Expect.Single(Expect.Single(store.Load()!.Profiles).Bindings);
    }

    [Test]
    public void Load_BindingSavedWithSettings_KeepsTheSettings()
    {
        // Arrange
        var path = TempFile();

        // Act
        var loaded = SavedAndLoaded(LaunchBinding(), path);

        // Assert
        Assert.That(loaded.Action.EffectiveSettings.Get(PathKey), Is.EqualTo(@"C:\Tools\app.exe"));
        File.Delete(path);
    }

    [Test]
    public void Load_BindingSavedWithSettings_KeepsTheWholeActionReference()
    {
        // Arrange
        var path = TempFile();
        var binding = LaunchBinding();

        // Act
        var loaded = SavedAndLoaded(binding, path);

        // Assert
        Assert.That(loaded.Action, Is.EqualTo(binding.Action));
        File.Delete(path);
    }
}

public class ActionHostTests
{
    private const string PluginId = "p";
    private const string FirstKey = "key.0";
    private const string Next = "next";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static DeviceProfile[] TwoProfilesWithASwitchKey(ActionReference reference) =>
    [
        new DeviceProfile("one", "One", FakeDevicePlugin.DeviceId, [new ControlBinding(FirstKey, ProfileActionKind.Single, reference, new ControlAppearance(), [])]),
        new DeviceProfile("two", "Two", FakeDevicePlugin.DeviceId, []),
    ];

    private static async Task<(FakeDevicePlugin Plugin, RecordingAction Action, DeviceHostService Host, VariableStore Store, TaskCompletionSource<string> Switched)> StartedWithASwitchActionAsync()
    {
        var plugin = new FakeDevicePlugin();
        var action = new RecordingAction("switch") { OnExecute = context => context.Host.ActivateRelativeProfile(context.DeviceId, 1) };
        var store = new VariableStore();
        var host = new DeviceHostService([plugin], [new TestApplicationPlugin(PluginId, action)], store);
        var reference = Bindings.Reference(PluginId, "switch") with { Settings = new ActionSettings([new("mode", Next)]) };
        host.SetProfiles(TwoProfilesWithASwitchKey(reference));
        var switched = new TaskCompletionSource<string>();
        host.ActiveProfileChanged += (_, profileId) => switched.TrySetResult(profileId);
        await host.StartAsync(CancellationToken.None);
        return (plugin, action, host, store, switched);
    }

    [Test]
    public async Task ReportInput_ActionThatSwitchesProfiles_ActivatesTheNextProfile()
    {
        // Arrange
        var (plugin, _, host, store, switched) = await StartedWithASwitchActionAsync();
        await using var _ = host;
        using var __ = store;

        // Act
        plugin.Context!.ReportInputEvent(FakeDevicePlugin.DeviceId, FirstKey, InputCapabilities.Press, 1);

        // Assert
        Assert.That(await switched.Task.WaitAsync(Patience), Is.EqualTo("two"));
    }

    [Test]
    public async Task ReportInput_BoundAction_ReceivesItsSettings()
    {
        // Arrange
        var (plugin, action, host, store, switched) = await StartedWithASwitchActionAsync();
        await using var _ = host;
        using var __ = store;

        // Act
        plugin.Context!.ReportInputEvent(FakeDevicePlugin.DeviceId, FirstKey, InputCapabilities.Press, 1);
        await switched.Task.WaitAsync(Patience);

        // Assert
        Assert.That(Expect.Single(action.Executions).Settings.Get("mode"), Is.EqualTo(Next));
    }

    [Test]
    public async Task SetApplicationPlugins_NoPlugins_UnbindsTheirActions()
    {
        // Arrange
        var plugin = new FakeDevicePlugin();
        var action = new RecordingAction("a");
        using var store = new VariableStore();
        await using var host = new DeviceHostService([plugin], [new TestApplicationPlugin(PluginId, action)], store);
        host.SetProfiles([new DeviceProfile("one", "One", FakeDevicePlugin.DeviceId, [Bindings.Single(FirstKey, PluginId, "a")])]);
        await host.StartAsync(CancellationToken.None);

        // Act
        host.SetApplicationPlugins([]);
        plugin.Context!.ReportInputEvent(FakeDevicePlugin.DeviceId, FirstKey, InputCapabilities.Press, 1);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(action.Executions, Is.Empty);
    }
}
