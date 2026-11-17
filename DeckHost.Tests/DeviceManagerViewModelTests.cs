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
        using var page = new DeviceManagerViewModel(_host);

        Assert.That(page.Listed.Select(static row => row.Id), Is.EquivalentTo(new[] { Plugged, Unplugged }));
        Assert.That(page.HasDiscovered, Is.False);
        Assert.That(page.HasIgnored, Is.False);
    }

    [Test]
    public async Task Remove_AListedDevice_MovesItToRemoved()
    {
        using var page = new DeviceManagerViewModel(_host);

        page.Listed.Single(static row => row.Id == Unplugged).RemoveCommand.Execute(null);
        await _host.WhenIdleAsync();
        page.Refresh();

        Assert.That(page.Listed.Select(static row => row.Id), Is.EqualTo(new[] { Plugged }));
        Assert.That(page.Ignored.Select(static row => row.Id), Is.EqualTo(new[] { Unplugged }));
        Assert.That(page.HasIgnored, Is.True);
    }

    [Test]
    public async Task Add_ARemovedDevice_PutsItBackInTheList()
    {
        using var page = new DeviceManagerViewModel(_host);
        page.Listed.Single(static row => row.Id == Unplugged).RemoveCommand.Execute(null);
        await _host.WhenIdleAsync();
        page.Refresh();

        page.Ignored.Single().AddCommand.Execute(null);
        await _host.WhenIdleAsync();
        page.Refresh();

        Assert.That(page.Listed.Select(static row => row.Id), Is.EquivalentTo(new[] { Plugged, Unplugged }));
        Assert.That(page.Ignored, Is.Empty);
    }

    [Test]
    public async Task CanRemove_OnlyOneDeviceListed_IsFalseForIt()
    {
        using var page = new DeviceManagerViewModel(_host);
        page.Listed.Single(static row => row.Id == Unplugged).RemoveCommand.Execute(null);
        await _host.WhenIdleAsync();

        page.Refresh();

        Assert.That(page.Listed.Single().CanRemove, Is.False);
        Assert.That(page.Listed.Single().RemoveTip, Does.Contain("always keeps one"));
    }

    [Test]
    public async Task AutoAdd_TurnedOff_LeavesANewPluggedInDeviceUnderFound()
    {
        using var page = new DeviceManagerViewModel(_host);
        page.AutoAdd = false;
        var newcomer = new FakeDevicePlugin(connected: true, deviceId: "new.device");
        await using var host = new DeviceHostService([new FakeDevicePlugin(connected: false, deviceId: Unplugged), newcomer], [], _store, _roster);

        using var second = new DeviceManagerViewModel(host);

        Assert.That(second.Discovered.Select(static row => row.Id), Is.EqualTo(new[] { "new.device" }));
        Assert.That(second.HasDiscovered, Is.True);
    }

    [Test]
    public async Task KeepIgnoring_AFoundDevice_MovesItToRemoved()
    {
        _roster.AutoAdd = false;
        var newcomer = new FakeDevicePlugin(connected: true, deviceId: "new.device");
        await using var host = new DeviceHostService([new FakeDevicePlugin(connected: false, deviceId: Unplugged), newcomer], [], _store, _roster);
        using var page = new DeviceManagerViewModel(host);

        page.Discovered.Single().KeepIgnoringCommand.Execute(null);
        await host.WhenIdleAsync();
        page.Refresh();

        Assert.That(page.Discovered, Is.Empty);
        Assert.That(page.Ignored.Select(static row => row.Id), Is.EqualTo(new[] { "new.device" }));
    }
}
