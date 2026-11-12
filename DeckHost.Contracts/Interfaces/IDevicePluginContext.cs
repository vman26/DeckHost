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

    /// <summary>
    /// Asks the host to send every control image again, e.g. after the plugin drew something over
    /// them (a full-screen background). Thread-safe; the redraw happens on the host's worker.
    /// </summary>
    void RequestRedraw(string deviceId)
    {
    }
}
