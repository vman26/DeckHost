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
    ProfileBackground? DefaultBackground = null,
    IReadOnlyDictionary<string, LightStyle>? DeviceLights = null)
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

    /// <summary>How many daily backups are kept.</summary>
    public const int BackupsKept = 14;

    /// <summary>Where the daily backups go: <c>backups/</c> next to the profiles file.</summary>
    public string BackupDirectory => Path.Combine(Path.GetDirectoryName(FilePath) ?? ".", "backups");

    /// <summary>The backups, newest first.</summary>
    public IReadOnlyList<FileInfo> ListBackups()
    {
        try
        {
            return Directory.Exists(BackupDirectory)
                ? [.. new DirectoryInfo(BackupDirectory).GetFiles("profiles-*.json").OrderByDescending(static file => file.Name, StringComparer.Ordinal)]
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Reads a backup (or any profiles file). Throws <see cref="JsonException"/> for a corrupt file.</summary>
    public static ProfileDocument? LoadFrom(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<ProfileDocument>(stream, SerializerOptions);
    }

    /// <summary>
    /// Copies the current file to today's backup (once a day, before the first change of the day),
    /// keeping the last <see cref="BackupsKept"/> days. A backup that can't be written doesn't stop the save.
    /// </summary>
    public void BackUp(bool force = false)
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            Directory.CreateDirectory(BackupDirectory);
            var today = Path.Combine(BackupDirectory, $"profiles-{DateTime.Now:yyyy-MM-dd}.json");
            if (File.Exists(today) && !force)
            {
                return;
            }

            File.Copy(FilePath, today, overwrite: true);
            foreach (var old in ListBackups().Skip(BackupsKept))
            {
                old.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Save(ProfileDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        BackUp();

        var tempPath = FilePath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, document, SerializerOptions);
        }

        File.Move(tempPath, FilePath, overwrite: true);
    }
}
