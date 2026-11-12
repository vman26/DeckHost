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
        ProfileBackground? Background = null);
}
