using System.Text.Json;

namespace DeckHost.Core.Plugins;

/// <summary>
/// The systems DeckHost learned from the manifests of plugins it installed, for store entries whose catalog doesn't say
/// (most OpenAction and Elgato plugins): once a plugin was installed, the store keeps showing what it is made for, also
/// after it is uninstalled. Kept in <c>%AppData%/DeckHost/learned-platforms.json</c> as <c>{"&lt;id&gt;": ["windows", "macos"]}</c>.
/// Not thread-safe: the store uses it on the UI thread.
/// </summary>
public sealed class LearnedPlatforms(string path)
{
    private Dictionary<string, IReadOnlyList<string>>? _known;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "DeckHost", "learned-platforms.json");

    /// <summary>What was learned about the plugin; null when it was never installed or its manifest names no systems.</summary>
    public IReadOnlyList<string>? For(string pluginId) => Known.GetValueOrDefault(pluginId);

    /// <summary>Remembers a plugin's systems (written right away when they are new or changed). False when nothing changed.</summary>
    public bool Learn(string pluginId, IReadOnlyList<string>? platforms)
    {
        var normalized = PluginPlatforms.FromNames(platforms);
        if (normalized is null || (Known.TryGetValue(pluginId, out var before) && before.SequenceEqual(normalized)))
        {
            return false;
        }

        Known[pluginId] = normalized;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Known.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToDictionary(), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Known for this run; written again with the next one learned.
        }

        return true;
    }

    private Dictionary<string, IReadOnlyList<string>> Known => _known ??= Load();

    private Dictionary<string, IReadOnlyList<string>> Load()
    {
        var known = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path)) is { } saved)
            {
                foreach (var (id, platforms) in saved)
                {
                    if (PluginPlatforms.FromNames(platforms) is { } normalized)
                    {
                        known[id] = normalized;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable: start over; installed plugins are learned again.
        }

        return known;
    }
}
