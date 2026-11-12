using System.Reflection;

namespace DeckHost.App;

/// <summary>The app's version as built (from the git tag, see Directory.Build.props).</summary>
internal static class AppInfo
{
    /// <summary>"1.2.0", or "1.2.1-alpha.0.3" between releases; without the commit hash.</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var metadata = informational.IndexOf('+', System.StringComparison.Ordinal);
        return metadata < 0 ? informational : informational[..metadata];
    }
}
