using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DeckHost.Core.Icons;

/// <summary>One icon in the library: a file in an installed pack.</summary>
public sealed record IconEntry(string PackId, string PackName, string Name, IReadOnlyList<string> Tags, string FilePath);

/// <summary>
/// An installed icon pack: a Stream Deck icon pack (<c>.streamDeckIconPack</c>), a zip or folder
/// of images, or the icons downloaded from Iconify (<see cref="IconLibrary.DownloadsPackId"/>).
/// </summary>
public sealed record IconPack(
    string Id,
    string Name,
    string Version,
    string? Author,
    string? Description,
    string? Url,
    string Directory,
    string? PreviewPath,
    string? LicenseText,
    IReadOnlyList<IconEntry> Icons)
{
    public bool IsDownloads => Id == IconLibrary.DownloadsPackId;
}

/// <summary>
/// The icons the user can put on controls, in <c>%AppData%/DeckHost/icons/&lt;pack id&gt;</c>.
/// Every pack is kept in the Stream Deck icon pack layout (<c>manifest.json</c>, <c>icons.json</c>,
/// <c>icons/</c>, <c>license.txt</c>), so packs made for Elgato's Stream Deck app install as they
/// are, and a zip of plain images gets a manifest written for it. Thread-safe.
/// </summary>
public sealed partial class IconLibrary
{
    /// <summary>The pack that collects icons downloaded from Iconify.</summary>
    public const string DownloadsPackId = "iconify";

    public const string ManifestFile = "manifest.json";
    public const string IconsFile = "icons.json";
    public const string LicenseFile = "license.txt";
    public const string IconsFolder = "icons";

    private static readonly string[] ImageExtensions = [".svg", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"];
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly object _sync = new();
    private List<IconPack>? _packs;

    public IconLibrary(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = rootDirectory;
    }

    /// <summary><c>%AppData%/DeckHost/icons</c> (or the platform equivalent).</summary>
    public static string DefaultRootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "DeckHost",
        "icons");

    public string RootDirectory { get; }

    /// <summary>Raised after a pack was installed, removed or an icon downloaded (on that thread).</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<IconPack> Packs
    {
        get
        {
            lock (_sync)
            {
                return _packs ??= LoadPacks();
            }
        }
    }

