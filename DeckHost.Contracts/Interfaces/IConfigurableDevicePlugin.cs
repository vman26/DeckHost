using DeckHost.Contracts.Models;

namespace DeckHost.Contracts.Interfaces;

/// <summary>
/// Optional: a device plugin with per-device settings (vibration, brightness, idle screen, ...).
/// DeckHost shows an editor generated from the definitions; the plugin stores and applies the
/// values itself (also again when the device reconnects).
/// </summary>
public interface IConfigurableDevicePlugin
{
    IReadOnlyList<ActionSettingDefinition> GetDeviceSettingDefinitions(string deviceId);

    ActionSettings GetDeviceSettings(string deviceId);

    /// <summary>Saves and applies the settings. Returns an error message, or null on success.</summary>
    string? ApplyDeviceSettings(string deviceId, ActionSettings settings);
}
