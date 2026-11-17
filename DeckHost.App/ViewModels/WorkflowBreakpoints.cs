using System;
using System.Collections.Generic;
using System.Linq;
using DeckHost.Core.Interfaces;

namespace DeckHost.App.ViewModels;

/// <summary>
/// Where runs of a control's workflow stop. Shared by every workflow editor, because a control's own workflow
/// and the shared workflows it calls are edited in different editors but stop the same runs.
/// </summary>
public sealed class WorkflowBreakpoints(IDeviceHostService? host)
{
    private readonly Dictionary<(string Device, string Control), HashSet<string>> _keys = [];

    public event EventHandler? Changed;

    public bool Has(string deviceId, string controlId, string key) =>
        _keys.TryGetValue((deviceId.ToLowerInvariant(), controlId.ToLowerInvariant()), out var set) && set.Contains(key);

    /// <summary>Adds the breakpoint, or takes it away when it is there. Returns whether it is set now.</summary>
    public bool Toggle(string deviceId, string controlId, string key)
    {
        var id = (deviceId.ToLowerInvariant(), controlId.ToLowerInvariant());
        if (!_keys.TryGetValue(id, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            _keys[id] = set;
        }

        var now = set.Add(key) || !set.Remove(key);
        host?.SetWorkflowBreakpoints(deviceId, controlId, [.. set]);
        Changed?.Invoke(this, EventArgs.Empty);
        return now;
    }

    public IReadOnlyCollection<string> Of(string deviceId, string controlId) =>
        _keys.TryGetValue((deviceId.ToLowerInvariant(), controlId.ToLowerInvariant()), out var set) ? [.. set] : [];
}