    /// <summary>
    /// Icons whose name, tags or pack name contain every word of <paramref name="query"/> (all
    /// icons for an empty query), best matches first.
    /// </summary>
    public IReadOnlyList<IconEntry> Search(string? query, int limit = 200)
    {
        var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Packs
            .SelectMany(static pack => pack.Icons)
            .Select(icon => (Icon: icon, Score: Score(icon, words)))
            .Where(static match => match.Score > 0)
            .OrderByDescending(static match => match.Score)
            .ThenBy(static match => match.Icon.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(static match => match.Icon)
            .ToList();
    }

    /// <summary>
    /// Installs a pack from a <c>.streamDeckIconPack</c>, a zip or a folder, replacing an installed
    /// pack with the same id. A pack without a manifest (just images) gets one named after the file.
    /// </summary>
    public IconPack Install(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var staging = Path.Combine(RootDirectory, ".staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            if (Directory.Exists(source))
            {
                // Under its own name, so a com.author.pack.sdIconPack folder gives the pack its id.
                CopyDirectory(source, Path.Combine(staging, Path.GetFileName(source.TrimEnd('/', '\\'))));
            }
            else if (File.Exists(source))
            {
                try
                {
                    // Rejects entries that would land outside the folder.
                    ZipFile.ExtractToDirectory(source, staging);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    throw new IconPackException($"Could not unpack '{Path.GetFileName(source)}': {ex.Message}");
                }
            }
            else
            {
                throw new IconPackException($"'{source}' doesn't exist.");
            }

            var packRoot = FindPackRoot(staging);
            var id = PackId(packRoot, source);
            if (id == DownloadsPackId)
            {
                id = "pack." + id;
            }

            if (!File.Exists(Path.Combine(packRoot, ManifestFile)))
            {
                WriteManifestForImages(packRoot, Path.GetFileNameWithoutExtension(source.TrimEnd('/', '\\')));
            }

            var pack = ReadPack(packRoot, id) ?? throw new IconPackException("The pack has no icons.");
            if (pack.Icons.Count == 0)
            {
                throw new IconPackException("The pack has no icons DeckHost can show (SVG, PNG, JPG, GIF, WebP).");
            }

            lock (_sync)
            {
                var target = Path.Combine(RootDirectory, id);
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }

                Directory.Move(packRoot, target);
                _packs = null;
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return Packs.First(installed => installed.Id == id);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    /// <summary>Removes a pack. Controls that use one of its icons fall back to the action's own icon.</summary>
    public bool Uninstall(string packId)
    {
        lock (_sync)
        {
            var target = Path.Combine(RootDirectory, packId);
            if (string.IsNullOrWhiteSpace(packId) || packId.Contains("..", StringComparison.Ordinal) || !Directory.Exists(target))
            {
                return false;
            }

            Directory.Delete(target, recursive: true);
            _packs = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Keeps an icon downloaded from Iconify in the downloads pack, with its set's license, and
    /// returns its file. Saving the same icon again returns the existing file.
    /// </summary>
    public string SaveDownloaded(IconifyIcon icon, string svg)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentException.ThrowIfNullOrWhiteSpace(svg);
        string path;
        lock (_sync)
        {
            var packDirectory = Path.Combine(RootDirectory, DownloadsPackId);
            var relative = $"{SafeName(icon.Prefix)}/{SafeName(icon.Name)}.svg";
            path = Path.Combine(packDirectory, IconsFolder, SafeName(icon.Prefix), SafeName(icon.Name) + ".svg");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, svg);

            var manifestPath = Path.Combine(packDirectory, ManifestFile);
            if (!File.Exists(manifestPath))
            {
                WriteJson(manifestPath, new JsonObject
                {
                    ["Name"] = "Downloaded icons",
                    ["Version"] = "1.0",
                    ["Description"] = "Icons picked from Iconify (open-source icon sets). Each set's license is in license.txt.",
                    ["Author"] = "Iconify",
                    ["URL"] = "https://iconify.design",
                    ["License"] = LicenseFile,
                });
            }

            var iconsPath = Path.Combine(packDirectory, IconsFile);
            var list = File.Exists(iconsPath) && JsonNode.Parse(File.ReadAllText(iconsPath)) is JsonArray existing ? existing : [];
            if (!list.OfType<JsonObject>().Any(entry => (string?)entry["path"] == relative))
            {
                var tags = new JsonArray(icon.SetName, icon.Prefix);
                if (!string.IsNullOrWhiteSpace(icon.License))
                {
                    tags.Add(icon.License);
                }

                list.Add(new JsonObject { ["path"] = relative, ["name"] = icon.Name, ["tags"] = tags });
                WriteJson(iconsPath, list);
            }

            var licensePath = Path.Combine(packDirectory, LicenseFile);
            var licenseLine = $"{icon.SetName} ({icon.Prefix}): {icon.License ?? "see the set"}{(icon.LicenseUrl is null ? string.Empty : $" - {icon.LicenseUrl}")}{(icon.Author is null ? string.Empty : $" - by {icon.Author}")}";
            var licenses = File.Exists(licensePath) ? File.ReadAllText(licensePath) : "Icons downloaded from Iconify (https://iconify.design). Licenses per icon set:\n";
            if (!licenses.Contains($"({icon.Prefix}):", StringComparison.Ordinal))
            {
                File.WriteAllText(licensePath, licenses.TrimEnd() + "\n" + licenseLine + "\n");
            }

            _packs = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return path;
    }

    private List<IconPack> LoadPacks()
    {
        if (!Directory.Exists(RootDirectory))
        {
            return [];
        }

        var packs = new List<IconPack>();
        foreach (var directory in Directory.GetDirectories(RootDirectory))
        {
            var id = Path.GetFileName(directory);
            if (id.StartsWith('.'))
            {
                continue;
            }

            try
            {
                if (ReadPack(directory, id) is { } pack)
                {
                    packs.Add(pack);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // A damaged pack doesn't hide the others.
            }
        }

        return packs.OrderBy(static pack => pack.IsDownloads ? 0 : 1).ThenBy(static pack => pack.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Reads a pack in the Stream Deck layout; null without a manifest.</summary>
    internal static IconPack? ReadPack(string directory, string id)
    {
        var manifestPath = Path.Combine(directory, ManifestFile);
        if (!File.Exists(manifestPath) || JsonNode.Parse(File.ReadAllText(manifestPath)) is not JsonObject manifest)
        {
            return null;
        }

        var name = Text(manifest, "Name") ?? id;
        var iconsDirectory = Path.Combine(directory, IconsFolder);
        var icons = new List<IconEntry>();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var iconsPath = Path.Combine(directory, IconsFile);
        if (File.Exists(iconsPath) && JsonNode.Parse(File.ReadAllText(iconsPath)) is JsonArray entries)
        {
            foreach (var entry in entries.OfType<JsonObject>())
            {
                var relative = Text(entry, "path");
                if (relative is null || !IsImage(relative))
                {
                    continue;
                }

                var file = Path.GetFullPath(Path.Combine(iconsDirectory, relative));
                if (!file.StartsWith(Path.GetFullPath(iconsDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(file))
                {
                    continue;
                }

                var tags = entry["tags"] is JsonArray tagArray
                    ? tagArray.Select(static tag => tag?.GetValueKind() == JsonValueKind.String ? (string?)tag : null).OfType<string>().ToList()
                    : [];
                icons.Add(new IconEntry(id, name, Text(entry, "name") ?? Path.GetFileNameWithoutExtension(relative), tags, file));
                listed.Add(file);
            }
        }

        // Images in icons/ that icons.json doesn't list still show (named after the file).
        if (Directory.Exists(iconsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(iconsDirectory, "*", SearchOption.AllDirectories).Where(IsImage).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (listed.Add(Path.GetFullPath(file)))
                {
                    icons.Add(new IconEntry(id, name, Path.GetFileNameWithoutExtension(file), [], Path.GetFullPath(file)));
                }
            }
        }

        var licenseFile = Text(manifest, "License") ?? LicenseFile;
        var licensePath = Path.Combine(directory, licenseFile);
        var previewFile = Text(manifest, "Icon");
        var previewPath = previewFile is null ? null : Path.Combine(directory, previewFile);
        return new IconPack(
            id,
            name,
            Text(manifest, "Version") ?? "1.0",
            Text(manifest, "Author"),
            Text(manifest, "Description"),
            Text(manifest, "URL"),
            directory,
            previewPath is not null && File.Exists(previewPath) ? previewPath : null,
            File.Exists(licensePath) && !licenseFile.Contains("..", StringComparison.Ordinal) ? File.ReadAllText(licensePath) : null,
            icons);
    }

    /// <summary>The folder with the manifest: the archive's root, or its one <c>*.sdIconPack</c> (or other) folder.</summary>
    private static string FindPackRoot(string staging)
    {
        if (File.Exists(Path.Combine(staging, ManifestFile)))
        {
            return staging;
        }

        var directories = Directory.GetDirectories(staging);
        var withManifest = directories.Where(directory => File.Exists(Path.Combine(directory, ManifestFile))).ToList();
        if (withManifest.Count == 1)
        {
            return withManifest[0];
        }

        // Only images: they become the pack's icons/ folder.
        var images = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Where(IsImage).ToList();
        if (images.Count == 0)
        {
            throw new IconPackException("No icon pack found: there is no manifest.json and there are no images.");
        }

        var root = Path.Combine(staging, ".pack");
        var iconsDirectory = Path.Combine(root, IconsFolder);
        Directory.CreateDirectory(iconsDirectory);
        foreach (var image in images)
        {
            var relative = Path.GetRelativePath(staging, image);
            var target = Path.Combine(iconsDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(image, target);
        }

        return root;
    }

    private static string PackId(string packRoot, string source)
    {
        var folder = Path.GetFileName(packRoot.TrimEnd('/', '\\'));
        var name = folder.EndsWith(".sdIconPack", StringComparison.OrdinalIgnoreCase)
            ? folder[..^".sdIconPack".Length]
            : File.Exists(Path.Combine(packRoot, ManifestFile)) && folder != ".pack" && !folder.StartsWith(".staging-", StringComparison.Ordinal)
                ? folder
                : Path.GetFileNameWithoutExtension(source.TrimEnd('/', '\\'));
        var id = SafeName(name).ToLowerInvariant();
        return string.IsNullOrEmpty(id) ? "pack" : id;
    }

    private static void WriteManifestForImages(string packRoot, string name)
    {
        WriteJson(Path.Combine(packRoot, ManifestFile), new JsonObject
        {
            ["Name"] = name,
            ["Version"] = "1.0",
            ["Description"] = "Images installed as an icon pack.",
        });
    }

    private static int Score(IconEntry icon, string[] words)
    {
        if (words.Length == 0)
        {
            return 1;
        }

        var score = 0;
        foreach (var word in words)
        {
            if (icon.Name.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                score += 8;
            }
            else if (icon.Name.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
            }
            else if (icon.Name.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }
            else if (icon.Tags.Any(tag => tag.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                score += 2;
            }
            else if (icon.PackName.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }
            else
            {
                return 0;
            }
        }

        return score;
    }

    internal static bool IsImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string? Text(JsonObject node, string key) =>
        node.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value is JsonValue value &&
        value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    private static string SafeName(string name) => UnsafeCharacters().Replace(name.Trim(), "-").Trim('-', '.');

    private static void WriteJson(string path, JsonNode node) => File.WriteAllText(path, node.ToJsonString(WriteOptions));

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    [GeneratedRegex(@"[^A-Za-z0-9._-]+")]
    private static partial Regex UnsafeCharacters();
}

/// <summary>An icon pack couldn't be installed; the message says why, for the user.</summary>
public sealed class IconPackException(string message) : Exception(message);
