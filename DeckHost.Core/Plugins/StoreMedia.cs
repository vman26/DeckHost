using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DeckHost.Core.Plugins;

/// <summary>A plugin's README: its Markdown and where its relative links and images point.</summary>
/// <param name="ImageBase">Base for relative image paths (raw files).</param>
/// <param name="LinkBase">Base for relative links (the repository's web pages).</param>
public sealed record StoreReadme(string Markdown, Uri ImageBase, Uri LinkBase);

/// <summary>A place in a GitHub repository: owner, repository, branch or tag (null: the default branch) and a folder.</summary>
public sealed record GitHubLocation(string Owner, string Repository, string? Reference, string Folder)
{
    /// <summary>
    /// Reads <c>https://github.com/{owner}/{repo}</c>, optionally with <c>.git</c> or
    /// <c>/tree/{ref}/{folder}</c> (a plugin in a monorepo). Null for anything else.
    /// </summary>
    public static GitHubLocation? Parse(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) && !string.Equals(uri.Host, "www.github.com", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (parts.Length < 2)
        {
            return null;
        }

        var repository = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        if (repository.Length == 0)
        {
            return null;
        }

        return parts.Length >= 4 && parts[2] is "tree" or "blob"
            ? new GitHubLocation(parts[0], repository, parts[3], string.Join('/', parts.Skip(4)))
            : new GitHubLocation(parts[0], repository, null, string.Empty);
    }

    private string FolderPath => Folder.Length == 0 ? string.Empty : Folder.TrimEnd('/') + "/";

    /// <summary>Raw file base: <c>https://raw.githubusercontent.com/{owner}/{repo}/{ref|HEAD}/{folder}/</c>.</summary>
    public Uri RawBase => new($"https://raw.githubusercontent.com/{Escape(Owner)}/{Escape(Repository)}/{Escape(Reference ?? "HEAD")}/{EscapePath(FolderPath)}");

    /// <summary>Web page base: <c>https://github.com/{owner}/{repo}/blob/{ref|HEAD}/{folder}/</c>.</summary>
    public Uri WebBase => new($"https://github.com/{Escape(Owner)}/{Escape(Repository)}/blob/{Escape(Reference ?? "HEAD")}/{EscapePath(FolderPath)}");

    private static string Escape(string part) => Uri.EscapeDataString(part);

    private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}

/// <summary>
/// Store pictures and READMEs: images (plugin icons, README images) are cached on disk by URL
/// (<c>%LocalAppData%/DeckHost/cache/store</c>), READMEs in memory for the session. Failures give
/// null, never an exception: a missing picture only leaves the placeholder.
/// </summary>
public sealed class StoreMedia
{
    /// <summary>Larger downloads are dropped (a README image or an icon is far smaller).</summary>
    public const int MaxImageBytes = 8 * 1024 * 1024;

    private const int MaxReadmeBytes = 1024 * 1024;

    private static readonly string[] ReadmeNames = ["README.md", "readme.md", "Readme.md", "README.MD", "README.markdown", "README"];

    private readonly HttpClient _http;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _downloads = new(6);
    private readonly ConcurrentDictionary<string, Task<byte[]?>> _images = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<StoreReadme?>> _readmes = new(StringComparer.OrdinalIgnoreCase);

    public StoreMedia(HttpClient http, string? cacheDirectory = null)
    {
        _http = http;
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeckHost", "cache", "store");
    }

    /// <summary>
    /// The image at <paramref name="url"/> (http(s), or a local file), from the disk cache when it
    /// was downloaded before. GitHub "blob" page URLs are read from the raw file.
    /// </summary>
    public Task<byte[]?> GetImageAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult<byte[]?>(null);
        }

        if (Path.IsPathFullyQualified(url) || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return Task.Run(() => ReadLocal(url), cancellationToken);
        }

        if (!Uri.TryCreate(ToRawGitHubUrl(url), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return Task.FromResult<byte[]?>(null);
        }

        // One download per URL, shared by everyone who asks; a failure may be retried later.
        var task = _images.GetOrAdd(uri.AbsoluteUri, _ => DownloadImageAsync(uri));
        if (task.IsCompleted && task.Result is null)
        {
            _images.TryRemove(new KeyValuePair<string, Task<byte[]?>>(uri.AbsoluteUri, task));
        }

        return task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// The README of a store plugin: from its GitHub repository (the catalog's repository, or a
    /// GitHub link). Null when the plugin has no GitHub repository or the repository has no README.
    /// </summary>
    public Task<StoreReadme?> GetReadmeAsync(StoreEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (FindRepository(entry) is not { } location)
        {
            return Task.FromResult<StoreReadme?>(null);
        }

        var task = _readmes.GetOrAdd(entry.Id, _ => LoadReadmeAsync(location));
        if (task.IsFaulted || task.IsCanceled)
        {
            _readmes.TryRemove(new KeyValuePair<string, Task<StoreReadme?>>(entry.Id, task));
        }

        return task.WaitAsync(cancellationToken);
    }

    /// <summary>The plugin's GitHub repository, from the repository or page URLs of its catalog entry.</summary>
    public static GitHubLocation? FindRepository(StoreEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return GitHubLocation.Parse(entry.RepositoryUrl) ?? GitHubLocation.Parse(entry.PageUrl);
    }

    /// <summary><c>github.com/{o}/{r}/blob/{ref}/{path}</c> → <c>raw.githubusercontent.com/{o}/{r}/{ref}/{path}</c>; other URLs unchanged.</summary>
    public static string ToRawGitHubUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5 && parts[2] is "blob" or "raw")
            {
                return $"https://raw.githubusercontent.com/{parts[0]}/{parts[1]}/{string.Join('/', parts.Skip(3))}";
            }
        }

        return url;
    }

    private async Task<StoreReadme?> LoadReadmeAsync(GitHubLocation location)
    {
        foreach (var name in ReadmeNames)
        {
            var uri = new Uri(location.RawBase, name);
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            response.EnsureSuccessStatusCode();
            var bytes = await ReadLimitedAsync(response, MaxReadmeBytes).ConfigureAwait(false);
            return bytes is null ? null : new StoreReadme(Encoding.UTF8.GetString(bytes), location.RawBase, location.WebBase);
        }

        return null;
    }

    private async Task<byte[]?> DownloadImageAsync(Uri uri)
    {
        var cacheFile = Path.Combine(_cacheDirectory, Hash(uri.AbsoluteUri));
        try
        {
            if (File.Exists(cacheFile))
            {
                return await File.ReadAllBytesAsync(cacheFile).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        await _downloads.WaitAsync().ConfigureAwait(false);
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var bytes = await ReadLimitedAsync(response, MaxImageBytes).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                return null;
            }

            try
            {
                Directory.CreateDirectory(_cacheDirectory);
                await File.WriteAllBytesAsync(cacheFile, bytes).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not cached: it's downloaded again next session.
            }

            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
        finally
        {
            _downloads.Release();
        }
    }

    private static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response, int limit)
    {
        if (response.Content.Headers.ContentLength > limit)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static byte[]? ReadLocal(string pathOrUrl)
    {
        try
        {
            var path = pathOrUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(pathOrUrl).LocalPath : pathOrUrl;
            var info = new FileInfo(path);
            return info.Exists && info.Length <= MaxImageBytes ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UriFormatException)
        {
            return null;
        }
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
