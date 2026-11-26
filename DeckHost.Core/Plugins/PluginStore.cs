using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeckHost.Core.Plugins;

/// <summary>One plugin listed in a store catalog.</summary>
public sealed record StoreEntry(
    string Id,
    string Name,
    string? Author,
    string? Description,
    string Source,
    string? Version,
    string? IconUrl,
    string? RepositoryUrl,
    string? DownloadUrl,
    string? Category = null,
    string? PageUrl = null)
{
    /// <summary>The store page to open: the catalog's page, else the repository or author site.</summary>
    public string? StorePageUrl => PageUrl ?? PurchaseUrl ?? RepositoryUrl;

    /// <summary>The package's SHA-256 (hex): the download is refused when it doesn't match.</summary>
    public string? Sha256 { get; init; }

    /// <summary>What the plugin says it may do (plugin.json permissions), shown before installing.</summary>
    public IReadOnlyList<string>? Permissions { get; init; }

    /// <summary>A paid plugin: where to buy it (the store opens this instead of installing).</summary>
    public string? PurchaseUrl { get; init; }

    /// <summary>The price as the author writes it ("€5", "pay what you want"), for paid plugins.</summary>
    public string? Price { get; init; }

    /// <summary>
    /// The systems it runs on (<see cref="PluginPlatforms"/>): from the catalog's <c>platforms</c>, the package's manifest,
    /// or the <c>deckhost-plugins</c> platforms index (<see cref="PluginStore.PlatformsIndexUrl"/>). Null when nobody knows.
    /// </summary>
    public IReadOnlyList<string>? Platforms { get; init; }
}

/// <summary>
/// Reads plugin catalogs and downloads packages from them:
/// <list type="bullet">
/// <item><see cref="DeckHostSource"/>: the sources in <see cref="DeckHostCatalogUrls"/>. A source is
/// an http(s) URL or a local path of a catalog (a JSON object keyed by plugin id with name, author,
/// description, version, category, download_url, repository, icon; or an array of such objects
/// with an "id"), or a local folder, which is searched for plugin packages (.zip, .dhplugin,
/// .streamDeckPlugin) and built plugin folders (plugin.json next to its assembly).</item>
/// <item><see cref="OpenActionSource"/>: the open-source OpenAction catalog of Stream Deck SDK
/// plugins (https://openactionapi.github.io/plugins/catalogue.json). Packages come from the
/// entry's download_url or the repository's latest GitHub release.</item>
/// <item><see cref="ElgatoArchiveSource"/>: a community archive of Elgato Marketplace plugins
/// (https://plugins.amankhanna.me/catalogue.json). Elgato itself offers no public catalog API.
/// Plugins whose source repository has an open-source license may also be copied to the
/// <c>deckhost-plugins</c> mirror (<see cref="ElgatoMirrorCatalogUrl"/>): those entries download
/// from there, with a checksum, and the rest keep the archive's download.</item>
/// </list>
/// </summary>
public sealed class PluginStore(HttpClient http)
{
    public const string DeckHostSource = "DeckHost";
    public const string OpenActionSource = "OpenAction";
    public const string ElgatoArchiveSource = "Elgato (community archive)";
    public const string GitHubSource = "GitHub";

    private const string OpenActionCatalogUrl = "https://openactionapi.github.io/plugins/catalogue.json";
    private const string OpenActionIconUrl = "https://openactionapi.github.io/plugins/icons/{0}.png";

    /// <summary>The archive keeps each plugin's icon; the catalog's own icon links (appstore.elgato.com) no longer resolve.</summary>
    private const string ElgatoIconUrl = "https://plugins.amankhanna.me/icons/{0}.png";
    private const string ElgatoCatalogUrl = "https://plugins.amankhanna.me/catalogue.json";
    private const string ElgatoDownloadUrl = "https://plugins.amankhanna.me/rezipped/{0}.zip";
    /// <summary>
    /// The mirror's catalog (deckhost-plugins' GitHub Pages, made by its mirror workflow): per plugin the
    /// license, a copy of the package and its SHA-256 where the license allows one. Read next to the archive
    /// while <see cref="IncludeOfficialCatalog"/> is on; missing (404) or unreadable it is simply empty.
    /// </summary>
    public const string ElgatoMirrorCatalogUrl = "https://vman26.github.io/deckhost-plugins/mirror/catalog.json";

