using DeckHost.Contracts.Interfaces;
using DeckHost.Core.Devices;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>A device that is taken out of the list is let go, so other apps can open it.</summary>
public sealed class DeviceReleaseTests : IAsyncDisposable
{
    private const string Kept = "kept.device";
    private const string Shared = "shared.device";

    private readonly VariableStore _store = new();
    private readonly ReleasablePlugin _shared = new();
    private readonly DeviceHostService _host;

    public DeviceReleaseTests()
    {
        _host = new DeviceHostService([new FakeDevicePlugin(deviceId: Kept), _shared], [], _store, new DeviceRoster());
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        _store.Dispose();
    }

    [Test]
    public async Task RemoveDevice_AReleasableDevice_IsLetGo()
    {
        // Arrange
        await _host.StartAsync(CancellationToken.None);
        await _host.WhenIdleAsync();

        // Act
        _host.RemoveDevice(Shared);
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(_shared.Calls, Is.EqualTo(new[] { (Shared, true) }));
    }

    [Test]
    public async Task AddDevice_AfterItWasRemoved_IsTakenBack()
    {
        // Arrange
        await _host.StartAsync(CancellationToken.None);
        _host.RemoveDevice(Shared);
        await _host.WhenIdleAsync();

        // Act
        _host.AddDevice(Shared);
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(_shared.Calls, Is.EqualTo(new[] { (Shared, true), (Shared, false) }));
    }

    [Test]
    public async Task StartAsync_ADeviceInTheList_IsNeverReleased()
    {
        // Arrange (both devices are added by themselves)

        // Act
        await _host.StartAsync(CancellationToken.None);
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(_shared.Calls, Is.Empty);
    }

    private sealed class ReleasablePlugin : IDevicePlugin, IDeviceRelease
    {
        private readonly FakeDevicePlugin _inner = new(deviceId: Shared);

        public List<(string DeviceId, bool Released)> Calls { get; } = [];

        public string Id => "releasable.plugin";

        public string Name => "Releasable";

        public IReadOnlyCollection<Contracts.Models.DeviceDefinition> Devices => _inner.Devices;

        public IDeviceOutput Output => _inner.Output;

        public event EventHandler? DevicesChanged
        {
            add => _inner.DevicesChanged += value;
            remove => _inner.DevicesChanged -= value;
        }

        public Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken) => _inner.InitializeAsync(context, cancellationToken);

        public void SetDeviceReleased(string deviceId, bool released) => Calls.Add((deviceId, released));

        public void Dispose() => _inner.Dispose();
    }
}
