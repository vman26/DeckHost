using DeckHost.Contracts.Models;

namespace DeckHost.Contracts.Interfaces;

/// <summary>
/// Optional, next to <see cref="IDeviceOutput"/>: sets several LEDs in one go. The host uses it for
/// animated light effects, which change every LED many times a second; a device that sets all its
/// LEDs in one command (the N4 Pro) then sends one command instead of one per LED.
/// </summary>
public interface IDeviceLightBatchOutput
{
    bool TrySetLights(string deviceId, IReadOnlyList<LightColor> lights, out string? error);
}
