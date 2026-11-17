using DeckHost.App.ViewModels;
using DeckHost.Core.Plugins;

namespace DeckHost.Tests;

/// <summary>The store's search and filters.</summary>
public class StoreSearchTests
{
    private const string ObsStudio = "OBS Studio";
    private const string Spotify = "Spotify Now Playing";
    private const string AnalogClock = "Analog Clock";
    private const string HueLights = "Hue Lights";
    private const int BigCatalog = 20_000;
    private const int SearchRuns = 20;
    private const int MillisecondsPerSearch = 30;
    private const string BigSearch = "thing 1999";

    private static StoreItemViewModel Item(string id, string name, string? author = null, string? description = null, string source = "DeckHost", bool installed = false) =>
        new(null!, new StoreEntry(id, name, author, description, source, null, null, null, null), installed);

    private static readonly IReadOnlyList<StoreItemViewModel> Items =
    [
        Item("obs.control", ObsStudio, "DeckHost", "Switch scenes and mute sources"),
        Item("spotify.now", Spotify, "Someone", "Shows the track on a key", "OpenAction"),
        Item("clock.analog", AnalogClock, "Clockmaker", "A clock with hands", "Elgato archive", installed: true),
        Item("lights.hue", HueLights, "Nobody", "Control your lights"),
    ];

    private static List<string> Names(string search, string source = PluginStoreViewModel.AllSources, string category = PluginStoreViewModel.AllCategories, string status = PluginStoreViewModel.AllStatuses) =>
        [.. PluginStoreViewModel.Filter(Items, search, source, category, status).Select(static item => item.Name)];

    private static double AverageMillisecondsOfASearchOverABigCatalog()
    {
        var many = Enumerable.Range(0, BigCatalog).Select(index => Item($"plugin.{index}", $"Plugin number {index}", "Author " + (index % 50), "Does thing " + index)).ToList();
        PluginStoreViewModel.Filter(many, BigSearch, PluginStoreViewModel.AllSources, PluginStoreViewModel.AllCategories, PluginStoreViewModel.AllStatuses); // builds the keys

        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var run = 0; run < SearchRuns; run++)
        {
            PluginStoreViewModel.Filter(many, BigSearch, PluginStoreViewModel.AllSources, PluginStoreViewModel.AllCategories, PluginStoreViewModel.AllStatuses);
        }

        return watch.ElapsedMilliseconds / (double)SearchRuns;
    }

    [TestCase("spotify", Spotify)]
    [TestCase("CLOCKMAKER", AnalogClock)]
    [TestCase("mute sources", ObsStudio)]
    [TestCase("lights.hue", HueLights)]
    public void Filter_SearchText_MatchesTheNameAuthorDescriptionOrIdIgnoringCase(string search, string expected)
    {
        // Arrange (the four store items)

        // Act
        var names = Names(search);

        // Assert
        Assert.That(names, Is.EqualTo(new[] { expected }));
    }

    [Test]
    public void Filter_EmptySearch_ListsEverything()
    {
        // Arrange (the four store items)

        // Act
        var names = Names("");

        // Assert
        Assert.That(names, Has.Count.EqualTo(Items.Count));
    }

    [Test]
    public void Filter_TwoWordsInDifferentFields_BothHaveToMatch()
    {
        // Arrange (the four store items)

        // Act
        var names = Names("clock hands");

        // Assert
        Assert.That(names, Is.EqualTo(new[] { AnalogClock }));
    }

    [Test]
    public void Filter_TwoWordsThatNeverMatchTogether_FindsNothing()
    {
        // Arrange (the four store items)

        // Act
        var names = Names("clock mute");

        // Assert
        Assert.That(names, Is.Empty);
    }

    [Test]
    public void Filter_SearchWithExtraSpaces_IgnoresThem()
    {
        // Arrange (the four store items)

        // Act
        var names = Names("  obs   scenes ");

        // Assert
        Assert.That(names, Is.EqualTo(new[] { ObsStudio }));
    }

    [Test]
    public void Filter_Source_KeepsOnlyThatSource()
    {
        // Arrange (the four store items)

        // Act
        var names = Names("", source: PluginStore.OpenActionSource);

        // Assert
        Assert.That(names, Is.EqualTo(new[] { Spotify }));
    }

    [Test]
    public void Filter_InstalledStatus_KeepsOnlyInstalledPlugins()
    {
        // Arrange (the four store items)

        // Act
        var names = Names("", status: PluginStoreViewModel.InstalledStatus);

        // Assert
        Assert.That(names, Is.EqualTo(new[] { AnalogClock }));
    }

    [Test]
    public void Filter_NotInstalledStatusForAnInstalledPlugin_FindsNothing()
    {
        // Arrange (the four store items)

        // Act
        var names = Names("clock", status: PluginStoreViewModel.NotInstalledStatus);

        // Assert
        Assert.That(names, Is.Empty);
    }

    [Test]
    public void Filter_BigCatalog_IsFast()
    {
        // Arrange (twenty thousand plugins)

        // Act
        var milliseconds = AverageMillisecondsOfASearchOverABigCatalog();

        // Assert
        Assert.That(milliseconds, Is.LessThan(MillisecondsPerSearch), "ms per search over 20,000 plugins");
    }
}

/// <summary>What the Plugins page says a device plugin supports.</summary>
public class DevicePluginDescriptionTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "deckhost-desc-" + Guid.NewGuid().ToString("N"));

    [Test]
    public void DescribeSupport_MiraBoxPluginWithNothingPluggedIn_SumsUpItsModels()
    {
        // Arrange
        using var mirabox = new DeckHost.Device.MiraBox.MiraBoxDevicePlugin(TempFolder());

        // Act
        var text = PluginCatalog.DescribeSupport(mirabox);

        // Assert
        Assert.That(text, Does.StartWith("Supports 11 models, such as StreamDock N4 Pro"));
    }

    [Test]
    public void DescribeSupport_ElgatoPluginWithNothingPluggedIn_SumsUpItsModels()
    {
        // Arrange
        using var elgato = new DeckHost.Device.Elgato.ElgatoDevicePlugin(TempFolder());

        // Act
        var text = PluginCatalog.DescribeSupport(elgato);

        // Assert
        Assert.That(text, Does.StartWith("Supports ").And.Contain("models, such as").And.Not.EqualTo("Supports ."));
    }
}
