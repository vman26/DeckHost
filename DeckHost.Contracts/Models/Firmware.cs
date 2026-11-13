namespace DeckHost.Contracts.Models;

/// <summary>A device's firmware as its plugin knows it.</summary>
/// <param name="Version">The version the device reports.</param>
/// <param name="CanUpdate">Whether the plugin can install firmware on this device.</param>
/// <param name="Detail">A line for the user: where updates come from, or why there are none here.</param>
public sealed record DeviceFirmwareInfo(string Version, bool CanUpdate, string? Detail = null)
{
    /// <summary>File types the plugin installs from a file the user picks (".bin", ".dfu"); empty for none.</summary>
    public IReadOnlyList<string> FileExtensions { get; init; } = [];
}

/// <summary>Firmware offered for download by the device's maker.</summary>
/// <param name="Sha256">The file's SHA-256 (hex). DeckHost refuses a download without one or that doesn't match.</param>
public sealed record FirmwareRelease(string Version, Uri DownloadUri, string? Sha256, string? Notes = null);

/// <summary>A firmware file on this computer, downloaded and checked by DeckHost or picked by the user.</summary>
/// <param name="Version">The version, when known (from a release; null for a picked file).</param>
public sealed record FirmwareImage(string Path, string? Version = null);

/// <summary>How far an update is: 0–1 and the current step ("Erasing", "Writing", "Restarting").</summary>
public sealed record FirmwareProgress(double Fraction, string Step);

/// <summary>How an update ended.</summary>
public sealed record FirmwareUpdateResult(bool Success, string Message)
{
    public static FirmwareUpdateResult NotSupported { get; } = new(false, "This device's firmware can't be updated from DeckHost.");
}