    private const string ElgatoMarketplaceSearchUrl = "https://marketplace.elgato.com/stream-deck/plugins?search={0}";
    private const int MaxFolderScanDepth = 6;
    private static readonly string[] PackageExtensions = [".zip", ".dhplugin", ".streamdeckplugin"];
    private static readonly string[] SkippedFolders = ["obj", "node_modules", ".git", ".vs"];

    /// <summary>
    /// The DeckHost plugin catalog: the public <c>deckhost-plugins</c> repository, built into one file on
    /// its GitHub Pages (docs/maintainers/plugin-store.md). Read before the user's own sources; while it
    /// doesn't exist yet (404) it's simply empty.
    /// </summary>
    public const string OfficialCatalogUrl = "https://vman26.github.io/deckhost-plugins/catalog.json";

    /// <summary>
    /// The systems of plugins whose catalogs don't say (OpenAction, the Elgato archive, Stream Deck packages in the DeckHost
    /// catalog): <c>{"plugins": {"&lt;id&gt;": {"platforms": ["windows", "macos"], …}}}</c>, made by the <c>deckhost-plugins</c>
    /// repository from each package's manifest (its tools/platforms.py). Missing or unreadable: the systems stay unknown.
    /// </summary>
    public const string PlatformsIndexUrl = "https://vman26.github.io/deckhost-plugins/platforms.json";

    /// <summary>Whether to read <see cref="PlatformsIndexUrl"/> (tests turn it off).</summary>
    public bool IncludePlatformsIndex { get; set; } = true;

    /// <summary>Whether to read <see cref="OfficialCatalogUrl"/> (tests turn it off).</summary>
    public bool IncludeOfficialCatalog { get; set; } = true;

    /// <summary>The user's own DeckHost catalog sources (URLs, files or folders), next to the official one.</summary>
    public IReadOnlyList<string> DeckHostCatalogUrls { get; set; } = [];

