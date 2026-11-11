namespace DeckHost.Contracts.Interfaces
{
    /// <summary>
    /// A set of actions. Installed plugins are loaded from a package (see the README) and may
    /// implement <see cref="IDisposable"/>; they are disposed when disabled or uninstalled.
    /// </summary>
    public interface IApplicationPlugin
    {
        string Id { get; }
        string Name { get; }
        IReadOnlyCollection<IDeckAction> Actions { get; }

        string Version => "1.0.0";

        string? Author => null;

        string? Description => null;
    }
}
