using DeckHost.Contracts.Models;

namespace DeckHost.App.Profiles;

/// <summary>Which background a profile is drawn with.</summary>
internal static class ProfileBackgrounds
{
    /// <summary>
    /// The profile's own background when it overrides the default (<paramref name="overridesDefault"/>), else the one of its root profile
    /// (for a page or folder), else the default for all profiles; null when none of them is set.
    /// </summary>
    public static ProfileBackground? Effective(ProfileBackground? own, ProfileBackground? inherited, ProfileBackground? fallback, bool overridesDefault = true) =>
        overridesDefault && own is { IsEmpty: false } ? own
        : inherited is { IsEmpty: false } ? inherited
        : fallback;
}