    /// <summary>A client with a generic User-Agent (GitHub's API requires one).</summary>
    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DeckHost-PluginStore", "1.0"));
        return client;
    }

    /// <summary>Loads all catalogs; a failing source is reported in Errors and the others still load.</summary>
    public async Task<(IReadOnlyList<StoreEntry> Entries, IReadOnlyList<string> Errors)> LoadAsync(CancellationToken cancellationToken)
    {
        var sources = new List<Task<(IReadOnlyList<StoreEntry> Entries, string? Error)>>
        {
            LoadSourceAsync(OpenActionSource, async token => ParseOpenActionCatalog(await http.GetStringAsync(OpenActionCatalogUrl, token).ConfigureAwait(false)), cancellationToken),
            LoadSourceAsync(ElgatoArchiveSource, LoadElgatoAsync, cancellationToken),
        };

        if (IncludeOfficialCatalog && !DeckHostCatalogUrls.Contains(OfficialCatalogUrl, StringComparer.OrdinalIgnoreCase))
        {
            sources.Add(LoadSourceAsync(DeckHostSource, async token =>
            {
                using var response = await http.GetAsync(OfficialCatalogUrl, token).ConfigureAwait(false);
                return response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? []
                    : ParseDeckHostCatalog(await response.EnsureSuccessStatusCode().Content.ReadAsStringAsync(token).ConfigureAwait(false));
            }, cancellationToken));
        }

        foreach (var source in DeckHostCatalogUrls)
        {
            if (IsWebUrl(source))
            {
                sources.Add(LoadSourceAsync($"{DeckHostSource} ({source})", async token => ParseDeckHostCatalog(await http.GetStringAsync(source, token).ConfigureAwait(false)), cancellationToken));
            }
            else if (ToLocalPath(source) is { } local)
            {
                sources.Add(LoadSourceAsync($"{DeckHostSource} ({source})", token => Task.Run(() => LoadLocalSource(local), token), cancellationToken));
            }
        }

        var platforms = IncludePlatformsIndex ? LoadPlatformsIndexAsync(cancellationToken) : Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(EmptyIndex);
        var results = await Task.WhenAll(sources).ConfigureAwait(false);
        var index = await platforms.ConfigureAwait(false);
        return ([.. results.SelectMany(static result => result.Entries).Select(entry => WithPlatforms(entry, index))], [.. results.Select(static result => result.Error).OfType<string>()]);
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyIndex = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>An entry with the index's systems when its own catalog didn't give any.</summary>
    internal static StoreEntry WithPlatforms(StoreEntry entry, IReadOnlyDictionary<string, IReadOnlyList<string>> index) =>
        entry.Platforms is null && index.TryGetValue(entry.Id, out var platforms) ? entry with { Platforms = platforms } : entry;

    /// <summary>The platforms index; empty when it can't be read (the store works without it).</summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadPlatformsIndexAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(PlatformsIndexUrl, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? ParsePlatformsIndex(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))
                : EmptyIndex;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return EmptyIndex;
        }
    }

    /// <summary><c>{"plugins": {"&lt;id&gt;": {"platforms": [...]}}}</c>; an id without known systems is left out.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ParsePlatformsIndex(string json)
    {
        var index = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (JsonNode.Parse(json) is JsonObject root && root["plugins"] is JsonObject plugins)
        {
            foreach (var (id, plugin) in plugins)
            {
                if (plugin is JsonObject entry && PluginPlatforms.From(entry["platforms"]) is { } platforms)
                {
                    index[id] = platforms;
                }
            }
        }

        return index;
    }

    /// <summary>Downloads the entry's package into <paramref name="directory"/> and returns its path.</summary>
    /// <param name="progress">Fraction downloaded (0..1), or -1 while the size is unknown.</param>
    public async Task<string> DownloadAsync(StoreEntry entry, string directory, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (entry.DownloadUrl is null && entry.PurchaseUrl is not null)
        {
            throw new PluginException($"{entry.Name} is a paid plugin: get it from {entry.PurchaseUrl}, then install the file you receive.");
        }

        var url = entry.DownloadUrl ?? await FindReleaseAssetAsync(entry, cancellationToken).ConfigureAwait(false)
            ?? throw new PluginException($"{entry.Name} has no downloadable release.");

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{entry.Id}-{Guid.NewGuid():N}.zip");

        // Local sources: copy the package (or zip the built plugin folder), so the caller can delete it.
        if (!IsWebUrl(url) && ToLocalPath(url) is { } local)
        {
            if (Directory.Exists(local))
            {
                System.IO.Compression.ZipFile.CreateFromDirectory(local, path);
            }
            else if (File.Exists(local))
            {
                File.Copy(local, path);
            }
            else
            {
                throw new PluginException($"{local} doesn't exist anymore.");
            }

            progress?.Report(1);
            return path;
        }
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new PluginException($"Download failed ({(int)response.StatusCode} {response.ReasonPhrase}).");
        }

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var file = File.Create(path);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(total is > 0 ? (double)received / total.Value : -1);
        }

        await file.FlushAsync(cancellationToken).ConfigureAwait(false);
        file.Close();
        if (entry.Sha256 is { Length: > 0 } expected)
        {
            await using var check = File.OpenRead(path);
            var actual = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(check, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                check.Close();
                File.Delete(path);
                throw new PluginException($"The download of {entry.Name} doesn't match the catalog's checksum, so it wasn't installed. Try again later, or tell the plugin's author.");
            }
        }

        return path;
    }

    internal static IReadOnlyList<StoreEntry> ParseDeckHostCatalog(string json) =>
        ParseKeyedOrArray(json, DeckHostSource, iconTemplate: null);

    internal static IReadOnlyList<StoreEntry> ParseOpenActionCatalog(string json) =>
        ParseKeyedOrArray(json, OpenActionSource, OpenActionIconUrl);

    /// <summary>The archive, with the mirror's checksummed copies in place of its downloads; the mirror alone when the archive is down.</summary>
    private async Task<IReadOnlyList<StoreEntry>> LoadElgatoAsync(CancellationToken cancellationToken)
    {
        var mirrored = IncludeOfficialCatalog ? ReadElgatoMirrorAsync(cancellationToken) : Task.FromResult<IReadOnlyList<StoreEntry>>([]);
        IReadOnlyList<StoreEntry> archive;
        try
        {
            archive = ParseElgatoArchiveCatalog(await http.GetStringAsync(ElgatoCatalogUrl, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            var copies = await mirrored.ConfigureAwait(false);
            if (copies.Count == 0)
            {
                throw;
            }

            return copies;
        }

        return MergeElgatoMirror(archive, await mirrored.ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<StoreEntry>> ReadElgatoMirrorAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(ElgatoMirrorCatalogUrl, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? ParseElgatoMirrorCatalog(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))
                : [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return []; // the mirror only improves on the archive; without it nothing is lost
        }
    }

    /// <summary>The archive's entries, each replaced by its mirrored copy (same id) when there is one. A plugin only the mirror has is not added: the archive decides what is listed.</summary>
    internal static IReadOnlyList<StoreEntry> MergeElgatoMirror(IReadOnlyList<StoreEntry> archive, IReadOnlyList<StoreEntry> mirror)
    {
        if (mirror.Count == 0)
        {
            return archive;
        }

        var copies = mirror.GroupBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        return [.. archive.Select(entry => copies.TryGetValue(entry.Id, out var copy) ? copy : entry)];
    }

    /// <summary>
    /// The copies in the mirror's catalog: entries marked <c>mirrored</c> with an https download and a
    /// SHA-256 (anything less is left to the archive). The license is shown in the description.
    /// </summary>
    internal static IReadOnlyList<StoreEntry> ParseElgatoMirrorCatalog(string json)
    {
        var entries = new List<StoreEntry>();
        if (JsonNode.Parse(json) is not JsonArray catalog)
        {
            return entries;
        }

        foreach (var plugin in catalog.OfType<JsonObject>())
        {
            if (Text(plugin, "id") is not { } id ||
                plugin["mirrored"]?.GetValueKind() is not System.Text.Json.JsonValueKind.True ||
                Text(plugin, "download_url") is not { } download || !download.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                Text(plugin, "sha256") is not { Length: 64 } sha || !sha.All(char.IsAsciiHexDigit))
            {
                continue;
            }

            var name = Text(plugin, "name") ?? id;
            entries.Add(new StoreEntry(
                id,
                name,
                Text(plugin, "author"),
                Text(plugin, "license") is { } license ? $"Copy kept by the DeckHost plugin repository, under the {license} license." : null,
                ElgatoArchiveSource,
                Text(plugin, "version"),
                Text(plugin, "icon"),
                Text(plugin, "source_repository"),
                download,
                PluginCategories.Resolve(Text(plugin, "category"), name, null, id),
                Text(plugin, "marketplace_url"))
            {
                Sha256 = sha,
                Platforms = PluginPlatforms.From(plugin["platforms"]),
            });
        }

        return entries;
    }

    internal static IReadOnlyList<StoreEntry> ParseElgatoArchiveCatalog(string json)
    {
        var entries = new List<StoreEntry>();
        if (JsonNode.Parse(json) is not JsonArray catalog)
        {
            return entries;
        }

        foreach (var plugin in catalog.OfType<JsonObject>())
        {
            if (Text(plugin, "id") is not { } id)
            {
                continue;
            }

            entries.Add(new StoreEntry(
                id,
                Text(plugin, "name") ?? id,
                Text(plugin, "author"),
                Text(plugin, "description"),
                ElgatoArchiveSource,
                Text(plugin, "version"),
                string.Format(System.Globalization.CultureInfo.InvariantCulture, ElgatoIconUrl, Uri.EscapeDataString(id)),
                Text(plugin, "link"),
                // The catalog's own "download" field is sometimes truncated; the re-zipped copy is complete.
                string.Format(System.Globalization.CultureInfo.InvariantCulture, ElgatoDownloadUrl, Uri.EscapeDataString(id)),
                PluginCategories.Resolve(Text(plugin, "category"), Text(plugin, "name") ?? id, Text(plugin, "description"), id),
                // The archive's "link" is the author's site; the plugin's page is on the Elgato Marketplace.
                string.Format(System.Globalization.CultureInfo.InvariantCulture, ElgatoMarketplaceSearchUrl, Uri.EscapeDataString(Text(plugin, "name") ?? id))));
        }

        return entries;
    }

    private static IReadOnlyList<StoreEntry> ParseKeyedOrArray(string json, string source, string? iconTemplate)
    {
        var entries = new List<StoreEntry>();
        var root = JsonNode.Parse(json);
        IEnumerable<(string Id, JsonObject Plugin)> plugins = root switch
        {
            JsonObject keyed => keyed.Where(static pair => pair.Value is JsonObject).Select(static pair => (pair.Key, (JsonObject)pair.Value!)),
            JsonArray array => array.OfType<JsonObject>().Where(static plugin => Text(plugin, "id") is not null).Select(static plugin => (Text(plugin, "id")!, plugin)),
            _ => [],
        };

        foreach (var (id, plugin) in plugins)
        {
            entries.Add(new StoreEntry(
                id,
                Text(plugin, "name") ?? id,
                Text(plugin, "author"),
                Text(plugin, "description"),
                source,
                Text(plugin, "version"),
                Text(plugin, "icon") ?? (iconTemplate is null ? null : string.Format(System.Globalization.CultureInfo.InvariantCulture, iconTemplate, id)),
                Text(plugin, "repository"),
                Text(plugin, "download_url"),
                PluginCategories.Resolve(Text(plugin, "category"), Text(plugin, "name") ?? id, Text(plugin, "description"), id),
                Text(plugin, "page"))
            {
                Sha256 = Text(plugin, "sha256"),
                Permissions = plugin["permissions"] is JsonArray permissions ? [.. permissions.Select(static item => item?.GetValue<string>()).OfType<string>()] : null,
                PurchaseUrl = Text(plugin, "purchase_url"),
                Price = Text(plugin, "price"),
                Platforms = PluginPlatforms.From(plugin["platforms"]),
            });
        }

        return entries;
    }

    private static async Task<(IReadOnlyList<StoreEntry> Entries, string? Error)> LoadSourceAsync(
        string source,
        Func<CancellationToken, Task<IReadOnlyList<StoreEntry>>> load,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await load(cancellationToken).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return ([], $"{source}: {ex.Message}");
        }
    }

    /// <summary>The newest non-prerelease GitHub release asset ending in .streamDeckPlugin or .zip.</summary>
    private async Task<string?> FindReleaseAssetAsync(StoreEntry entry, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(entry.RepositoryUrl, UriKind.Absolute, out var repository) || repository.Host != "github.com")
        {
            return null;
        }

        var path = repository.AbsolutePath.Trim('/').Split('/');
        if (path.Length < 2)
        {
            return null;
        }

        var releasesJson = await http.GetStringAsync($"https://api.github.com/repos/{path[0]}/{path[1]}/releases", cancellationToken).ConfigureAwait(false);
        return PickReleaseAsset(releasesJson)?.Url;
    }

    /// <summary>
    /// "Install from GitHub": the newest release of a GitHub repository with a plugin file
    /// (<c>.streamDeckPlugin</c>, <c>.dhplugin</c> or <c>.zip</c>) as an entry to download, checked
    /// against the SHA-256 GitHub publishes for release files. Throws <see cref="PluginException"/>
    /// with a message for the user when there is none.
    /// </summary>
    public async Task<StoreEntry> FromGitHubAsync(string repositoryUrl, CancellationToken cancellationToken = default)
    {
        if (GitHubLocation.Parse(repositoryUrl) is not { } location)
        {
            throw new PluginException("That isn't a GitHub repository address, like https://github.com/owner/plugin.");
        }

        string releasesJson;
        try
        {
            releasesJson = await http.GetStringAsync(
                $"https://api.github.com/repos/{Uri.EscapeDataString(location.Owner)}/{Uri.EscapeDataString(location.Repository)}/releases?per_page=20",
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new PluginException(ex.StatusCode switch
            {
                System.Net.HttpStatusCode.NotFound => $"GitHub has no public repository {location.Owner}/{location.Repository}.",
                System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests => "GitHub is limiting requests from this computer for a while; try again later.",
                _ => $"Couldn't reach GitHub: {ex.Message}",
            }, ex);
        }

        var asset = PickReleaseAsset(releasesJson)
            ?? throw new PluginException($"{location.Owner}/{location.Repository} has no release with a plugin file (.streamDeckPlugin, .dhplugin or .zip).");
        return new StoreEntry(
            location.Repository,
            location.Repository,
            location.Owner,
            null,
            GitHubSource,
            asset.Version,
            null,
            $"https://github.com/{location.Owner}/{location.Repository}",
            asset.Url)
        {
            Sha256 = asset.Sha256,
        };
    }

    /// <summary>
    /// The plugin file of the newest release in GitHub's releases JSON (stable before pre-release,
    /// no drafts; .streamDeckPlugin, then .dhplugin, then .zip), with GitHub's sha256 digest when given.
    /// </summary>
    internal static (string Version, string Url, string? Sha256)? PickReleaseAsset(string releasesJson)
    {
        if (JsonNode.Parse(releasesJson) is not JsonArray releases)
        {
            return null;
        }

        static int Rank(string name) =>
            name.EndsWith(".streamdeckplugin", StringComparison.OrdinalIgnoreCase) ? 0
            : name.EndsWith(".dhplugin", StringComparison.OrdinalIgnoreCase) ? 1
            : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? 2
            : -1;

        var published = releases.OfType<JsonObject>().Where(static release => release["draft"]?.GetValue<bool>() != true).ToList();
        foreach (var release in published.Where(static release => release["prerelease"]?.GetValue<bool>() != true).Concat(published.Where(static release => release["prerelease"]?.GetValue<bool>() == true)))
        {
            var asset = (release["assets"] as JsonArray)?.OfType<JsonObject>()
                .Where(static asset => Text(asset, "name") is { } name && Rank(name) >= 0)
                .OrderBy(static asset => Rank(Text(asset, "name")!))
                .FirstOrDefault();
            if (asset is not null && Text(asset, "browser_download_url") is { } url)
            {
                var digest = Text(asset, "digest");
                var sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest["sha256:".Length..] : null;
                return ((Text(release, "tag_name") ?? "?").TrimStart('v', 'V'), url, sha256);
            }
        }

        return null;
    }

    private static bool IsWebUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    /// <summary>A local path or file:// URI as a full path, or null.</summary>
    private static string? ToLocalPath(string value)
    {
        var trimmed = value.Trim().Trim('"');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        try
        {
            return Path.IsPathFullyQualified(trimmed) ? Path.GetFullPath(trimmed) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>A local catalog file, or a folder searched for packages and built plugin folders.</summary>
    internal static IReadOnlyList<StoreEntry> LoadLocalSource(string path)
    {
        if (File.Exists(path))
        {
            return ParseDeckHostCatalog(File.ReadAllText(path));
        }

        if (!Directory.Exists(path))
        {
            throw new IOException($"{path} doesn't exist.");
        }

        var entries = new Dictionary<string, (StoreEntry Entry, DateTime Modified)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (entry, modified) in ScanFolder(path, 0))
        {
            // The same plugin found twice (a package and a build folder): keep the newest.
            if (!entries.TryGetValue(entry.Id, out var existing) || modified > existing.Modified)
            {
                entries[entry.Id] = (entry, modified);
            }
        }

        return [.. entries.Values.Select(static pair => pair.Entry)];
    }

    private static IEnumerable<(StoreEntry Entry, DateTime Modified)> ScanFolder(string directory, int depth)
    {
        IEnumerable<string> files;
        IEnumerable<string> folders;
        try
        {
            files = Directory.EnumerateFiles(directory).ToList();
            folders = Directory.EnumerateDirectories(directory).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, PluginManifest.FileName, StringComparison.OrdinalIgnoreCase))
            {
                if (ReadBuiltPlugin(directory) is { } built)
                {
                    yield return (built, File.GetLastWriteTimeUtc(file));
                }
            }
            else if (string.Equals(name, "catalog.json", StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<StoreEntry> catalog;
                try
                {
                    catalog = ParseDeckHostCatalog(File.ReadAllText(file));
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    continue;
                }

                foreach (var entry in catalog)
                {
                    yield return (entry, File.GetLastWriteTimeUtc(file));
                }
            }
            else if (PackageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) && ReadPackage(file) is { } package)
            {
                yield return (package, File.GetLastWriteTimeUtc(file));
            }
        }

        if (depth >= MaxFolderScanDepth)
        {
            yield break;
        }

        foreach (var folder in folders)
        {
            if (SkippedFolders.Contains(Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var found in ScanFolder(folder, depth + 1))
            {
                yield return found;
            }
        }
    }

    /// <summary>A folder with plugin.json and the assembly it names (e.g. a plugin's build output).</summary>
    private static StoreEntry? ReadBuiltPlugin(string directory)
    {
        try
        {
            var manifest = PluginManifest.Parse(File.ReadAllText(Path.Combine(directory, PluginManifest.FileName)));
            if (!File.Exists(Path.Combine(directory, manifest.Assembly)))
            {
                return null;
            }

            return LocalEntry(manifest, directory);
        }
        catch (Exception ex) when (ex is IOException or JsonException or PluginException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads the manifest inside a DeckHost (.zip/.dhplugin) or Stream Deck package.</summary>
    private static StoreEntry? ReadPackage(string file)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(file);
            if (archive.GetEntry(PluginManifest.FileName) is { } manifestEntry)
            {
                using var reader = new StreamReader(manifestEntry.Open());
                return LocalEntry(PluginManifest.Parse(reader.ReadToEnd()), file);
            }

            var sdManifest = archive.Entries.FirstOrDefault(static entry =>
                entry.FullName.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.Split('/').Length == 2 &&
                entry.FullName.Split('/')[0].EndsWith(".sdPlugin", StringComparison.OrdinalIgnoreCase));
            if (sdManifest is null)
            {
                return null;
            }

            using var sdReader = new StreamReader(sdManifest.Open());
            var json = JsonNode.Parse(sdReader.ReadToEnd()) as JsonObject;
            var id = sdManifest.FullName.Split('/')[0][..^".sdPlugin".Length];
            var name = json is null ? id : Text(json, "Name") ?? id;
            var description = json is null ? null : Text(json, "Description");
            return new StoreEntry(
                id,
                name,
                json is null ? null : Text(json, "Author"),
                description,
                DeckHostSource,
                json is null ? null : Text(json, "Version"),
                null,
                json is null ? null : Text(json, "URL"),
                file,
                PluginCategories.Resolve(json is null ? null : Text(json, "Category"), name, description, id))
            {
                Platforms = PluginPlatforms.From(json?["OS"]),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or PluginException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // A DeckHost plugin is .NET, like DeckHost: it runs wherever DeckHost does unless its plugin.json says otherwise.
    private static StoreEntry LocalEntry(PluginManifest manifest, string location) => new(
        manifest.Id,
        manifest.Name,
        manifest.Author,
        manifest.Description,
        DeckHostSource,
        manifest.Version,
        null,
        null,
        location,
        PluginCategories.Resolve(null, manifest.Name, manifest.Description, manifest.Id))
    {
        Platforms = PluginPlatforms.OfDeckHostPlugin(manifest.Platforms),
    };

    private static string? Text(JsonObject json, string name) =>
        json[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
