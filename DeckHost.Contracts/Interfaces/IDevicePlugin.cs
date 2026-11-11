using DeckHost.Contracts.Models;

namespace DeckHost.Contracts.Interfaces;

/// <summary>
/// Hardware support. Everything device-specific (SDK ids, native calls, image sizes,
/// LED layout) stays behind this interface and <see cref="IDeviceOutput"/>.
/// </summary>
public interface IDevicePlugin : IDisposable
{
    string Id { get; }

    string Name { get; }

    /// <summary>Current device definitions. Replaced (never mutated) when devices change.</summary>
    IReadOnlyCollection<DeviceDefinition> Devices { get; }

    /// <summary>Output side of the plugin, used by the host to draw on and light up controls.</summary>
    IDeviceOutput Output { get; }

    /// <summary>Raised on any thread when <see cref="Devices"/> changed (hot-plug, capabilities read, ...).</summary>
    event EventHandler? DevicesChanged;

    Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken);
}
