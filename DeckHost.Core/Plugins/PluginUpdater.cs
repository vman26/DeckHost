namespace DeckHost.Core.Plugins;

/// <summary>A newer version of an installed plugin, as a store catalog lists it.</summary>
public sealed record PluginUpdate(PluginEntry Installed, StoreEntry Available)
{
    public string Id => Installed.Id;

    public string Name => Installed.Name;

    public string FromVersion => Installed.Version;

    public string ToVersion => Available.Version ?? "?";
}

/// <summary>
/// Updates for installed plugins: compares them with the store catalogs and installs a newer
/// version (the catalog's download, checked against its sha256 when it has one). The installed
/// version is kept for <see cref="PluginCatalog.RollBack"/>. System plugins update with DeckHost.
/// </summary>
public sealed class PluginUpdater(PluginCatalog catalog, PluginStore store, string downloadDirectory)
{
    /// <summary>Loads the catalogs and returns the updates for what is installed.</summary>
    public async Task<IReadOnlyList<PluginUpdate>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var (entries, _) = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        return FindUpdates(catalog.Entries, entries);
    }

    /// <summary>Downloads and installs one update; returns the plugin as installed now.</summary>
    public async Task<PluginEntry> InstallAsync(PluginUpdate update, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        progress?.Report($"Downloading {update.Name} {update.ToVersion}…");
        var package = await store.DownloadAsync(update.Available, downloadDirectory, null, cancellationToken).ConfigureAwait(false);
        try
        {
            return catalog.Install(package, update.Id, progress, update.Installed.Origin ?? update.Available.Source);
        }
        finally
        {
            try
            {
                File.Delete(package);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// The installed plugins (not DeckHost's own) for which a catalog has a higher version with a
    /// download; for several catalogs listing one, the highest version.
    /// </summary>
    public static IReadOnlyList<PluginUpdate> FindUpdates(IEnumerable<PluginEntry> installed, IEnumerable<StoreEntry> available)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(available);
        var newest = available
            .Where(static entry => entry.DownloadUrl is not null && entry.PurchaseUrl is null && entry.Version is not null)
            .GroupBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Aggregate((best, next) => PluginVersion.Compare(next.Version, best.Version) > 0 ? next : best),
                StringComparer.OrdinalIgnoreCase);
        return [.. installed
            .Where(static entry => !entry.IsSystem && entry.CanUninstall)
            .Select(entry => newest.TryGetValue(entry.Id, out var offer) && PluginVersion.Compare(offer.Version, entry.Version) > 0
                ? new PluginUpdate(entry, offer)
                : null)
            .OfType<PluginUpdate>()
            .OrderBy(static update => update.Name, StringComparer.CurrentCultureIgnoreCase)];
    }
}

/// <summary>
/// Versions as plugins write them: <c>1.2</c>, <c>v1.2.3</c>, <c>2.0.0.14</c>, <c>1.4.0-beta.2</c>,
/// <c>1.0+build</c>. Numbers compare as numbers part by part (missing parts are 0); a pre-release
/// is lower than the same version without one; build metadata doesn't count.
/// </summary>
public static class PluginVersion
{
    public static int Compare(string? left, string? right)
    {
        var (leftNumbers, leftLabel) = Parse(left);
        var (rightNumbers, rightLabel) = Parse(right);
        for (var index = 0; index < Math.Max(leftNumbers.Count, rightNumbers.Count); index++)
        {
            var difference = (index < leftNumbers.Count ? leftNumbers[index] : 0).CompareTo(index < rightNumbers.Count ? rightNumbers[index] : 0);
            if (difference != 0)
            {
                return difference;
            }
        }

        return (leftLabel, rightLabel) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => ComparePreRelease(leftLabel, rightLabel),
        };
    }

    private static (List<long> Numbers, string? Label) Parse(string? version)
    {
        var text = (version ?? string.Empty).Trim().TrimStart('v', 'V');
        var plus = text.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            text = text[..plus];
        }

        var dash = text.IndexOf('-', StringComparison.Ordinal);
        var label = dash >= 0 ? text[(dash + 1)..] : null;
        var core = dash >= 0 ? text[..dash] : text;
        var numbers = core.Split('.')
            .Select(static part => long.TryParse(new string([.. part.TakeWhile(char.IsDigit)]), out var number) ? number : 0)
            .ToList();
        return (numbers, string.IsNullOrEmpty(label) ? null : label);
    }

    private static int ComparePreRelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            if (index >= leftParts.Length)
            {
                return -1;
            }

            if (index >= rightParts.Length)
            {
                return 1;
            }

            var bothNumbers = long.TryParse(leftParts[index], out var leftNumber) & long.TryParse(rightParts[index], out var rightNumber);
            var difference = bothNumbers
                ? leftNumber.CompareTo(rightNumber)
                : string.Compare(leftParts[index], rightParts[index], StringComparison.OrdinalIgnoreCase);
            if (difference != 0)
            {
                return difference;
            }
        }

        return 0;
    }
}
