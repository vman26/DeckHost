using System;
using System.Collections.Generic;
using System.Linq;
using DeckHost.Core.Interfaces;

namespace DeckHost.App.ViewModels;

/// <summary>
/// Where runs of a control's workflow stop. Shared by every workflow editor, because a control's own workflow
/// and the shared workflows it calls are edited in different editors but stop the same runs.
/// Debugging only works while an editor is open (<see cref="Attach"/>): the points are kept, but the host only stops
/// runs at them while someone can see and continue the run. Closing the last editor lets held runs go on.
/// </summary>
public sealed class WorkflowBreakpoints(IDeviceHostService? host)
{
    private readonly Dictionary<(string Device, string Control), HashSet<string>> _keys = [];
    private int _editors;

    /// <summary>Whether a workflow editor is open, so runs stop at the points.</summary>
    public bool IsDebugging => _editors > 0;

    /// <summary>A workflow editor opened: the host starts stopping runs at the points.</summary>
    public void Attach()
    {
        if (_editors++ == 0)
        {
            foreach (var ((device, control), keys) in _keys)
            {
                host?.SetWorkflowBreakpoints(device, control, [.. keys]);
            }
        }
    }

    /// <summary>A workflow editor closed: with the last one the host stops stopping runs, and runs that were held go on.</summary>
    public void Detach()
    {
        if (_editors == 0 || --_editors > 0)
        {
            return;
        }

        foreach (var (device, control) in _keys.Keys)
        {
            host?.SetWorkflowBreakpoints(device, control, []);
        }

        host?.ReleaseWorkflowRuns();
    }

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
        if (IsDebugging)
        {
            host?.SetWorkflowBreakpoints(deviceId, controlId, [.. set]);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return now;
    }

    public IReadOnlyCollection<string> Of(string deviceId, string controlId) =>
        _keys.TryGetValue((deviceId.ToLowerInvariant(), controlId.ToLowerInvariant()), out var set) ? [.. set] : [];
}
