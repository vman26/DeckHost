using DeckHost.Contracts.Enums;

namespace DeckHost.Contracts.Interfaces;

/// <summary>
/// Given to device plugins by the host. All methods are thread-safe and return quickly,
/// so they can be called straight from a native SDK callback.
/// </summary>
public interface IDevicePluginContext
{
    void ReportInputEvent(string deviceId, string controlId, InputCapabilities eventType, int value);

    void ReportConnectionStatus(string deviceId, string state, string message);

    /// <param name="level">Debug, Info, Warning or Error.</param>
    void Log(string level, string message);
}
