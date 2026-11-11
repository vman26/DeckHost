using DeckHost.Contracts.Models;

namespace DeckHost.Core.Profiles;

/// <summary>Chooses the profile for the focused app (automatic profile switching).</summary>
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

    /// <summary>
    /// The first profile of the device (in list order) that lists the app, or null when no profile
    /// does; the caller then returns to the profile the user picked by hand.
    /// </summary>
    public static DeviceProfile? FindProfileForApp(IEnumerable<DeviceProfile> profiles, string deviceId, string? appName)
    {
        var normalized = NormalizeAppName(appName);
        if (normalized.Length == 0)
        {
            return null;
        }

        return profiles.FirstOrDefault(profile =>
            string.Equals(profile.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase) &&
            (profile.AutoSwitchApps ?? []).Any(app => NormalizeAppName(app) == normalized));
    }
}
