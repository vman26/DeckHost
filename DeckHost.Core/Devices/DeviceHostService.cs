using DeckHost.Contracts;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Models;
using DeckHost.Core.Rendering;
using System.Collections.Concurrent;
using System.Threading.Channels;

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
/// </summary>
public sealed class DeviceHostService : IDeviceHostService, IDeviceInputSink
{
    private const int MaxLogEntries = 500;

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
    private readonly Dictionary<(int Width, int Height), SurfaceRenderer> _renderers = [];
    private readonly Dictionary<(string DeviceId, string ControlId), int> _lightVersions = [];
    private readonly List<TaskCompletionSource> _idleWaiters = [];
    private readonly SkiaDrawingResources _drawingResources = new();
    private bool _pluginsInitialized;

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
            var context = new DevicePluginContext(this, Log, ReportConnectionStatus);
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
        var activeProfile = GetActiveProfile(inputEvent.DeviceId);
        if (activeProfile is null)
        {
            Log("Debug", $"Input from '{inputEvent.DeviceId}/{inputEvent.ControlId}' ignored: no active profile.");
            return;
        }

        var dispatched = _actionRouter.Dispatch(activeProfile, inputEvent);
        Log("Debug", $"Input {inputEvent.EventType} ({inputEvent.Value}) on '{inputEvent.ControlId}' {(dispatched ? "dispatched" : "had no matching action")}.");

        if (dispatched)
        {
            HandleLightFeedback(activeProfile, inputEvent.ControlId);
        }

