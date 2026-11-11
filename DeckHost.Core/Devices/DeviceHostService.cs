using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices.Interfaces;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Models;
using DeckHost.Core.Rendering;
using DeckHost.Device.MiraBox;
using System.Runtime.InteropServices;

namespace DeckHost.Core.Devices;

public sealed class DeviceHostService : IDeviceHostService, IDeviceInputSink
{
    private readonly IVariableStore _variableStore;
    private readonly IReadOnlyList<IDevicePlugin> _devicePlugins;
    private readonly IActionRouter _actionRouter;
    private readonly Dictionary<string, DeviceProfile> _profilesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _activeProfilesByDeviceId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceDefinition> _devicesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IDeviceDisplaySession> _displaySessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RuntimeLogEntry> _runtimeLogs = [];
    private readonly Dictionary<string, DeviceConnectionStatus> _connectionStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyCollection<DeviceDefinition> _devices;
    private readonly DevicePluginContext _pluginContext;
    private bool _started;

    public event Action<RuntimeLogEntry>? RuntimeLogReceived;
    public event Action<DeviceConnectionStatus>? ConnectionStatusChanged;

    public DeviceHostService(
        IEnumerable<IDevicePlugin> devicePlugins,
        IEnumerable<IApplicationPlugin> applicationPlugins,
        IVariableStore variableStore)
        : this(devicePlugins, new ActionRouter(variableStore, applicationPlugins), variableStore)
    {
    }

    public DeviceHostService(IEnumerable<IDevicePlugin> devicePlugins, IActionRouter actionRouter, IVariableStore variableStore)
    {
        ArgumentNullException.ThrowIfNull(devicePlugins);
        ArgumentNullException.ThrowIfNull(actionRouter);
        ArgumentNullException.ThrowIfNull(variableStore);

        _variableStore = variableStore;
        _devicePlugins = devicePlugins.ToList();
        _actionRouter = actionRouter;
        _devices = _devicePlugins.SelectMany(static plugin => plugin.Devices).ToArray();
        foreach (var device in _devices)
        {
            _devicesById[device.Id] = device;
            UpdateConnectionStatus(device.Id, "Discovered", "Device definition loaded.");
            TryCreateDisplaySession(device);
        }

        _pluginContext = new DevicePluginContext(this);
    }

    public IReadOnlyCollection<DeviceDefinition> Devices => _devices;

    public IReadOnlyCollection<DeviceProfile> Profiles => _profilesById.Values.ToArray();

    public IReadOnlyCollection<RuntimeLogEntry> RuntimeLogs => _runtimeLogs.AsReadOnly();

    public IReadOnlyCollection<DeviceConnectionStatus> ConnectionStatuses => _connectionStatuses.Values.ToArray();

    public DeviceProfile? GetActiveProfile(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        if (_activeProfilesByDeviceId.TryGetValue(deviceId, out var profileId) &&
            _profilesById.TryGetValue(profileId, out var activeProfile))
        {
            return activeProfile;
        }

        var fallbackProfile = _profilesById.Values.FirstOrDefault(profile =>
            string.Equals(profile.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));

        if (fallbackProfile is not null)
        {
            _activeProfilesByDeviceId[deviceId] = fallbackProfile.Id;
        }

        return fallbackProfile;
    }

    public void SetProfiles(IEnumerable<DeviceProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        _profilesById.Clear();
        _activeProfilesByDeviceId.Clear();

        foreach (var profile in profiles)
        {
            _profilesById[profile.Id] = profile;
            if (!_activeProfilesByDeviceId.ContainsKey(profile.DeviceId))
            {
                _activeProfilesByDeviceId[profile.DeviceId] = profile.Id;
            }
        }

        Log("Info", $"Loaded {_profilesById.Count} runtime profile(s).");

        if (!_started)
        {
            Log("Info", "Queued runtime profiles; device plugins are not initialized yet.");
            return;
        }

        foreach (var activeProfile in _activeProfilesByDeviceId.Values
                     .Select(profileId => _profilesById.TryGetValue(profileId, out var profile) ? profile : null)
                     .Where(static profile => profile is not null))
        {
            RenderProfileOutputs(activeProfile!);
        }
    }

    public bool SetActiveProfile(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        if (!_profilesById.TryGetValue(profileId, out var profile))
        {
            return false;
        }

        _activeProfilesByDeviceId[profile.DeviceId] = profile.Id;
        Log("Info", $"Activated profile '{profile.Name}' for device '{profile.DeviceId}'.");

        if (_started)
        {
            RenderProfileOutputs(profile);
        }
        return true;
    }

