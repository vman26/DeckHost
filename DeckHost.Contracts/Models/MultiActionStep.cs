namespace DeckHost.Contracts.Models
{
    public sealed record MultiActionStep(
        int Order,
        ActionReference Action);
}
