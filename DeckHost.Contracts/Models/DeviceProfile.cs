namespace DeckHost.Contracts.Models
{
    /// <param name="AutoSwitchApps">
    /// Process names (for example "obs64" or "chrome.exe") that activate this profile automatically
    /// while their window has focus. Empty or null: never switched to automatically.
    /// </param>
    /// <param name="Background">Optional background spanning all controls.</param>
    public sealed record DeviceProfile(
        string Id,
        string Name,
        string DeviceId,
        IReadOnlyList<ControlBinding> Bindings,
        IReadOnlyList<string>? AutoSwitchApps = null,
        ProfileBackground? Background = null)
    {
        /// <summary>
        /// For a page or folder: the layout it belongs to. A page belongs to its profile; a folder to
        /// the page (or folder) whose key opens it. Null for a profile (its own first page).
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("parentId")]
        public string? ParentId { get; init; }

        /// <summary>What this layout is: null for a profile, <see cref="ProfileKinds.Page"/> or <see cref="ProfileKinds.Folder"/>.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("kind")]
        public string? Kind { get; init; }

        /// <summary>
        /// Whether this layout uses <see cref="Background"/> instead of the default background for all profiles. Null (files from before
        /// the checkbox) means yes when it has a background. Off keeps the background stored but unused, so turning it on again brings it back.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("overridesBackground")]
        public bool? OverridesBackground { get; init; }

        public bool IsRoot => Kind is null;
    }

    /// <summary>The kinds of layouts besides a profile.</summary>
    public static class ProfileKinds
    {
        /// <summary>Another page of a profile (Next/Previous Page, swiping the touch strip).</summary>
        public const string Page = "page";

        /// <summary>A layout a key opens, with a Back key.</summary>
        public const string Folder = "folder";
    }
}
