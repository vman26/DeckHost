using DeckHost.Core.Plugins;

namespace DeckHost.Tests;

/// <summary>Finding newer versions of installed plugins in the store catalogs.</summary>
public sealed class PluginUpdaterTests
{
    [Theory]
    [InlineData("1.2.0", "1.10.0", -1)]
    [InlineData("v2.0", "2.0.0", 0)]
    [InlineData("2.0.0.14", "2.0.0.9", 1)]
    [InlineData("1.4.0-beta.2", "1.4.0", -1)]
    [InlineData("1.4.0-beta.10", "1.4.0-beta.2", 1)]
    [InlineData("1.4.0-alpha", "1.4.0-beta", -1)]
    [InlineData("1.0+build5", "1.0", 0)]
    [InlineData("1.0", null, 1)]
    public void Versions_compare_as_plugins_write_them(string? left, string? right, int expected) =>
        Assert.Equal(expected, Math.Sign(PluginVersion.Compare(left, right)));

    [Fact]
    public void An_installed_plugin_with_a_newer_download_has_an_update()
    {
        var updates = PluginUpdater.FindUpdates(
            [Installed("com.example.clock", "1.0.0"), Installed("com.example.same", "2.0")],
            [Offer("com.example.clock", "1.2.0"), Offer("com.example.same", "2.0.0"), Offer("com.example.other", "9.0")]);

        var update = Assert.Single(updates);
        Assert.Equal(("com.example.clock", "1.0.0", "1.2.0"), (update.Id, update.FromVersion, update.ToVersion));
    }

    [Fact]
    public void The_highest_offer_of_several_catalogs_wins()
    {
        var update = Assert.Single(PluginUpdater.FindUpdates(
            [Installed("COM.example.clock", "1.0")],
            [Offer("com.example.clock", "1.1"), Offer("com.example.clock", "1.3"), Offer("com.example.clock", "1.2")]));

        Assert.Equal("1.3", update.ToVersion);
    }

    [Fact]
    public void System_plugins_paid_plugins_and_entries_without_a_download_get_no_update()
    {
        var updates = PluginUpdater.FindUpdates(
            [Installed("deckhost.plugin.builtin", "0.1", system: true), Installed("paid", "1.0"), Installed("nofile", "1.0")],
            [Offer("deckhost.plugin.builtin", "9.0"), Offer("paid", "2.0") with { PurchaseUrl = "https://example.com/buy" }, Offer("nofile", "2.0") with { DownloadUrl = null }]);

        Assert.Empty(updates);
    }

    private static PluginEntry Installed(string id, string version, bool system = false) =>
        new(id, id, version, null, null, PluginKind.Application, system, true, !system, !system, 1, null);

    private static StoreEntry Offer(string id, string version) =>
        new(id, id, null, null, "test", version, null, null, $"https://example.com/{id}.zip");
}

/// <summary>The Plugins page's update panel: check, update all, failures, the automatic setting.</summary>
public sealed class PluginUpdatesViewModelTests
{
    private static PluginUpdate Update(string id, string to) =>
        new(new PluginEntry(id, id, "1.0", null, null, PluginKind.Application, false, true, true, true, 1, null),
            new StoreEntry(id, id, null, null, "test", to, null, null, $"https://example.com/{id}.zip"));

    [Fact]
    public async Task Update_all_installs_every_update_and_reports_failures()
    {
        var installed = new List<string>();
        var settings = new DeckHost.App.Platform.AppSettingsData();
        var updates = new DeckHost.App.ViewModels.PluginUpdatesViewModel(
            () => Task.FromResult<IReadOnlyList<PluginUpdate>>([Update("a", "2.0"), Update("b", "3.0")]),
            (update, _) =>
            {
                if (update.Id == "b")
                {
                    throw new IOException("disk full");
                }

                installed.Add(update.Id);
                return Task.FromResult(update.Installed with { Version = update.ToVersion });
            },
            () => settings,
            saved => settings = saved);

        await updates.CheckAsync(manual: true);
        Assert.Equal(2, updates.Available.Count);
        Assert.Equal("Update 2 plugins", updates.UpdateAllLabel);
        Assert.NotNull(updates.For("A"));

        await updates.UpdateAllAsync();

        Assert.Equal(["a"], installed);
        Assert.Equal("b", Assert.Single(updates.Available).Id);
        Assert.Contains("disk full", updates.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void Installing_automatically_is_saved()
    {
        var settings = new DeckHost.App.Platform.AppSettingsData();
        var updates = new DeckHost.App.ViewModels.PluginUpdatesViewModel(
            () => Task.FromResult<IReadOnlyList<PluginUpdate>>([]), (update, _) => Task.FromResult(update.Installed), () => settings, saved => settings = saved);

        updates.InstallAutomatically = true;

        Assert.True(settings.AutoUpdatePlugins);
    }
}
