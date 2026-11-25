using DeckHost.App.ViewModels;
using DeckHost.Core.Devices;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>The Devices page: which devices it lists where, and what its buttons do.</summary>
public sealed class DeviceManagerViewModelTests : IAsyncDisposable
{
    private const string Plugged = "fake.device";
    private const string Unplugged = "other.device";

    private readonly VariableStore _store = new();
    private readonly DeviceHostService _host;
    private readonly DeviceRoster _roster;

    public DeviceManagerViewModelTests()
    {
        _roster = new DeviceRoster();
        _host = new DeviceHostService([new FakeDevicePlugin(), new FakeDevicePlugin(connected: false, deviceId: Unplugged)], [], _store, _roster);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        _store.Dispose();
    }

    [Test]
    public void Constructor_TwoDevicesAutoAdded_ListsBothUnderYourDevices()
    {
        // Act
        using var page = new DeviceManagerViewModel(_host);

        // Assert
        Assert.That(page.Listed.Select(static row => row.Id), Is.EquivalentTo(new[] { Plugged, Unplugged }));
        Assert.That(page.HasDiscovered, Is.False);
        Assert.That(page.HasIgnored, Is.False);
    }

    [Test]
    public async Task Remove_AListedDevice_MovesItToRemoved()
    {
        // Arrange
        using var page = new DeviceManagerViewModel(_host);

        // Act
        page.Listed.Single(static row => row.Id == Unplugged).RemoveCommand.Execute(null);
        await _host.WhenIdleAsync();
        page.Refresh();

        // Assert
        Assert.That(page.Listed.Select(static row => row.Id), Is.EqualTo(new[] { Plugged }));
        Assert.That(page.Ignored.Select(static row => row.Id), Is.EqualTo(new[] { Unplugged }));
        Assert.That(page.HasIgnored, Is.True);
    }

    [Test]
    public async Task Add_ARemovedDevice_PutsItBackInTheList()
    {
        // Arrange
        using var page = new DeviceManagerViewModel(_host);
        page.Listed.Single(static row => row.Id == Unplugged).RemoveCommand.Execute(null);
        await _host.WhenIdleAsync();
        page.Refresh();

        // Act
        page.Ignored.Single().AddCommand.Execute(null);
        await _host.WhenIdleAsync();
        page.Refresh();

        // Assert
        Assert.That(page.Listed.Select(static row => row.Id), Is.EquivalentTo(new[] { Plugged, Unplugged }));
        Assert.That(page.Ignored, Is.Empty);
    }

    [Test]
    public async Task CanRemove_OnlyOneDeviceListed_IsFalseForIt()
    {
        // Arrange
        using var page = new DeviceManagerViewModel(_host);
        page.Listed.Single(static row => row.Id == Unplugged).RemoveCommand.Execute(null);
        await _host.WhenIdleAsync();

        // Act
        page.Refresh();

        // Assert
        Assert.That(page.Listed.Single().CanRemove, Is.False);
        Assert.That(page.Listed.Single().RemoveTip, Does.Contain("always keeps one"));
    }

    [Test]
    public async Task AutoAdd_TurnedOff_LeavesANewPluggedInDeviceUnderFound()
    {
        // Arrange
        using var page = new DeviceManagerViewModel(_host);
        page.AutoAdd = false;
        var newcomer = new FakeDevicePlugin(connected: true, deviceId: "new.device");
        await using var host = new DeviceHostService([new FakeDevicePlugin(connected: false, deviceId: Unplugged), newcomer], [], _store, _roster);

        // Act
        using var second = new DeviceManagerViewModel(host);

        // Assert
        Assert.That(second.Discovered.Select(static row => row.Id), Is.EqualTo(new[] { "new.device" }));
        Assert.That(second.HasDiscovered, Is.True);
    }

    [Test]
    public async Task KeepIgnoring_AFoundDevice_MovesItToRemoved()
    {
        // Arrange
        _roster.AutoAdd = false;
        var newcomer = new FakeDevicePlugin(connected: true, deviceId: "new.device");
        await using var host = new DeviceHostService([new FakeDevicePlugin(connected: false, deviceId: Unplugged), newcomer], [], _store, _roster);
        using var page = new DeviceManagerViewModel(host);

        // Act
        page.Discovered.Single().KeepIgnoringCommand.Execute(null);
        await host.WhenIdleAsync();
        page.Refresh();

        // Assert
        Assert.That(page.Discovered, Is.Empty);
        Assert.That(page.Ignored.Select(static row => row.Id), Is.EqualTo(new[] { "new.device" }));
    }

    [Test]
    public void Constructor_ListedDevice_HasACardWithItsConnectionState()
    {
        // Act
        using var page = new DeviceManagerViewModel(_host);

        // Assert
        var plugged = page.Listed.Single(static row => row.Id == Plugged);
        var unplugged = page.Listed.Single(static row => row.Id == Unplugged);
        Assert.That(plugged.Card?.Device.Id, Is.EqualTo(Plugged));
        Assert.That((plugged.State, unplugged.State), Is.EqualTo(("Connected", "Not connected")));
    }

    [Test]
    public void CreateDeviceSettings_WithoutASettingsService_IsNull()
    {
        // Arrange
        using var page = new DeviceManagerViewModel(_host);
        var row = page.Listed.Single(static row => row.Id == Plugged);

        // Act
        var settings = page.CreateDeviceSettings(row);

        // Assert
        Assert.That((row.HasSettings, settings), Is.EqualTo((false, (DeviceSettingsViewModel?)null)));
    }

    [Test]
    public async Task Resume_AfterARemovalWhileThePageWasCovered_ShowsTheRemoval()
    {
        // Arrange
        var page = new DeviceManagerViewModel(_host);
        page.Dispose();
        _host.RemoveDevice(Unplugged);
        await _host.WhenIdleAsync();

        // Act
        page.Resume();

        // Assert
        Assert.That(page.Ignored.Select(static row => row.Id), Is.EqualTo(new[] { Unplugged }));
        page.Dispose();
    }

    [Test]
    public async Task Card_OfARemovedDevice_IsNull()
    {
        // Arrange
        using var page = new DeviceManagerViewModel(_host);
        _host.RemoveDevice(Unplugged);
        await _host.WhenIdleAsync();

        // Act
        page.Refresh();

        // Assert
        Assert.That(page.Ignored.Single().Card, Is.Null);
    }
}
