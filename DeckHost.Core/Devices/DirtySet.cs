namespace DeckHost.Core.Devices;

/// <summary>
/// What the host has to draw again: per device either everything (the whole device) or a set of controls.
/// Owned by the host's worker, so it has no locks (<c>docs/maintainers/adr/0002-single-worker.md</c>).
/// </summary>
internal sealed class DirtySet
{
    // A null set means the whole device.
    private readonly Dictionary<string, HashSet<string>?> _byDevice = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => _byDevice.Count == 0;

    public void MarkDevice(string deviceId) => _byDevice[deviceId] = null;

    public void MarkDevices(IEnumerable<string> deviceIds)
    {
        foreach (var deviceId in deviceIds)
        {
            MarkDevice(deviceId);
        }
    }

    /// <summary>Marks one control; nothing changes when the whole device is already marked.</summary>
    public void MarkControl(string deviceId, string controlId)
    {
        if (!_byDevice.TryGetValue(deviceId, out var controls))
        {
            _byDevice[deviceId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { controlId };
            return;
        }

        controls?.Add(controlId);
    }

    public bool IsWhole(string deviceId) => _byDevice.TryGetValue(deviceId, out var controls) && controls is null;

    /// <summary>What was marked, per device (null: the whole device); the set is empty afterwards.</summary>
    public IReadOnlyList<(string DeviceId, IReadOnlySet<string>? Controls)> Drain()
    {
        var marked = _byDevice.Select(static pair => (pair.Key, (IReadOnlySet<string>?)pair.Value)).ToList();
        _byDevice.Clear();
        return marked;
    }
}
