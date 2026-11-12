using System.Reflection;

namespace DeckHost.App;

/// <summary>The app's version as built (from git through GitVersion, see GitVersion.yml).</summary>
internal static class AppInfo
{
    private const int ShortCommitLength = 7;

    /// <summary>
    /// "1.2.0" for a release; for anything else the version plus the commit it was built from,
    /// "1.3.0-alpha.3+4b09090", so a pre-release can be traced to its commit.
    /// </summary>
    public static string Version { get; } = Describe(
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3));

    /// <summary>
    /// From the informational version, which ends in the commit ("1.3.0-alpha.3+4b09090a5183..." from
    /// CI, "1.3.0-alpha.3+Branch.develop.Sha.4b09090a5183....4b09090" from a local build): releases
    /// without the commit, pre-releases with its first 7 characters.
    /// </summary>
    internal static string Describe(string? informationalVersion, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return fallback ?? "0.0.0";
        }

        var plus = informationalVersion.IndexOf('+', System.StringComparison.Ordinal);
        var version = plus < 0 ? informationalVersion : informationalVersion[..plus];
        if (plus < 0 || !version.Contains('-', System.StringComparison.Ordinal))
        {
            return version;
        }

        var commit = informationalVersion[(plus + 1)..].Split('.')[^1];
        return commit.Length == 0 ? version : $"{version}+{commit[..System.Math.Min(ShortCommitLength, commit.Length)]}";
    }
}
