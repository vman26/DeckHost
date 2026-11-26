using CommunityToolkit.Mvvm.Input;
using DeckHost.App.Platform;
using DeckHost.Core.Updates;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DeckHost.App.ViewModels;

/// <summary>
/// "New in DeckHost &lt;version&gt;": shown once on the first start after DeckHost's version went up, with the notes
/// of the changelog that ships inside the app (CHANGELOG.md, embedded): the version's section, else its base version's,
/// else <c>[Unreleased]</c> for a pre-release, the way scripts/release-notes.py picks a release's notes.
/// </summary>
public sealed partial class WhatsNewViewModel : ViewModelBase
{
    /// <summary>The embedded changelog's resource name (DeckHost.App.csproj).</summary>
    internal const string ChangelogResource = "DeckHost.CHANGELOG.md";

    private const string UnreleasedSection = "Unreleased";

    private static readonly Regex Heading = new(@"^## \[(?<name>[^\]]+)\]", RegexOptions.CultureInvariant);
    private static readonly Regex LinkDefinition = new(@"^\[[^\]]+\]:\s*https?://", RegexOptions.CultureInvariant);

    private readonly string _releasesPageUrl;

    /// <summary>The running DeckHost and the changelog built into it.</summary>
    public WhatsNewViewModel()
        : this(AppInfo.Version, ReadEmbeddedChangelog(), $"https://github.com/{AppInfo.UpdateRepository}/releases")
    {
    }

    /// <param name="version">The version to show the notes of (build metadata is ignored).</param>
    /// <param name="changelog">CHANGELOG.md's text, or null when there is none.</param>
    /// <param name="releasesPageUrl">Where All changes leads.</param>
    public WhatsNewViewModel(string version, string? changelog, string releasesPageUrl)
    {
        Version = WithoutBuild(version);
        Notes = changelog is null ? null : NotesFor(changelog, version);
        _releasesPageUrl = releasesPageUrl;
    }

    /// <summary>The version without its build metadata ("1.3.0-alpha.3").</summary>
    public string Version { get; }

    public string Title => $"New in DeckHost {Version}";

    /// <summary>The changelog's notes for this version (Markdown), or null when the changelog has none.</summary>
    public string? Notes { get; }

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    /// <summary>Raised by Close: the page closes.</summary>
    public event EventHandler? Closed;

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);

    /// <summary>Opens the GitHub releases page in the browser.</summary>
    [RelayCommand]
    private void OpenAllChanges()
    {
        if (Uri.TryCreate(_releasesPageUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https")
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
        }
    }

    // ---------------------------------------------------------------- once per version

    /// <summary>
    /// Whether the page should open on this start: DeckHost's version went up since the last start and the changelog
    /// has notes for it. With no version seen before (a fresh install) the version is only stored.
    /// </summary>
    public static bool ShouldShow()
    {
        var settings = AppSettings.Load();
        var show = Decide(settings.LastSeenVersion, AppInfo.Version) switch
        {
            WhatsNewDecision.Show => NotesFor(ReadEmbeddedChangelog() ?? string.Empty, AppInfo.Version) is not null,
            _ => false,
        };
        if (!show && settings.LastSeenVersion != WithoutBuild(AppInfo.Version))
        {
            MarkSeen();
        }

        return show;
    }

    /// <summary>Remembers the running version as seen, so the page doesn't open again for it.</summary>
    public static void MarkSeen()
    {
        try
        {
            AppSettings.Save(AppSettings.Load() with { LastSeenVersion = WithoutBuild(AppInfo.Version) });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// What to do on a start: nothing seen yet (a fresh install, or a DeckHost from before this page) only remembers the
    /// version; a newer version than the one seen shows the page; the same (build metadata aside), an older one or a
    /// version that can't be read shows nothing.
    /// </summary>
    internal static WhatsNewDecision Decide(string? lastSeenVersion, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(lastSeenVersion))
        {
            return WhatsNewDecision.Remember;
        }

        return SemanticVersion.TryParse(lastSeenVersion, out var seen) &&
               SemanticVersion.TryParse(currentVersion, out var current) &&
               current > seen
            ? WhatsNewDecision.Show
            : WhatsNewDecision.Nothing;
    }

    /// <summary>"1.3.0-alpha.3+4b09090" → "1.3.0-alpha.3"; a leading "v" goes too.</summary>
    internal static string WithoutBuild(string version)
    {
        var text = version.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var plus = text.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? text : text[..plus];
    }

    // ---------------------------------------------------------------- the changelog

    /// <summary>
    /// The notes for <paramref name="version"/>: the section named after it (build metadata aside), else after its base
    /// version ("1.3.0" for "1.3.0-rc.1"), else for a pre-release <c>[Unreleased]</c>. Null when none of them has text.
    /// </summary>
    internal static string? NotesFor(string changelog, string version)
    {
        var exact = WithoutBuild(version);
        var dash = exact.IndexOf('-', StringComparison.Ordinal);
        var baseVersion = dash < 0 ? exact : exact[..dash];
        var sections = Sections(changelog);
        IEnumerable<string> candidates = dash < 0 ? [exact] : [exact, baseVersion, UnreleasedSection];
        return candidates
            .Select(name => sections.GetValueOrDefault(name))
            .FirstOrDefault(static body => !string.IsNullOrWhiteSpace(body));
    }

    /// <summary>The sections of a Keep a Changelog file by name ("Unreleased", "1.2.0"), without the link definitions.</summary>
    internal static IReadOnlyDictionary<string, string> Sections(string changelog)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? name = null;
        var body = new StringBuilder();
        foreach (var line in changelog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Add();
                name = Heading.Match(line) is { Success: true } match ? match.Groups["name"].Value.Trim() : null;
                body.Clear();
            }
            else if (name is not null && !LinkDefinition.IsMatch(line))
            {
                body.Append(line).Append('\n');
            }
        }

        Add();
        return sections;

        void Add()
        {
            if (name is not null)
            {
                sections.TryAdd(name, body.ToString().Trim());
            }
        }
    }

    /// <summary>CHANGELOG.md as built into the app, or null when it isn't there.</summary>
    internal static string? ReadEmbeddedChangelog()
    {
        using var stream = typeof(WhatsNewViewModel).Assembly.GetManifestResourceStream(ChangelogResource);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

/// <summary>What <see cref="WhatsNewViewModel.Decide"/> says to do on a start.</summary>
internal enum WhatsNewDecision
{
    /// <summary>Same version (or older, or unreadable): nothing.</summary>
    Nothing,

    /// <summary>No version seen yet: remember this one, show nothing.</summary>
    Remember,

    /// <summary>A newer version than the one seen: show the page.</summary>
    Show,
}
