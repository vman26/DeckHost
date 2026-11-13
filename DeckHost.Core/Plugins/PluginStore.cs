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
/// (https://plugins.amankhanna.me/catalogue.json). Elgato itself offers no public catalog API.</item>
/// </list>
/// </summary>
public sealed class PluginStore(HttpClient http)
{
    public const string DeckHostSource = "DeckHost";
    public const string OpenActionSource = "OpenAction";
    public const string ElgatoArchiveSource = "Elgato (community archive)";

    private const string OpenActionCatalogUrl = "https://openactionapi.github.io/plugins/catalogue.json";
    private const string OpenActionIconUrl = "https://openactionapi.github.io/plugins/icons/{0}.png";

    /// <summary>The archive keeps each plugin's icon; the catalog's own icon links (appstore.elgato.com) no longer resolve.</summary>
    private const string ElgatoIconUrl = "https://plugins.amankhanna.me/icons/{0}.png";
    private const string ElgatoCatalogUrl = "https://plugins.amankhanna.me/catalogue.json";
    private const string ElgatoDownloadUrl = "https://plugins.amankhanna.me/rezipped/{0}.zip";
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
            LoadSourceAsync(ElgatoArchiveSource, async token => ParseElgatoArchiveCatalog(await http.GetStringAsync(ElgatoCatalogUrl, token).ConfigureAwait(false)), cancellationToken),
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

        var results = await Task.WhenAll(sources).ConfigureAwait(false);
        return ([.. results.SelectMany(static result => result.Entries)], [.. results.Select(static result => result.Error).OfType<string>()]);
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
        if (JsonNode.Parse(releasesJson) is not JsonArray releases)
        {
            return null;
        }

        var release = releases.OfType<JsonObject>().FirstOrDefault(static release => release["prerelease"]?.GetValue<bool>() != true)
            ?? releases.OfType<JsonObject>().FirstOrDefault();

        return (release?["assets"] as JsonArray)?.OfType<JsonObject>()
            .Where(static asset => Text(asset, "name") is { } name &&
                (name.EndsWith(".streamdeckplugin", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(static asset => Text(asset, "name")!.EndsWith(".streamdeckplugin", StringComparison.OrdinalIgnoreCase))
            .Select(static asset => Text(asset, "browser_download_url"))
            .FirstOrDefault();
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
                PluginCategories.Resolve(json is null ? null : Text(json, "Category"), name, description, id));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or PluginException or UnauthorizedAccessException)
        {
            return null;
        }
    }

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
        PluginCategories.Resolve(null, manifest.Name, manifest.Description, manifest.Id));

    private static string? Text(JsonObject json, string name) =>
        json[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