    public bool DispatchInput(DeviceInputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);

        var activeProfile = GetActiveProfile(inputEvent.DeviceId);
        if (activeProfile is null)
        {
            Log("Warning", $"Input from '{inputEvent.DeviceId}/{inputEvent.ControlId}' ignored because no active profile exists.");
            return false;
        }

        var dispatched = _actionRouter.Dispatch(activeProfile, inputEvent);
        Log(dispatched ? "Info" : "Warning", $"Input {inputEvent.EventType} on '{inputEvent.DeviceId}/{inputEvent.ControlId}' {(dispatched ? "dispatched" : "had no matching action")}." );

        // Handle RGB feedback for the control
        HandleRgbFeedback(activeProfile, inputEvent.ControlId);

        PublishAppearanceState(activeProfile);
        RenderProfileOutputs(activeProfile);
        return dispatched;
    }

    private void HandleRgbFeedback(DeviceProfile profile, string controlId)
    {
        var binding = profile.Bindings.FirstOrDefault(b =>
            string.Equals(b.ControlId, controlId, StringComparison.OrdinalIgnoreCase));

        if (binding?.Appearance?.RgbFeedback is null)
            return;

        if (!controlId.StartsWith("key.", StringComparison.OrdinalIgnoreCase))
            return;

        if (!int.TryParse(controlId.AsSpan(4), out var keyIndex))
            return;

        // Map logical index to actual SDK key ID
        if (!_devicesById.TryGetValue(profile.DeviceId, out var device))
            return;

        if (device.Capabilities is null)
            return;

        var sdkKeyId = device.GetSdkKeyId(keyIndex);

        var feedback = binding.Appearance.RgbFeedback;
        var (r, g, b) = feedback.GetActiveRgb();

        // Send active color to LED
        var result = MiraBoxNative.SetKeyRGB(0, sdkKeyId, r, g, b);
        if (result == 0)
        {
            Log("Debug", $"RGB feedback applied to key {keyIndex} (SDK ID {sdkKeyId}): RGB({r},{g},{b})");

            // Schedule return to idle color after active duration
            if (feedback.ActiveDuration > 0)
            {
                _ = Task.Delay(feedback.ActiveDuration).ContinueWith(_ =>
                {
                    var (idleR, idleG, idleB) = feedback.GetIdleRgb();
                    MiraBoxNative.SetKeyRGB(0, sdkKeyId, idleR, idleG, idleB);
                });
            }
        }
        else
        {
            Log("Warning", $"Failed to apply RGB feedback to key {keyIndex} (SDK ID {sdkKeyId}): error code {result}");
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        foreach (var plugin in _devicePlugins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await plugin.InitializeAsync(_pluginContext, cancellationToken).ConfigureAwait(false);
                foreach (var device in plugin.Devices)
                {
                    _devicesById[device.Id] = device;
                    TryCreateDisplaySession(device);
                    UpdateConnectionStatus(device.Id, "Initialized", $"{plugin.Name} initialized successfully.");
                }

                Log("Info", $"Initialized device plugin '{plugin.Name}'.");
            }
            catch (Exception ex)
            {
                foreach (var device in plugin.Devices)
                {
                    UpdateConnectionStatus(device.Id, "Failed", ex.Message);
                }

                Log("Error", $"Failed to initialize device plugin '{plugin.Name}': {ex.Message}");
            }
        }

        _started = true;

        foreach (var activeProfile in _activeProfilesByDeviceId.Values
                     .Select(profileId => _profilesById.TryGetValue(profileId, out var profile) ? profile : null)
                     .Where(static profile => profile is not null))
        {
            RenderProfileOutputs(activeProfile!);
        }

        Log("Info", "Initial device profile render completed.");
    }

    private void PublishAppearanceState(DeviceProfile profile)
    {
        foreach (var binding in profile.Bindings)
        {
            _variableStore.Publish($"devices.{profile.DeviceId}.{binding.ControlId}.appearance.text", binding.Appearance.Text ?? string.Empty);
            _variableStore.Publish($"devices.{profile.DeviceId}.{binding.ControlId}.appearance.image", binding.Appearance.ImagePath ?? string.Empty);
        }
    }

    private void PublishSdkKeyMapping(string deviceId, DeviceDefinition device)
    {
        foreach (var control in device.Controls.Where(static c => c.Type == ControlType.Key))
        {
            if (!control.Id.StartsWith("key.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(control.Id.AsSpan(4), out var keyIndex))
            {
                continue;
            }

            var sdkKeyId = device.Capabilities is null ? keyIndex : device.GetSdkKeyId(keyIndex);
            _variableStore.Publish($"devices.{deviceId}.{control.Id}.sdkKeyId", sdkKeyId);
        }
    }

    private void RenderProfileOutputs(DeviceProfile profile)
    {
        if (!_devicesById.TryGetValue(profile.DeviceId, out var deviceDefinition) ||
            !_displaySessions.TryGetValue(profile.DeviceId, out var displaySession))
        {
            return;
        }

        PublishAppearanceState(profile);
        PublishSdkKeyMapping(profile.DeviceId, deviceDefinition);

        // Use per-control rendering strategy
        var previewAction = new ProfilePreviewAction(deviceDefinition, profile);
        var renderJobs = previewAction.GetControlRenderJobs();

        foreach (var job in renderJobs)
        {
            RenderControlAndPush(displaySession, profile.DeviceId, job);
        }

        Log("Info", $"Rendered {renderJobs.Count} control outputs for device '{profile.DeviceId}'.");
    }

    private void RenderControlAndPush(IDeviceDisplaySession displaySession, string deviceId, ControlRenderJob job)
    {
        try
        {
            var renderContext = new ActionContext(deviceId, job.ControlId, _variableStore);

            if (job.ControlType == ControlType.TouchSurface)
            {
                // Touch display uses legacy display session rendering
                displaySession.RenderAndPush(job.RenderAction, renderContext);
            }
            else if (job.ControlType == ControlType.Key)
            {
                // Per-key rendering via native bridge
                RenderKeyImage(deviceId, job);
            }
            // Encoders skip visual rendering for now (RGB feedback only)
        }
        catch (Exception ex)
        {
            Log("Error", $"Failed to render {job.ControlType} '{job.ControlId}': {ex.Message}");
        }
    }

    private void RenderKeyImage(string deviceId, ControlRenderJob job)
    {
        // Extract key index from control ID (format: "key.0", "key.1", etc.)
        if (!job.ControlId.StartsWith("key.", StringComparison.OrdinalIgnoreCase))
            return;

        if (!int.TryParse(job.ControlId.AsSpan(4), out var keyIndex))
            return;

        // Map logical index to actual SDK key ID using device capabilities
        if (!_devicesById.TryGetValue(deviceId, out var device))
            return;

        if (device.Capabilities is null)
            return;

        var sdkKeyId = device.GetSdkKeyId(keyIndex);

        // Create a temporary display session for this key's specific dimensions
        using var keyRenderer = new SurfaceRenderer(job.Width, job.Height);
        ReadOnlySpan<byte> pixelData = keyRenderer.Render(canvas =>
        {
            var renderContext = new SkiaRenderContext(canvas, job.Width, job.Height, 
                new ActionContext(deviceId, job.ControlId, _variableStore));
            job.RenderAction.Render(renderContext);
        });

        if (!pixelData.IsEmpty)
        {
            ref byte bufferRef = ref MemoryMarshal.GetReference(pixelData);
            var result = MiraBoxNative.SendKeyImage(0, sdkKeyId, in bufferRef, pixelData.Length, job.Width, job.Height);
            if (result != 0)
            {
                Log("Warning", $"Failed to render key {keyIndex} (SDK ID {sdkKeyId}): error code {result}");
            }
        }
    }

    private void TryCreateDisplaySession(DeviceDefinition device)
    {
        var width = device.ScreenWidth > 0 ? device.ScreenWidth : 800;
        var height = device.ScreenHeight > 0 ? device.ScreenHeight : 480;

        _displaySessions[device.Id] = new DeviceDisplaySession(0, width, height);
        Log("Info", $"Created display session for '{device.Id}' ({width}x{height}).");
    }

    private void UpdateConnectionStatus(string deviceId, string state, string message)
    {
        var status = new DeviceConnectionStatus(deviceId, state, message);
        _connectionStatuses[deviceId] = status;
        ConnectionStatusChanged?.Invoke(status);
    }

    private void Log(string level, string message)
    {
        var entry = new RuntimeLogEntry(DateTimeOffset.UtcNow, level, message);
        _runtimeLogs.Add(entry);
        RuntimeLogReceived?.Invoke(entry);
    }
}
