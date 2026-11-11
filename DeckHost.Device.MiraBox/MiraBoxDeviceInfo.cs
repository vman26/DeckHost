using System.Text.Json;

namespace DeckHost.Device.MiraBox;

/// <summary>Device description returned by MiraBox_GetDeviceInfo (StreamDockInfo + FeatureOption).</summary>
internal sealed record MiraBoxDeviceInfo(
    string DeviceKey,
    int OriginType,
    int VendorId,
    int ProductId,
    string SerialNumber,
    string FirmwareVersion,
    int Width,
    int Height,
    int KeyWidth,
    int KeyHeight,
    int MinKey,
    int MaxKey,
    bool HasSecondScreen,
    int MinSecondScreenKey,
    int MaxSecondScreenKey,
    int SecondScreenWidth,
    int SecondScreenHeight,
    bool HasRgbLed,
    int LedCount)
{
    /// <summary>DeviceOriginType::SDN4Pro in the SDK.</summary>
    public const int N4ProOriginType = 0x07;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public bool IsN4Pro => OriginType == N4ProOriginType;

    public static MiraBoxDeviceInfo? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var info = JsonSerializer.Deserialize<MiraBoxDeviceInfo>(json, Options);

            // The firmware reports fixed-size, NUL/space padded strings.
            return info is null ? null : info with
            {
                FirmwareVersion = (info.FirmwareVersion ?? string.Empty).Trim('\0', ' ', '\t'),
                SerialNumber = (info.SerialNumber ?? string.Empty).Trim('\0', ' ', '\t'),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
