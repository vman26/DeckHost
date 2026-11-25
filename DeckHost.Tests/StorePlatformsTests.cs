using System.Text.Json.Nodes;
using DeckHost.App.ViewModels;
using DeckHost.Core.Plugins;

namespace DeckHost.Tests;

/// <summary>Which systems a store plugin is made for: read from catalogs, manifests and the platforms index, and filtered on.</summary>
public class StorePlatformsTests
{
    private const string PluginId = "com.example.clock";
    private const string Elsewhere = "com.example.elsewhere";
    private const string Unknown = "com.example.unknown";

    private static string Other => PluginPlatforms.Current == PluginPlatforms.Windows ? PluginPlatforms.Linux : PluginPlatforms.Windows;

    private static StoreItemViewModel Item(string id, IReadOnlyList<string>? platforms, bool installed = false) =>
        new(null!, new StoreEntry(id, id, null, null, PluginStore.OpenActionSource, null, null, null, null) { Platforms = platforms }, installed);

    private static readonly IReadOnlyList<StoreItemViewModel> Items =
    [
        Item(PluginId, [PluginPlatforms.Current]),
        Item(Elsewhere, [Other]),
        Item(Unknown, null),
    ];

    private static List<string> Ids(string system) =>
        [.. PluginStoreViewModel.Filter(Items, string.Empty, PluginStoreViewModel.AllSources, PluginStoreViewModel.AllCategories, PluginStoreViewModel.AllStatuses, system).Select(static item => item.Entry.Id)];

    [Test]
    public void From_StreamDeckOsObjects_GivesTheSystemsInDisplayOrder()
    {
        // Arrange
        var os = JsonNode.Parse("""[{"Platform": "mac", "MinimumVersion": "10.15"}, {"Platform": "windows"}]""");

        // Act
        var platforms = PluginPlatforms.From(os);

        // Assert
        Assert.That(platforms, Is.EqualTo(new[] { PluginPlatforms.Windows, PluginPlatforms.MacOS }));
    }

    [Test]
    public void From_NothingItKnows_IsNullNotEmpty()
    {
        // Arrange
        var list = JsonNode.Parse("""["amiga"]""");

        // Act
        var platforms = PluginPlatforms.From(list);

        // Assert
        Assert.That(platforms, Is.Null);
    }

    [Test]
    public void List_ThreeSystems_ReadsLikeASentence()
    {
        // Act
        var text = PluginPlatforms.List(PluginPlatforms.All);

        // Assert
        Assert.That(text, Is.EqualTo("Windows, macOS and Linux"));
    }

    [Test]
    public void ParseDeckHostCatalog_EntryWithPlatforms_KeepsThem()
    {
        // Arrange
        const string catalog = """{"com.example.clock": {"name": "Clock", "platforms": ["linux", "windows"]}}""";

        // Act
        var entry = Expect.Single(PluginStore.ParseDeckHostCatalog(catalog));

        // Assert
        Assert.That(entry.Platforms, Is.EqualTo(new[] { PluginPlatforms.Windows, PluginPlatforms.Linux }));
    }

    [Test]
    public void WithPlatforms_EntryWithoutSystems_TakesTheIndexs()
    {
        // Arrange
        var index = PluginStore.ParsePlatformsIndex("""{"plugins": {"com.example.clock": {"version": "1.0", "platforms": ["mac"]}}}""");
        var entry = new StoreEntry(PluginId, "Clock", null, null, PluginStore.OpenActionSource, null, null, null, null);

        // Act
        var filled = PluginStore.WithPlatforms(entry, index);

        // Assert
        Assert.That(filled.Platforms, Is.EqualTo(new[] { PluginPlatforms.MacOS }));
    }

    [Test]
    public void WithPlatforms_EntryWithItsOwnSystems_KeepsThem()
    {
        // Arrange
        var index = PluginStore.ParsePlatformsIndex("""{"plugins": {"com.example.clock": {"platforms": ["mac"]}}}""");
        var entry = new StoreEntry(PluginId, "Clock", null, null, PluginStore.DeckHostSource, null, null, null, null) { Platforms = [PluginPlatforms.Linux] };

        // Act
        var filled = PluginStore.WithPlatforms(entry, index);

        // Assert
        Assert.That(filled.Platforms, Is.EqualTo(new[] { PluginPlatforms.Linux }));
    }

    [Test]
    public void Filter_WorksHere_HidesOnlyPluginsMadeForOtherSystems()
    {
        // Act
        var ids = Ids(PluginStoreViewModel.WorksHereSystem);

        // Assert
        Assert.That(ids, Is.EqualTo(new[] { PluginId, Unknown }));
    }

    [Test]
    public void Filter_MadeForThisSystem_ShowsOnlyTheKnownOnes()
    {
        // Act
        var ids = Ids(PluginStoreViewModel.ListedHereSystem);

        // Assert
        Assert.That(ids, Is.EqualTo(new[] { PluginId }));
    }

    [Test]
    public void Filter_AnInstalledPluginMadeForAnotherSystem_StaysVisible()
    {
        // Arrange
        var installed = Item(Elsewhere, [Other], installed: true);

        // Act
        var shown = PluginStoreViewModel.Filter([installed], string.Empty, PluginStoreViewModel.AllSources, PluginStoreViewModel.AllCategories, PluginStoreViewModel.AllStatuses, PluginStoreViewModel.ListedHereSystem);

        // Assert
        Assert.That(shown, Has.Count.EqualTo(1));
    }

    [Test]
    public void CanInstall_PluginMadeForAnotherSystem_IsFalse()
    {
        // Arrange
        var item = new StoreItemViewModel(null!, new StoreEntry(Elsewhere, "Elsewhere", null, null, PluginStore.OpenActionSource, null, null, null, "https://example.com/p.zip") { Platforms = [Other] }, isInstalled: false);

        // Act
        var canInstall = item.CanInstall;

        // Assert
        Assert.That((canInstall, item.IsNotMadeForThisSystem), Is.EqualTo((false, true)));
    }

    [Test]
    public void LoadLocalSource_StreamDeckPackage_ReadsItsSystemsFromTheManifest()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory("deckhost-platforms-test").FullName;
        try
        {
            var package = Path.Combine(folder, "clock.streamDeckPlugin");
            using (var zip = System.IO.Compression.ZipFile.Open(package, System.IO.Compression.ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry($"{PluginId}.sdPlugin/manifest.json").Open()))
            {
                writer.Write("""{"Name": "Clock", "OS": [{"Platform": "windows"}]}""");
            }

            // Act
            var entry = Expect.Single(PluginStore.LoadLocalSource(folder));

            // Assert
            Assert.That(entry.Platforms, Is.EqualTo(new[] { PluginPlatforms.Windows }));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
