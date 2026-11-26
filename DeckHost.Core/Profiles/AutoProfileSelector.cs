using System.Text.RegularExpressions;
using DeckHost.Contracts.Models;

namespace DeckHost.Core.Profiles;

/// <summary>
/// Chooses the profile for the focused app (automatic profile switching). A profile lists rules
/// (<see cref="DeviceProfile.AutoSwitchApps"/>): an app's process name (<c>obs64</c>), a wildcard
/// pattern (<c>steam_app_*</c>), and optionally a window title after a bar
/// (<c>chrome | *YouTube*</c>, or <c>* | *Zoom Meeting*</c> for any app).
/// </summary>
public static class AutoProfileSelector
{
    /// <summary>"Code.exe", " code " and "CODE" all become "code".</summary>
    public static string NormalizeAppName(string? appName)
    {
        var name = (appName ?? string.Empty).Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.ToLowerInvariant();
    }

    /// <summary>A rule as it is stored: the app part normalized, the title part as typed ("Chrome.exe|*YouTube*" → "chrome | *YouTube*").</summary>
    public static string NormalizeRule(string? rule)
    {
        var parsed = AutoSwitchRule.Parse(rule);
        return parsed.TitlePattern is null ? parsed.AppPattern : $"{parsed.AppPattern} | {parsed.TitlePattern}";
    }

    /// <summary>
    /// The profile of the device whose rule fits the focused app best: a rule with a window title
    /// beats one without, an exact app name beats a pattern, and among equals the first profile in
    /// list order wins. Null when no rule fits; the caller then returns to the profile the user
    /// picked by hand.
    /// </summary>
    public static DeviceProfile? FindProfileForApp(IEnumerable<DeviceProfile> profiles, string deviceId, string? appName, string? windowTitle = null)
    {
        var normalized = NormalizeAppName(appName);
        if (normalized.Length == 0)
        {
            return null;
        }

        DeviceProfile? best = null;
        var bestScore = 0;
        foreach (var profile in profiles.Where(profile => string.Equals(profile.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var rule in (profile.AutoSwitchApps ?? []).Select(AutoSwitchRule.Parse))
            {
                var score = rule.Score(normalized, windowTitle);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = profile;
                }
            }
        }

        return best;
    }
}

/// <summary>One automatic switching rule: an app (name or * ? pattern) and an optional window title pattern.</summary>
public sealed record AutoSwitchRule(string AppPattern, string? TitlePattern)
{
    public static AutoSwitchRule Parse(string? text)
    {
        var parts = (text ?? string.Empty).Split('|', 2, StringSplitOptions.TrimEntries);
        var app = AutoProfileSelector.NormalizeAppName(parts[0]);
        var title = parts.Length == 2 && parts[1].Length > 0 ? parts[1] : null;
        return new AutoSwitchRule(app.Length == 0 && title is not null ? "*" : app, title);
    }

    private bool AppIsPattern => AppPattern.Contains('*', StringComparison.Ordinal) || AppPattern.Contains('?', StringComparison.Ordinal);

    /// <summary>0 when the rule doesn't fit; higher fits better (a title counts more than an exact app name).</summary>
    public int Score(string normalizedApp, string? windowTitle)
    {
        if (AppPattern.Length == 0 || !Wildcard(AppPattern, normalizedApp))
        {
            return 0;
        }

        if (TitlePattern is not null && (windowTitle is null || !Wildcard(TitlePattern, windowTitle)))
        {
            return 0;
        }

        return 1 + (AppIsPattern ? 0 : 1) + (TitlePattern is null ? 0 : 2);
    }

    /// <summary>Case-insensitive match with * (any text) and ? (one character); without either the whole text must be equal.</summary>
    public static bool Wildcard(string pattern, string text)
    {
        var expression = "^" + Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(text, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50));
    }
}
