namespace DeckHost.Contracts.Interfaces;

/// <summary>
/// Optional, next to <see cref="IConfigurableDevicePlugin"/>: buttons on the device's settings page
/// for things that aren't a value to save (make a new pairing code, reset the device, …).
/// </summary>
public interface IDeviceSettingActions
{
    /// <summary>The buttons for this device.</summary>
    IReadOnlyList<DeviceSettingAction> GetSettingActions(string deviceId);

    /// <summary>
    /// Does what the button says (DeckHost calls it off the UI thread). Returns a line for the user;
    /// DeckHost reloads the device's settings afterwards, so changed values show.
    /// </summary>
    string RunSettingAction(string deviceId, string actionId);
}

/// <summary>A button on a device's settings page.</summary>
/// <param name="Description">One line under the button: what it does.</param>
/// <param name="Confirm">When set, DeckHost asks this question before running it.</param>
public sealed record DeviceSettingAction(string Id, string Label, string? Description = null, string? Confirm = null);
