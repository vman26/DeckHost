namespace DeckHost.Device.Hid;

/// <summary>Where the device creator keeps what it learned: the descriptions of generic USB devices.</summary>
public interface IHidDescriptionStore
{
    /// <summary>The saved descriptions.</summary>
    IReadOnlyList<HidDeviceDescription> Descriptions { get; }

    /// <summary>Saves a description (new or changed).</summary>
    void Save(HidDeviceDescription description);

    /// <summary>Removes a description.</summary>
    void Delete(string id);
}

/// <summary>
/// The descriptions as files in <c>devices/hid</c>, without the plugin: the device creator in the app uses this, and the
/// HID device plugin (a bundled package, in its own load context) notices the changed files and reloads.
/// </summary>
public sealed class HidDescriptionFiles(string? directory = null) : IHidDescriptionStore
{
    /// <summary>Where the descriptions are kept.</summary>
    public string Directory { get; } = directory ?? DefaultDirectory;

    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost", "devices", "hid");

    public IReadOnlyList<HidDeviceDescription> Descriptions
    {
        get
        {
            var found = new List<HidDeviceDescription>();
            try
            {
                if (System.IO.Directory.Exists(Directory))
                {
                    foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
                    {
                        if (HidDeviceDescription.FromJson(File.ReadAllText(file)) is { } description)
                        {
                            found.Add(description);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable: the creator shows what it could read.
            }

            return found;
        }
    }

    public void Save(HidDeviceDescription description)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(PathOf(Directory, description.Id), description.ToJson());
    }

    public void Delete(string id)
    {
        var path = PathOf(Directory, id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    internal static string PathOf(string directory, string id) => Path.Combine(directory, HidDeviceDescription.MakeId(id) + ".json");
}
