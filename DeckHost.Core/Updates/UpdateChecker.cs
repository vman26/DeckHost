using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DeckHost.Core.Updates;

/// <summary>A file attached to a release.</summary>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

/// <summary>A published DeckHost release (a GitHub release; the tag is the version).</summary>
/// <param name="Notes">The release notes (Markdown).</param>
public sealed record ReleaseInfo(
    string Tag,
    SemanticVersion Version,
    string Name,
    string Notes,
    string PageUrl,
    bool IsPrerelease,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ReleaseAsset> Assets);

/// <summary>What a check found: the newest release, and every release newer than the running one (newest first).</summary>
public sealed record UpdateCheckResult(SemanticVersion Current, ReleaseInfo? Latest, IReadOnlyList<ReleaseInfo> Newer)
{
    public bool IsUpdateAvailable => Newer.Count > 0;

    /// <summary>The release notes of all newer releases, newest first, as one Markdown document.</summary>
    public string Changelog => string.Join(
        "\n\n",
        Newer.Select(static release =>
            $"## {release.Name}\n\n{(release.PublishedAt is { } date ? $"*{date:d MMMM yyyy}*\n\n" : string.Empty)}{(string.IsNullOrWhiteSpace(release.Notes) ? "No release notes." : release.Notes.Trim())}"));
}

