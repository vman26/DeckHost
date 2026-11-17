using DeckHost.App.ViewModels;
using DeckHost.Core.Plugins;

namespace DeckHost.Tests;

/// <summary>The store's search and filters.</summary>
public class StoreSearchTests
{
    private static StoreItemViewModel Item(string id, string name, string? author = null, string? description = null, string source = "DeckHost", bool installed = false) =>
        new(null!, new StoreEntry(id, name, author, description, source, null, null, null, null), installed);

    private static readonly IReadOnlyList<StoreItemViewModel> Items =
    [
        Item("obs.control", "OBS Studio", "DeckHost", "Switch scenes and mute sources"),
        Item("spotify.now", "Spotify Now Playing", "Someone", "Shows the track on a key", "OpenAction"),
        Item("clock.analog", "Analog Clock", "Clockmaker", "A clock with hands", "Elgato archive", installed: true),
        Item("lights.hue", "Hue Lights", "Nobody", "Control your lights"),
    ];

    private static List<string> Names(string search, string source = PluginStoreViewModel.AllSources, string category = PluginStoreViewModel.AllCategories, string status = PluginStoreViewModel.AllStatuses) =>
        [.. PluginStoreViewModel.Filter(Items, search, source, category, status).Select(static item => item.Name)];

    [Test]
    public void The_search_matches_the_name_author_description_and_id_ignoring_case()
    {
        Assert.That(Names("spotify"), Is.EqualTo(new[] { "Spotify Now Playing" }));
        Assert.That(Names("CLOCKMAKER"), Is.EqualTo(new[] { "Analog Clock" }));
        Assert.That(Names("mute sources"), Is.EqualTo(new[] { "OBS Studio" }));
        Assert.That(Names("lights.hue"), Is.EqualTo(new[] { "Hue Lights" }));
        Assert.That(Names(""), Has.Count.EqualTo(4));
    }

    [Test]
    public void Every_word_of_the_search_has_to_match_wherever_it_is()
    {
        Assert.That(Names("clock hands"), Is.EqualTo(new[] { "Analog Clock" }));
        Assert.That(Names("clock mute"), Is.Empty);
        Assert.That(Names("  obs   scenes "), Is.EqualTo(new[] { "OBS Studio" }));
    }

    [Test]
    public void The_source_and_status_narrow_the_search()
    {
        Assert.That(Names("", source: PluginStore.OpenActionSource), Is.EqualTo(new[] { "Spotify Now Playing" }));
        Assert.That(Names("", status: PluginStoreViewModel.InstalledStatus), Is.EqualTo(new[] { "Analog Clock" }));
        Assert.That(Names("clock", status: PluginStoreViewModel.NotInstalledStatus), Is.Empty);
    }

    [Test]
    public void A_big_catalog_is_filtered_quickly()
    {
        var many = Enumerable.Range(0, 20_000).Select(index => Item($"plugin.{index}", $"Plugin number {index}", "Author " + (index % 50), "Does thing " + index)).ToList();
        PluginStoreViewModel.Filter(many, "thing 1999", PluginStoreViewModel.AllSources, PluginStoreViewModel.AllCategories, PluginStoreViewModel.AllStatuses); // builds the keys

        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var run = 0; run < 20; run++)
        {
            PluginStoreViewModel.Filter(many, "thing 1999", PluginStoreViewModel.AllSources, PluginStoreViewModel.AllCategories, PluginStoreViewModel.AllStatuses);
        }

        Assert.That(watch.ElapsedMilliseconds / 20.0, Is.LessThan(30), "ms per search over 20,000 plugins");
    }
}

/// <summary>What the Plugins page says a device plugin supports.</summary>
public class DevicePluginDescriptionTests
{
    [Test]
    public void The_MiraBox_and_Elgato_plugins_sum_up_their_models_even_with_nothing_plugged_in()
    {
        using var mirabox = new DeckHost.Device.MiraBox.MiraBoxDevicePlugin(Path.Combine(Path.GetTempPath(), "deckhost-desc-" + Guid.NewGuid().ToString("N")));
        using var elgato = new DeckHost.Device.Elgato.ElgatoDevicePlugin(Path.Combine(Path.GetTempPath(), "deckhost-desc-" + Guid.NewGuid().ToString("N")));

        Assert.That(PluginCatalog.DescribeSupport(mirabox), Does.StartWith("Supports 11 models, such as StreamDock N4 Pro"));
        Assert.That(PluginCatalog.DescribeSupport(elgato), Does.StartWith("Supports ").And.Contain("models, such as").And.Not.EqualTo("Supports ."));
    }
}
