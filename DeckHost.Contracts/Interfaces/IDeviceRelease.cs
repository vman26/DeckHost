namespace DeckHost.Contracts.Interfaces;

/// <summary>
/// Optional for a device plugin: let go of a device so other software (the vendor's app, Bitfocus
/// Companion, …) can open it. DeckHost releases every device the user removed or hasn't added on
/// the Devices page, and takes it back when it is added again.
/// </summary>
public interface IDeviceRelease
{
    /// <summary>
    /// Closes the device and doesn't open it again while <paramref name="released"/> (true), or opens
    /// it again (false). A released device that is plugged in still reports
    /// <see cref="Models.DeviceDefinition.IsConnected"/>, so DeckHost can offer to add it back.
    /// Called on the host's worker: it must not block for long or raise <see cref="IDevicePlugin.DevicesChanged"/> synchronously.
    /// </summary>
    void SetDeviceReleased(string deviceId, bool released);
}
