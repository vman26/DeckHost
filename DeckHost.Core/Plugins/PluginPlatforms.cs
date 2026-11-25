using System.Text.Json.Nodes;

namespace DeckHost.Core.Plugins;

/// <summary>
/// The systems a plugin runs on, as the store and the Plugins page show them: <c>windows</c>, <c>macos</c>, <c>linux</c>.
/// A Stream Deck manifest's <c>OS</c> list names them <c>windows</c> and <c>mac</c>; catalogs list them as plain strings.
/// </summary>
public static class PluginPlatforms
{
    public const string Windows = "windows";
    public const string MacOS = "macos";
    public const string Linux = "linux";

    /// <summary>Every system DeckHost runs on, in the order they are shown.</summary>
    public static IReadOnlyList<string> All { get; } = [Windows, MacOS, Linux];

    /// <summary>The system DeckHost runs on now.</summary>
    public static string Current => OperatingSystem.IsWindows() ? Windows : OperatingSystem.IsMacOS() ? MacOS : Linux;

    /// <summary>One system's name in the spelling of a manifest or catalog ("mac", "Windows", "osx"); null when it isn't one.</summary>
    public static string? Normalize(string? platform) => platform?.Trim().ToLowerInvariant() switch
    {
        "windows" or "win" or "win32" or "win64" => Windows,
        "mac" or "macos" or "osx" or "darwin" => MacOS,
        "linux" => Linux,
        _ => null,
    };

    /// <summary>
    /// A list of systems from JSON: strings (<c>["windows", "mac"]</c>) or a Stream Deck manifest's <c>OS</c> objects
    /// (<c>[{"Platform": "mac"}]</c>). Null when there's no list or nothing in it is a system: unknown, not "none".
    /// </summary>
    public static IReadOnlyList<string>? From(JsonNode? list)
    {
        if (list is not JsonArray items)
        {
            return null;
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var name = item switch
            {
                JsonValue value when value.TryGetValue<string>(out var text) => text,
                JsonObject os => os["Platform"] is JsonValue platform && platform.TryGetValue<string>(out var text) ? text : null,
                _ => null,
            };
            if (Normalize(name) is { } system)
            {
                found.Add(system);
            }
        }

        return found.Count == 0 ? null : [.. All.Where(found.Contains)];
    }

    /// <summary>"Windows", "macOS", "Linux".</summary>
    public static string DisplayName(string platform) => platform switch
    {
        Windows => "Windows",
        MacOS => "macOS",
        Linux => "Linux",
        _ => platform,
    };

    /// <summary>"Windows · macOS".</summary>
    public static string Describe(IEnumerable<string> platforms) => string.Join(" · ", platforms.Select(DisplayName));

    /// <summary>"Windows and macOS", "Windows, macOS and Linux".</summary>
    public static string List(IReadOnlyList<string> platforms) => platforms.Count switch
    {
        0 => string.Empty,
        1 => DisplayName(platforms[0]),
        _ => string.Join(", ", platforms.Take(platforms.Count - 1).Select(DisplayName)) + " and " + DisplayName(platforms[^1]),
    };

    /// <summary>True or false when the systems are known, null when they aren't.</summary>
    public static bool? RunsOn(IReadOnlyList<string>? platforms, string system) => platforms is null ? null : platforms.Contains(system, StringComparer.Ordinal);
}
