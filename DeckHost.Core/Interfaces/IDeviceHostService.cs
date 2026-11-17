using DeckHost.Contracts.Models;
using DeckHost.Core.Models;

namespace DeckHost.Core.Interfaces;

/// <summary>
/// Runtime host. All work (plugin init, input dispatch, rendering, device I/O) runs on one
/// background worker, so methods return immediately and are safe to call from any thread.
/// Events are raised on that worker thread: UI code must marshal to its dispatcher.
/// </summary>
public interface IDeviceHostService : IAsyncDisposable
{
    event Action<RuntimeLogEntry>? RuntimeLogReceived;
    event Action<DeviceConnectionStatus>? ConnectionStatusChanged;
    event Action? DevicesChanged;

    /// <summary>(deviceId, profileId) when the active profile changes, also when an action switched it.</summary>
    event Action<string, string>? ActiveProfileChanged;

    /// <summary>A control was rendered differently than before (also while the device is unplugged): the live preview.</summary>
    event Action<ControlFrame>? ControlFrameRendered;

    /// <summary>Every input from a device or from <see cref="SimulateInput"/>.</summary>
    event Action<DeviceInputEvent>? InputReceived;

    /// <summary>A control's LED changed color (effects included, also while the device is unplugged).</summary>
    event Action<LightState>? LightChanged;

    /// <summary>A workflow run started, entered a node or ended (the workflow editor's live view).</summary>
    event Action<WorkflowTrace>? WorkflowTraced
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Pauses a device: no images or lights are sent to it and its input is ignored (a firmware update
    /// is writing to it). Resuming redraws it.
    /// </summary>
    void SetDevicePaused(string deviceId, bool paused)
    {
    }

    /// <summary>Per device: inputs, images and lights sent, failures and the last error since start.</summary>
    IReadOnlyList<DeviceStatistics> DeviceStatistics => [];

    IReadOnlyCollection<DeviceDefinition> Devices { get; }
    IReadOnlyCollection<DeviceProfile> Profiles { get; }
    IReadOnlyCollection<RuntimeLogEntry> RuntimeLogs { get; }
    IReadOnlyCollection<DeviceConnectionStatus> ConnectionStatuses { get; }

    /// <summary>Replaces all profiles. The first profile per device becomes active unless one is already active.</summary>
    void SetProfiles(IEnumerable<DeviceProfile> profiles);

    void SetActiveProfile(string profileId);

    /// <summary>
    /// The default light of a device's LEDs (a color and an effect), for controls whose binding sets
    /// no light of its own; null for dark.
    /// </summary>
    void SetDeviceLight(string deviceId, LightStyle? style);

    /// <summary>Handles an input as if it came from the device (test mode in the editor).</summary>
    void SimulateInput(DeviceInputEvent inputEvent);

    /// <summary>Renders every control of the device again and raises their frames (e.g. when the editor switches to it).</summary>
    void Redraw(string deviceId);

    /// <summary>Sets a variable in the host's variable store (e.g. the app in the foreground).</summary>
    void SetVariable(string key, string value)
    {
    }

    /// <summary>The variables workflows and actions have set (name and value; names are lower case), for the editor's variable view.</summary>
    IReadOnlyList<KeyValuePair<string, string>> UserVariables() => [];

    /// <summary>Every variable the app knows: DeckHost's own (<c>system.*</c>) and the ones workflows set, with scope, type and value.</summary>
    IReadOnlyList<State.VariableInfo> Variables() => [];

    /// <summary>Removes a user variable (<c>vars.*</c>) from the store.</summary>
    void RemoveVariable(string key)
    {
    }

    /// <summary>The shared workflows Call nodes can run (replaces the previous list).</summary>
    void SetSharedWorkflows(IReadOnlyList<Workflow> workflows)
    {
    }

    /// <summary>Sets where the runs of a control's workflow stop (see <c>WorkflowEngine.BreakpointKey</c>).</summary>
    void SetWorkflowBreakpoints(string deviceId, string controlId, IReadOnlyCollection<string> keys)
    {
    }

    /// <summary>Lets every run that is held at a breakpoint or a step go on (the workflow editor closed, so nobody can continue them).</summary>
    void ReleaseWorkflowRuns()
    {
    }

    /// <summary>Lets a paused run go on until the next breakpoint.</summary>
    void ContinueWorkflow(int runId)
    {
    }

    /// <summary>Lets a paused run enter its next node, then holds it again.</summary>
    void StepWorkflow(int runId)
    {
    }

    /// <summary>Stops every run of the workflow on a control.</summary>
    void StopWorkflows(string deviceId, string controlId)
    {
    }

    /// <summary>Starts the workflow on a control in the active profile as if <paramref name="trigger"/> happened (the editor's Run).</summary>
    void RunWorkflowTrigger(string deviceId, string controlId, string trigger)
    {
    }

    /// <summary>Adds a line to the runtime log (for parts of the app outside the host).</summary>
    void AppendLog(string level, string message);

    /// <summary>Opens the own settings page of the action bound to a control in the active profile.</summary>
    Task<Uri?> GetActionSettingsPageAsync(string deviceId, string controlId);

    /// <summary>Replaces the enabled application plugins (after enabling, disabling, installing or uninstalling one).</summary>
    void SetApplicationPlugins(IEnumerable<DeckHost.Contracts.Interfaces.IApplicationPlugin> applicationPlugins);

    /// <summary>Initializes device plugins. Completes when every plugin finished (or failed) initializing.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Completes once all work queued before this call has been processed.</summary>
    Task WhenIdleAsync();
}
