using System.Text.Json.Serialization;

namespace DeckHost.Contracts.Models;

/// <summary>
/// Represents the actual capabilities and dimensions of a connected device,
/// queried from the device via StreamDockInfo.
/// </summary>
public sealed record DeviceCapabilities(
    [property: JsonPropertyName("deviceName")]
    string DeviceName,

    [property: JsonPropertyName("width")]
    int DisplayWidth,

    [property: JsonPropertyName("height")]
    int DisplayHeight,

    [property: JsonPropertyName("keyWidth")]
    int KeyWidth,

    [property: JsonPropertyName("keyHeight")]
    int KeyHeight,

    [property: JsonPropertyName("minKey")]
    int MinKeyId,

    [property: JsonPropertyName("maxKey")]
    int MaxKeyId)
{
    /// <summary>
    /// Gets the total number of supported keys (MaxKeyId - MinKeyId + 1).
    /// </summary>
    [JsonIgnore]
    public int KeyCount => MaxKeyId - MinKeyId + 1;


    private static readonly System.Text.Json.JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
    /// <summary>
    /// Parses device capabilities from JSON returned by the native bridge.
    /// </summary>
    public static DeviceCapabilities? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        { 
            return System.Text.Json.JsonSerializer.Deserialize<DeviceCapabilities>(
                json,
                options);
        }
        catch
        {
            return null;
        }
    }
}
