using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

namespace DeckHost.Core.Icons;

/// <summary>An icon found on Iconify, with what its set says about license and colors.</summary>
/// <param name="IsMonochrome">The set draws in one color (<c>currentColor</c>), so the icon can be tinted.</param>
public sealed record IconifyIcon(
    string Prefix,
    string Name,
    string SetName,
    string? License,
    string? LicenseUrl,
    string? Author,
    bool IsMonochrome)
{
    /// <summary><c>prefix:name</c>, as Iconify writes it.</summary>
    public string Id => $"{Prefix}:{Name}";
}

/// <summary>
/// Searches Iconify (<see href="https://iconify.design"/>), a public collection of open-source
/// icon sets, and fetches the icons as SVG. No key is needed. Each icon set has its own license,
/// which <see cref="IconifyIcon"/> carries and the icon library keeps next to downloaded icons.
/// </summary>
public sealed class IconifyClient(HttpClient http, Uri? baseUri = null)
{
    public static readonly Uri DefaultBaseUri = new("https://api.iconify.design/");

    private readonly Uri _baseUri = baseUri ?? DefaultBaseUri;

    /// <summary>Icons matching <paramref name="query"/>, best first (Iconify returns at least 32 when there are that many).</summary>
    public async Task<IReadOnlyList<IconifyIcon>> SearchAsync(string query, int limit = 64, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var limitText = Math.Clamp(limit, 32, 999).ToString(CultureInfo.InvariantCulture);
        var json = await GetJsonAsync($"search?query={Uri.EscapeDataString(query.Trim())}&limit={limitText}", cancellationToken).ConfigureAwait(false);
        var collections = json["collections"] as JsonObject;
        var icons = new List<IconifyIcon>();
        foreach (var id in (json["icons"] as JsonArray ?? []).Select(static node => node?.GetValue<string>()).OfType<string>())
        {
            var separator = id.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator == id.Length - 1)
            {
                continue;
            }

            var prefix = id[..separator];
            var set = collections?[prefix] as JsonObject;
            icons.Add(new IconifyIcon(
                prefix,
                id[(separator + 1)..],
                (string?)set?["name"] ?? prefix,
                (string?)set?["license"]?["title"],
                (string?)set?["license"]?["url"],
                (string?)set?["author"]?["name"],
                set?["palette"] is JsonValue palette && palette.TryGetValue<bool>(out var hasPalette) ? !hasPalette : true));
        }

        return icons;
    }

    /// <summary>The icons as standalone SVG documents, by <see cref="IconifyIcon.Id"/> (fetched one request per set).</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetSvgsAsync(IEnumerable<IconifyIcon> icons, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(icons);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in icons.GroupBy(static icon => icon.Prefix))
        {
            var names = string.Join(',', group.Select(static icon => icon.Name).Distinct());
            var json = await GetJsonAsync($"{Uri.EscapeDataString(group.Key)}.json?icons={Uri.EscapeDataString(names)}", cancellationToken).ConfigureAwait(false);
            foreach (var icon in group)
            {
                if (ToSvg(json, icon.Name) is { } svg)
                {
                    result[icon.Id] = svg;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// One icon of an Iconify set's JSON as an SVG document: its body in a view box of the icon's
    /// (or the set's) size. Aliases use their parent's drawing.
    /// </summary>
    internal static string? ToSvg(JsonObject set, string name)
    {
        var icons = set["icons"] as JsonObject;
        var icon = icons?[name] as JsonObject;
        if (icon is null && set["aliases"]?[name]?["parent"] is JsonValue parent && parent.TryGetValue<string>(out var parentName))
        {
            icon = icons?[parentName] as JsonObject;
        }

        if (icon?["body"] is not JsonValue bodyValue || !bodyValue.TryGetValue<string>(out var body))
        {
            return null;
        }

        double Number(string key, double fallback) =>
            (icon[key] ?? set[key]) is JsonValue value && value.TryGetValue<double>(out var number) ? number : fallback;

        var width = Number("width", 16);
        var height = Number("height", 16);
        var left = Number("left", 0);
        var top = Number("top", 0);
        string Format(double value) => value.ToString(CultureInfo.InvariantCulture);
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Format(width)}\" height=\"{Format(height)}\" viewBox=\"{Format(left)} {Format(top)} {Format(width)} {Format(height)}\">{body}</svg>";
    }

    private async Task<JsonObject> GetJsonAsync(string relative, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(_baseUri, relative), cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return [];
            }

            response.EnsureSuccessStatusCode();
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonNode.Parse(text) as JsonObject ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new IconPackException($"Couldn't reach Iconify: {ex.Message}");
        }
    }
}
