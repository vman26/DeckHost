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

    /// <summary>(deviceId, profileId) when the active profile changes, also when an action switched it.</summary>
    event Action<string, string>? ActiveProfileChanged;

    /// <summary>A control was rendered differently than before (also while the device is unplugged): the live preview.</summary>
    event Action<ControlFrame>? ControlFrameRendered;

    /// <summary>Every input from a device or from <see cref="SimulateInput"/>.</summary>
    event Action<DeviceInputEvent>? InputReceived;

    IReadOnlyCollection<DeviceDefinition> Devices { get; }
    IReadOnlyCollection<DeviceProfile> Profiles { get; }
    IReadOnlyCollection<RuntimeLogEntry> RuntimeLogs { get; }
    IReadOnlyCollection<DeviceConnectionStatus> ConnectionStatuses { get; }

    /// <summary>Replaces all profiles. The first profile per device becomes active unless one is already active.</summary>
    void SetProfiles(IEnumerable<DeviceProfile> profiles);

    void SetActiveProfile(string profileId);

    /// <summary>Handles an input as if it came from the device (test mode in the editor).</summary>
    void SimulateInput(DeviceInputEvent inputEvent);

    /// <summary>Adds a line to the runtime log (for parts of the app outside the host).</summary>
    void AppendLog(string level, string message);

    /// <summary>Opens the own settings page of the action bound to a control in the active profile.</summary>
    Task<Uri?> GetActionSettingsPageAsync(string deviceId, string controlId);

    /// <summary>Replaces the enabled application plugins (after enabling, disabling, installing or uninstalling one).</summary>
    void SetApplicationPlugins(IEnumerable<DeckHost.Contracts.Interfaces.IApplicationPlugin> applicationPlugins);

    /// <summary>Initializes device plugins. Completes when every plugin finished (or failed) initializing.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Completes once all work queued before this call has been processed.</summary>
    Task WhenIdleAsync();
}
