using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DeckHost.Core.Plugins;

/// <summary>
/// <c>plugin.json</c> at the root of a plugin package (.zip):
/// <code>
/// { "id": "deckhost.plugin.obs", "name": "OBS Studio", "version": "1.0.0",
///   "author": "...", "description": "...", "assembly": "DeckHost.Plugin.Obs.dll", "hostApiVersion": 1,
///   "icon": "icon.png", "permissions": ["network"],
///   "url": "https://example.com/docs", "repository": "https://github.com/you/plugin" }
/// </code>
/// <c>icon</c> (optional) is an image in the package, shown in the Plugin Manager and the store.
/// <c>permissions</c> (optional) says what the plugin does beyond drawing and reacting to input
/// (<see cref="PluginPermissions"/>); shown to the user. <c>url</c> (its website or documentation)
/// and <c>repository</c> (its source; a GitHub repository's README is shown in DeckHost) are optional.
/// </summary>
public sealed partial record PluginManifest(
    string Id,
    string Name,
    string Version,
    string Assembly,
    string? Author = null,
    string? Description = null,
    int HostApiVersion = PluginManifest.CurrentHostApiVersion,
    string? Icon = null,
    IReadOnlyList<string>? Permissions = null,
    string? Url = null,
    string? Repository = null)
{
    public const string FileName = "plugin.json";

    /// <summary>Bumped when IApplicationPlugin / IDeckAction change incompatibly.</summary>
    public const int CurrentHostApiVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static PluginManifest Parse(string json)
    {
        PluginManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new PluginException($"{FileName} is not valid JSON: {ex.Message}");
        }

        if (manifest is null)
        {
            throw new PluginException($"{FileName} is empty.");
        }

        manifest.Validate();
        return manifest;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || !IdRegex().IsMatch(Id))
        {
            throw new PluginException("The plugin id must be 3-64 characters: lowercase letters, digits, '.', '-' or '_'.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new PluginException("The plugin has no name.");
        }

        if (string.IsNullOrWhiteSpace(Assembly) ||
            Path.GetFileName(Assembly) != Assembly ||
            !Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new PluginException("'assembly' must be the file name of a .dll in the package root.");
        }

        if (HostApiVersion != CurrentHostApiVersion)
        {
            throw new PluginException($"The plugin targets host API {HostApiVersion}; this DeckHost supports {CurrentHostApiVersion}.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,63}$")]
    private static partial Regex IdRegex();
}

public sealed class PluginException(string message, Exception? innerException = null) : Exception(message, innerException);
