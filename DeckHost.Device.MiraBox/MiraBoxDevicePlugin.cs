using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DeckHost.Device.MiraBox;

/// <summary>
/// Device plugin for the MiraBox / StreamDock N4 Pro (see <see cref="N4ProLayout"/>).
/// <para>
/// The first N4 Pro gets the id <see cref="PrimaryDeviceId"/>, so profiles keep working when it is
/// unplugged and plugged back in; further units get <c>mirabox.n4pro-2</c>, <c>-3</c>, ...
/// The primary definition always exists, connected or not, so profiles can be edited offline.
/// </para>
/// </summary>
public sealed unsafe class MiraBoxDevicePlugin : IDevicePlugin, IDeviceOutput, IDeviceBackdropOutput, IConfigurableDevicePlugin
{
    public IReadOnlyList<ActionSettingDefinition> GetDeviceSettingDefinitions(string deviceId) => N4ProDeviceSettings.Definitions;

    public ActionSettings GetDeviceSettings(string deviceId) => N4ProDeviceSettings.Load(deviceId);

    public string? ApplyDeviceSettings(string deviceId, ActionSettings settings)
    {
        // Keep the markers of what was already sent to the device.
        var stored = N4ProDeviceSettings.Load(deviceId);
        foreach (var marker in N4ProDeviceSettings.Markers)
        {
            settings = settings.With(marker, stored.TryGetValue(marker, out var applied) ? applied : null);
        }

        N4ProDeviceSettings.Save(deviceId, settings);

        if (!TryGetConnectedKey(deviceId, out var deviceKey, out _))
        {
            return null; // Saved; applied when the device connects.
        }

        var problems = ApplyStoredSettings(deviceId, deviceKey);
        return problems.Count == 0 ? null : "Not everything could be applied: " + string.Join("; ", problems);
    }

    private List<string> ApplyStoredSettings(string deviceId, string deviceKey)
    {
        var settings = N4ProDeviceSettings.Load(deviceId);
        var (problems, idleScreenWritten) = N4ProDeviceSettings.Apply(deviceKey, settings, (marker, value) =>
            N4ProDeviceSettings.Save(deviceId, N4ProDeviceSettings.Load(deviceId).With(marker, value)));
        if (idleScreenWritten)
        {
            // Writing the idle screen also blanks the keys on the device: send their images again.
            _context?.RequestRedraw(deviceId);
        }

        foreach (var problem in problems)
        {
            _context?.Log("Warning", $"{deviceId}: could not apply {problem}");
        }

        return problems;
    }

    /// <summary>
    /// Loads the native bridge, lists the connected StreamDock devices and unloads it again: checks
    /// an installation without opening the app (<c>DeckHost --check-native</c>). Not while the
    /// plugin itself is initialized.
    /// </summary>
    public static (bool Ok, string Report) CheckNativeBridge()
    {
        try
        {
            var result = MiraBoxNative.Initialize();
            if (result != 0)
            {
                return (false, $"The MiraBox bridge loaded but could not start: {MiraBoxNative.Describe(result)}.");
            }

            try
            {
                var lines = new List<string> { "MiraBox bridge loaded." };
                var count = MiraBoxNative.GetDeviceCount();
                lines.Add($"StreamDock devices found: {Math.Max(0, count)}");
                for (var index = 0; index < count; index++)
                {
                    if (MiraBoxNative.GetDeviceKey(index) is { } key &&
                        MiraBoxDeviceInfo.FromJson(MiraBoxNative.GetDeviceInfoJson(key, out _)) is { } info)
                    {
                        lines.Add($"  {(info.IsN4Pro ? "N4 Pro" : $"model {info.OriginType}")} {info.VendorId:X4}:{info.ProductId:X4}, serial {info.SerialNumber}, firmware {info.FirmwareVersion}");
                    }
                }

                return (true, string.Join(Environment.NewLine, lines));
            }
            finally
            {
                MiraBoxNative.Shutdown();
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return (false, MiraBoxNative.DescribeLoadFailure() ?? $"The MiraBox bridge could not be loaded: {ex.Message}");
        }
    }

    public const string PrimaryDeviceId = "mirabox.n4pro";
    private const string DeviceName = "MiraBox N4 Pro";
    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>The N4 Pro reports a knob press but never its release; one is reported this long after the press.</summary>
    private static readonly TimeSpan KnobReleaseDelay = TimeSpan.FromMilliseconds(120);

    /// <summary>The backdrop is one color, so a few pixels are enough: the device scales them to its screen.</summary>
    private const int BackdropSize = 8;

    // The native callback is a static function pointer, so it needs a static route back to the plugin.
    private static MiraBoxDevicePlugin? s_active;

    private readonly Lock _sync = new();
    private readonly List<DeviceSession> _sessions = [new DeviceSession(PrimaryDeviceId)];
    private readonly HashSet<string> _reportedUnsupported = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string DeviceId, string ControlId), Timer> _longPressTimers = new();
    private IReadOnlyCollection<DeviceDefinition> _devices;
    private IDevicePluginContext? _context;
    private int _refreshQueued;
    private bool _nativeInitialized;
    private bool _disposed;

