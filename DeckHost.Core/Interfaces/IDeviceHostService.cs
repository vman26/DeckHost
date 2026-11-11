using DeckHost.Contracts.Models;
using DeckHost.Core.Models;

namespace DeckHost.Core.Interfaces;

/// <summary>
/// Runtime host. All work (plugin init, input dispatch, rendering, device I/O) runs on one
/// background worker, so methods return immediately and are safe to call from any thread.
/// Events are raised on that worker thread: UI code must marshal to its dispatcher.
/// </summary>
public interface IDeviceHostService : IAsyncDisposable
{
    event Action<RuntimeLogEntry>? RuntimeLogReceived;
    event Action<DeviceConnectionStatus>? ConnectionStatusChanged;
    event Action? DevicesChanged;

    IReadOnlyCollection<DeviceDefinition> Devices { get; }
    IReadOnlyCollection<DeviceProfile> Profiles { get; }
    IReadOnlyCollection<RuntimeLogEntry> RuntimeLogs { get; }
    IReadOnlyCollection<DeviceConnectionStatus> ConnectionStatuses { get; }

    /// <summary>Replaces all profiles. The first profile per device becomes active unless one is already active.</summary>
    void SetProfiles(IEnumerable<DeviceProfile> profiles);

    void SetActiveProfile(string profileId);

    /// <summary>Initializes device plugins. Completes when every plugin finished (or failed) initializing.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Completes once all work queued before this call has been processed.</summary>
    Task WhenIdleAsync();
}
