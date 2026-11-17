using DeckHost.Core.Plugins;

namespace DeckHost.Tests;

/// <summary>Finding newer versions of installed plugins in the store catalogs.</summary>
public sealed class PluginUpdaterTests
{
    private const string Clock = "com.example.clock";
    private const string Same = "com.example.same";
    private const string Other = "com.example.other";
    private const string BuiltIn = "deckhost.plugin.builtin";

    [TestCase("1.2.0", "1.10.0", -1)]
    [TestCase("v2.0", "2.0.0", 0)]
    [TestCase("2.0.0.14", "2.0.0.9", 1)]
    [TestCase("1.4.0-beta.2", "1.4.0", -1)]
    [TestCase("1.4.0-beta.10", "1.4.0-beta.2", 1)]
    [TestCase("1.4.0-alpha", "1.4.0-beta", -1)]
    [TestCase("1.0+build5", "1.0", 0)]
    [TestCase("1.0", null, 1)]
    public void Compare_TwoVersionsAsPluginsWriteThem_OrdersThem(string? left, string? right, int expected)
    {
        // Arrange (the two versions)

        // Act
        var order = Math.Sign(PluginVersion.Compare(left, right));

        // Assert
        Assert.That(order, Is.EqualTo(expected));
    }

    private static IReadOnlyList<PluginUpdate> UpdatesForAClockAndASameVersion() => PluginUpdater.FindUpdates(
        [Installed(Clock, "1.0.0"), Installed(Same, "2.0")],
        [Offer(Clock, "1.2.0"), Offer(Same, "2.0.0"), Offer(Other, "9.0")]);

    [Test]
    public void FindUpdates_InstalledPluginWithANewerDownload_HasAnUpdate()
    {
        // Arrange (a clock 1.0.0 installed, 1.2.0 offered)

        // Act
        var updates = UpdatesForAClockAndASameVersion();

        // Assert
        var update = Expect.Single(updates);
        Assert.That((update.Id, update.FromVersion, update.ToVersion), Is.EqualTo((Clock, "1.0.0", "1.2.0")));
    }

    [Test]
    public void FindUpdates_PluginThatIsNotInstalled_GetsNoUpdate()
    {
        // Arrange (an offer for a plugin nobody installed)

        // Act
        var updates = UpdatesForAClockAndASameVersion();

        // Assert
        Assert.That(updates.Select(static update => update.Id), Does.Not.Contain(Other));
    }

    [Test]
    public void FindUpdates_SeveralCatalogsOffering_TheHighestOfferWins()
    {
        // Arrange
        var installed = new[] { Installed("COM.example.clock", "1.0") };
        var offers = new[] { Offer(Clock, "1.1"), Offer(Clock, "1.3"), Offer(Clock, "1.2") };

        // Act
        var update = Expect.Single(PluginUpdater.FindUpdates(installed, offers));

        // Assert
        Assert.That(update.ToVersion, Is.EqualTo("1.3"));
    }

    [Test]
    public void FindUpdates_SystemPlugin_GetsNoUpdate()
    {
        // Arrange
        var installed = new[] { Installed(BuiltIn, "0.1", system: true) };

        // Act
        var updates = PluginUpdater.FindUpdates(installed, [Offer(BuiltIn, "9.0")]);

        // Assert
        Assert.That(updates, Is.Empty);
    }

    [Test]
    public void FindUpdates_PaidPlugin_GetsNoUpdate()
    {
        // Arrange
        var installed = new[] { Installed("paid", "1.0") };

        // Act
        var updates = PluginUpdater.FindUpdates(installed, [Offer("paid", "2.0") with { PurchaseUrl = "https://example.com/buy" }]);

        // Assert
        Assert.That(updates, Is.Empty);
    }

    [Test]
    public void FindUpdates_EntryWithoutADownload_GetsNoUpdate()
    {
        // Arrange
        var installed = new[] { Installed("nofile", "1.0") };

        // Act
        var updates = PluginUpdater.FindUpdates(installed, [Offer("nofile", "2.0") with { DownloadUrl = null }]);

        // Assert
        Assert.That(updates, Is.Empty);
    }

    private static PluginEntry Installed(string id, string version, bool system = false) =>
        new(id, id, version, null, null, PluginKind.Application, system, true, !system, !system, 1, null);

    private static StoreEntry Offer(string id, string version) =>
        new(id, id, null, null, "test", version, null, null, $"https://example.com/{id}.zip");
}

