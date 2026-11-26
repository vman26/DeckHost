using DeckHost.Contracts.Interfaces;

namespace DeckHost.Core.Devices;

/// <summary>
/// The device plugins that are available right now: the ones compiled into DeckHost plus the ones loaded from
/// packages (bundled or installed), which come and go. Enumerating gives a snapshot, so services that keep this
/// instead of a copied list see plugins that were added later.
/// </summary>
public sealed class LiveDevicePlugins(IEnumerable<IDevicePlugin> builtIn) : IEnumerable<IDevicePlugin>
{
    private readonly IReadOnlyList<IDevicePlugin> _builtIn = builtIn.ToList();
    private readonly Lock _gate = new();
    private IReadOnlyList<IDevicePlugin> _loaded = [];

    /// <summary>Adds a plugin that was loaded from a package (once).</summary>
    public void Add(IDevicePlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        lock (_gate)
        {
            if (!_loaded.Contains(plugin))
            {
                _loaded = [.. _loaded, plugin];
            }
        }
    }

    /// <summary>Forgets a plugin that was unloaded.</summary>
    public void Remove(IDevicePlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        lock (_gate)
        {
            _loaded = [.. _loaded.Where(other => !ReferenceEquals(other, plugin))];
        }
    }

    public IEnumerator<IDevicePlugin> GetEnumerator()
    {
        IReadOnlyList<IDevicePlugin> loaded;
        lock (_gate)
        {
            loaded = _loaded;
        }

        return _builtIn.Concat(loaded).ToList().GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
