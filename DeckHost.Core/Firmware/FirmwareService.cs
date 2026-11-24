using System.Security.Cryptography;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Interfaces;

namespace DeckHost.Core.Firmware;

/// <summary>
/// Firmware updates for devices whose plugin implements <see cref="IDeviceFirmware"/>. Everything
/// around the plugin's own writing is here, so every plugin gets the same safety: only connected
/// devices, one update at a time, downloads checked against their SHA-256 before the plugin sees
/// them, the device paused in the host (no images, lights or input) while it's written, and redrawn
/// afterwards.
/// </summary>
public sealed class FirmwareService(IEnumerable<IDevicePlugin> plugins, IDeviceHostService host, HttpClient? http = null, string? downloadDirectory = null)
{
    private readonly IEnumerable<IDevicePlugin> _plugins = plugins; // not copied: packaged plugins come and go
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly string _downloads = downloadDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeckHost", "cache", "firmware");

    /// <summary>Whether the device's plugin reports firmware at all.</summary>
    public bool Supports(string deviceId) => Find(deviceId) is not null;

    public DeviceFirmwareInfo? GetInfo(string deviceId)
    {
        try
        {
            return Find(deviceId)?.Firmware.GetFirmwareInfo(deviceId);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>A newer firmware from the maker, or null.</summary>
    public async Task<FirmwareRelease?> CheckAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (Find(deviceId) is not { } found || GetInfo(deviceId) is not { CanUpdate: true })
        {
            return null;
        }

        return await found.Firmware.CheckForFirmwareAsync(deviceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Downloads a release and checks its SHA-256; throws <see cref="FirmwareException"/> when it can't be trusted.</summary>
    public async Task<FirmwareImage> DownloadAsync(FirmwareRelease release, IProgress<FirmwareProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (string.IsNullOrWhiteSpace(release.Sha256))
        {
            throw new FirmwareException("The firmware comes without a checksum, so DeckHost can't check it: not installed.");
        }

        if (release.DownloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new FirmwareException("Firmware is only downloaded over HTTPS.");
        }

        Directory.CreateDirectory(_downloads);
        var path = Path.Combine(_downloads, $"{Sanitize(release.Version)}-{Path.GetFileName(release.DownloadUri.LocalPath)}");
        progress?.Report(new FirmwareProgress(0, "Downloading"));
        try
        {
            var client = http ?? SharedHttp.Value;
            using (var response = await client.GetAsync(release.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(path + ".part");
                await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            File.Move(path + ".part", path, overwrite: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            throw new FirmwareException($"The download failed: {ex.Message}", ex);
        }

        if (!Matches(path, release.Sha256))
        {
            File.Delete(path);
            throw new FirmwareException("The downloaded firmware doesn't match its checksum: not installed.");
        }

        progress?.Report(new FirmwareProgress(0, "Downloaded and checked"));
        return new FirmwareImage(path, release.Version);
    }

    /// <summary>
    /// Installs <paramref name="image"/> on a connected device: pauses it in the host, lets its plugin
    /// write, resumes and redraws it. Throws <see cref="FirmwareException"/> when it can't start.
    /// </summary>
    public async Task<FirmwareUpdateResult> InstallAsync(string deviceId, FirmwareImage image, IProgress<FirmwareProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (Find(deviceId) is not { } found)
        {
            throw new FirmwareException("This device's plugin doesn't handle firmware.");
        }

        if (!found.Plugin.Devices.Any(device => string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase) && device.IsConnected))
        {
            throw new FirmwareException("Connect the device first.");
        }

        if (GetInfo(deviceId) is not { CanUpdate: true })
        {
            throw new FirmwareException(FirmwareUpdateResult.NotSupported.Message);
        }

        if (!File.Exists(image.Path))
        {
            throw new FirmwareException($"Firmware file not found: {image.Path}");
        }

        if (!await _oneAtATime.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            throw new FirmwareException("Another firmware update is running; wait for it to finish.");
        }

        host.SetDevicePaused(deviceId, paused: true);

        // The pause is queued on the host's worker: wait until it's applied, so no image is on its way while the plugin writes.
        await host.WhenIdleAsync().ConfigureAwait(false);
        host.AppendLog("Information", $"Installing firmware {image.Version ?? Path.GetFileName(image.Path)} on '{deviceId}'.");
        try
        {
            var result = await found.Firmware.InstallFirmwareAsync(deviceId, image, progress ?? new Progress<FirmwareProgress>(), cancellationToken).ConfigureAwait(false);
            host.AppendLog(result.Success ? "Information" : "Error", $"Firmware update of '{deviceId}': {result.Message}");
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A plugin should report errors in its result; one that throws still mustn't leave the device paused.
            host.AppendLog("Error", $"Firmware update of '{deviceId}' failed: {ex}");
            return new FirmwareUpdateResult(false, $"The update failed: {ex.Message}");
        }
        finally
        {
            host.SetDevicePaused(deviceId, paused: false);
            _oneAtATime.Release();
        }
    }

    /// <summary>Whether a file's SHA-256 is <paramref name="sha256"/> (hex, any case).</summary>
    public static bool Matches(string path, string sha256)
    {
        using var stream = File.OpenRead(path);
        return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), sha256.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private (IDevicePlugin Plugin, IDeviceFirmware Firmware)? Find(string deviceId) =>
        _plugins.FirstOrDefault(plugin => plugin is IDeviceFirmware && plugin.Devices.Any(device => string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))) is { } plugin
            ? (plugin, (IDeviceFirmware)plugin)
            : null;

    private static string Sanitize(string text) => new([.. text.Select(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '_')]);

    private static readonly Lazy<HttpClient> SharedHttp = new(static () => new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
}

public sealed class FirmwareException(string message, Exception? inner = null) : Exception(message, inner);
