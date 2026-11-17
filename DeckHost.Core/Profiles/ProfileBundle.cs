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

    private sealed record Manifest(string Format, int Version, IReadOnlyList<DeviceProfile> Profiles)
    {
        /// <summary>The shared workflows the profile's Call nodes run (directly or through other shared workflows).</summary>
        public IReadOnlyList<Workflow>? SharedWorkflows { get; init; }
    }

    /// <summary>What a bundle holds: the layouts and the shared workflows they call (with new ids).</summary>
    public sealed record Imported(IReadOnlyList<DeviceProfile> Layouts, IReadOnlyList<Workflow> SharedWorkflows);

    /// <summary>The shared workflows the layouts call, directly or through other shared workflows.</summary>
    public static IReadOnlyList<Workflow> SharedWorkflowsUsed(IEnumerable<DeviceProfile> layouts, IReadOnlyList<Workflow> shared)
    {
        var byId = shared.Where(static workflow => workflow.Id is not null).GroupBy(static workflow => workflow.Id!, StringComparer.Ordinal).ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var used = new List<Workflow>();
        var queue = new Queue<string>(layouts.SelectMany(static layout => layout.Bindings).Where(static binding => binding.Workflow is not null).SelectMany(static binding => CalledIds(binding.Workflow!)));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (seen.Add(id) && byId.TryGetValue(id, out var workflow))
            {
                used.Add(workflow);
                foreach (var next in CalledIds(workflow))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return used;
    }

    private static IEnumerable<string> CalledIds(Workflow workflow) =>
        workflow.Nodes.Where(static node => node.Kind == WorkflowNodeKinds.Call)
            .Select(static node => node.EffectiveSettings.Get(WorkflowKeys.Workflow).Trim())
            .Where(static id => id.Length > 0);

    /// <summary>Writes a profile (first) with its pages and folders, and the image files they use.</summary>
    /// <param name="sharedWorkflows">All the user's shared workflows: the ones the layouts call go into the file.</param>
    public static void Export(IReadOnlyList<DeviceProfile> layouts, string path, IReadOnlyList<Workflow>? sharedWorkflows = null)
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
                    JsonSerializer.Serialize(stream, new Manifest(Format, Version, packed) { SharedWorkflows = sharedWorkflows is { Count: > 0 } ? SharedWorkflowsUsed(packed, sharedWorkflows) is { Count: > 0 } used ? used : null : null }, Options);
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
    public static IReadOnlyList<DeviceProfile> Import(string path, string deviceId, string assetDirectory) =>
        ImportBundle(path, deviceId, assetDirectory).Layouts;

    /// <summary>As <see cref="Import"/>, with the shared workflows the profile calls (new ids; the Call nodes follow).</summary>
    public static Imported ImportBundle(string path, string deviceId, string assetDirectory)
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

            var workflowIds = (manifest.SharedWorkflows ?? []).Where(static workflow => workflow.Id is not null)
                .GroupBy(static workflow => workflow.Id!, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, group => $"{group.Key}.{stamp}", StringComparer.Ordinal);
            return new Imported(
                [.. manifest.Profiles.Select(layout => Renumber(MapFiles(layout, Unpack), ids, deviceId, workflowIds))],
                [.. (manifest.SharedWorkflows ?? []).Where(workflow => workflow.Id is not null).Select(workflow => RemapCalls(workflow, workflowIds) with { Id = workflowIds[workflow.Id!] })]);
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
        return [.. layouts.Select(layout => Renumber(layout, ids, deviceId, new Dictionary<string, string>()))];
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

    /// <summary>The workflow with its Call nodes pointing at the new ids of the shared workflows.</summary>
    private static Workflow RemapCalls(Workflow workflow, IReadOnlyDictionary<string, string> workflowIds) =>
        workflowIds.Count == 0
            ? workflow
            : workflow with
            {
                Nodes = [.. workflow.Nodes.Select(node =>
                    node.Kind == WorkflowNodeKinds.Call && workflowIds.TryGetValue(node.EffectiveSettings.Get(WorkflowKeys.Workflow).Trim(), out var newId)
                        ? node with { Settings = new ActionSettings(node.EffectiveSettings.Select(pair => pair.Key == WorkflowKeys.Workflow ? new KeyValuePair<string, string>(pair.Key, newId) : pair)) }
                        : node)],
            };

    private static DeviceProfile Renumber(DeviceProfile layout, IReadOnlyDictionary<string, string> ids, string deviceId, IReadOnlyDictionary<string, string> workflowIds)
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
                    ? RemapCalls(workflow with { Nodes = [.. workflow.Nodes.Select(node => node.Action is { } action ? node with { Action = Point(action) } : node)] }, workflowIds)
                    : null,
            })],
        };
    }
}
