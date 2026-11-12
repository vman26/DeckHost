using DeckHost.Contracts.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeckHost.Core.Profiles;

/// <summary>Saved editor state: all profiles plus which one was selected.</summary>
/// <param name="SelectedProfileId">The profile chosen by hand (not one activated automatically).</param>
/// <param name="AutoSwitchEnabled">Whether profiles follow the focused app.</param>
public sealed record ProfileDocument(
    int Version,
    string? SelectedProfileId,
    IReadOnlyList<DeviceProfile> Profiles,
    bool AutoSwitchEnabled = true,
    ProfileBackground? DefaultBackground = null)
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Persists profiles as JSON. Writes go to a temp file first and are then moved into place,
/// so a crash mid-write never leaves a truncated profiles file.
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public ProfileStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
    }

    public string FilePath { get; }

    /// <summary>The default location: <c>%AppData%/DeckHost/profiles.json</c> (or the platform equivalent).</summary>
    public static ProfileStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "DeckHost",
        "profiles.json"));

    /// <summary>Returns null when there is no file yet. Throws <see cref="JsonException"/> for a corrupt file.</summary>
    public ProfileDocument? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        using var stream = File.OpenRead(FilePath);
        return JsonSerializer.Deserialize<ProfileDocument>(stream, SerializerOptions);
    }

    public void Save(ProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = FilePath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, document, SerializerOptions);
        }

        File.Move(tempPath, FilePath, overwrite: true);
    }
}
