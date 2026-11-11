using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Models;

namespace DeckHost.Core.Interfaces;

public interface IDeviceHostService
{
    event Action<RuntimeLogEntry>? RuntimeLogReceived;
    event Action<DeviceConnectionStatus>? ConnectionStatusChanged;

    IReadOnlyCollection<DeviceDefinition> Devices { get; }
    IReadOnlyCollection<DeviceProfile> Profiles { get; }
    IReadOnlyCollection<RuntimeLogEntry> RuntimeLogs { get; }
    IReadOnlyCollection<DeviceConnectionStatus> ConnectionStatuses { get; }
    DeviceProfile? GetActiveProfile(string deviceId);
    void SetProfiles(IEnumerable<DeviceProfile> profiles);
    bool SetActiveProfile(string profileId);
    bool DispatchInput(DeviceInputEvent inputEvent);
    Task StartAsync(CancellationToken cancellationToken);
}