    public MiraBoxDevicePlugin()
    {
        _devices = BuildDefinitions();
    }

    public event EventHandler? DevicesChanged;

    public string Id => "deckhost.device.mirabox";

    public string Name => "MiraBox Device Plugin";

    public IReadOnlyCollection<DeviceDefinition> Devices => Volatile.Read(ref _devices);

    public IDeviceOutput Output => this;

    public Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (_nativeInitialized)
        {
            return Task.CompletedTask;
        }

        if (Interlocked.CompareExchange(ref s_active, this, null) is not null)
        {
            throw new InvalidOperationException("Only one MiraBoxDevicePlugin can be initialized per process.");
        }

        _context = context;
        try
        {
            var result = MiraBoxNative.Initialize();
            if (result != 0)
            {
                throw new ExternalException($"Failed to initialize the MiraBox bridge: {MiraBoxNative.Describe(result)}.", result);
            }

            MiraBoxNative.SetCallback(&OnNativeEvent);
            _nativeInitialized = true;
        }
        catch (DllNotFoundException ex)
        {
            Interlocked.CompareExchange(ref s_active, null, this);
            throw new InvalidOperationException("MiraBoxBridge native library not found. Build with CMake available so it is copied to plugins/device/deckhost.device.mirabox.", ex);
        }
        catch
        {
            Interlocked.CompareExchange(ref s_active, null, this);
            throw;
        }

        RefreshDevices();
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------- IDeviceOutput