/// <summary>The Plugins page's update panel: check, update all, failures, the automatic setting.</summary>
public sealed class PluginUpdatesViewModelTests
{
    private const string FailingId = "b";
    private const string DiskFull = "disk full";

    private static PluginUpdate Update(string id, string to) =>
        new(new PluginEntry(id, id, "1.0", null, null, PluginKind.Application, false, true, true, true, 1, null),
            new StoreEntry(id, id, null, null, "test", to, null, null, $"https://example.com/{id}.zip"));

    private sealed record Panel(DeckHost.App.ViewModels.PluginUpdatesViewModel View, List<string> Installed);

    private static Panel PanelWithTwoUpdatesWhereTheSecondFails()
    {
        var installed = new List<string>();
        var settings = new DeckHost.App.Platform.AppSettingsData();
        var view = new DeckHost.App.ViewModels.PluginUpdatesViewModel(
            () => Task.FromResult<IReadOnlyList<PluginUpdate>>([Update("a", "2.0"), Update(FailingId, "3.0")]),
            (update, _) =>
            {
                installed.Add(update.Id);
                return update.Id == FailingId ? throw new IOException(DiskFull) : Task.FromResult(update.Installed with { Version = update.ToVersion });
            },
            () => settings,
            saved => settings = saved);
        return new Panel(view, installed);
    }

    [Test]
    public async Task CheckAsync_TwoUpdatesAvailable_ListsBoth()
    {
        // Arrange
        var panel = PanelWithTwoUpdatesWhereTheSecondFails();

        // Act
        await panel.View.CheckAsync(manual: true);

        // Assert
        Assert.That(panel.View.Available.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task UpdateAllLabel_TwoUpdatesAvailable_CountsThePlugins()
    {
        // Arrange
        var panel = PanelWithTwoUpdatesWhereTheSecondFails();
        await panel.View.CheckAsync(manual: true);

        // Act
        var label = panel.View.UpdateAllLabel;

        // Assert
        Assert.That(label, Is.EqualTo("Update 2 plugins"));
    }

    [Test]
    public async Task For_PluginIdInAnotherCase_FindsItsUpdate()
    {
        // Arrange
        var panel = PanelWithTwoUpdatesWhereTheSecondFails();
        await panel.View.CheckAsync(manual: true);

        // Act
        var update = panel.View.For("A");

        // Assert
        Assert.That(update, Is.Not.Null);
    }

    [Test]
    public async Task UpdateAllAsync_OneUpdateFails_KeepsTheFailedOneAvailable()
    {
        // Arrange
        var panel = PanelWithTwoUpdatesWhereTheSecondFails();
        await panel.View.CheckAsync(manual: true);

        // Act
        await panel.View.UpdateAllAsync();

        // Assert
        Assert.That(Expect.Single(panel.View.Available).Id, Is.EqualTo(FailingId));
    }

    [Test]
    public async Task UpdateAllAsync_OneUpdateFails_ReportsTheFailureInTheStatus()
    {
        // Arrange
        var panel = PanelWithTwoUpdatesWhereTheSecondFails();
        await panel.View.CheckAsync(manual: true);

        // Act
        await panel.View.UpdateAllAsync();

        // Assert
        Assert.That(panel.View.Status, Does.Contain(DiskFull));
    }

    [Test]
    public async Task UpdateAllAsync_TwoUpdates_InstallsTheFirstOneAndTriesTheSecond()
    {
        // Arrange
        var panel = PanelWithTwoUpdatesWhereTheSecondFails();
        await panel.View.CheckAsync(manual: true);

        // Act
        await panel.View.UpdateAllAsync();

        // Assert
        Assert.That(panel.Installed, Is.EqualTo(new[] { "a", FailingId }));
    }

    [Test]
    public void InstallAutomatically_Enabled_IsSaved()
    {
        // Arrange
        var settings = new DeckHost.App.Platform.AppSettingsData();
        var updates = new DeckHost.App.ViewModels.PluginUpdatesViewModel(
            () => Task.FromResult<IReadOnlyList<PluginUpdate>>([]), (update, _) => Task.FromResult(update.Installed), () => settings, saved => settings = saved);

        // Act
        updates.InstallAutomatically = true;

        // Assert
        Assert.That(settings.AutoUpdatePlugins, Is.True);
    }
}
