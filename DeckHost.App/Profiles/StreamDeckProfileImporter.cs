using DeckHost.App.SystemPlugins;
using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeckHost.App.Profiles;

/// <summary>What an import brought in and what it couldn't.</summary>
/// <param name="Layouts">The profile first, then its pages and folders.</param>
/// <param name="Imported">Keys and dials that got an action.</param>
/// <param name="NotPlaced">Keys and dials with no place on this device (a bigger Stream Deck).</param>
/// <param name="Unavailable">Actions DeckHost doesn't have (their key keeps its title and image).</param>
public sealed record StreamDeckImportResult(IReadOnlyList<DeviceProfile> Layouts, int Imported, int NotPlaced, IReadOnlyList<string> Unavailable)
{
    /// <summary>Every action DeckHost couldn't map, with the plugin the profile says it comes from (to find missing plugins).</summary>
    public IReadOnlyList<StreamDeckActionUse> UnavailableActions { get; init; } = [];
}

/// <summary>An action a Stream Deck profile uses, as the profile names it.</summary>
/// <param name="ActionUuid">The action's UUID (<c>&lt;plugin uuid&gt;.&lt;action&gt;</c> by Stream Deck convention).</param>
/// <param name="PluginUuid">The plugin's UUID, when the profile records it (Stream Deck 6 writes a <c>Plugin</c> object per action).</param>
/// <param name="PluginName">The plugin's name, when the profile records it.</param>
public sealed record StreamDeckActionUse(string ActionUuid, string? PluginUuid = null, string? PluginName = null);

/// <summary>A Stream Deck plugin an imported profile uses that isn't installed.</summary>
/// <param name="Id">The plugin's UUID (the store's id for Elgato archive and OpenAction plugins).</param>
/// <param name="Name">Its name, when the profile records it.</param>
/// <param name="ActionUuids">The actions of it the profile uses.</param>
public sealed record MissingPlugin(string Id, string? Name, IReadOnlyList<string> ActionUuids);

/// <summary>
/// Imports an Elgato <c>.streamDeckProfile</c> (a zip of a <c>&lt;id&gt;.sdProfile</c> folder) as
/// a DeckHost profile. Reads both layouts the Stream Deck app writes: one page with its
/// <c>Actions</c> in the profile's manifest, and pages (<c>Pages</c>) with a manifest each under
/// <c>Profiles/&lt;page&gt;</c> holding <c>Controllers</c> (Keypad, Encoder). Keys keep their place
/// ("column,row", as far as this device has room), their title and image. Elgato's built-in
/// actions become DeckHost's (hotkey, website, open, text, media, pages, folders, Multi Action,
/// Multi Action Switch); actions of installed Stream Deck plugins stay those actions; anything
/// else keeps its title and image so the layout still looks the same.
/// </summary>
public static class StreamDeckProfileImporter
{
    public const string Extension = ".streamDeckProfile";

    private const string BuiltIn = SystemPluginSet.BuiltInPluginId;
    private const string Media = SystemPluginSet.MediaPluginId;
    private const string System = SystemPluginSet.SystemControlsPluginId;

