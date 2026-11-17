using DeckHost.Core.Devices;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>Which reported devices are in the list: adding by themselves, discovering, removing and ignoring.</summary>
public sealed class DeviceRosterTests : IAsyncDisposable
{
    private const string Id = "fake.device";
    private const string Other = "other.device";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "deckhost-roster-" + Guid.NewGuid().ToString("N"));
    private readonly List<IAsyncDisposable> _hosts = [];
    private readonly VariableStore _store = new();

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }

        _store.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string RosterFile => Path.Combine(_folder, "roster.json");

    // The editor always needs a device, so these hosts also have one that stays listed (unplugged: it counts as added earlier).
    private DeviceHostService HostFor(FakeDevicePlugin device, DeviceRoster roster)
    {
        var host = new DeviceHostService([new FakeDevicePlugin(connected: false, deviceId: Other), device], [], _store, roster);
        _hosts.Add(host);
        return host;
    }

    // ---------------------------------------------------------------- the rules alone

    [Test]
    public void Decide_NewDeviceWithAutoAddOn_IsListed()
    {
        var roster = new DeviceRoster();

        var listing = roster.Decide(Id, isConnected: true);

        Assert.That(listing, Is.EqualTo(DeviceListing.Listed));
    }

    [Test]
    public void Decide_NewPluggedInDeviceWithAutoAddOff_IsDiscovered()
    {
        var roster = new DeviceRoster { AutoAdd = false };

        var listing = roster.Decide(Id, isConnected: true);

        Assert.That(listing, Is.EqualTo(DeviceListing.Discovered));
    }

    [Test]
    public void Decide_NewUnpluggedDeviceWithAutoAddOff_StaysListedBecauseItWasAddedEarlier()
    {
        var roster = new DeviceRoster { AutoAdd = false };

        var listing = roster.Decide(Id, isConnected: false);

        Assert.That(listing, Is.EqualTo(DeviceListing.Listed));
    }

    [Test]
    public void Add_ADiscoveredDevice_ListsIt()
    {
        var roster = new DeviceRoster { AutoAdd = false };
        roster.Decide(Id, isConnected: true);

        roster.Add(Id);

        Assert.That(roster.Decide(Id, isConnected: true), Is.EqualTo(DeviceListing.Listed));
    }

    [Test]
    public void Remove_APluggedInDevice_IgnoresItWhileItStaysPluggedIn()
    {
        var roster = new DeviceRoster();
        roster.Decide(Id, isConnected: true);

        roster.Remove(Id, isConnected: true);

        Assert.That(roster.Decide(Id, isConnected: true), Is.EqualTo(DeviceListing.Ignored));
    }

    [Test]
    public void Decide_RemovedDevicePluggedInAgain_IsDiscoveredAgain()
    {
        var roster = new DeviceRoster();
        roster.Decide(Id, isConnected: true);
        roster.Remove(Id, isConnected: true);
        roster.Decide(Id, isConnected: false);

        var listing = roster.Decide(Id, isConnected: true);

        Assert.That(listing, Is.EqualTo(DeviceListing.Discovered));
    }

    [Test]
    public void Decide_RemovedDeviceAfterKeepIgnoring_StaysIgnoredUntilItIsPluggedInAgain()
    {
        var roster = new DeviceRoster();
        roster.Decide(Id, isConnected: true);
        roster.Remove(Id, isConnected: true);
        roster.Decide(Id, isConnected: false);
        roster.Decide(Id, isConnected: true);

        roster.Ignore(Id, isConnected: true);

        Assert.That(roster.Decide(Id, isConnected: true), Is.EqualTo(DeviceListing.Ignored));
    }

    [Test]
    public void Decide_AfterARestart_RemembersWhatWasListedAndIgnored()
    {
        var first = new DeviceRoster(RosterFile) { AutoAdd = false };
        first.Add("listed.one");
        first.Ignore("ignored.one", isConnected: false);

        var second = new DeviceRoster(RosterFile);

        Assert.That(second.AutoAdd, Is.False);
        Assert.That(second.Decide("listed.one", isConnected: true), Is.EqualTo(DeviceListing.Listed));
        Assert.That(second.Decide("ignored.one", isConnected: false), Is.EqualTo(DeviceListing.Ignored));
    }

    [Test]
    public void Constructor_WithAnUnreadableFile_StartsFreshAndAutoAdds()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(RosterFile, "{ not json");

        var roster = new DeviceRoster(RosterFile);

        Assert.That(roster.Decide(Id, isConnected: true), Is.EqualTo(DeviceListing.Listed));
    }

    // ---------------------------------------------------------------- the host with a roster

    [Test]
    public async Task Devices_AutoAddOff_KeepsAPluggedInDeviceOutOfTheListUntilItIsAdded()
    {
        var host = HostFor(new FakeDevicePlugin(), new DeviceRoster { AutoAdd = false });
        await host.StartAsync(CancellationToken.None);

        var before = host.Devices.Count;
        host.AddDevice(Id);
        await host.WhenIdleAsync();

        Assert.That(before, Is.EqualTo(1));
        Assert.That(host.Devices.Select(static device => device.Id), Is.EquivalentTo(new[] { Id, Other }));
    }

    [Test]
    public async Task DeviceRoster_AutoAddOff_ShowsThePluggedInDeviceAsDiscovered()
    {
        var host = HostFor(new FakeDevicePlugin(), new DeviceRoster { AutoAdd = false });
        await host.StartAsync(CancellationToken.None);

        var entry = host.DeviceRoster().Single(candidate => candidate.Id == Id);

        Assert.That((entry.Listing, entry.IsConnected), Is.EqualTo((DeviceListing.Discovered, true)));
    }

    [Test]
    public async Task RemoveDevice_AListedDevice_TakesItOutOfTheListAndKeepsItAsIgnored()
    {
        var host = HostFor(new FakeDevicePlugin(), new DeviceRoster());
        await host.StartAsync(CancellationToken.None);

        host.RemoveDevice(Id);
        await host.WhenIdleAsync();

        Assert.That(host.Devices.Select(static device => device.Id), Is.EqualTo(new[] { Other }));
        Assert.That(host.DeviceRoster().Single(candidate => candidate.Id == Id).Listing, Is.EqualTo(DeviceListing.Ignored));
    }

    [Test]
    public async Task RemoveDevice_APluggedInDevice_ComesBackAsDiscoveredAfterItIsPluggedInAgain()
    {
        var device = new FakeDevicePlugin();
        var host = HostFor(device, new DeviceRoster());
        await host.StartAsync(CancellationToken.None);
        host.RemoveDevice(Id);
        await host.WhenIdleAsync();

        device.SetConnected(false);
        await host.WhenIdleAsync();
        device.SetConnected(true);
        await host.WhenIdleAsync();

        Assert.That(host.DeviceRoster().Single(candidate => candidate.Id == Id).Listing, Is.EqualTo(DeviceListing.Discovered));
        Assert.That(host.Devices.Select(static device => device.Id), Is.EqualTo(new[] { Other }));
    }

    [Test]
    public async Task RemoveDevice_TheOnlyListedDevice_KeepsItBecauseTheEditorNeedsOne()
    {
        var host = new DeviceHostService([new FakeDevicePlugin()], [], _store, new DeviceRoster());
        _hosts.Add(host);
        await host.StartAsync(CancellationToken.None);

        host.RemoveDevice(Id);
        await host.WhenIdleAsync();

        Assert.That(host.Devices.Select(static device => device.Id), Is.EqualTo(new[] { Id }));
    }

    [Test]
    public async Task Devices_EverythingWaitsAsDiscovered_ListsTheFirstOneSoThereIsADevice()
    {
        var host = new DeviceHostService([new FakeDevicePlugin()], [], _store, new DeviceRoster { AutoAdd = false });
        _hosts.Add(host);

        await host.StartAsync(CancellationToken.None);

        Assert.That(host.Devices.Select(static device => device.Id), Is.EqualTo(new[] { Id }));
    }

    [Test]
    public async Task RosterChanged_ADeviceIsRemoved_IsRaised()
    {
        var host = HostFor(new FakeDevicePlugin(), new DeviceRoster());
        await host.StartAsync(CancellationToken.None);
        var raised = 0;
        host.RosterChanged += () => Interlocked.Increment(ref raised);

        host.RemoveDevice(Id);
        await host.WhenIdleAsync();

        Assert.That(raised, Is.GreaterThan(0));
    }

    [Test]
    public async Task Devices_WithoutARoster_ListsEveryReportedDevice()
    {
        var host = new DeviceHostService([new FakeDevicePlugin()], [], _store);
        _hosts.Add(host);
        await host.StartAsync(CancellationToken.None);

        Assert.That(host.Devices.Select(static device => device.Id), Is.EqualTo(new[] { Id }));
    }
}
