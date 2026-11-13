using DeckHost.Contracts.Models;

namespace DeckHost.Contracts.Interfaces;

/// <summary>
/// Optional, on an <see cref="IDevicePlugin"/>: the device's firmware. A plugin that can only read
/// the version implements <see cref="GetFirmwareInfo"/> (with <see cref="DeviceFirmwareInfo.CanUpdate"/>
/// false); one that knows the maker's update protocol also installs firmware.
/// </summary>
/// <remarks>
/// Installing goes through DeckHost (<c>FirmwareService</c>), which:
/// <list type="bullet">
/// <item>only offers it for a connected device, after the user confirms;</item>
/// <item>downloads a <see cref="FirmwareRelease"/> and checks its SHA-256 before the plugin sees it
/// (a release without a checksum is refused), or passes a file the user picked;</item>
/// <item>stops sending images and lights to the device and ignores its input while the plugin
/// writes, then redraws it;</item>
/// <item>runs one update at a time.</item>
/// </list>
/// The plugin checks the image is meant for this device before writing anything, reports progress,
/// and handles the device dropping off and coming back (as on any reconnect). Only implement
/// installing with the maker's documented protocol: a wrong write can make a device unusable.
/// </remarks>
public interface IDeviceFirmware
{
    /// <summary>What the device runs now and what DeckHost can do about it; null when unknown (unplugged, not read yet).</summary>
    DeviceFirmwareInfo? GetFirmwareInfo(string deviceId);

    /// <summary>The newest firmware the maker offers for this device, when newer than what it runs; null when none (or the plugin doesn't look).</summary>
    Task<FirmwareRelease?> CheckForFirmwareAsync(string deviceId, CancellationToken cancellationToken) => Task.FromResult<FirmwareRelease?>(null);

    /// <summary>
    /// Writes <paramref name="image"/> to the device. Cancelling is only honored before writing starts.
    /// Never throws for device errors: the result says what happened.
    /// </summary>
    Task<FirmwareUpdateResult> InstallFirmwareAsync(string deviceId, FirmwareImage image, IProgress<FirmwareProgress> progress, CancellationToken cancellationToken) =>
        Task.FromResult(FirmwareUpdateResult.NotSupported);
}
