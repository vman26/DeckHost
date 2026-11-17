using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Models;
using DeckHost.Core.Rendering;
using DeckHost.Core.Workflows;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Reactive.Linq;

namespace DeckHost.Core.Devices;

/// <summary>
/// Owns the runtime: plugin initialization, input dispatch, rendering and device output.
/// <para>
/// Threading model: every public call only queues work. One worker task processes the queue in
/// order and owns all mutable state, so nothing here needs locks except the log buffer and the
/// published snapshots. Native SDK callbacks therefore return immediately, and the UI thread
/// never waits on USB I/O.
/// </para>
/// <para>
/// Rendering: after each batch of work, every "dirty" device is re-rendered control by control,
/// but a control is only pushed to the device when its pixels differ from the last frame pushed.
/// </para>
/// <para>
/// A control without a display can be linked to another control's display
/// (<see cref="PhysicalControl.DisplayControlId"/>: a knob and the touch zone above it). Its action
/// is then drawn there, and a tap on a display that has no action of its own presses it.
/// </para>
/// </summary>
public sealed class DeviceHostService : IDeviceHostService, IDeviceInputSink
{
    private const int MaxLogEntries = 500;

    /// <summary>Frame interval for animated GIFs (10 fps keeps USB traffic reasonable).</summary>
    private static readonly TimeSpan AnimationInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Light effects run faster than images: about 30 frames a second.</summary>
    private static readonly TimeSpan LightFrameInterval = TimeSpan.FromMilliseconds(33);

    /// <summary>Steady lights are sent again this often, in case the device changed them itself (sleep, its own config).</summary>
    private const long SteadyLightRefreshMilliseconds = 5000;

    private int _lightFrameQueued;
    private long _lastSteadyLightRefresh;

    private readonly IVariableStore _variableStore;
    private readonly IReadOnlyList<IDevicePlugin> _devicePlugins;
    private readonly IActionRouter _actionRouter;
    private readonly Channel<Func<ValueTask>> _work = Channel.CreateUnbounded<Func<ValueTask>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _worker;

