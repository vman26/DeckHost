namespace DeckHost.Contracts.Models
{
    public sealed record ControlBinding(
        string ControlId,
        ProfileActionKind ActionKind,
        ActionReference Action,
        ControlAppearance Appearance,
        IReadOnlyList<MultiActionStep> Steps);
}
