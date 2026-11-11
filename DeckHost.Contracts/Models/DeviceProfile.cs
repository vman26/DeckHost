namespace DeckHost.Contracts.Models
{
    public sealed record DeviceProfile(
        string Id,
        string Name,
        string DeviceId,
        IReadOnlyList<ControlBinding> Bindings);
}
