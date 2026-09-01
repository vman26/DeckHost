using DeckHost.Contracts.Models;

namespace DeckHost.Contracts.Interfaces
{
    public interface IDevicePlugin
    {
        string Id { get; }
        string Name { get; }
        IReadOnlyCollection<DeviceDefinition> Devices { get; }
        Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken);
    }
}