    // Worker-owned state.
    private readonly Dictionary<string, DeviceProfile> _profilesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _activeProfilesByDeviceId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceEntry> _devicesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirtyDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string DeviceId, string ControlId), byte[]> _lastFrames = [];
    private readonly Dictionary<string, (byte Red, byte Green, byte Blue)> _lastBackdrops = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int Width, int Height), SurfaceRenderer> _renderers = [];
    private readonly Dictionary<(string DeviceId, string ControlId), int> _lightVersions = [];
    private readonly Dictionary<(string DeviceId, string ControlId), (byte R, byte G, byte B)> _lightsShown = [];
    private readonly Dictionary<(string DeviceId, string ControlId), long> _lightHeldUntil = [];
    private readonly Dictionary<string, LightStyle> _deviceLights = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TaskCompletionSource> _idleWaiters = [];
    private readonly SkiaDrawingResources _drawingResources = new();
    private bool _pluginsInitialized;
    private readonly IActionHost _actionHost;
    private readonly CancellationTokenSource _refreshCancellation = new();

    // Workflow event triggers (a variable changes, an app comes to the front, a timer, the profile is shown).
    private readonly WorkflowEventThrottle _eventThrottle = new();

    // Backgrounds with layers that follow a variable: the variables, and the redraw they ask for (at most one per AnimationInterval).
    private volatile HashSet<string> _backgroundKeys = [];
    private int _backgroundPending;
    private int _backgroundScheduled;
    private readonly Dictionary<string, string> _eventValues = new(StringComparer.Ordinal);
    private readonly Dictionary<(string DeviceId, string ControlId, string NodeId), DateTime> _timerLastStart = [];
    private IDisposable? _variableChanges;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private Dictionary<string, (IDeckAction Action, ActionContext Context)> _appeared = new(StringComparer.Ordinal);
    private readonly Dictionary<(string DeviceId, string ControlId), DateTimeOffset> _nextLiveRender = [];

    // Thread-safe published state.
    private readonly Lock _logLock = new();
    private readonly Queue<RuntimeLogEntry> _runtimeLogs = new();
    private readonly ConcurrentDictionary<string, DeviceConnectionStatus> _connectionStatuses = new(StringComparer.OrdinalIgnoreCase);
    private volatile DeviceDefinition[] _devicesSnapshot = [];
    private volatile DeviceProfile[] _profilesSnapshot = [];
    private int _startRequested;
    private int _disposed;
    private Task? _startTask;

    public event Action<RuntimeLogEntry>? RuntimeLogReceived;
    public event Action<DeviceConnectionStatus>? ConnectionStatusChanged;
    public event Action? DevicesChanged;
    public event Action<string, string>? ActiveProfileChanged;
    public event Action<ControlFrame>? ControlFrameRendered;

    public event Action<LightState>? LightChanged;
    public event Action<DeviceInputEvent>? InputReceived;
    public event Action<WorkflowTrace>? WorkflowTraced;

    public DeviceHostService(
        IEnumerable<IDevicePlugin> devicePlugins,
        IEnumerable<IApplicationPlugin> applicationPlugins,
        IVariableStore variableStore)
        : this(devicePlugins, CreateRouter(variableStore, applicationPlugins, out var onActionFailed), variableStore)
    {
        onActionFailed.Target = this;
    }

    public DeviceHostService(IEnumerable<IDevicePlugin> devicePlugins, IActionRouter actionRouter, IVariableStore variableStore)
    {
        ArgumentNullException.ThrowIfNull(devicePlugins);
        ArgumentNullException.ThrowIfNull(actionRouter);
        ArgumentNullException.ThrowIfNull(variableStore);

        _variableStore = variableStore;
        _devicePlugins = devicePlugins.ToList();
        _actionRouter = actionRouter;
        if (_actionRouter is ActionRouter router)
        {
            // Steps with a wait continue later on this worker.
            router.Scheduler = (deviceId, delay, work) => _ = Task.Delay(delay).ContinueWith(
                _ => Enqueue(() =>
                {
                    work();
                    _dirtyDevices.Add(deviceId);
                }),
                TaskScheduler.Default);
        }
        _actionHost = new ActionHostServices(this);
        if (_actionRouter is ActionRouter tracedRouter)
        {
            tracedRouter.Workflows.Traced += OnWorkflowTraced;
        }

        if (variableStore is State.VariableStore observable)
        {
            _variableChanges = observable.Changes
                .Where(change => IsEventVariable(change.Key) || _backgroundKeys.Contains(change.Key))
                .Subscribe(change =>
                {
                    var key = change.Key;
                    if (_backgroundKeys.Contains(key))
                    {
                        RequestBackgroundRedraw();
                    }

                    if (IsEventVariable(key))
                    {
                        var value = change.Value?.ToString() ?? string.Empty;
                        Enqueue(() => OnEventVariable(key, value));
                    }
                });
        }

        foreach (var duplicate in _actionRouter.DuplicateActionKeys)
        {
            Log("Warning", $"Action '{duplicate}' is registered more than once; only the first registration is used.");
        }

        // Read the initial definitions synchronously so the UI can show devices before StartAsync.
        RefreshDevices(raiseEvent: false);

        foreach (var plugin in _devicePlugins)
        {
            plugin.DevicesChanged += OnPluginDevicesChanged;
        }

        _worker = Task.Run(RunWorkerAsync);
    }

    public IReadOnlyCollection<DeviceDefinition> Devices => _devicesSnapshot;

    public IReadOnlyCollection<DeviceProfile> Profiles => _profilesSnapshot;

    public IReadOnlyCollection<RuntimeLogEntry> RuntimeLogs
    {
        get
        {
            lock (_logLock)
            {
                return _runtimeLogs.ToArray();
            }
        }
    }

    public IReadOnlyCollection<DeviceConnectionStatus> ConnectionStatuses => _connectionStatuses.Values.ToArray();

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DeviceStatistics> _statistics = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DeviceStatistics> DeviceStatistics => [.. _statistics.Values.OrderBy(static statistics => statistics.DeviceId, StringComparer.Ordinal)];

    /// <summary>Updates a device's statistics (on the worker; readers get immutable snapshots).</summary>
    private void Count(string deviceId, Func<DeviceStatistics, DeviceStatistics> change) =>
        _statistics.AddOrUpdate(deviceId, id => change(new DeviceStatistics(id, 0, null, 0, 0, 0, null, null)), (_, current) => change(current));

    public void SetProfiles(IEnumerable<DeviceProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var snapshot = profiles.ToArray();
        Enqueue(() => ApplyProfiles(snapshot));
    }

    public void SetActiveProfile(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        Enqueue(() => ActivateProfile(profileId));
    }

    public void ReportInput(DeviceInputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);
        Enqueue(() => HandleInput(inputEvent));
    }

    public void SetDeviceLight(string deviceId, LightStyle? style) => Enqueue(() =>
    {
        if (style is null)
        {
            _deviceLights.Remove(deviceId);
        }
        else
        {
            _deviceLights[deviceId] = style;
        }

        ApplyIdleLights(deviceId);
    });

    public void SimulateInput(DeviceInputEvent inputEvent) => ReportInput(inputEvent);

    private readonly HashSet<string> _pausedDevices = new(StringComparer.OrdinalIgnoreCase);

    public void SetDevicePaused(string deviceId, bool paused) => Enqueue(() =>
    {
        if (paused ? _pausedDevices.Add(deviceId) : _pausedDevices.Remove(deviceId))
        {
            Log("Information", paused ? $"Paused '{deviceId}' (firmware update)." : $"Resumed '{deviceId}'.");
            if (!paused)
            {
                ForgetFrames(deviceId);
                _dirtyDevices.Add(deviceId);
                ApplyIdleLights(deviceId);
            }
        }
    });

    public void Redraw(string deviceId) => Enqueue(() =>
    {
        ForgetControlFrames(deviceId);
        _dirtyDevices.Add(deviceId);
    });

    public void AppendLog(string level, string message) => Log(level, message);

    public void SetVariable(string key, string value) => Enqueue(() => _variableStore.Publish(key, value));

    public IReadOnlyList<KeyValuePair<string, string>> UserVariables() =>
        _variableStore is State.VariableStore store
            ? [.. store.Snapshot("vars.")
                .Select(static pair => new KeyValuePair<string, string>(WorkflowScopes.Describe(pair.Key), pair.Value?.ToString() ?? string.Empty))
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)]
            : [];

    public void SetSharedWorkflows(IReadOnlyList<Workflow> workflows)
    {
        var byId = workflows.Where(static workflow => !string.IsNullOrWhiteSpace(workflow.Id))
            .GroupBy(static workflow => workflow.Id!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        Enqueue(() =>
        {
            if (_actionRouter is ActionRouter router)
            {
                router.Workflows.SharedWorkflows = id => byId.GetValueOrDefault(id);
            }
        });
    }

    public void SetWorkflowBreakpoints(string deviceId, string controlId, IReadOnlyCollection<string> keys)
    {
        var copy = keys.ToArray();
        Enqueue(() =>
        {
            if (_actionRouter is ActionRouter router)
            {
                router.Workflows.SetBreakpoints(deviceId, controlId, copy);
            }
        });
    }

    public void ContinueWorkflow(int runId) => Enqueue(() =>
    {
        if (_actionRouter is ActionRouter router && router.Workflows.Continue(runId))
        {
            _dirtyDevices.UnionWith(_devicesById.Keys);
        }
    });

    public void StepWorkflow(int runId) => Enqueue(() =>
    {
        if (_actionRouter is ActionRouter router && router.Workflows.Step(runId))
        {
            _dirtyDevices.UnionWith(_devicesById.Keys);
        }
    });

    public void StopWorkflows(string deviceId, string controlId) => Enqueue(() =>
    {
        if (_actionRouter is ActionRouter router)
        {
            router.Workflows.StopAll(deviceId, controlId);
        }
    });

    public void RunWorkflowTrigger(string deviceId, string controlId, string trigger) => Enqueue(() =>
    {
        if (_actionRouter is not ActionRouter router || GetActiveProfile(deviceId) is not { } profile)
        {
            return;
        }

        var binding = profile.Bindings.FirstOrDefault(candidate =>
            candidate.ActionKind == ProfileActionKind.Workflow &&
            string.Equals(candidate.ControlId, controlId, StringComparison.OrdinalIgnoreCase));
        if (binding is null)
        {
            Log("Info", $"No workflow on '{controlId}' in the active profile to run.");
            return;
        }

        var (eventType, value) = trigger switch
        {
            WorkflowTriggers.RotateLeft => (InputCapabilities.Rotate, -1),
            WorkflowTriggers.RotateRight => (InputCapabilities.Rotate, 1),
            WorkflowTriggers.SwipeLeft => (InputCapabilities.Swipe, -1),
            WorkflowTriggers.SwipeRight => (InputCapabilities.Swipe, 1),
            WorkflowTriggers.Up => (InputCapabilities.Release, 0),
            _ => (InputCapabilities.Press, 1),
        };
        router.Workflows.Fire(profile, binding, trigger, new DeviceInputEvent(deviceId, controlId, eventType, value), _actionHost);
        _dirtyDevices.Add(deviceId);
    });

    private void OnWorkflowTraced(WorkflowTrace trace)
    {
        if (trace.Kind is WorkflowTraceKind.Started or WorkflowTraceKind.Finished or WorkflowTraceKind.Stopped or WorkflowTraceKind.Failed)
        {
            Log(trace.Kind == WorkflowTraceKind.Failed ? "Warning" : "Info", $"Workflow on '{trace.ControlId}' (run {trace.RunId}): {trace.Kind}: {trace.Detail}");
        }

        SafeInvoke(() => WorkflowTraced?.Invoke(trace));
    }

    public Task<Uri?> GetActionSettingsPageAsync(string deviceId, string controlId)
    {
        var completion = new TaskCompletionSource<Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_work.Writer.TryWrite(() =>
            {
                Uri? uri = null;
                if (GetActiveProfile(deviceId) is { } profile &&
                    profile.Bindings.FirstOrDefault(candidate => string.Equals(candidate.ControlId, controlId, StringComparison.OrdinalIgnoreCase)) is { } binding &&
                    _actionRouter.FindAction(binding.Action) is { HasSettingsPage: true } action)
                {
                    SafeInvoke(() => uri = action.GetSettingsPageUri(new ActionContext(deviceId, controlId, _variableStore)
                    {
                        Settings = binding.Action.EffectiveSettings,
                        Host = _actionHost,
                        ProfileId = profile.Id,
                    }));
                }

                completion.TrySetResult(uri);
                return ValueTask.CompletedTask;
            }))
        {
            completion.TrySetResult(null);
        }

        return completion.Task;
    }

    public void SetApplicationPlugins(IEnumerable<IApplicationPlugin> applicationPlugins)
    {
        ArgumentNullException.ThrowIfNull(applicationPlugins);
        var snapshot = applicationPlugins.ToArray();
        Enqueue(() =>
        {
            _actionRouter.SetApplicationPlugins(snapshot);
            foreach (var duplicate in _actionRouter.DuplicateActionKeys)
            {
                Log("Warning", $"Action '{duplicate}' is registered more than once; only the first registration is used.");
            }

            // Bindings may now resolve to different (or no) actions.
            foreach (var deviceId in _devicesById.Keys)
            {
                _dirtyDevices.Add(deviceId);
            }

            Log("Info", $"{snapshot.Length} application plugin(s) enabled.");
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _startRequested, 1) == 1)
        {
            return _startTask ?? Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _startTask = completion.Task;
        EnqueueAsync(async () =>
        {
            try
            {
                await InitializePluginsAsync(cancellationToken).ConfigureAwait(false);
                _ = RunLiveRefreshAsync(_refreshCancellation.Token);
                _ = RunLightLoopAsync(_refreshCancellation.Token);
                _ = RunTimerLoopAsync(_refreshCancellation.Token);
                FireEvent(WorkflowTriggers.ProfileShown, null);
                completion.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
        });

        return completion.Task;
    }

    public Task WhenIdleAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_work.Writer.TryWrite(() =>
            {
                _idleWaiters.Add(completion);
                return ValueTask.CompletedTask;
            }))
        {
            completion.TrySetResult();
        }

        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        foreach (var plugin in _devicePlugins)
        {
            plugin.DevicesChanged -= OnPluginDevicesChanged;
        }

        // Blank the devices so they don't keep showing a profile nobody is running.
        _work.Writer.TryWrite(() =>
        {
            if (_pluginsInitialized)
            {
                foreach (var (deviceId, entry) in _devicesById)
                {
                    if (entry.Device.IsConnected)
                    {
                        entry.Plugin.Output.Clear(deviceId);
                    }
                }
            }

            return ValueTask.CompletedTask;
        });
        _refreshCancellation.Cancel();
        _variableChanges?.Dispose();
        _work.Writer.TryComplete();

        await Task.WhenAny(_worker, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

        foreach (var plugin in _devicePlugins)
        {
            try
            {
                plugin.Dispose();
            }
            catch (Exception ex)
            {
                Log("Error", $"Disposing device plugin '{plugin.Name}' failed: {ex.Message}");
            }
        }

        if (_worker.IsCompleted)
        {
            foreach (var renderer in _renderers.Values)
            {
                renderer.Dispose();
            }

            _renderers.Clear();
            _drawingResources.Dispose();
        }
    }

    // ---------------------------------------------------------------- worker

    private async Task RunWorkerAsync()
    {
        var reader = _work.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var item))
            {
                try
                {
                    await item().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log("Error", $"Runtime work item failed: {ex.Message}");
                }
            }

            try
            {
                RenderDirtyDevices();
            }
            catch (Exception ex)
            {
                Log("Error", $"Rendering failed: {ex.Message}");
            }

            foreach (var waiter in _idleWaiters)
            {
                waiter.TrySetResult();
            }

            _idleWaiters.Clear();
        }

        foreach (var waiter in _idleWaiters)
        {
            waiter.TrySetResult();
        }
    }

    private void Enqueue(Action work) => _work.Writer.TryWrite(() =>
    {
        work();
        return ValueTask.CompletedTask;
    });

    private void EnqueueAsync(Func<Task> work) => _work.Writer.TryWrite(() => new ValueTask(work()));

    private async Task InitializePluginsAsync(CancellationToken cancellationToken)
    {
        foreach (var plugin in _devicePlugins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = new DevicePluginContext(this, Log, ReportConnectionStatus, deviceId => Enqueue(() =>
            {
                // The device's own screen (and with its configuration, its LEDs) was overwritten:
                // push every image and light again. A steady light (or Off) would otherwise never
                // be sent again and the device's own lighting stayed on.
                ForgetFrames(deviceId);
                _dirtyDevices.Add(deviceId);
                ApplyIdleLights(deviceId);
            }));
            try
            {
                await plugin.InitializeAsync(context, cancellationToken).ConfigureAwait(false);
                Log("Info", $"Initialized device plugin '{plugin.Name}'.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                foreach (var device in plugin.Devices)
                {
                    ReportConnectionStatus(new DeviceConnectionStatus(device.Id, "Failed", ex.Message));
                }

                Log("Error", $"Failed to initialize device plugin '{plugin.Name}': {ex.Message}");
            }
        }

        _pluginsInitialized = true;
        RefreshDevices(raiseEvent: true);

        foreach (var deviceId in _devicesById.Keys)
        {
            _dirtyDevices.Add(deviceId);
            ApplyIdleLights(deviceId);
        }
    }

    private void HandleInput(DeviceInputEvent inputEvent)
    {
        if (_pausedDevices.Contains(inputEvent.DeviceId))
        {
            return;
        }

        // Reported for every press (also unbound controls) so the editor can show it live.
        SafeInvoke(() => InputReceived?.Invoke(inputEvent));
        Count(inputEvent.DeviceId, static statistics => statistics with { Inputs = statistics.Inputs + 1, LastInput = DateTimeOffset.Now });

        var activeProfile = GetActiveProfile(inputEvent.DeviceId);
        if (activeProfile is null)
        {
            Log("Info", $"Input from '{inputEvent.DeviceId}/{inputEvent.ControlId}' ignored: no active profile.");
            return;
        }

        // Key Logic keys run a press, a double press or a hold, which needs timing.
        if (activeProfile.Bindings.FirstOrDefault(binding => binding.ActionKind == ProfileActionKind.KeyLogic &&
                string.Equals(binding.ControlId, inputEvent.ControlId, StringComparison.OrdinalIgnoreCase)) is { } keyLogic &&
            _actionRouter is ActionRouter keyLogicRouter)
        {
            HandleKeyLogic(keyLogicRouter, activeProfile, keyLogic, inputEvent);
            _dirtyDevices.Add(inputEvent.DeviceId);
            return;
        }

        // Workflows start on inputs, and on a tap, a double press or a hold (which need timing).
        if (activeProfile.Bindings.FirstOrDefault(binding => binding.ActionKind == ProfileActionKind.Workflow &&
                string.Equals(binding.ControlId, inputEvent.ControlId, StringComparison.OrdinalIgnoreCase)) is { } workflow &&
            _actionRouter is ActionRouter workflowRouter)
        {
            HandleWorkflowInput(workflowRouter, activeProfile, workflow, inputEvent);
            _dirtyDevices.Add(inputEvent.DeviceId);
            return;
        }

        var target = FindTapTarget(activeProfile, inputEvent);
        bool dispatched;
        if (target is null)
        {
            dispatched = _actionRouter.Dispatch(activeProfile, inputEvent, _actionHost);
        }
        else
        {
            // The display only shows the linked control's action: a tap is a press of that control.
            dispatched = _actionRouter.Dispatch(activeProfile, inputEvent with { ControlId = target }, _actionHost);
            dispatched |= _actionRouter.Dispatch(activeProfile, new DeviceInputEvent(inputEvent.DeviceId, target, InputCapabilities.Release, 0), _actionHost);
        }

        // A swipe over a touch strip without an action of its own flips through the profile's pages.
        if (!dispatched && target is null && inputEvent.EventType == InputCapabilities.Swipe && inputEvent.Value != 0 &&
            !activeProfile.Bindings.Any(binding => string.Equals(binding.ControlId, inputEvent.ControlId, StringComparison.OrdinalIgnoreCase)))
        {
            // Like a phone: swiping left brings the next page in from the right.
            ActivateRelativePage(inputEvent.DeviceId, inputEvent.Value < 0 ? 1 : -1);
            dispatched = true;
        }

        Log("Info", $"Input {inputEvent.EventType} ({inputEvent.Value}) on '{inputEvent.ControlId}'{(target is null ? string.Empty : $" (as a press of '{target}')")} {(dispatched ? "dispatched" : "had no matching action")}.");

        if (dispatched)
        {
            HandleLightFeedback(activeProfile, target ?? inputEvent.ControlId);
        }

        // Actions can change any variable a renderer reads; unchanged controls are skipped at push time.
        _dirtyDevices.Add(inputEvent.DeviceId);
    }

    /// <summary>How long Key Logic waits for a second press before it runs the single press.</summary>
    public static readonly TimeSpan DoublePressWindow = TimeSpan.FromMilliseconds(300);

    private readonly Dictionary<(string DeviceId, string ControlId), KeyLogicState> _keyLogic = [];

    private sealed class KeyLogicState
    {
        public bool Held { get; set; }

        public bool Pending { get; set; }

        public int Version { get; set; }
    }

    /// <summary>
    /// Key Logic: holding runs the hold steps (on the device's long press); a release runs the press
    /// steps, unless a second press follows within <see cref="DoublePressWindow"/>, which runs the
    /// double-press steps. Without double-press steps a press runs at once.
    /// </summary>
    private void HandleKeyLogic(ActionRouter router, DeviceProfile profile, ControlBinding binding, DeviceInputEvent inputEvent) =>
        HandleGestures(
            inputEvent,
            ActionRouter.HasGroup(binding, StepGroups.Hold),
            ActionRouter.HasGroup(binding, StepGroups.DoublePress),
            group =>
            {
                if (router.DispatchGroup(profile, binding, group, inputEvent.DeviceId, _actionHost))
                {
                    HandleLightFeedback(profile, inputEvent.ControlId);
                }

                Log("Info", $"Key Logic on '{inputEvent.ControlId}': {group}.");
            });

    /// <summary>
    /// A workflow: inputs that start triggers right away go through the router; a tap, a double
    /// press and a hold are told apart like Key Logic, when the workflow listens to them.
    /// </summary>
    private void HandleWorkflowInput(ActionRouter router, DeviceProfile profile, ControlBinding binding, DeviceInputEvent inputEvent)
    {
        if (router.Dispatch(profile, inputEvent, _actionHost))
        {
            HandleLightFeedback(profile, inputEvent.ControlId);
        }

        var triggers = WorkflowEngine.TriggersOf(binding.Workflow);
        if (inputEvent.EventType is not (InputCapabilities.Press or InputCapabilities.Release or InputCapabilities.LongPress) ||
            !(triggers.Contains(WorkflowTriggers.Tap) || triggers.Contains(WorkflowTriggers.DoublePress) || triggers.Contains(WorkflowTriggers.Hold)))
        {
            return;
        }

        HandleGestures(
            inputEvent,
            triggers.Contains(WorkflowTriggers.Hold),
            triggers.Contains(WorkflowTriggers.DoublePress),
            group =>
            {
                var trigger = group switch
                {
                    StepGroups.Hold => WorkflowTriggers.Hold,
                    StepGroups.DoublePress => WorkflowTriggers.DoublePress,
                    _ => WorkflowTriggers.Tap,
                };

                // The profile and binding as they are now (a wait for a double press may span an edit).
                var current = GetActiveProfile(inputEvent.DeviceId);
                var currentBinding = current?.Bindings.FirstOrDefault(candidate =>
                    candidate.ActionKind == ProfileActionKind.Workflow &&
                    string.Equals(candidate.ControlId, inputEvent.ControlId, StringComparison.OrdinalIgnoreCase));
                if (current is not null && currentBinding is not null &&
                    router.Workflows.Fire(current, currentBinding, trigger, inputEvent with { EventType = InputCapabilities.Press, Value = 1 }, _actionHost))
                {
                    HandleLightFeedback(current, inputEvent.ControlId);
                }
            });
    }

    /// <summary>
    /// Tells a press, a double press and a hold apart: <paramref name="run"/> gets
    /// <see cref="StepGroups.Hold"/> on the device's long press (when <paramref name="hasHold"/>),
    /// <see cref="StepGroups.Press"/> on a release that didn't end a hold, or, when
    /// <paramref name="hasDouble"/>, <see cref="StepGroups.DoublePress"/> for a second release within
    /// <see cref="DoublePressWindow"/> (the single press then waits that long).
    /// </summary>
    private void HandleGestures(DeviceInputEvent inputEvent, bool hasHold, bool hasDouble, Action<string> run)
    {
        var key = (inputEvent.DeviceId, inputEvent.ControlId);
        if (!_keyLogic.TryGetValue(key, out var state))
        {
            state = new KeyLogicState();
            _keyLogic[key] = state;
        }

        switch (inputEvent.EventType)
        {
            case InputCapabilities.Press:
                state.Held = false;
                break;
            case InputCapabilities.LongPress when hasHold:
                state.Held = true;
                state.Pending = false;
                run(StepGroups.Hold);
                break;
            case InputCapabilities.Release when state.Held:
                state.Held = false;
                break;
            case InputCapabilities.Release when state.Pending:
                state.Pending = false;
                run(StepGroups.DoublePress);
                break;
            case InputCapabilities.Release when !hasDouble:
                run(StepGroups.Press);
                break;
            case InputCapabilities.Release:
                state.Pending = true;
                var version = ++state.Version;
                _ = Task.Delay(DoublePressWindow).ContinueWith(
                    _ => Enqueue(() =>
                    {
                        if (state.Pending && state.Version == version)
                        {
                            state.Pending = false;
                            run(StepGroups.Press);
                            _dirtyDevices.Add(inputEvent.DeviceId);
                        }
                    }),
                    TaskScheduler.Default);
                break;
        }
    }

    private void ApplyProfiles(IReadOnlyList<DeviceProfile> profiles)
    {
        _profilesById.Clear();
        foreach (var profile in profiles)
        {
            _profilesById[profile.Id] = profile;
        }

        _backgroundKeys = profiles.SelectMany(static profile => BackgroundLayers.BoundVariables(profile.Background)).ToHashSet(StringComparer.Ordinal);

        // Keep the active profile per device when it still exists, else fall back to the first one.
        foreach (var deviceId in _activeProfilesByDeviceId.Keys.ToList())
        {
            if (!_profilesById.ContainsKey(_activeProfilesByDeviceId[deviceId]))
            {
                _activeProfilesByDeviceId.Remove(deviceId);
            }
        }

        foreach (var profile in profiles)
        {
            _activeProfilesByDeviceId.TryAdd(profile.DeviceId, profile.Id);
        }

        _profilesSnapshot = [.. profiles];
        OnActiveProfilesChanged();
    }

    private void ActivateProfile(string profileId)
    {
        if (!_profilesById.TryGetValue(profileId, out var profile))
        {
            Log("Warning", $"Cannot activate unknown profile '{profileId}'.");
            return;
        }

        if (_activeProfilesByDeviceId.TryGetValue(profile.DeviceId, out var current) &&
            string.Equals(current, profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activeProfilesByDeviceId[profile.DeviceId] = profile.Id;
        Log("Info", $"Activated profile '{profile.Name}' for device '{profile.DeviceId}'.");
        OnActiveProfilesChanged();
        SafeInvoke(() => ActiveProfileChanged?.Invoke(profile.DeviceId, profile.Id));

        // Timers count from when the profile came; its "profile shown" workflows start.
        foreach (var key in _timerLastStart.Keys.Where(key => string.Equals(key.DeviceId, profile.DeviceId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _timerLastStart.Remove(key);
        }

        FireEvent(WorkflowTriggers.ProfileShown, null, profile.DeviceId);
    }

    // ---------------------------------------------------------------- reactive backgrounds

    private static bool IsEventVariable(string key) => key == VariableKeys.ForegroundApp || key.StartsWith("vars.", StringComparison.Ordinal);

    /// <summary>A variable a background layer follows changed: draw the devices that show such a background again, soon (not on every value).</summary>
    private void RequestBackgroundRedraw()
    {
        Volatile.Write(ref _backgroundPending, 1);
        if (Interlocked.Exchange(ref _backgroundScheduled, 1) == 0)
        {
            Enqueue(FlushBackgroundRedraw);
        }
    }

    private void FlushBackgroundRedraw()
    {
        Volatile.Write(ref _backgroundPending, 0);
        foreach (var deviceId in _devicesById.Keys)
        {
            if (BackgroundLayers.IsReactive(GetActiveProfile(deviceId)?.Background))
            {
                _dirtyDevices.Add(deviceId);
            }
        }

        // The next redraw is at least one animation interval away; a value that changed meanwhile gets it.
        _ = Task.Delay(AnimationInterval).ContinueWith(
            _ => Enqueue(() =>
            {
                Volatile.Write(ref _backgroundScheduled, 0);
                if (Volatile.Read(ref _backgroundPending) == 1)
                {
                    RequestBackgroundRedraw();
                }
            }),
            TaskScheduler.Default);
    }

    // ---------------------------------------------------------------- event triggers

    /// <summary>Starts the workflows of the shown profiles that listen to an event (and whose trigger node accepts it).</summary>
    private void FireEvent(string trigger, Func<WorkflowNode, WorkflowScope, bool>? accept, string? onlyDeviceId = null)
    {
        if (_actionRouter is not ActionRouter router)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var deviceId in _devicesById.Keys.ToList())
        {
            if ((onlyDeviceId is not null && !string.Equals(deviceId, onlyDeviceId, StringComparison.OrdinalIgnoreCase)) ||
                GetActiveProfile(deviceId) is not { } profile)
            {
                continue;
            }

            foreach (var binding in profile.Bindings)
            {
                if (binding.ActionKind != ProfileActionKind.Workflow || !WorkflowEngine.TriggersOf(binding.Workflow).Contains(trigger))
                {
                    continue;
                }

                var throttleKey = $"{deviceId}|{binding.ControlId}|{trigger}";
                if (!_eventThrottle.Allow(throttleKey, now, out var justPaused))
                {
                    if (justPaused)
                    {
                        Log("Warning", $"The workflow on '{binding.ControlId}' keeps starting itself ({WorkflowTriggers.Describe(trigger).ToLowerInvariant()}); its event trigger is paused for {WorkflowEventThrottle.Pause.TotalSeconds:0} seconds.");
                    }

                    continue;
                }

                var scope = new WorkflowScope(profile.Id, deviceId, binding.ControlId);
                if (router.Workflows.Fire(profile, binding, trigger, new DeviceInputEvent(deviceId, binding.ControlId, InputCapabilities.Press, 1), _actionHost, accept is null ? null : node => accept(node, scope)))
                {
                    _dirtyDevices.Add(deviceId);
                }
            }
        }
    }

    /// <summary>A user variable or the app in front changed: start the workflows that wait for it.</summary>
    private void OnEventVariable(string key, string value)
    {
        if (_eventValues.TryGetValue(key, out var before) && before == value)
        {
            return;
        }

        _eventValues[key] = value;
        if (key == VariableKeys.ForegroundApp)
        {
            FireEvent(WorkflowTriggers.AppInFront, (node, _) => WorkflowEvents.MatchesApp(node, value));
        }
        else
        {
            FireEvent(WorkflowTriggers.VariableChanged, (node, scope) => WorkflowEvents.MatchesVariable(node, key, value, scope));
        }
    }

    /// <summary>Starts every timer trigger that is due (a timer counts from when its profile was shown).</summary>
    internal void TickTimers(DateTime now)
    {
        if (_actionRouter is not ActionRouter router)
        {
            return;
        }

        foreach (var deviceId in _devicesById.Keys.ToList())
        {
            if (GetActiveProfile(deviceId) is not { } profile)
            {
                continue;
            }

            foreach (var binding in profile.Bindings)
            {
                if (binding.ActionKind != ProfileActionKind.Workflow || binding.Workflow is not { } workflow)
                {
                    continue;
                }

                foreach (var node in workflow.Nodes.Where(static node => node.Kind == WorkflowNodeKinds.Trigger &&
                             node.EffectiveSettings.Get(WorkflowKeys.On, WorkflowTriggers.Down) == WorkflowTriggers.Timer))
                {
                    var key = (deviceId, binding.ControlId, node.Id);
                    if (!_timerLastStart.TryGetValue(key, out var last))
                    {
                        _timerLastStart[key] = now;
                        continue;
                    }

                    if (now - last < WorkflowEvents.EveryOf(node))
                    {
                        continue;
                    }

                    _timerLastStart[key] = now;
                    if (router.Workflows.Fire(profile, binding, WorkflowTriggers.Timer, new DeviceInputEvent(deviceId, binding.ControlId, InputCapabilities.Press, 1), _actionHost, candidate => candidate.Id == node.Id))
                    {
                        _dirtyDevices.Add(deviceId);
                    }
                }
            }
        }
    }

    /// <summary>Runs <see cref="TickTimers"/> on the worker at a given time (tests).</summary>
    internal Task TickTimersAsync(DateTime now)
    {
        Enqueue(() => TickTimers(now));
        return WhenIdleAsync();
    }

    private async Task RunTimerLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                Enqueue(() => TickTimers(DateTime.UtcNow));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ActivateProfileByIdOrName(string deviceId, string profileIdOrName)
    {
        // By name only profiles (not their pages or folders, which may share a name).
        var profile = _profilesById.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Id, profileIdOrName, StringComparison.OrdinalIgnoreCase))
            ?? _profilesById.Values.FirstOrDefault(candidate =>
                candidate.IsRoot &&
                string.Equals(candidate.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Name.Trim(), profileIdOrName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            Log("Warning", $"No profile '{profileIdOrName}' for device '{deviceId}'.");
            return;
        }

        ActivateProfile(profile.Id);
    }

    private void ActivateRelativeProfile(string deviceId, int offset)
    {
        // Same order the UI shows: the order the profiles were given in. Pages and folders belong
        // to their profile and aren't stepped through here.
        var profiles = _profilesSnapshot.Where(profile => profile.IsRoot && string.Equals(profile.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (profiles.Count < 2)
        {
            return;
        }

        var current = GetActiveProfile(deviceId) is { } active ? RootOf(active) : null;
        var index = current is null ? 0 : profiles.FindIndex(profile => profile.Id == current.Id);
        var next = ((index + offset) % profiles.Count + profiles.Count) % profiles.Count;
        ActivateProfile(profiles[next].Id);
    }

    // ---------------------------------------------------------------- pages and folders

    /// <summary>The profile a page or folder belongs to (a profile is its own).</summary>
    private DeviceProfile RootOf(DeviceProfile layout)
    {
        var current = layout;
        for (var depth = 0; depth < 64 && !current.IsRoot && current.ParentId is { } parentId && _profilesById.TryGetValue(parentId, out var parent); depth++)
        {
            current = parent;
        }

        return current;
    }

    /// <summary>The page a layout is on: itself for a page or profile, else the page its folders hang from.</summary>
    private DeviceProfile PageOf(DeviceProfile layout)
    {
        var current = layout;
        for (var depth = 0; depth < 64 && current.Kind == ProfileKinds.Folder && current.ParentId is { } parentId && _profilesById.TryGetValue(parentId, out var parent); depth++)
        {
            current = parent;
        }

        return current;
    }

    /// <summary>A profile's pages in order: the profile itself, then its pages in the order they were given.</summary>
    private List<DeviceProfile> PagesOf(DeviceProfile root) =>
        [root, .. _profilesSnapshot.Where(profile => profile.Kind == ProfileKinds.Page && string.Equals(profile.ParentId, root.Id, StringComparison.OrdinalIgnoreCase))];

    private void ActivateRelativePage(string deviceId, int offset)
    {
        if (GetActiveProfile(deviceId) is not { } active)
        {
            return;
        }

        var pages = PagesOf(RootOf(active));
        if (pages.Count < 2)
        {
            return;
        }

        var index = Math.Max(0, pages.FindIndex(page => page.Id == PageOf(active).Id));
        ActivateProfile(pages[((index + offset) % pages.Count + pages.Count) % pages.Count].Id);
    }

    private void ActivatePageNumber(string deviceId, int number)
    {
        if (GetActiveProfile(deviceId) is { } active && PagesOf(RootOf(active)) is { } pages && number >= 1 && number <= pages.Count)
        {
            ActivateProfile(pages[number - 1].Id);
        }
    }

    private void OpenFolder(string deviceId, string folderId)
    {
        if (_profilesById.TryGetValue(folderId, out var folder) &&
            folder.Kind == ProfileKinds.Folder &&
            string.Equals(folder.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
        {
            ActivateProfile(folder.Id);
        }
        else
        {
            Log("Warning", $"The folder '{folderId}' doesn't exist (anymore).");
        }
    }

    private void CloseFolder(string deviceId)
    {
        if (GetActiveProfile(deviceId) is { Kind: ProfileKinds.Folder, ParentId: { } parentId } && _profilesById.ContainsKey(parentId))
        {
            ActivateProfile(parentId);
        }
    }

    private void OnActiveProfilesChanged()
    {
        foreach (var deviceId in _devicesById.Keys)
        {
            var profile = GetActiveProfile(deviceId);
            if (profile is not null)
            {
                PublishAppearanceState(profile);
            }

            _dirtyDevices.Add(deviceId);
            ApplyIdleLights(deviceId);
        }
    }

    private DeviceProfile? GetActiveProfile(string deviceId)
    {
        if (_activeProfilesByDeviceId.TryGetValue(deviceId, out var profileId) &&
            _profilesById.TryGetValue(profileId, out var activeProfile))
        {
            return activeProfile;
        }

        var fallback = _profilesSnapshot.FirstOrDefault(profile =>
                profile.IsRoot && string.Equals(profile.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            ?? _profilesById.Values.FirstOrDefault(profile => string.Equals(profile.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        if (fallback is not null)
        {
            _activeProfilesByDeviceId[deviceId] = fallback.Id;
        }

        return fallback;
    }

    private void OnPluginDevicesChanged(object? sender, EventArgs e) => Enqueue(() => RefreshDevices(raiseEvent: true));

    private void RefreshDevices(bool raiseEvent)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var plugin in _devicePlugins)
        {
            foreach (var device in plugin.Devices)
            {
                if (!seen.Add(device.Id))
                {
                    Log("Warning", $"Device id '{device.Id}' is reported by more than one plugin; ignoring the one from '{plugin.Name}'.");
                    continue;
                }

                if (_devicesById.TryGetValue(device.Id, out var existing) && existing.Device == device)
                {
                    continue;
                }

                changed = true;
                _devicesById[device.Id] = new DeviceEntry(device, plugin);
                ForgetFrames(device.Id);
                PublishHardwareIds(device);

                if (_pluginsInitialized && device.IsConnected)
                {
                    _dirtyDevices.Add(device.Id);
                    ApplyIdleLights(device.Id);
                }
            }
        }

        foreach (var removed in _devicesById.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            changed = true;
            _devicesById.Remove(removed);
            ForgetFrames(removed);
        }

        if (!changed && _devicesSnapshot.Length == _devicesById.Count)
        {
            return;
        }

        _devicesSnapshot = [.. _devicesById.Values.Select(static entry => entry.Device)];
        if (raiseEvent)
        {
            SafeInvoke(() => DevicesChanged?.Invoke());
        }
    }

    /// <summary>Forgets everything sent to the device's screen, so all of it is sent again.</summary>
    private void ForgetFrames(string deviceId)
    {
        _lastBackdrops.Remove(deviceId);
        foreach (var key in _lightsShown.Keys.Where(key => string.Equals(key.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _lightsShown.Remove(key);
        }

        ForgetControlFrames(deviceId);
    }

    private void ForgetControlFrames(string deviceId)
    {
        foreach (var key in _lastFrames.Keys.Where(key => string.Equals(key.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _lastFrames.Remove(key);
        }
    }

    // ---------------------------------------------------------------- linked displays

    private static ControlBinding? FindBinding(DeviceProfile? profile, string controlId) =>
        profile?.Bindings.FirstOrDefault(candidate => string.Equals(candidate.ControlId, controlId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The binding of a control that shows itself on <paramref name="display"/> (a knob on the
    /// touch zone above it), or null when the display is only its own.
    /// </summary>
    private LinkedBinding? FindLinkedBinding(DeviceDefinition device, DeviceProfile? profile, PhysicalControl display)
    {
        if (profile is null)
        {
            return null;
        }

        foreach (var control in device.Controls)
        {
            if (string.Equals(control.DisplayControlId, display.Id, StringComparison.OrdinalIgnoreCase) &&
                FindBinding(profile, control.Id) is { Appearance.ShowOnLinkedDisplay: true } binding)
            {
                var above = control.Bounds.Y + (control.Bounds.Height / 2) < display.Bounds.Y + (display.Bounds.Height / 2);
                return new LinkedBinding(control.Id, binding, _actionRouter.FindAction(binding.Action), StripAtTop: above);
            }
        }

        return null;
    }

    /// <summary>
    /// The control a tap really belongs to: when the tapped display has nothing of its own (see
    /// <see cref="ControlRenderer.HasOwnContent"/>) and shows a linked control's action instead,
    /// that control. Null for every other input.
    /// </summary>
    private string? FindTapTarget(DeviceProfile profile, DeviceInputEvent inputEvent)
    {
        if (inputEvent.EventType != InputCapabilities.Press ||
            !_devicesById.TryGetValue(inputEvent.DeviceId, out var entry) ||
            entry.Device.FindControl(inputEvent.ControlId) is not { HasImageOutput: true } display)
        {
            return null;
        }

        var own = FindBinding(profile, display.Id);
        if (ControlRenderer.HasOwnContent(own, own is null ? null : _actionRouter.FindAction(own.Action)))
        {
            return null;
        }

        return FindLinkedBinding(entry.Device, profile, display)?.ControlId;
    }

    // ---------------------------------------------------------------- rendering

    /// <summary>
    /// Tells actions when they become visible or hidden (Stream Deck willAppear / willDisappear):
    /// compares the bindings of every device's active profile with the previous batch.
    /// </summary>
    private void UpdateAppearances()
    {
        var visible = new Dictionary<string, (IDeckAction Action, ActionContext Context)>(StringComparer.Ordinal);
        foreach (var deviceId in _devicesById.Keys)
        {
            if (GetActiveProfile(deviceId) is not { } profile)
            {
                continue;
            }

            foreach (var binding in profile.Bindings)
            {
                var references = binding.ActionKind != ProfileActionKind.Single
                    ? binding.StepActions
                    : [binding.Action];
                var index = 0;
                foreach (var reference in references)
                {
                    if (_actionRouter.FindAction(reference) is { } action)
                    {
                        var key = $"{deviceId}|{profile.Id}|{binding.ControlId}|{index}|{reference.PluginId}:{reference.ActionId}|{reference.Settings?.GetHashCode()}";
                        visible[key] = (action, new ActionContext(deviceId, binding.ControlId, _variableStore)
                        {
                            Settings = reference.EffectiveSettings,
                            Host = _actionHost,
                            ProfileId = profile.Id,
                        });
                    }

                    index++;
                }
            }
        }

        foreach (var (key, (action, context)) in _appeared)
        {
            if (!visible.ContainsKey(key))
            {
                SafeInvoke(() => action.OnDisappear(context));
            }
        }

        foreach (var (key, (action, context)) in visible)
        {
            if (!_appeared.ContainsKey(key))
            {
                SafeInvoke(() => action.OnAppear(context));
            }
        }

        _appeared = visible;
    }

    private void RenderDirtyDevices()
    {
        if (!_pluginsInitialized || _dirtyDevices.Count == 0)
        {
            return;
        }

        UpdateAppearances();

        foreach (var deviceId in _dirtyDevices)
        {
            RenderDevice(deviceId);
        }

        _dirtyDevices.Clear();
    }

    private void RenderDevice(string deviceId)
    {
        if (!_devicesById.TryGetValue(deviceId, out var entry))
        {
            return;
        }

        // Rendered even while the device is unplugged: the editor shows the same frames as a preview.
        var connected = entry.Device.IsConnected && !_pausedDevices.Contains(deviceId);
        var profile = GetActiveProfile(deviceId);
        var layoutBounds = ControlRenderer.LayoutBounds(entry.Device);
        var clock = _clock.Elapsed;
        var pushed = 0;

        if (connected)
        {
            PushBackdrop(deviceId, entry, profile);
        }

        foreach (var control in entry.Device.Controls)
        {
            if (!control.HasImageOutput)
            {
                continue;
            }

            var binding = FindBinding(profile, control.Id);
            var action = binding is null ? null : _actionRouter.FindAction(binding.Action);
            var linked = FindLinkedBinding(entry.Device, profile, control);
            var renderer = GetRenderer(control.RenderWidth, control.RenderHeight);

            ReadOnlySpan<byte> pixels;
            try
            {
                pixels = renderer.Render(canvas =>
                {
                    var context = new SkiaRenderContext(canvas, renderer.Width, renderer.Height, deviceId, control.Id, _variableStore, _drawingResources, binding?.Action.EffectiveSettings, clock, profile?.Id ?? string.Empty);
                    ControlRenderer.Draw(context, binding, action, profile?.Background, layoutBounds, control.Bounds, linked);
                });
            }
            catch (Exception ex)
            {
                Log("Error", $"Failed to render '{deviceId}/{control.Id}': {ex.Message}");
                continue;
            }

            var key = (deviceId, control.Id);
            if (_lastFrames.TryGetValue(key, out var lastFrame) && pixels.SequenceEqual(lastFrame))
            {
                continue;
            }

            if (connected)
            {
                if (!entry.Plugin.Output.TryPushImage(deviceId, control.Id, pixels, renderer.Width, renderer.Height, out var error))
                {
                    _lastFrames.Remove(key);
                    Log("Warning", $"Could not update '{deviceId}/{control.Id}': {error}");
                    Count(deviceId, statistics => statistics with { FrameFailures = statistics.FrameFailures + 1, LastError = error, LastErrorTime = DateTimeOffset.Now });
                    RaiseFrame(deviceId, control.Id, renderer.Width, renderer.Height, pixels);
                    continue;
                }

                pushed++;
                Count(deviceId, static statistics => statistics with { FramesSent = statistics.FramesSent + 1 });
            }

            if (lastFrame is null || lastFrame.Length != pixels.Length)
            {
                lastFrame = new byte[pixels.Length];
                _lastFrames[key] = lastFrame;
            }

            pixels.CopyTo(lastFrame);
            RaiseFrame(deviceId, control.Id, renderer.Width, renderer.Height, pixels);
        }

        if (pushed > 0)
        {
            Log("Debug", $"Pushed {pushed} changed control image(s) to '{deviceId}'.");
        }
    }

    /// <summary>
    /// Fills the screen around the control images with the profile's background color. Without
    /// it a device that stores a picture of its own (the N4 Pro's idle screen) keeps showing that
    /// picture between the keys, whatever the profile is.
    /// </summary>
    private void PushBackdrop(string deviceId, DeviceEntry entry, DeviceProfile? profile)
    {
        if (entry.Plugin.Output is not IDeviceBackdropOutput output)
        {
            return;
        }

        var color = ProfileBackdrop.GetColor(profile?.Background);
        if (_lastBackdrops.TryGetValue(deviceId, out var last) && last == color)
        {
            return;
        }

        // Remembered even when it fails, so a device that can't do this isn't asked on every frame.
        _lastBackdrops[deviceId] = color;
        if (!output.TrySetBackdrop(deviceId, color.Red, color.Green, color.Blue, out var error))
        {
            Log("Warning", $"Could not set the background of '{deviceId}': {error}");
            return;
        }

        // The device may have drawn it over the control images: send those again.
        ForgetControlFrames(deviceId);
    }

    private void RaiseFrame(string deviceId, string controlId, int width, int height, ReadOnlySpan<byte> pixels)
    {
        if (ControlFrameRendered is { } handler)
        {
            var frame = new ControlFrame(deviceId, controlId, width, height, pixels.ToArray());
            SafeInvoke(() => handler(frame));
        }
    }

    private SurfaceRenderer GetRenderer(int width, int height)
    {
        if (!_renderers.TryGetValue((width, height), out var renderer))
        {
            renderer = new SurfaceRenderer(width, height);
            _renderers[(width, height)] = renderer;
        }

        return renderer;
    }

    // ---------------------------------------------------------------- lights

    private void HandleLightFeedback(DeviceProfile profile, string controlId)
    {
        var binding = profile.Bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.ControlId, controlId, StringComparison.OrdinalIgnoreCase));
        var feedback = binding?.Appearance.RgbFeedback;
        if (feedback is null || !TryGetLightControl(profile.DeviceId, controlId, out _))
        {
            return;
        }

        var key = (profile.DeviceId, controlId);
        var version = _lightVersions.GetValueOrDefault(key) + 1;
        _lightVersions[key] = version;

        // The active color holds the light: an idle effect waits until it's over.
        _lightHeldUntil[key] = feedback.ActiveDuration <= 0 ? long.MaxValue : _clock.ElapsedMilliseconds + feedback.ActiveDuration;
        ShowLights(profile.DeviceId, [(controlId, feedback.GetActiveRgb())]);

        if (feedback.ActiveDuration <= 0)
        {
            return;
        }

        // Pulse: blink active/idle a few times within the active duration, then back to the idle light.
        var steps = feedback.PulseOnActive ? 4 : 1;
        var stepDuration = Math.Max(30, feedback.ActiveDuration / steps);
        for (var step = 1; step <= steps; step++)
        {
            var last = step == steps;
            var blinkOff = !last && step % 2 == 1;
            _ = Task.Delay(stepDuration * step).ContinueWith(
                _ => Enqueue(() =>
                {
                    // A newer press restarted the feedback: let that one finish instead.
                    if (_lightVersions.GetValueOrDefault(key) != version || !TryGetLightControl(key.DeviceId, key.controlId, out var unused))
                    {
                        return;
                    }

                    if (last)
                    {
                        _lightHeldUntil.Remove(key);
                    }

                    var color = last || blinkOff ? IdleLightColor(key.DeviceId, key.controlId) : feedback.GetActiveRgb();
                    ShowLights(key.DeviceId, [(key.controlId, color)]);
                }),
                TaskScheduler.Default);
        }
    }

    /// <summary>
    /// Every LED of the device back to its idle light: the binding's own (Customize → Light), else
    /// the device's default (<see cref="SetDeviceLight"/>), else dark. Also while the device is
    /// unplugged, so the editor shows the lights.
    /// </summary>
    private void ApplyIdleLights(string deviceId)
    {
        if (!_pluginsInitialized || !_devicesById.TryGetValue(deviceId, out var entry))
        {
            return;
        }

        var lights = new List<(string, (byte, byte, byte))>();
        foreach (var control in entry.Device.Controls.Where(static control => control.HasLight))
        {
            var key = (deviceId, control.Id);
            _lightVersions[key] = _lightVersions.GetValueOrDefault(key) + 1;
            _lightHeldUntil.Remove(key);
            lights.Add((control.Id, IdleLightColor(deviceId, control.Id)));
        }

        ShowLights(deviceId, lights);
    }

    /// <summary>Moves animated idle lights (breathing, rainbow) on; called on the animation tick.</summary>
    private void AnimateLights()
    {
        if (!_pluginsInitialized)
        {
            return;
        }

        var now = _clock.ElapsedMilliseconds;
        var refreshSteady = now - _lastSteadyLightRefresh >= SteadyLightRefreshMilliseconds;
        if (refreshSteady)
        {
            _lastSteadyLightRefresh = now;
        }

        foreach (var (deviceId, entry) in _devicesById)
        {
            List<(string, (byte, byte, byte))>? lights = null;
            foreach (var control in entry.Device.Controls)
            {
                var key = (deviceId, control.Id);
                if (!control.HasLight || _lightHeldUntil.GetValueOrDefault(key) > now)
                {
                    continue;
                }

                if (!LightEffects.IsAnimated(IdleLightStyle(deviceId, control.Id)?.Effect))
                {
                    if (!refreshSteady || !entry.Device.IsConnected)
                    {
                        continue;
                    }

                    // Sent again even though it didn't change.
                    _lightsShown.Remove(key);
                }

                (lights ??= []).Add((control.Id, IdleLightColor(deviceId, control.Id)));
            }

            if (lights is not null)
            {
                ShowLights(deviceId, lights);
            }
        }
    }

    private LightStyle? IdleLightStyle(string deviceId, string controlId)
    {
        if (GetActiveProfile(deviceId)?.Bindings.FirstOrDefault(candidate =>
                string.Equals(candidate.ControlId, controlId, StringComparison.OrdinalIgnoreCase))?.Appearance.RgbFeedback?.IdleStyle is { } own)
        {
            return own;
        }

        // The device's light, or this LED's own part of it (Knob lights → each knob its own effect).
        return _deviceLights.GetValueOrDefault(deviceId) is { } device && _devicesById.TryGetValue(deviceId, out var entry)
            ? device.ForLed(entry.Device.Controls.Where(static control => control.HasLight).ToList()
                .FindIndex(control => string.Equals(control.Id, controlId, StringComparison.OrdinalIgnoreCase)))
            : _deviceLights.GetValueOrDefault(deviceId);
    }

    private (byte R, byte G, byte B) IdleLightColor(string deviceId, string controlId)
    {
        if (IdleLightStyle(deviceId, controlId) is not { } style || !_devicesById.TryGetValue(deviceId, out var entry))
        {
            return (0, 0, 0);
        }

        // The LED's place among the device's lights, for effects that run across them.
        var lights = entry.Device.Controls.Where(static control => control.HasLight).ToList();
        var phase = lights.FindIndex(control => string.Equals(control.Id, controlId, StringComparison.OrdinalIgnoreCase));
        return LightEffectMath.ColorAt(style, _clock.Elapsed, Math.Max(0, phase), lights.Count);
    }

    private bool TryGetLightControl(string deviceId, string controlId, out DeviceEntry entry)
    {
        if (_pluginsInitialized &&
            _devicesById.TryGetValue(deviceId, out entry!) &&
            entry.Device.FindControl(controlId)?.HasLight == true)
        {
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>
    /// Shows LED colors: tells the editor (<see cref="LightChanged"/>) and, when the device is
    /// connected, sends the ones that changed, in one go where the device can (<see cref="IDeviceLightBatchOutput"/>).
    /// </summary>
    private void ShowLights(string deviceId, IReadOnlyList<(string ControlId, (byte R, byte G, byte B) Color)> lights)
    {
        if (!_devicesById.TryGetValue(deviceId, out var entry))
        {
            return;
        }

        var changed = new List<LightColor>();
        foreach (var (controlId, color) in lights)
        {
            var key = (deviceId, controlId);
            if (_lightsShown.TryGetValue(key, out var shown) && shown == color)
            {
                continue;
            }

            _lightsShown[key] = color;
            changed.Add(new LightColor(controlId, color.R, color.G, color.B));
            if (LightChanged is { } handler)
            {
                var state = new LightState(deviceId, controlId, color.R, color.G, color.B);
                SafeInvoke(() => handler(state));
            }
        }

        if (changed.Count == 0 || !entry.Device.IsConnected || _pausedDevices.Contains(deviceId))
        {
            return;
        }

        if (entry.Plugin.Output is IDeviceLightBatchOutput batch && changed.Count > 1)
        {
            if (!batch.TrySetLights(deviceId, changed, out var batchError))
            {
                Log("Warning", $"Could not set the lights of '{deviceId}': {batchError}");
                Count(deviceId, statistics => statistics with { LightFailures = statistics.LightFailures + 1, LastError = batchError, LastErrorTime = DateTimeOffset.Now });
            }

            return;
        }

        foreach (var light in changed)
        {
            if (!entry.Plugin.Output.TrySetLight(deviceId, light.ControlId, light.Red, light.Green, light.Blue, out var error))
            {
                Log("Warning", $"Could not set the light of '{deviceId}/{light.ControlId}': {error}");
                Count(deviceId, statistics => statistics with { LightFailures = statistics.LightFailures + 1, LastError = error, LastErrorTime = DateTimeOffset.Now });
            }
        }
    }

    // ---------------------------------------------------------------- variables, status, log

    private void PublishAppearanceState(DeviceProfile profile)
    {
        foreach (var binding in profile.Bindings)
        {
            _variableStore.Publish(VariableKeys.For(profile.DeviceId, binding.ControlId, VariableKeys.AppearanceText), binding.Appearance.Text ?? string.Empty);
            _variableStore.Publish(VariableKeys.For(profile.DeviceId, binding.ControlId, VariableKeys.AppearanceImage), binding.Appearance.ImagePath ?? string.Empty);
            _variableStore.Publish(VariableKeys.For(profile.DeviceId, binding.ControlId, VariableKeys.AppearanceBackground), binding.Appearance.BackgroundColor ?? string.Empty);
            _variableStore.Publish(VariableKeys.For(profile.DeviceId, binding.ControlId, VariableKeys.AppearanceIcon), binding.Appearance.IconPath ?? string.Empty);
            _variableStore.Publish(VariableKeys.For(profile.DeviceId, binding.ControlId, VariableKeys.AppearanceTile), binding.Appearance.ShowTile ? "true" : string.Empty);
            _variableStore.Publish(VariableKeys.For(profile.DeviceId, binding.ControlId, VariableKeys.AppearanceTileStyle), binding.Appearance.Tile ?? new TileStyle());
        }
    }

    private void PublishHardwareIds(DeviceDefinition device)
    {
        foreach (var control in device.Controls.Where(static control => control.HardwareId is not null))
        {
            _variableStore.Publish(VariableKeys.For(device.Id, control.Id, VariableKeys.HardwareId), control.HardwareId!);
        }
    }

    private void ReportConnectionStatus(DeviceConnectionStatus status)
    {
        _connectionStatuses[status.DeviceId] = status;
        SafeInvoke(() => ConnectionStatusChanged?.Invoke(status));
    }

    private void Log(string level, string message)
    {
        var entry = new RuntimeLogEntry(DateTimeOffset.UtcNow, level, message);
        lock (_logLock)
        {
            _runtimeLogs.Enqueue(entry);
            while (_runtimeLogs.Count > MaxLogEntries)
            {
                _runtimeLogs.Dequeue();
            }
        }

        SafeInvoke(() => RuntimeLogReceived?.Invoke(entry));
    }

    private static void SafeInvoke(Action raise)
    {
        try
        {
            raise();
        }
        catch
        {
            // A misbehaving subscriber must never take down the runtime worker or a native callback.
        }
    }

    private static ActionRouter CreateRouter(IVariableStore variableStore, IEnumerable<IApplicationPlugin> applicationPlugins, out ActionFailureRelay relay)
    {
        var localRelay = new ActionFailureRelay();
        relay = localRelay;
        return new ActionRouter(variableStore, applicationPlugins, localRelay.Report);
    }

    // ---------------------------------------------------------------- live actions

    /// <summary>Redraws controls whose action has a <see cref="IDeckAction.RefreshInterval"/> (clocks, system info).</summary>
    private async Task RunLiveRefreshAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(AnimationInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                Enqueue(MarkDueLiveControlsDirty);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Moves light effects on at about 30 frames a second, so fades look smooth.</summary>
    private async Task RunLightLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(LightFrameInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                // Skip a frame while the previous one is still queued (a slow device), instead of piling up.
                if (Interlocked.Exchange(ref _lightFrameQueued, 1) == 0)
                {
                    Enqueue(() =>
                    {
                        Volatile.Write(ref _lightFrameQueued, 0);
                        AnimateLights();
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void MarkDueLiveControlsDirty()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (deviceId, entry) in _devicesById)
        {
            if (GetActiveProfile(deviceId) is not { } profile)
            {
                continue;
            }

            // An animated profile background changes every control.
            if (_drawingResources.IsAnimated(profile.Background?.ImagePath))
            {
                var backgroundKey = (deviceId, "*background*");
                if (!_nextLiveRender.TryGetValue(backgroundKey, out var backgroundDue) || backgroundDue <= now)
                {
                    _nextLiveRender[backgroundKey] = now + AnimationInterval;
                    _dirtyDevices.Add(deviceId);
                }
            }

            foreach (var binding in profile.Bindings)
            {
                var interval = _actionRouter.FindAction(binding.Action)?.RefreshInterval;
                if (_drawingResources.IsAnimated(binding.Appearance.ImagePath))
                {
                    interval = interval is null || interval > AnimationInterval ? AnimationInterval : interval;
                }

                if (interval is null)
                {
                    continue;
                }

                var key = (deviceId, binding.ControlId);
                if (_nextLiveRender.TryGetValue(key, out var due) && due > now)
                {
                    continue;
                }

                _nextLiveRender[key] = now + (interval.Value < AnimationInterval ? AnimationInterval : interval.Value);
                _dirtyDevices.Add(deviceId);
            }
        }
    }

    /// <summary>What actions may ask of the host. Everything is queued onto the worker.</summary>
    private sealed class ActionHostServices(DeviceHostService host) : IActionHost
    {
        public void RequestRender(string deviceId) => host.Enqueue(() => host._dirtyDevices.Add(deviceId));

        public void ActivateProfile(string deviceId, string profileIdOrName) =>
            host.Enqueue(() => host.ActivateProfileByIdOrName(deviceId, profileIdOrName));

        public void ActivateRelativeProfile(string deviceId, int offset) =>
            host.Enqueue(() => host.ActivateRelativeProfile(deviceId, offset));

        public void ActivateRelativePage(string deviceId, int offset) =>
            host.Enqueue(() => host.ActivateRelativePage(deviceId, offset));

        public void ActivatePage(string deviceId, int number) =>
            host.Enqueue(() => host.ActivatePageNumber(deviceId, number));

        public void OpenFolder(string deviceId, string folderId) =>
            host.Enqueue(() => host.OpenFolder(deviceId, folderId));

        public void CloseFolder(string deviceId) =>
            host.Enqueue(() => host.CloseFolder(deviceId));
    }

    private sealed record DeviceEntry(DeviceDefinition Device, IDevicePlugin Plugin);

    /// <summary>Lets the router report action failures to a host that doesn't exist yet when the router is built.</summary>
    private sealed class ActionFailureRelay
    {
        public DeviceHostService? Target { get; set; }

        public void Report(ActionReference action, Exception exception) =>
            Target?.Log("Error", $"Action '{action.ActionName}' ({action.PluginId}:{action.ActionId}) failed: {exception.Message}");
    }
}