    /// <param name="controls">The device's controls (keys in reading order, then the rest).</param>
    /// <param name="actions">Every loaded action, to find DeckHost's and installed Stream Deck plugins' actions.</param>
    /// <param name="assetDirectory">Where the keys' images are unpacked (a folder per import).</param>
    public static StreamDeckImportResult Import(string path, string deviceId, IReadOnlyList<PhysicalControl> controls, IReadOnlyCollection<ActionItemViewModel> actions, string assetDirectory)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return new Reader(zip, deviceId, controls, actions, assetDirectory).Read(Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new Core.Profiles.ProfileBundleException($"Couldn't read {Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    /// <summary>The Stream Deck app's own actions: part of the app, not a plugin anybody can install.</summary>
    private const string StreamDeckAppPrefix = "com.elgato.streamdeck.";

    /// <summary>
    /// The plugins behind <paramref name="actions"/> that aren't installed, most used first. A plugin is the one the profile
    /// names, else the action's UUID without its last part. It counts as installed when an installed id is that plugin
    /// or a prefix of the action's UUID (<c>com.elgato.discord</c> for <c>com.elgato.discord.voice.mute</c>). The Stream
    /// Deck app's own actions (<c>com.elgato.streamdeck.*</c>) are left out.
    /// </summary>
    public static IReadOnlyList<MissingPlugin> FindMissingPlugins(IEnumerable<StreamDeckActionUse> actions, IEnumerable<string> installedPluginIds)
    {
        var installed = installedPluginIds.Where(static id => id.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var uses = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var action in actions)
        {
            if (PluginIdOf(action) is not { } id || IsStreamDeckApp(action.ActionUuid) || IsStreamDeckApp(id + ".") ||
                installed.Contains(id) || installed.Any(installedId => action.ActionUuid.StartsWith(installedId + ".", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!uses.TryGetValue(id, out var actionUuids))
            {
                uses[id] = actionUuids = [];
                order.Add(id);
            }

            if (!actionUuids.Contains(action.ActionUuid, StringComparer.OrdinalIgnoreCase))
            {
                actionUuids.Add(action.ActionUuid);
            }

            if (names.GetValueOrDefault(id) is null && !string.IsNullOrWhiteSpace(action.PluginName))
            {
                names[id] = action.PluginName.Trim();
            }
        }

        return [.. order
            .Select(id => new MissingPlugin(id, names.GetValueOrDefault(id), uses[id]))
            .OrderByDescending(static plugin => plugin.ActionUuids.Count)];
    }

    private static bool IsStreamDeckApp(string uuid) => uuid.StartsWith(StreamDeckAppPrefix, StringComparison.OrdinalIgnoreCase);

    private static string? PluginIdOf(StreamDeckActionUse action)
    {
        if (!string.IsNullOrWhiteSpace(action.PluginUuid))
        {
            return action.PluginUuid.Trim();
        }

        var dot = action.ActionUuid.LastIndexOf('.');
        return dot > 0 ? action.ActionUuid[..dot] : null;
    }

    private sealed class Reader(ZipArchive zip, string deviceId, IReadOnlyList<PhysicalControl> controls, IReadOnlyCollection<ActionItemViewModel> actions, string assetDirectory)
    {
        private readonly string _stamp = Guid.NewGuid().ToString("N")[..8];
        private readonly List<DeviceProfile?> _layouts = [];
        private readonly HashSet<string> _unavailable = new(StringComparer.Ordinal);
        private readonly List<StreamDeckActionUse> _unavailableActions = [];
        private readonly HashSet<string> _read = new(StringComparer.OrdinalIgnoreCase);
        private string _assets = assetDirectory;
        private int _imported;
        private int _notPlaced;

        private List<PhysicalControl> Keys => [.. controls.Where(static control => control.Type == ControlType.Key)];

        private List<PhysicalControl> Encoders => [.. controls.Where(static control => control.Type == ControlType.Encoder)];

        public StreamDeckImportResult Read(string fallbackName)
        {
            // The profile's manifest: the one closest to the top of the archive.
            var top = zip.Entries
                .Where(static entry => entry.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static entry => entry.FullName.Count(static c => c == '/'))
                .FirstOrDefault() ?? throw new InvalidDataException("It isn't a Stream Deck profile (no manifest.json).");
            var manifest = ReadJson(top) ?? throw new InvalidDataException("Its manifest is empty.");
            var folder = Directory(top.FullName);
            var name = Text(manifest, "Name") is { Length: > 0 } title ? title : fallbackName;
            var rootId = $"profile.import.{_stamp}";
            _assets = Path.Combine(assetDirectory, rootId);

            if (manifest["Pages"] is JsonObject pages && pages["Pages"] is JsonArray pageIds && pageIds.Count > 0)
            {
                // Pages: the first is the profile itself, the others its pages.
                var first = true;
                foreach (var pageId in pageIds.Select(static id => id?.GetValue<string>()).OfType<string>())
                {
                    var pageManifest = FindLayout(folder, pageId);
                    var id = first ? rootId : $"{rootId}.page.{_layouts.Count}";
                    AddLayout(id, first ? name : $"{name} page", first ? null : ProfileKinds.Page, first ? null : rootId, pageManifest, folder);
                    first = false;
                }

                if (_layouts.Count == 0)
                {
                    AddLayout(rootId, name, null, null, null, folder);
                }
            }
            else
            {
                AddLayout(rootId, name, null, null, (manifest, folder), folder);
            }

            return new StreamDeckImportResult([.. _layouts.OfType<DeviceProfile>()], _imported, _notPlaced, [.. _unavailable.Order(StringComparer.Ordinal)])
            {
                UnavailableActions = [.. _unavailableActions.Distinct()],
            };
        }

        /// <summary>A page or child profile's manifest, by its id: a folder named after it under Profiles.</summary>
        private (JsonObject Manifest, string Folder)? FindLayout(string profileFolder, string id)
        {
            var entry = zip.Entries.FirstOrDefault(candidate =>
                candidate.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) &&
                Directory(candidate.FullName) is var folder &&
                folder.StartsWith(profileFolder, StringComparison.OrdinalIgnoreCase) &&
                (Path.GetFileName(folder.TrimEnd('/')).Equals(id, StringComparison.OrdinalIgnoreCase) ||
                 Path.GetFileName(folder.TrimEnd('/')).Equals(id + ".sdProfile", StringComparison.OrdinalIgnoreCase)));
            return entry is not null && ReadJson(entry) is { } manifest ? (manifest, Directory(entry.FullName)) : null;
        }

        private void AddLayout(string id, string name, string? kind, string? parentId, (JsonObject Manifest, string Folder)? source, string profileFolder)
        {
            if (!_read.Add(id))
            {
                return;
            }

            // Its place first: a layout comes before the folders its keys open (read while reading it).
            var slot = _layouts.Count;
            _layouts.Add(null);
            var bindings = new List<ControlBinding>();
            if (source is { } layout)
            {
                // One page: Actions at the top (single-page profiles) or per controller (paged ones).
                if (layout.Manifest["Controllers"] is JsonArray controllers)
                {
                    foreach (var controller in controllers.OfType<JsonObject>())
                    {
                        var encoder = string.Equals(Text(controller, "Type"), "Encoder", StringComparison.OrdinalIgnoreCase);
                        AddActions(controller["Actions"] as JsonObject, encoder, layout.Folder, id, profileFolder, bindings);
                    }
                }
                else
                {
                    AddActions(layout.Manifest["Actions"] as JsonObject, encoder: false, layout.Folder, id, profileFolder, bindings);
                }
            }

            _layouts[slot] = new DeviceProfile(id, name, deviceId, bindings) { Kind = kind, ParentId = parentId };
        }

        private void AddActions(JsonObject? actionsByPlace, bool encoder, string layoutFolder, string layoutId, string profileFolder, List<ControlBinding> bindings)
        {
            if (actionsByPlace is null)
            {
                return;
            }

            var keys = Keys;
            var encoders = Encoders;
            var columns = Math.Max(1, keys.Count(key => Math.Abs(key.Bounds.Y - (keys.FirstOrDefault()?.Bounds.Y ?? 0)) < 0.5));
            foreach (var (place, node) in actionsByPlace)
            {
                if (node is not JsonObject action || ParsePlace(place) is not { } at)
                {
                    continue;
                }

                var control = encoder
                    ? (at.Column < encoders.Count ? encoders[at.Column] : null)
                    : (at.Column < columns && (at.Row * columns) + at.Column < keys.Count ? keys[(at.Row * columns) + at.Column] : null);
                if (control is null)
                {
                    _notPlaced++;
                    continue;
                }

                if (MapBinding(action, control, layoutFolder, layoutId, profileFolder) is { } binding)
                {
                    bindings.Add(binding);
                    _imported++;
                }
            }
        }

        private ControlBinding? MapBinding(JsonObject action, PhysicalControl control, string layoutFolder, string layoutId, string profileFolder)
        {
            var uuid = Text(action, "UUID") ?? string.Empty;
            var settings = action["Settings"] as JsonObject;
            var appearance = Appearance(action, layoutFolder);

            switch (uuid)
            {
                case "com.elgato.streamdeck.multiactions.routine":
                    return Steps(control, appearance, ProfileActionKind.MultiAction, "builtin.multi-action", (settings?["Routine"], null));
                case "com.elgato.streamdeck.multiactions.routine2":
                    return Steps(control, appearance, ProfileActionKind.MultiActionSwitch, "builtin.multi-action-switch", (settings?["Routine"], StepGroups.First), (settings?["RoutineAlt"], StepGroups.Second));
                case "com.elgato.streamdeck.profile.openchild" when Text(settings, "ProfileUUID") is { } child:
                {
                    // The folder's keys are in the child profile; it opens from this layout.
                    var folderId = $"{layoutId}.folder.{_layouts.Count + 1}";
                    var childLayout = FindLayout(profileFolder, child);
                    AddLayout(folderId, Text(childLayout?.Manifest, "Name") is { Length: > 0 } folderName ? folderName : appearance.Text ?? "Folder", ProfileKinds.Folder, layoutId, childLayout, profileFolder);
                    return Single(control, appearance, BuiltIn, FolderAction.ActionId, (FolderAction.FolderIdKey, folderId), (FolderAction.NameKey, appearance.Text ?? "Folder"));
                }
            }

            if (MapAction(uuid, settings) is { } mapped)
            {
                return Single(control, appearance, mapped.PluginId, mapped.ActionId, mapped.Settings);
            }

            // An installed Stream Deck plugin's action stays that action (its settings are set up again in DeckHost).
            if (actions.FirstOrDefault(item => item.Id.Equals(uuid, StringComparison.OrdinalIgnoreCase)) is { } plugin && plugin.FitsControl(control))
            {
                return new ControlBinding(control.Id, ProfileActionKind.Single, plugin.ToActionReference(plugin.DefaultSettings), appearance, []);
            }

            // Not available here: keep how the key looked, and which plugin's action it is, so the key
            // shows it as unavailable and gets the action back by itself once the plugin is installed.
            var name = Text(action, "Name") is { Length: > 0 } actionName ? actionName : uuid;
            _unavailable.Add(name);
            if (NoteUnavailable(action, uuid) is { } use)
            {
                var pluginId = PluginIdOf(use) ?? uuid;
                var reference = new ActionReference(pluginId, use.PluginName ?? pluginId, uuid, name, InputCapabilities.None, OutputCapabilities.None, ActionSettings.Empty);
                return new ControlBinding(control.Id, ProfileActionKind.Single, reference, appearance, []);
            }

            return appearance.Text is null && appearance.ImagePath is null
                ? null
                : Single(control, appearance, BuiltIn, "builtin.display-content");
        }

        /// <summary>Elgato's built-in actions as DeckHost's: (plugin, action, settings).</summary>
        private static (string PluginId, string ActionId, (string, string)[] Settings)? MapAction(string uuid, JsonObject? settings) => uuid switch
        {
            "com.elgato.streamdeck.system.hotkey" or "com.elgato.streamdeck.system.hotkeyswitch" => (System, "system.hotkey", []),
            "com.elgato.streamdeck.system.website" => (System, "system.open", Pair("target", Text(settings, "path") ?? Text(settings, "url"))),
            "com.elgato.streamdeck.system.open" => (System, "system.launch", Pair("path", Text(settings, "path"))),
            "com.elgato.streamdeck.system.text" => (System, "system.type-text", Pair("text", Text(settings, "pastedText") ?? Text(settings, "text"))),
            "com.elgato.streamdeck.system.multimedia" => (Media, MediaAction(settings), []),
            "com.elgato.streamdeck.page.next" => (BuiltIn, "builtin.page", [("mode", "next")]),
            "com.elgato.streamdeck.page.previous" => (BuiltIn, "builtin.page", [("mode", "previous")]),
            "com.elgato.streamdeck.page.goto" or "com.elgato.streamdeck.page.indicator" => (BuiltIn, "builtin.page", [("mode", "next")]),
            "com.elgato.streamdeck.profile.backtoparent" => (BuiltIn, FolderBackAction.ActionId, []),
            "com.elgato.streamdeck.profile.rotate" => (BuiltIn, "builtin.switch-profile", [("mode", "next")]),
            _ => null,
        };

        /// <summary>The Multimedia action's choice (actionIdx): play/pause, next, previous, mute, volume up, volume down.</summary>
        private static string MediaAction(JsonObject? settings) =>
            (settings?["actionIdx"] is JsonValue value && value.TryGetValue<int>(out var index) ? index : 0) switch
            {
                1 => "media.next",
                2 => "media.previous",
                3 => "media.mute",
                4 => "media.volumeup",
                5 => "media.volumedown",
                _ => "media.playpause",
            };

        private static (string, string)[] Pair(string key, string? value) => value is { Length: > 0 } ? [(key, value)] : [];

        private ControlBinding Steps(PhysicalControl control, ControlAppearance appearance, ProfileActionKind kind, string scaffoldId, params (JsonNode? Routine, string? Group)[] sets)
        {
            var steps = new List<MultiActionStep>();
            foreach (var (routine, group) in sets)
            {
                foreach (var step in (routine as JsonArray ?? []).OfType<JsonObject>())
                {
                    var uuid = Text(step, "UUID") ?? string.Empty;
                    if (MapAction(uuid, step["Settings"] as JsonObject) is { } mapped)
                    {
                        steps.Add(new MultiActionStep(steps.Count, Reference(mapped.PluginId, mapped.ActionId, mapped.Settings)) { Group = group });
                    }
                    else if (actions.FirstOrDefault(item => item.Id.Equals(uuid, StringComparison.OrdinalIgnoreCase)) is { CanBeMultiActionStep: true } plugin)
                    {
                        steps.Add(new MultiActionStep(steps.Count, plugin.ToActionReference(plugin.DefaultSettings)) { Group = group });
                    }
                    else
                    {
                        _unavailable.Add(Text(step, "Name") is { Length: > 0 } name ? name : uuid);
                        NoteUnavailable(step, uuid);
                    }
                }
            }

            return new ControlBinding(control.Id, kind, Reference(BuiltIn, scaffoldId, []), appearance, steps);
        }

        /// <summary>Keeps an unmapped action with the plugin its <c>Plugin</c> object names (Stream Deck 6 profiles).</summary>
        private StreamDeckActionUse? NoteUnavailable(JsonObject action, string uuid)
        {
            if (uuid.Length == 0)
            {
                return null;
            }

            var plugin = action["Plugin"] as JsonObject;
            var use = new StreamDeckActionUse(uuid, Text(plugin, "UUID"), Text(plugin, "Name"));
            _unavailableActions.Add(use);
            return use;
        }

        private ControlBinding Single(PhysicalControl control, ControlAppearance appearance, string pluginId, string actionId, params (string, string)[] settings) =>
            new(control.Id, ProfileActionKind.Single, Reference(pluginId, actionId, settings), appearance, []);

        private ActionReference Reference(string pluginId, string actionId, (string, string)[] settings)
        {
            var item = actions.FirstOrDefault(candidate => candidate.PluginId == pluginId && candidate.Id == actionId);
            var values = new ActionSettings((item?.DefaultSettings ?? ActionSettings.Empty)
                .Where(pair => !settings.Any(setting => setting.Item1 == pair.Key))
                .Concat(settings.Select(static setting => new KeyValuePair<string, string>(setting.Item1, setting.Item2))));
            return item is not null
                ? item.ToActionReference(values)
                : new ActionReference(pluginId, pluginId, actionId, actionId, InputCapabilities.None, OutputCapabilities.None, values);
        }

        /// <summary>The key's title and image (of its current state), the image unpacked next to the profile.</summary>
        private ControlAppearance Appearance(JsonObject action, string layoutFolder)
        {
            var states = action["States"] as JsonArray;
            var stateIndex = action["State"] is JsonValue value && value.TryGetValue<int>(out var index) ? index : 0;
            var state = states is { Count: > 0 } ? states[Math.Clamp(stateIndex, 0, states.Count - 1)] as JsonObject : null;
            var title = Text(state, "Title");
            var showTitle = Text(state, "TitleShow") is not "off" and not "false";
            return new ControlAppearance(showTitle && !string.IsNullOrWhiteSpace(title) ? title : null, Unpack(Text(state, "Image"), layoutFolder));
        }

        private string? Unpack(string? image, string layoutFolder)
        {
            if (string.IsNullOrWhiteSpace(image) || image.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var relative = image.Replace('\\', '/').TrimStart('/');
            var entry = zip.GetEntry(layoutFolder + relative)
                        ?? zip.Entries.FirstOrDefault(candidate => candidate.FullName.StartsWith(layoutFolder, StringComparison.OrdinalIgnoreCase) &&
                                                                   candidate.FullName.EndsWith("/" + relative, StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.Length > Core.Plugins.StoreMedia.MaxImageBytes)
            {
                return null;
            }

            global::System.IO.Directory.CreateDirectory(_assets);
            var target = Path.Combine(_assets, $"{zip.Entries.IndexOf(entry):D4}{Path.GetExtension(entry.Name).ToLowerInvariant()}");
            if (!File.Exists(target))
            {
                entry.ExtractToFile(target);
            }

            return target;
        }

        private static (int Column, int Row)? ParsePlace(string place)
        {
            var parts = place.Split(',');
            return parts.Length == 2 &&
                   int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var column) &&
                   int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var row)
                ? (column, row)
                : null;
        }

        private static JsonObject? ReadJson(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            return JsonNode.Parse(stream) as JsonObject;
        }

        private static string Directory(string entryPath)
        {
            var slash = entryPath.LastIndexOf('/');
            return slash < 0 ? string.Empty : entryPath[..(slash + 1)];
        }

        private static string? Text(JsonObject? json, string key) =>
            json?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}
