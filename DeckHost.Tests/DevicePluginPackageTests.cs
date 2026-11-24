using DeckHost.Core.Devices;
using DeckHost.Core.Plugins;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>Device plugins that come from packages join and leave the running host, and plugin.json says what kind a package is.</summary>
public class DevicePluginPackageTests
{
    private const string AddedDeviceId = "added.device";
    private const string FirstDeviceId = FakeDevicePlugin.DeviceId;

    private static DeviceHostService CreateHost(VariableStore store, params FakeDevicePlugin[] plugins) =>
        new(plugins, [], store);

    [Test]
    public async Task AddDevicePluginAsync_AfterStart_ListsItsDevicesAndStartsIt()
    {
        // Arrange
        using var store = new VariableStore();
        await using var host = CreateHost(store, new FakeDevicePlugin());
        await host.StartAsync(CancellationToken.None);
        var added = new FakeDevicePlugin(deviceId: AddedDeviceId);

        // Act
        await host.AddDevicePluginAsync(added);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(host.Devices.Select(static device => device.Id), Is.EquivalentTo(new[] { FirstDeviceId, AddedDeviceId }));
        Assert.That(added.Context, Is.Not.Null);
    }

    [Test]
    public async Task AddDevicePluginAsync_BeforeStart_StartsItWithTheOthers()
    {
        // Arrange
        using var store = new VariableStore();
        await using var host = CreateHost(store, new FakeDevicePlugin());
        var added = new FakeDevicePlugin(deviceId: AddedDeviceId);

        // Act
        await host.AddDevicePluginAsync(added);
        await host.StartAsync(CancellationToken.None);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(host.Devices.Select(static device => device.Id), Does.Contain(AddedDeviceId));
        Assert.That(added.Context, Is.Not.Null);
    }

    [Test]
    public async Task RemoveDevicePluginAsync_AfterStart_BlanksAndUnlistsItsDevicesWithoutDisposingIt()
    {
        // Arrange
        using var store = new VariableStore();
        await using var host = CreateHost(store, new FakeDevicePlugin());
        await host.StartAsync(CancellationToken.None);
        var added = new FakeDevicePlugin(deviceId: AddedDeviceId);
        await host.AddDevicePluginAsync(added);

        // Act
        await host.RemoveDevicePluginAsync(added);
        await host.WhenIdleAsync();

        // Assert
        Assert.That(host.Devices.Select(static device => device.Id), Is.EqualTo(new[] { FirstDeviceId }));
        Assert.That(added.ClearCount, Is.EqualTo(1));
        Assert.That(added.Disposed, Is.False);
    }

    [Test]
    public async Task DisposeAsync_WithAnAddedPlugin_LeavesItToWhoeverLoadedIt()
    {
        // Arrange
        using var store = new VariableStore();
        var owned = new FakeDevicePlugin();
        var host = CreateHost(store, owned);
        await host.StartAsync(CancellationToken.None);
        var added = new FakeDevicePlugin(deviceId: AddedDeviceId);
        await host.AddDevicePluginAsync(added);

        // Act
        await host.DisposeAsync();

        // Assert
        Assert.That(owned.Disposed, Is.True);
        Assert.That(added.Disposed, Is.False);
    }

    [Test]
    public async Task AddDevicePluginAsync_AfterDispose_CompletesWithoutThrowing()
    {
        // Arrange
        using var store = new VariableStore();
        var host = CreateHost(store, new FakeDevicePlugin());
        await host.DisposeAsync();

        // Act
        var add = host.AddDevicePluginAsync(new FakeDevicePlugin(deviceId: AddedDeviceId));

        // Assert
        Assert.That(async () => await add, Throws.Nothing);
    }

    [Test]
    public void Enumerate_AfterAddAndRemove_ListsBuiltInThenLoadedPlugins()
    {
        // Arrange
        var builtIn = new FakeDevicePlugin();
        var loaded = new FakeDevicePlugin(deviceId: AddedDeviceId);
        var live = new LiveDevicePlugins([builtIn]);

        // Act
        live.Add(loaded);
        live.Add(loaded);
        var withLoaded = live.ToList();
        live.Remove(loaded);

        // Assert
        Assert.That(withLoaded, Is.EqualTo(new[] { builtIn, loaded }));
        Assert.That(live.ToList(), Is.EqualTo(new[] { builtIn }));
    }

    [Test]
    public void Parse_WithKindDevice_IsADevicePlugin()
    {
        // Arrange
        const string json = """{ "id": "deckhost.device.test", "name": "Test", "version": "1.0.0", "assembly": "Test.dll", "kind": "device" }""";

        // Act
        var manifest = PluginManifest.Parse(json);

        // Assert
        Assert.That(manifest.IsDevice, Is.True);
    }

    [Test]
    public void Parse_WithoutKind_IsAnApplicationPlugin()
    {
        // Arrange
        const string json = """{ "id": "deckhost.plugin.test", "name": "Test", "version": "1.0.0", "assembly": "Test.dll" }""";

        // Act
        var manifest = PluginManifest.Parse(json);

        // Assert
        Assert.That(manifest.IsDevice, Is.False);
    }

    [Test]
    public void Parse_WithUnknownKind_Throws()
    {
        // Arrange
        const string json = """{ "id": "deckhost.plugin.test", "name": "Test", "version": "1.0.0", "assembly": "Test.dll", "kind": "gadget" }""";

        // Act / Assert
        Assert.That(() => PluginManifest.Parse(json), Throws.TypeOf<PluginException>().With.Message.Contains("kind"));
    }

    [Test]
    public void Load_WithABundledDevicePluginWhoseAssemblyIsMissing_ReportsTheErrorAndKeepsTheEntry()
    {
        // Arrange
        var root = Directory.CreateTempSubdirectory("deckhost-bundled-test").FullName;
        try
        {
            var bundled = Path.Combine(root, "bundled", "deckhost.device.test");
            Directory.CreateDirectory(bundled);
            File.WriteAllText(
                Path.Combine(bundled, PluginManifest.FileName),
                """{ "id": "deckhost.device.test", "name": "Test", "version": "1.0.0", "assembly": "Test.dll", "kind": "device" }""");
            using var catalog = new PluginCatalog(Path.Combine(root, "plugins"), [], [], [])
            {
                BundledDirectory = Path.Combine(root, "bundled"),
            };

            // Act
            catalog.Load();

            // Assert
            var entry = Expect.Single(catalog.Entries.Where(static entry => entry.Id == "deckhost.device.test"));
            Assert.That(entry.Kind, Is.EqualTo(PluginKind.Device));
            Assert.That(entry.Error, Is.Not.Null);
            Assert.That(entry.CanUninstall, Is.False);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