        // Actions can change any variable a renderer reads; unchanged controls are skipped at push time.
        _dirtyDevices.Add(inputEvent.DeviceId);
    }

    private void ApplyProfiles(IReadOnlyList<DeviceProfile> profiles)
    {
        _profilesById.Clear();
        foreach (var profile in profiles)
        {
            _profilesById[profile.Id] = profile;
        }

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

        var fallback = _profilesById.Values.FirstOrDefault(profile =>
            string.Equals(profile.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
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

    private void ForgetFrames(string deviceId)
    {
        foreach (var key in _lastFrames.Keys.Where(key => string.Equals(key.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _lastFrames.Remove(key);
        }
    }

    // ---------------------------------------------------------------- rendering

    private void RenderDirtyDevices()
    {
        if (!_pluginsInitialized || _dirtyDevices.Count == 0)
        {
            return;
        }

        foreach (var deviceId in _dirtyDevices)
        {
            RenderDevice(deviceId);
        }

        _dirtyDevices.Clear();
    }

    private void RenderDevice(string deviceId)
    {
        if (!_devicesById.TryGetValue(deviceId, out var entry) || !entry.Device.IsConnected)
        {
            return;
        }

        var profile = GetActiveProfile(deviceId);
        var pushed = 0;

        foreach (var control in entry.Device.Controls)
        {
            if (!control.HasImageOutput)
            {
                continue;
            }

            var binding = profile?.Bindings.FirstOrDefault(candidate =>
                string.Equals(candidate.ControlId, control.Id, StringComparison.OrdinalIgnoreCase));
            var action = binding is null ? null : _actionRouter.FindAction(binding.Action);
            var renderer = GetRenderer(control.RenderWidth, control.RenderHeight);

            ReadOnlySpan<byte> pixels;
            try
            {
                pixels = renderer.Render(canvas =>
                {
                    var context = new SkiaRenderContext(canvas, renderer.Width, renderer.Height, deviceId, control.Id, _variableStore, _drawingResources);
                    ControlRenderer.Draw(context, binding, action);
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

            if (!entry.Plugin.Output.TryPushImage(deviceId, control.Id, pixels, renderer.Width, renderer.Height, out var error))
            {
                _lastFrames.Remove(key);
                Log("Warning", $"Could not update '{deviceId}/{control.Id}': {error}");
                continue;
            }

            if (lastFrame is null || lastFrame.Length != pixels.Length)
            {
                lastFrame = new byte[pixels.Length];
                _lastFrames[key] = lastFrame;
            }

            pixels.CopyTo(lastFrame);
            pushed++;
        }

        if (pushed > 0)
        {
            Log("Debug", $"Pushed {pushed} changed control image(s) to '{deviceId}'.");
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
        if (feedback is null || !TryGetLightControl(profile.DeviceId, controlId, out var entry))
        {
            return;
        }

        var key = (profile.DeviceId, controlId);
        var version = _lightVersions.GetValueOrDefault(key) + 1;
        _lightVersions[key] = version;

        SetLight(entry, profile.DeviceId, controlId, feedback.GetActiveRgb());

        if (feedback.ActiveDuration <= 0)
        {
            return;
        }

        // Pulse: blink active/idle a few times within the active duration, then settle on idle.
        var steps = feedback.PulseOnActive ? 4 : 1;
        var stepDuration = Math.Max(30, feedback.ActiveDuration / steps);
        for (var step = 1; step <= steps; step++)
        {
            var color = step == steps || step % 2 == 1 ? feedback.GetIdleRgb() : feedback.GetActiveRgb();
            _ = Task.Delay(stepDuration * step).ContinueWith(
                _ => Enqueue(() =>
                {
                    // A newer press restarted the feedback: let that one finish instead.
                    if (_lightVersions.GetValueOrDefault(key) == version && TryGetLightControl(key.DeviceId, key.controlId, out var current))
                    {
                        SetLight(current, key.DeviceId, key.controlId, color);
                    }
                }),
                TaskScheduler.Default);
        }
    }

    private void ApplyIdleLights(string deviceId)
    {
        if (!_pluginsInitialized || !_devicesById.TryGetValue(deviceId, out var entry) || !entry.Device.IsConnected)
        {
            return;
        }

        var profile = GetActiveProfile(deviceId);
        foreach (var control in entry.Device.Controls.Where(static control => control.HasLight))
        {
            var key = (deviceId, control.Id);
            _lightVersions[key] = _lightVersions.GetValueOrDefault(key) + 1;

            var feedback = profile?.Bindings.FirstOrDefault(candidate =>
                string.Equals(candidate.ControlId, control.Id, StringComparison.OrdinalIgnoreCase))?.Appearance.RgbFeedback;
            SetLight(entry, deviceId, control.Id, feedback?.GetIdleRgb() ?? ((byte)0, (byte)0, (byte)0));
        }
    }

    private bool TryGetLightControl(string deviceId, string controlId, out DeviceEntry entry)
    {
        if (_pluginsInitialized &&
            _devicesById.TryGetValue(deviceId, out entry!) &&
            entry.Device.IsConnected &&
            entry.Device.FindControl(controlId)?.HasLight == true)
        {
            return true;
        }

        entry = null!;
        return false;
    }

    private void SetLight(DeviceEntry entry, string deviceId, string controlId, (byte R, byte G, byte B) color)
    {
        if (!entry.Plugin.Output.TrySetLight(deviceId, controlId, color.R, color.G, color.B, out var error))
        {
            Log("Warning", $"Could not set the light of '{deviceId}/{controlId}': {error}");
        }
    }

    // ---------------------------------------------------------------- variables, status, log

    private void PublishAppearanceState(DeviceProfile profile)
    {
        foreach (var binding in profile.Bindings)
        {
            _variableStore.Publish(VariableKeys.For(profile.DeviceId, binding.ControlId, VariableKeys.AppearanceText), binding.Appearance.Text ?? string.Empty);
            _variableStore.Publish(VariableKeys.For(profile.DeviceId, binding.ControlId, VariableKeys.AppearanceImage), binding.Appearance.ImagePath ?? string.Empty);
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

    private sealed record DeviceEntry(DeviceDefinition Device, IDevicePlugin Plugin);

    /// <summary>Lets the router report action failures to a host that doesn't exist yet when the router is built.</summary>
    private sealed class ActionFailureRelay
    {
        public DeviceHostService? Target { get; set; }

        public void Report(ActionReference action, Exception exception) =>
            Target?.Log("Error", $"Action '{action.ActionName}' ({action.PluginId}:{action.ActionId}) failed: {exception.Message}");
    }
}