/// <summary>An update could not be checked, downloaded or verified.</summary>
public sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Checks the GitHub releases of DeckHost (<c>api.github.com/repos/{repository}/releases</c>, no
/// sign-in: 60 requests an hour per address, plenty for a check every few hours), picks the package
/// for this computer and downloads it, verified against the release's <c>SHA256SUMS.txt</c>.
/// </summary>
public sealed class UpdateChecker
{
    public const string DefaultRepository = "vman26/DeckHost";

    /// <summary>The checksum file the release workflow attaches.</summary>
    public const string ChecksumFileName = "SHA256SUMS.txt";

    private readonly HttpClient _http;
    private readonly string _repository;

    public UpdateChecker(HttpClient http, string repository = DefaultRepository)
    {
        _http = http;
        _repository = repository;
    }

    /// <summary>An HttpClient with the headers GitHub's API asks for.</summary>
    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DeckHost-Updater", "1.0"));
        return client;
    }

    /// <summary>The releases page, for "see all releases".</summary>
    public string ReleasesPageUrl => $"https://github.com/{_repository}/releases";

    /// <summary>
    /// Finds releases newer than <paramref name="currentVersion"/>. Pre-releases count only with
    /// <paramref name="includePrereleases"/>; drafts and tags that aren't versions never do.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, bool includePrereleases, CancellationToken cancellationToken = default)
    {
        if (!SemanticVersion.TryParse(currentVersion, out var current))
        {
            throw new UpdateException($"The running version \"{currentVersion}\" isn't a version number.");
        }

        string json;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_repository}/releases?per_page=30");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is 403 or 429 && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0")
            {
                throw new UpdateException("GitHub's limit for update checks from this network is reached; try again in an hour.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException($"Couldn't reach GitHub: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException("GitHub didn't answer in time.", ex);
        }

        return Evaluate(current, ParseReleases(json), includePrereleases);
    }

    /// <summary>Picks the releases newer than <paramref name="current"/>, newest first.</summary>
    public static UpdateCheckResult Evaluate(SemanticVersion current, IEnumerable<ReleaseInfo> releases, bool includePrereleases)
    {
        var eligible = releases
            .Where(release => includePrereleases || !release.IsPrerelease)
            .OrderByDescending(static release => release.Version)
            .ToList();
        return new UpdateCheckResult(current, eligible.FirstOrDefault(), [.. eligible.Where(release => release.Version > current)]);
    }

    internal static IReadOnlyList<ReleaseInfo> ParseReleases(string json)
    {
        var releases = new List<ReleaseInfo>();
        if (JsonNode.Parse(json) is not JsonArray array)
        {
            throw new UpdateException("GitHub's answer wasn't a list of releases.");
        }

        foreach (var release in array.OfType<JsonObject>())
        {
            var tag = Text(release, "tag_name");
            if (Bool(release, "draft") || tag is null || !SemanticVersion.TryParse(tag, out var version))
            {
                continue;
            }

            var assets = (release["assets"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Where(static asset => Text(asset, "name") is not null && Text(asset, "browser_download_url") is not null)
                .Select(static asset => new ReleaseAsset(Text(asset, "name")!, Text(asset, "browser_download_url")!, (long?)asset["size"] ?? 0))
                .ToList();
            releases.Add(new ReleaseInfo(
                tag,
                version,
                Text(release, "name") is { Length: > 0 } name ? name : $"DeckHost {version}",
                Text(release, "body") ?? string.Empty,
                Text(release, "html_url") ?? string.Empty,
                Bool(release, "prerelease") || version.IsPrerelease,
                DateTimeOffset.TryParse(Text(release, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published) ? published : null,
                assets));
        }

        return releases;
    }

    /// <summary>
    /// The package for a platform (<c>win-x64</c>, <c>linux-x64</c>, <c>osx-arm64</c>, ...):
    /// Windows' setup program, else its zip; the Linux tar.gz; the macOS zip.
    /// </summary>
    public static ReleaseAsset? SelectAsset(ReleaseInfo release, string runtimeIdentifier)
    {
        ArgumentNullException.ThrowIfNull(release);
        var suffixes = runtimeIdentifier.StartsWith("win", StringComparison.Ordinal)
            ? new[] { $"-{runtimeIdentifier}-setup.exe", $"-{runtimeIdentifier}.zip" }
            : runtimeIdentifier.StartsWith("osx", StringComparison.Ordinal)
                ? [$"-{runtimeIdentifier}.zip"]
                : [$"-{runtimeIdentifier}.tar.gz"];
        return suffixes
            .Select(suffix => release.Assets.FirstOrDefault(asset => asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(static asset => asset is not null);
    }

    /// <summary>This computer's platform as a release names it.</summary>
    public static string CurrentRuntimeIdentifier
    {
        get
        {
            var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            var arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                Architecture.Arm => "arm",
                _ => "x64",
            };
            return $"{os}-{arch}";
        }
    }

    /// <summary>
    /// Downloads <paramref name="asset"/> into <paramref name="directory"/> and checks it against
    /// the release's checksum file (when the release has one: a mismatch deletes the file and throws).
    /// </summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, ReleaseAsset asset, string directory, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(asset);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(asset.Name));
        try
        {
            string? expected = null;
            if (release.Assets.FirstOrDefault(static file => file.Name.Equals(ChecksumFileName, StringComparison.OrdinalIgnoreCase)) is { } sums)
            {
                expected = FindChecksum(await _http.GetStringAsync(sums.DownloadUrl, cancellationToken).ConfigureAwait(false), asset.Name)
                           ?? throw new UpdateException($"{ChecksumFileName} doesn't list {asset.Name}.");
            }

            using var response = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? asset.Size;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = File.Create(path))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    done += read;
                    progress?.Report(total > 0 ? Math.Min(1d, done / (double)total) : -1d);
                }
            }

            if (expected is not null)
            {
                string actual;
                await using (var file = File.OpenRead(path))
                {
                    actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
                }

                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UpdateException($"The download of {asset.Name} is damaged (its checksum doesn't match). Try again.");
                }
            }

            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or UpdateException or OperationCanceledException)
        {
            TryDelete(path);
            if (ex is UpdateException or OperationCanceledException)
            {
                throw;
            }

            throw new UpdateException($"Couldn't download {asset.Name}: {ex.Message}", ex);
        }
    }

    /// <summary>The SHA-256 for a file in <c>sha256sum</c> output ("&lt;hash&gt;  &lt;name&gt;" or "&lt;hash&gt; *&lt;name&gt;").</summary>
    internal static string? FindChecksum(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            if (space == 64 && line[(space + 1)..].TrimStart(' ', '*') == fileName)
            {
                return line[..64];
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string? Text(JsonObject json, string key) =>
        json[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static bool Bool(JsonObject json, string key) =>
        json[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}

/// <summary>
/// A semantic version (2.0): <c>1.4.0</c>, <c>1.4.0-alpha.3</c>; an optional leading "v" and build
/// metadata (<c>+4b09090</c>) are accepted and ignored for ordering, as SemVer says.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, string[] prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public IReadOnlyList<string> Prerelease { get; }

    public bool IsPrerelease => Prerelease.Count > 0;

    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = null!;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (text[0] is 'v' or 'V')
        {
            text = text[1..];
        }

        var plus = text.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            text = text[..plus];
        }

        var dash = text.IndexOf('-', StringComparison.Ordinal);
        var core = dash < 0 ? text : text[..dash];
        string[] prerelease = dash < 0 ? [] : text[(dash + 1)..].Split('.');
        var parts = core.Split('.');
        if (parts.Length is < 2 or > 3 ||
            prerelease.Any(static identifier => identifier.Length == 0) ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            return false;
        }

        var patch = 0;
        if (parts.Length == 3 && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"\"{text}\" isn't a version number.");

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result == 0)
        {
            result = Minor.CompareTo(other.Minor);
        }

        if (result == 0)
        {
            result = Patch.CompareTo(other.Patch);
        }

        if (result != 0)
        {
            return result;
        }

        // A pre-release comes before its release; identifiers compare numerically when both are
        // numbers, else as text, and numbers come before text; a longer list wins a tie.
        if (IsPrerelease != other.IsPrerelease)
        {
            return IsPrerelease ? -1 : 1;
        }

        for (var index = 0; index < Math.Min(Prerelease.Count, other.Prerelease.Count); index++)
        {
            var left = Prerelease[index];
            var right = other.Prerelease[index];
            var leftNumber = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var l);
            var rightNumber = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var r);
            result = (leftNumber, rightNumber) switch
            {
                (true, true) => l.CompareTo(r),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(left, right),
            };
            if (result != 0)
            {
                return result;
            }
        }

        return Prerelease.Count.CompareTo(other.Prerelease.Count);
    }

    public bool Equals(SemanticVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', Prerelease));

    public override string ToString()
    {
        var builder = new StringBuilder().Append(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
        if (IsPrerelease)
        {
            builder.Append('-').AppendJoin('.', Prerelease);
        }

        return builder.ToString();
    }

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator ==(SemanticVersion? left, SemanticVersion? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);
}
