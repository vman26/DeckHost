using DeckHost.Contracts.Models;

namespace DeckHost.Contracts.Interfaces
{
    public interface IApplicationPlugin
    {
        string Id { get; }
        string Name { get; }
        IReadOnlyCollection<IDeckAction> Actions { get; }
    }
}
