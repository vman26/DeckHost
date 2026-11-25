using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeckHost.Core.StreamDeck;

/// <summary>
/// manifest.json of an Elgato Stream Deck plugin (a "&lt;uuid&gt;.sdPlugin" folder).
/// Only the fields DeckHost uses are read; property names are matched case-insensitively.
/// </summary>
public sealed class StreamDeckManifest
{
    private StreamDeckManifest(string directory, JsonObject json)
    {
        Directory = directory;
        Uuid = Path.GetFileName(directory.TrimEnd('\\', '/')) is { } folder && folder.EndsWith(".sdPlugin", StringComparison.OrdinalIgnoreCase)
            ? folder[..^".sdPlugin".Length]
            : Get(json, "UUID") ?? Path.GetFileName(directory);
        Name = Get(json, "Name") ?? Uuid;
        Author = Get(json, "Author");
        Version = Get(json, "Version") ?? "1.0";
        Description = Get(json, "Description");
        Category = Get(json, "Category");
        Icon = Get(json, "Icon");
        Url = Get(json, "URL");
        PropertyInspectorPath = Get(json, "PropertyInspectorPath");
        DeviceNamespace = Get(json, "DeviceNamespace");
        CodePath = SelectCodePath(json, out var unsupported);
        UnsupportedReason = unsupported;

        foreach (var node in GetArray(json, "Actions"))
        {
            if (node is JsonObject action && Get(action, "UUID") is { } uuid)
            {
                Actions.Add(new StreamDeckActionManifest(
                    uuid,
                    Get(action, "Name") ?? uuid,
                    Get(action, "Tooltip"),
                    Get(action, "Icon"),
                    GetArray(action, "Controllers").Select(static controller => controller?.GetValue<string>() ?? string.Empty).Where(static c => c.Length > 0).DefaultIfEmpty("Keypad").ToArray(),
                    GetArray(action, "States").OfType<JsonObject>().Select(static state => new StreamDeckState(Get(state, "Image"), Get(state, "Title"))).ToArray(),
                    Get(action, "PropertyInspectorPath") ?? PropertyInspectorPath,
                    GetBool(action, "SupportedInMultiActions") ?? true,
                    GetBool(action, "VisibleInActionsList") ?? true));
            }
        }
    }

    public string Directory { get; }

    public string Uuid { get; }

    public string Name { get; }

    public string? Author { get; }

    public string Version { get; }

    public string? Description { get; }

    public string? Category { get; }

    public string? Icon { get; }

    /// <summary>The plugin's website (the manifest's <c>URL</c>).</summary>
    public string? Url { get; }

    public string? PropertyInspectorPath { get; }

    /// <summary>
    /// OpenAction device plugins (OpenDeck's: Mirabox, Ajazz, ...) name the prefix of the ids of
    /// the devices they drive. Such a plugin has no actions: it registers devices and reports
    /// their input (<see cref="OpenActionDevicePlugin"/>).
    /// </summary>
    public string? DeviceNamespace { get; }

    public bool IsDevicePlugin => !string.IsNullOrWhiteSpace(DeviceNamespace);

    /// <summary>Path (relative to <see cref="Directory"/>) of the plugin program for this OS, or null.</summary>
    public string? CodePath { get; }

    /// <summary>The plugin is a web page (run in a hidden browser) rather than a program.</summary>
    public bool IsHtml => CodePath is { } path &&
        (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase));

    /// <summary>Why the plugin can't run here (wrong OS, HTML plugin, ...); null when it can.</summary>
    public string? UnsupportedReason { get; }

    public List<StreamDeckActionManifest> Actions { get; } = [];

    public static StreamDeckManifest Load(string directory)
    {
        var path = Path.Combine(directory, "manifest.json");
        if (!File.Exists(path))
        {
            throw new InvalidDataException("manifest.json not found.");
        }

        var text = File.ReadAllText(path).TrimStart('﻿');
        if (JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is not JsonObject json)
        {
            throw new InvalidDataException("manifest.json is not a JSON object.");
        }

        // manifest.windows.json / manifest.macos.json / manifest.linux.json override fields.
        var osName = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var overridePath = Path.Combine(directory, $"manifest.{osName}.json");
        if (File.Exists(overridePath) && JsonNode.Parse(File.ReadAllText(overridePath).TrimStart('﻿')) is JsonObject overrides)
        {
            foreach (var (key, value) in overrides)
            {
                json[key] = value?.DeepClone();
            }
        }

        return new StreamDeckManifest(directory, json);
    }

    /// <summary>Finds an image the way Stream Deck does: path without extension, @2x preferred.</summary>
    public string? ResolveImage(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var basePath = Path.Combine(Directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        foreach (var candidate in new[] { basePath + "@2x.png", basePath + ".png", basePath + ".svg", basePath, basePath + ".gif", basePath + ".jpg" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? SelectCodePath(JsonObject json, out string? unsupported)
    {
        unsupported = null;
        var platforms = GetArray(json, "OS").OfType<JsonObject>().Select(static os => Get(os, "Platform")?.ToLowerInvariant()).ToList();
        var current = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac" : "linux";
        if (platforms.Count > 0 && !platforms.Contains(current))
        {
            var known = Plugins.PluginPlatforms.From(Find(json, "OS"));
            unsupported = known is null ? $"Made for {string.Join(", ", platforms)} only." : $"Made for {Plugins.PluginPlatforms.List(known)} only.";
            return null;
        }

        var codePath = (OperatingSystem.IsWindows() ? Get(json, "CodePathWin") : OperatingSystem.IsMacOS() ? Get(json, "CodePathMac") : Get(json, "CodePathLin"))
            ?? Get(json, "CodePath");
        if (codePath is null)
        {
            unsupported = "The manifest has no CodePath for this system.";
            return null;
        }

        // HTML plugins run in DeckHost's hidden browser (see IHtmlPluginHost).
        return codePath;
    }

    private static JsonNode? Find(JsonObject json, string name) =>
        json.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? Get(JsonObject json, string name) => Find(json, name) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool? GetBool(JsonObject json, string name) => Find(json, name) is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    private static IEnumerable<JsonNode?> GetArray(JsonObject json, string name) => Find(json, name) as JsonArray ?? [];
}

public sealed record StreamDeckState(string? Image, string? Title);

public sealed record StreamDeckActionManifest(
    string Uuid,
    string Name,
    string? Tooltip,
    string? Icon,
    IReadOnlyList<string> Controllers,
    IReadOnlyList<StreamDeckState> States,
    string? PropertyInspectorPath,
    bool SupportedInMultiActions,
    bool VisibleInActionsList)
{
    public bool SupportsKeypad => Controllers.Contains("Keypad", StringComparer.OrdinalIgnoreCase);

    /// <summary>A dial action: Stream Deck's <c>Encoder</c>, or <c>Knob</c> as MiraBox StreamDock plugins say.</summary>
    public bool SupportsEncoder => Controllers.Contains("Encoder", StringComparer.OrdinalIgnoreCase) || Controllers.Contains("Knob", StringComparer.OrdinalIgnoreCase);

    /// <summary>The controller name the plugin uses for a dial (sent back in its events).</summary>
    public string EncoderController => Controllers.Contains("Encoder", StringComparer.OrdinalIgnoreCase) || !Controllers.Contains("Knob", StringComparer.OrdinalIgnoreCase) ? "Encoder" : "Knob";
}
