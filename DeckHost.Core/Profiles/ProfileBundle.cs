using DeckHost.Contracts.Models;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeckHost.Core.Profiles;

/// <summary>A profile that couldn't be read or written.</summary>
public sealed class ProfileBundleException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// A shared profile: a <c>.deckhostprofile</c> zip with <c>profile.json</c> (the profile with its
/// pages and folders) and the images it uses in <c>assets/</c>, so it works on another computer.
/// Importing gives everything new ids (an import never replaces a profile) and unpacks the
/// images into a folder of their own.
/// </summary>
public static class ProfileBundle
{
    public const string Extension = ".deckhostprofile";
    public const string Format = "deckhost-profile";
    public const int Version = 1;

    private const string ManifestName = "profile.json";
    private const string AssetsFolder = "assets/";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record Manifest(string Format, int Version, IReadOnlyList<DeviceProfile> Profiles);

    /// <summary>Writes a profile (first) with its pages and folders, and the image files they use.</summary>
    public static void Export(IReadOnlyList<DeviceProfile> layouts, string path)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        if (layouts.Count == 0)
        {
            throw new ProfileBundleException("There is no profile to export.");
        }

        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // file on disk -> name in the bundle
        string? Pack(string? file)
        {
            if (string.IsNullOrWhiteSpace(file) || !Path.IsPathFullyQualified(file) || !File.Exists(file))
            {
                return file;
            }

            if (!assets.TryGetValue(file, out var name))
            {
                name = $"{AssetsFolder}{assets.Count + 1:D3}{Path.GetExtension(file).ToLowerInvariant()}";
                assets[file] = name;
            }

            return name;
        }

        var packed = layouts.Select(layout => MapFiles(layout, Pack)).ToList();
        try
        {
            var temp = path + ".tmp";
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                using (var stream = zip.CreateEntry(ManifestName).Open())
                {
                    JsonSerializer.Serialize(stream, new Manifest(Format, Version, packed), Options);
                }

                foreach (var (file, name) in assets)
                {
                    zip.CreateEntryFromFile(file, name);
                }
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProfileBundleException($"Couldn't write {Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reads a bundle for a device: new ids for every layout (Folder keys follow), images unpacked
    /// into <paramref name="assetDirectory"/>. The profile comes first.
    /// </summary>
    public static IReadOnlyList<DeviceProfile> Import(string path, string deviceId, string assetDirectory)
    {
        Manifest manifest;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry(ManifestName) ?? throw new ProfileBundleException($"{Path.GetFileName(path)} isn't a DeckHost profile (no {ManifestName}).");
            using (var stream = entry.Open())
            {
                manifest = JsonSerializer.Deserialize<Manifest>(stream, Options)
                    ?? throw new ProfileBundleException($"{Path.GetFileName(path)} is empty.");
            }

            if (manifest.Format != Format || manifest.Profiles is not { Count: > 0 })
            {
                throw new ProfileBundleException($"{Path.GetFileName(path)} isn't a DeckHost profile.");
            }

            if (manifest.Version > Version)
            {
                throw new ProfileBundleException($"{Path.GetFileName(path)} was made by a newer DeckHost; update DeckHost to import it.");
            }

            // New ids: the profile, its pages and folders (and every setting that points at one).
            var stamp = Guid.NewGuid().ToString("N")[..8];
            var ids = manifest.Profiles.ToDictionary(static layout => layout.Id, layout => $"{layout.Id}.{stamp}", StringComparer.Ordinal);
            var folder = Path.Combine(assetDirectory, ids[manifest.Profiles[0].Id]);
            string? Unpack(string? file)
            {
                if (string.IsNullOrWhiteSpace(file) || !file.StartsWith(AssetsFolder, StringComparison.Ordinal))
                {
                    return file;
                }

                var target = Path.GetFullPath(Path.Combine(folder, Path.GetFileName(file)));
                if (!File.Exists(target) && zip.GetEntry(file) is { } asset)
                {
                    Directory.CreateDirectory(folder);
                    asset.ExtractToFile(target, overwrite: true);
                }

                return target;
            }

            return [.. manifest.Profiles.Select(layout => Renumber(MapFiles(layout, Unpack), ids, deviceId))];
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            throw new ProfileBundleException($"Couldn't read {Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// A copy of a profile's layouts for <paramref name="deviceId"/> with new ids (settings that point at a
    /// layout, such as Folder keys, follow). Images stay where they are.
    /// </summary>
    public static IReadOnlyList<DeviceProfile> Copy(IReadOnlyList<DeviceProfile> layouts, string deviceId)
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var ids = layouts.ToDictionary(static layout => layout.Id, layout => $"{layout.Id}.{stamp}", StringComparer.Ordinal);
        return [.. layouts.Select(layout => Renumber(layout, ids, deviceId))];
    }

    /// <summary>A layout with every image path passed through <paramref name="map"/>.</summary>
    internal static DeviceProfile MapFiles(DeviceProfile layout, Func<string?, string?> map) => layout with
    {
        Background = layout.Background is { } background ? background with { ImagePath = map(background.ImagePath) } : null,
        Bindings = [.. layout.Bindings.Select(binding => binding with
        {
            Appearance = binding.Appearance with
            {
                ImagePath = map(binding.Appearance.ImagePath),
                IconPath = map(binding.Appearance.IconPath),
            },
        })],
    };

    private static DeviceProfile Renumber(DeviceProfile layout, IReadOnlyDictionary<string, string> ids, string deviceId)
    {
        ActionReference Point(ActionReference action) => action with
        {
            Settings = new ActionSettings(action.EffectiveSettings.Select(pair =>
                ids.TryGetValue(pair.Value, out var newId) ? new KeyValuePair<string, string>(pair.Key, newId) : pair)),
        };

        return layout with
        {
            Id = ids[layout.Id],
            DeviceId = deviceId,
            ParentId = layout.ParentId is { } parent && ids.TryGetValue(parent, out var newParent) ? newParent : null,
            Bindings = [.. layout.Bindings.Select(binding => binding with
            {
                Action = Point(binding.Action),
                Steps = [.. binding.Steps.Select(step => step with { Action = Point(step.Action) })],
                Workflow = binding.Workflow is { } workflow
                    ? workflow with { Nodes = [.. workflow.Nodes.Select(node => node.Action is { } action ? node with { Action = Point(action) } : node)] }
                    : null,
            })],
        };
    }
}