    public bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, out string? error)
    {
        if (!TryGetConnectedKey(deviceId, out var deviceKey, out error))
        {
            return false;
        }

        if (N4ProLayout.GetImageKeyId(controlId) is not { } keyId)
        {
            error = $"'{controlId}' has no image output.";
            return false;
        }

        var result = MiraBoxNative.SendKeyImage(deviceKey, keyId, rgba, width, height);
        return CheckResult(deviceId, result, out error);
    }

    public bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, out string? error)
    {
        if (N4ProLayout.GetLedIndex(controlId) is not { } ledIndex)
        {
            error = $"'{controlId}' has no LED.";
            return false;
        }

        byte[] colors;
        string deviceKey;
        lock (_sync)
        {
            var session = FindSession(deviceId);
            if (session is not { DeviceKey: not null } || session.LedColors.Length < (ledIndex + 1) * 3)
            {
                error = session?.DeviceKey is null ? "device not connected" : $"LED {ledIndex} does not exist.";
                return false;
            }

            // The SDK sets all LEDs in one call, so keep the full state and always send all of it.
            session.LedColors[ledIndex * 3] = red;
            session.LedColors[(ledIndex * 3) + 1] = green;
            session.LedColors[(ledIndex * 3) + 2] = blue;
            colors = (byte[])session.LedColors.Clone();
            deviceKey = session.DeviceKey;
        }

        return CheckResult(deviceId, MiraBoxNative.SetLedColors(deviceKey, colors), out error);
    }

    /// <summary>
    /// Draws a temporary background behind the keys. The N4 Pro shows its stored idle screen
    /// everywhere no key image is, also while DeckHost runs; this covers it until <see cref="Clear"/>.
    /// </summary>
    public bool TrySetBackdrop(string deviceId, byte red, byte green, byte blue, out string? error)
    {
        if (!TryGetConnectedKey(deviceId, out var deviceKey, out error))
        {
            return false;
        }

        Span<byte> pixels = stackalloc byte[BackdropSize * BackdropSize * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = red;
            pixels[offset + 1] = green;
            pixels[offset + 2] = blue;
            pixels[offset + 3] = 255;
        }

        return CheckResult(deviceId, MiraBoxNative.SetTemporaryBackground(deviceKey, pixels, BackdropSize, BackdropSize), out error);
    }

    public void Clear(string deviceId)
    {
        if (!TryGetConnectedKey(deviceId, out var deviceKey, out _))
        {
            return;
        }

        // Back to the device's own idle screen.
        MiraBoxNative.ClearTemporaryBackground(deviceKey);

        byte[] colors;
        lock (_sync)
        {
            var session = FindSession(deviceId)!;
            Array.Clear(session.LedColors);
            colors = session.LedColors.ToArray();
        }

        MiraBoxNative.ClearAllKeys(deviceKey);
        if (colors.Length > 0)
        {
            MiraBoxNative.SetLedColors(deviceKey, colors);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var timer in _longPressTimers.Values)
        {
            timer.Dispose();
        }

        _longPressTimers.Clear();

        if (_nativeInitialized)
        {
            MiraBoxNative.SetCallback(null);
            MiraBoxNative.Shutdown();
            _nativeInitialized = false;
        }

        Interlocked.CompareExchange(ref s_active, null, this);
    }

    // ---------------------------------------------------------------- native events

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNativeEvent(byte* deviceKey, int eventType, int controlId, int value)
    {
        // Runs on an SDK read thread while the SDK holds a lock: never block, never throw.
        try
        {
            var key = Marshal.PtrToStringUTF8((nint)deviceKey) ?? string.Empty;
            s_active?.HandleNativeEvent(key, (MiraBoxEventType)eventType, controlId);
        }
        catch
        {
        }
    }

    private void HandleNativeEvent(string deviceKey, MiraBoxEventType eventType, int sdkControlId)
    {
        if (_disposed || _context is null)
        {
            return;
        }

        if (eventType == MiraBoxEventType.DevicesChanged)
        {
            QueueRefresh();
            return;
        }

        string? deviceId;
        lock (_sync)
        {
            deviceId = _sessions.FirstOrDefault(session => session.DeviceKey == deviceKey)?.DeviceId;
        }

        if (deviceId is null)
        {
            QueueRefresh();
            return;
        }

        if (!N4ProLayout.TryMapInput(eventType, sdkControlId, out var controlId, out var inputEvent, out var value))
        {
            _context.Log("Debug", $"Ignored N4 Pro event {eventType} for SDK id {sdkControlId}.");
            return;
        }

        TrackLongPress(deviceId, controlId, inputEvent);
        _context.ReportInputEvent(deviceId, controlId, inputEvent, value);

        if (eventType == MiraBoxEventType.KnobPress)
        {
            // Without a release the knob would stay "held" for the editor and for "Released" triggers.
            _ = Task.Delay(KnobReleaseDelay).ContinueWith(
                _ =>
                {
                    if (!_disposed)
                    {
                        _context?.ReportInputEvent(deviceId, controlId, InputCapabilities.Release, 0);
                    }
                },
                TaskScheduler.Default);
        }
    }

    /// <summary>Raises LongPress when a key stays down for <see cref="LongPressDelay"/>.</summary>
    private void TrackLongPress(string deviceId, string controlId, InputCapabilities inputEvent)
    {
        if (!controlId.StartsWith(N4ProLayout.KeyPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var key = (deviceId, controlId);
        if (_longPressTimers.TryRemove(key, out var previous))
        {
            previous.Dispose();
        }

        if (inputEvent != InputCapabilities.Press)
        {
            return;
        }

        Timer? timer = null;
        timer = new Timer(_ =>
        {
            if (_longPressTimers.TryRemove(new KeyValuePair<(string, string), Timer>(key, timer!)))
            {
                timer!.Dispose();
                _context?.ReportInputEvent(deviceId, controlId, InputCapabilities.LongPress, 1);
            }
        });
        _longPressTimers[key] = timer;
        timer.Change(LongPressDelay, Timeout.InfiniteTimeSpan);
    }

    // ---------------------------------------------------------------- device tracking

    private void QueueRefresh()
    {
        // Don't call back into the SDK from inside its callback; coalesce bursts of hot-plug events.
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Volatile.Write(ref _refreshQueued, 0);
                try
                {
                    RefreshDevices();
                }
                catch (Exception ex)
                {
                    _context?.Log("Error", $"Refreshing MiraBox devices failed: {ex.Message}");
                }
            });
        }
    }

    private void RefreshDevices()
    {
        if (_disposed || !_nativeInitialized)
        {
            return;
        }

        var connected = new List<MiraBoxDeviceInfo>();
        var count = MiraBoxNative.GetDeviceCount();
        for (var index = 0; index < count; index++)
        {
            var deviceKey = MiraBoxNative.GetDeviceKey(index);
            if (deviceKey is null)
            {
                continue;
            }

            var info = MiraBoxDeviceInfo.FromJson(MiraBoxNative.GetDeviceInfoJson(deviceKey, out _));
            if (info is null)
            {
                continue;
            }

            if (!info.IsN4Pro)
            {
                if (_reportedUnsupported.Add(deviceKey))
                {
                    _context?.Log("Warning", $"StreamDock device {info.VendorId:X4}:{info.ProductId:X4} (model {info.OriginType}) is not supported yet; only the N4 Pro is.");
                }

                continue;
            }

            connected.Add(info);
        }

        var statusChanges = new List<(string DeviceId, string State, string Message)>();
        bool changed;
        lock (_sync)
        {
            changed = AssignSessions(connected, statusChanges);
            if (changed)
            {
                Volatile.Write(ref _devices, BuildDefinitions());
            }
        }

        foreach (var (deviceId, state, message) in statusChanges)
        {
            _context?.ReportConnectionStatus(deviceId, state, message);
            if (state == "Connected" && TryGetConnectedKey(deviceId, out var deviceKey, out _))
            {
                // Device settings (vibration, brightness, idle screen) are re-applied on every connect.
                ApplyStoredSettings(deviceId, deviceKey);
            }
        }

        if (changed)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Matches connected devices to sessions (by device key, then serial, then a free slot).</summary>
    private bool AssignSessions(IReadOnlyList<MiraBoxDeviceInfo> connected, List<(string, string, string)> statusChanges)
    {
        var changed = false;
        var matched = new HashSet<DeviceSession>();

        foreach (var info in connected)
        {
            var session = _sessions.FirstOrDefault(candidate => candidate.DeviceKey == info.DeviceKey && !matched.Contains(candidate))
                ?? _sessions.FirstOrDefault(candidate => candidate.DeviceKey is null && candidate.SerialNumber == info.SerialNumber && !matched.Contains(candidate))
                ?? _sessions.FirstOrDefault(candidate => candidate.DeviceKey is null && candidate.SerialNumber is null && !matched.Contains(candidate));

            if (session is null)
            {
                session = new DeviceSession($"{PrimaryDeviceId}-{_sessions.Count + 1}");
                _sessions.Add(session);
            }

            matched.Add(session);
            if (session.DeviceKey == info.DeviceKey && session.Info == info)
            {
                continue;
            }

            changed = true;
            session.DeviceKey = info.DeviceKey;
            session.SerialNumber = info.SerialNumber;
            session.Info = info;
            if (session.LedColors.Length != info.LedCount * 3)
            {
                session.LedColors = new byte[Math.Max(0, info.LedCount) * 3];
            }

            statusChanges.Add((session.DeviceId, "Connected", $"N4 Pro {info.SerialNumber}, firmware {info.FirmwareVersion}."));
        }

        foreach (var session in _sessions.Where(session => !matched.Contains(session)))
        {
            if (session.DeviceKey is not null || !session.StatusReported)
            {
                changed |= session.DeviceKey is not null;
                session.DeviceKey = null;
                statusChanges.Add((session.DeviceId, "Disconnected", "Plug in the N4 Pro over USB."));
            }
        }

        foreach (var session in _sessions)
        {
            session.StatusReported = true;
        }

        return changed;
    }

    private DeviceDefinition[] BuildDefinitions() =>
    [
        .. _sessions.Select(session => N4ProLayout.Create(
            session.DeviceId,
            session.DeviceId == PrimaryDeviceId ? DeviceName : $"{DeviceName} ({session.SerialNumber})",
            session.Info,
            session.DeviceKey is not null,
            session.Info is null ? null : $"Serial {session.Info.SerialNumber}, firmware {session.Info.FirmwareVersion}"))
    ];

    private DeviceSession? FindSession(string deviceId) =>
        _sessions.FirstOrDefault(session => string.Equals(session.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));

    private bool TryGetConnectedKey(string deviceId, out string deviceKey, out string? error)
    {
        lock (_sync)
        {
            if (FindSession(deviceId)?.DeviceKey is { } key)
            {
                deviceKey = key;
                error = null;
                return true;
            }
        }

        deviceKey = string.Empty;
        error = "device not connected";
        return false;
    }

    private bool CheckResult(string deviceId, int result, out string? error)
    {
        if (result >= 0)
        {
            error = null;
            return true;
        }

        error = MiraBoxNative.Describe(result);
        if (result == (int)MiraBoxResult.DeviceNotFound)
        {
            // Unplugged: re-enumerate so the host stops rendering to it.
            QueueRefresh();
        }

        return false;
    }

    private sealed class DeviceSession(string deviceId)
    {
        public string DeviceId { get; } = deviceId;

        public string? DeviceKey { get; set; }

        public string? SerialNumber { get; set; }

        public MiraBoxDeviceInfo? Info { get; set; }

        public byte[] LedColors { get; set; } = new byte[N4ProLayout.EncoderCount * 3];

        public bool StatusReported { get; set; }
    }
}
