using DeckHost.App.Profiles;
using DeckHost.App.ViewModels;
using DeckHost.Core.Plugins;

namespace DeckHost.Tests;

public sealed class MissingPluginsTests
{
    private const string Discord = "com.elgato.discord";
    private const string DiscordMute = "com.elgato.discord.voice.mute";
    private const string DiscordDeafen = "com.elgato.discord.voice.deafen";
    private const string Weather = "com.example.weather";
    private const string WeatherNow = "com.example.weather.now";
    private const string DiscordName = "Discord";

    private static StoreEntry Entry(string id, string name, string? download = "https://example.com/plugin.zip") =>
        new(id, name, null, null, PluginStore.ElgatoArchiveSource, "1.0", null, null, download);

    [Test]
    public void FindMissingPlugins_ActionsOfAPluginThatIsntInstalled_ListsItOnceWithItsActions()
    {
        // Arrange
        StreamDeckActionUse[] actions = [new(DiscordMute), new(DiscordDeafen), new(DiscordMute)];

        // Act
        var missing = StreamDeckProfileImporter.FindMissingPlugins(actions, []);

        // Assert
        var plugin = Expect.Single(missing);
        Assert.That(plugin.Id, Is.EqualTo(Discord + ".voice"));
        Assert.That(plugin.ActionUuids, Is.EqualTo(new[] { DiscordMute, DiscordDeafen }));
    }

    [Test]
    public void FindMissingPlugins_ThePluginTheProfileNames_IsUsedWithItsName()
    {
        // Arrange
        StreamDeckActionUse[] actions = [new(DiscordMute, Discord, DiscordName)];

        // Act
        var missing = StreamDeckProfileImporter.FindMissingPlugins(actions, []);

        // Assert
        var plugin = Expect.Single(missing);
        Assert.That((plugin.Id, plugin.Name), Is.EqualTo((Discord, DiscordName)));
    }

    [Test]
    public void FindMissingPlugins_AnInstalledPluginWhoseIdPrefixesTheAction_IsLeftOut()
    {
        // Arrange
        StreamDeckActionUse[] actions = [new(DiscordMute), new(WeatherNow)];

        // Act
        var missing = StreamDeckProfileImporter.FindMissingPlugins(actions, [Discord.ToUpperInvariant()]);

        // Assert
        Assert.That(missing.Select(plugin => plugin.Id), Is.EqualTo(new[] { Weather }));
    }

    [Test]
    public void FindMissingPlugins_TheStreamDeckAppsOwnActions_AreLeftOut()
    {
        // Arrange
        StreamDeckActionUse[] actions = [new("com.elgato.streamdeck.soundboard.playaudio"), new("com.elgato.streamdeck.system.close")];

        // Act
        var missing = StreamDeckProfileImporter.FindMissingPlugins(actions, []);

        // Assert
        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void FindMissingPlugins_SeveralPlugins_MostUsedFirst()
    {
        // Arrange
        StreamDeckActionUse[] actions = [new(WeatherNow), new(DiscordMute, Discord), new(DiscordDeafen, Discord)];

        // Act
        var missing = StreamDeckProfileImporter.FindMissingPlugins(actions, []);

        // Assert
        Assert.That(missing.Select(plugin => plugin.Id), Is.EqualTo(new[] { Discord, Weather }));
    }

    [Test]
    public void FindInStore_AnEntryWhoseIdPrefixesAnAction_IsFound()
    {
        // Arrange
        var plugin = new MissingPlugin(Discord + ".voice", null, [DiscordMute]);
        StoreEntry[] entries = [Entry("com.elgato", "Too short"), Entry(Discord, DiscordName), Entry(Weather, "Weather")];

        // Act
        var entry = MissingPluginsViewModel.FindInStore(plugin, entries);

        // Assert
        Assert.That(entry?.Id, Is.EqualTo(Discord));
    }

    [Test]
    public void FindInStore_TwoEntriesWithTheId_PrefersTheOneThatDownloads()
    {
        // Arrange
        var plugin = new MissingPlugin(Discord, null, [DiscordMute]);
        StoreEntry[] entries = [Entry(Discord, "No download", download: null), Entry(Discord, DiscordName)];

        // Act
        var entry = MissingPluginsViewModel.FindInStore(plugin, entries);

        // Assert
        Assert.That(entry?.Name, Is.EqualTo(DiscordName));
    }

    [Test]
    public void FindInStore_NoMatchingEntry_IsNull()
    {
        // Arrange
        var plugin = new MissingPlugin(Weather, null, [WeatherNow]);

        // Act
        var entry = MissingPluginsViewModel.FindInStore(plugin, [Entry(Discord, DiscordName)]);

        // Assert
        Assert.That(entry, Is.Null);
    }

    [Test]
    public async Task ShowAsync_TheStoreListsOnePlugin_OffersItAndSaysTheOtherIsntThere()
    {
        // Arrange
        var store = new MissingPluginsViewModel(null, _ => Task.FromResult<(IReadOnlyList<StoreEntry>, IReadOnlyList<string>)>(([Entry(Discord, DiscordName)], [])));
        MissingPlugin[] plugins = [new(Discord, null, [DiscordMute]), new(Weather, null, [WeatherNow])];

        // Act
        await store.ShowAsync(plugins);

        // Assert
        Assert.That(store.Items.Select(item => (item.Name, item.IsInStore, item.IsNotInStore)), Is.EqualTo(new[] { (DiscordName, true, false), (Weather, false, true) }));
        Assert.That(store.Title, Is.EqualTo("This profile uses 2 plugins you don't have"));
    }

    [Test]
    public async Task ShowAsync_TheStoreIsOffline_ListsThePluginsAndSaysSo()
    {
        // Arrange
        var store = new MissingPluginsViewModel(null, _ => Task.FromResult<(IReadOnlyList<StoreEntry>, IReadOnlyList<string>)>(([], ["no network"])));

        // Act
        await store.ShowAsync([new MissingPlugin(Discord, DiscordName, [DiscordMute])]);

        // Assert
        var item = Expect.Single(store.Items);
        Assert.That((item.Name, item.IsInStore), Is.EqualTo((DiscordName, false)));
        Assert.That(store.StoreStatus, Does.Contain("couldn't be reached"));
    }

    [Test]
    public async Task ShowAsync_TheStoreThrows_KeepsThePluginsListed()
    {
        // Arrange
        var store = new MissingPluginsViewModel(null, _ => throw new HttpRequestException("offline"));

        // Act
        await store.ShowAsync([new MissingPlugin(Discord, null, [DiscordMute])]);

        // Assert
        Assert.That((store.HasItems, store.HasStoreStatus, store.IsLoadingStore), Is.EqualTo((true, true, false)));
    }
}
