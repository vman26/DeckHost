using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DeckHost.Device.MiraBox;

/// <summary>
/// Device plugin for MiraBox / StreamDock devices: the N4 Pro, and the other models the vendor SDK
/// knows (<see cref="MiraBoxModels"/>), picked by the SDK's origin type.
/// <para>
/// The first unit of a model gets the id <c>mirabox.{model}</c> (<see cref="PrimaryDeviceId"/> for
/// the N4 Pro), so profiles keep working when it is unplugged and plugged back in; further units get
/// <c>-2</c>, <c>-3</c>, ... The N4 Pro's definition always exists, and every device seen before is
/// remembered (<c>devices/mirabox-known.json</c>), connected or not, so profiles can be edited offline.
/// </para>
/// </summary>
public sealed unsafe class MiraBoxDevicePlugin : IDevicePlugin, IDeviceOutput, IDeviceBackdropOutput, IDeviceLightBatchOutput, IConfigurableDevicePlugin, IDeviceFirmware, IDeviceModelCatalog, IDeviceSettingActions
{
    public const string ForgetActionId = "forget";

    /// <summary>
    /// An unplugged device can be forgotten: it leaves the device list until it's plugged in again
    /// (the N4 Pro shown before any device was plugged in too).
    /// </summary>
    public IReadOnlyList<DeviceSettingAction> GetSettingActions(string deviceId)
    {
        lock (_sync)
        {
            return FindSession(deviceId) is { DeviceKey: null } session
                ? [new DeviceSettingAction(ForgetActionId, "Forget this device", "Removes it from the device list until it's plugged in again. Its profiles are kept for then.", $"Forget MiraBox {session.Model.ShortName}?")]
                : [];
        }
    }

    public string RunSettingAction(string deviceId, string actionId)
    {
        if (actionId != ForgetActionId)
        {
            return "Unknown action.";
        }

        lock (_sync)
        {
            if (FindSession(deviceId) is not { DeviceKey: null } session)
            {
                return "Only an unplugged device can be forgotten.";
            }

            _sessions.Remove(session);
            Volatile.Write(ref _devices, BuildDefinitions());
        }

        SaveKnown();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        return "Forgotten. It comes back when you plug it in.";
    }

    /// <summary>Every StreamDock model DeckHost knows, unplugged (for the device simulator).</summary>
    public IReadOnlyList<DeviceDefinition> DescribeModels() =>
        [.. MiraBoxModels.All.Select(static model => model.Describe(model.DefaultDeviceId, $"MiraBox {model.ShortName}", null, isConnected: false, detail: null))];

    /// <summary>The version the SDK reads; the StreamDock SDK has no way to write firmware.</summary>
    public DeviceFirmwareInfo? GetFirmwareInfo(string deviceId)
    {
        lock (_sync)
        {
            return FindSession(deviceId)?.Info is { FirmwareVersion.Length: > 0 } info
                ? new DeviceFirmwareInfo(info.FirmwareVersion, CanUpdate: false, "MiraBox offers no firmware update protocol: update it with MiraBox's own software, if they release one.")
                : null;
        }
    }

    public IReadOnlyList<ActionSettingDefinition> GetDeviceSettingDefinitions(string deviceId) => MiraBoxDeviceSettings.For(ModelOf(deviceId));

    public ActionSettings GetDeviceSettings(string deviceId) => MiraBoxDeviceSettings.Load(deviceId);

    public string? ApplyDeviceSettings(string deviceId, ActionSettings settings)
    {
        // Keep the markers of what was already sent to the device.
        var stored = MiraBoxDeviceSettings.Load(deviceId);
        foreach (var marker in MiraBoxDeviceSettings.Markers)
        {
            settings = settings.With(marker, stored.TryGetValue(marker, out var applied) ? applied : null);
        }

        MiraBoxDeviceSettings.Save(deviceId, settings);

        if (!TryGetConnectedKey(deviceId, out var deviceKey, out _))
        {
            return null; // Saved; applied when the device connects.
        }

        var problems = ApplyStoredSettings(deviceId, deviceKey);
        return problems.Count == 0 ? null : "Not everything could be applied: " + string.Join("; ", problems);
    }

    /// <summary>When the images are sent again after writing the idle screen.</summary>
    internal static readonly TimeSpan[] IdleScreenRedrawDelays = [TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(4)];

    private List<string> ApplyStoredSettings(string deviceId, string deviceKey)
    {
        var settings = MiraBoxDeviceSettings.Load(deviceId);
        var (problems, idleScreenWritten) = MiraBoxDeviceSettings.Apply(deviceKey, ModelOf(deviceId), settings, (marker, value) =>
            MiraBoxDeviceSettings.Save(deviceId, MiraBoxDeviceSettings.Load(deviceId).With(marker, value)));
        // Writing the idle screen blanks the keys, and the device configuration resets the LEDs to
        // the device's own lighting: send images and lights again.
        _context?.RequestRedraw(deviceId);
        if (idleScreenWritten)
        {
            // The device stores the idle screen and shows it while it does, over the keys; what is
            // sent meanwhile can be lost. Send everything again once it's done.
            foreach (var delay in IdleScreenRedrawDelays)
            {
                _ = Task.Delay(delay).ContinueWith(_ => _context?.RequestRedraw(deviceId), TaskScheduler.Default);
            }
        }

        foreach (var problem in problems)
        {
            _context?.Log("Warning", $"{deviceId}: could not apply {problem}");
        }

        return problems;
    }

    /// <summary>The self-check <c>DeckHost --check-native</c> runs for every bundled device plugin that has one.</summary>
    public static (bool Ok, string Report) CheckRuntime() => CheckNativeBridge();

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
                        lines.Add($"  {MiraBoxModels.For(info.OriginType)?.ShortName ?? $"unknown model {info.OriginType}"} {info.VendorId:X4}:{info.ProductId:X4}, serial {info.SerialNumber}, firmware {info.FirmwareVersion}");
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
    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>Some models (the N4 Pro) report a knob press but never its release; one is reported this long after the press.</summary>
    private static readonly TimeSpan KnobReleaseDelay = TimeSpan.FromMilliseconds(120);

    /// <summary>The backdrop is one color, so a few pixels are enough: the device scales them to its screen.</summary>
    private const int BackdropSize = 8;

    // The native callback is a static function pointer, so it needs a static route back to the plugin.
    private static MiraBoxDevicePlugin? s_active;

    private readonly Lock _sync = new();
    private readonly List<DeviceSession> _sessions = [new DeviceSession(PrimaryDeviceId, MiraBoxModels.N4Pro)];
    private readonly string _directory;
    private readonly HashSet<string> _reportedUnsupported = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string DeviceId, string ControlId), Timer> _longPressTimers = new();
    private IReadOnlyCollection<DeviceDefinition> _devices;
    private IDevicePluginContext? _context;
    private int _refreshQueued;
    private bool _nativeInitialized;
    private bool _disposed;

    public MiraBoxDevicePlugin()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost", "devices"))
    {
    }

    /// <param name="directory">Where the devices seen before are remembered.</param>
    public MiraBoxDevicePlugin(string directory)
    {
        _directory = directory;
        foreach (var known in LoadKnown())
        {
            if (known.Forgotten)
            {
                // The N4 Pro shown before anything was plugged in, forgotten.
                _sessions.RemoveAll(session => string.Equals(session.DeviceId, known.Id, StringComparison.OrdinalIgnoreCase));
                continue;
            }

            if (MiraBoxModels.For(known.OriginType) is { } model && FindSession(known.Id) is null)
            {
                _sessions.Add(new DeviceSession(known.Id, model) { SerialNumber = string.IsNullOrEmpty(known.Serial) ? null : known.Serial });
            }
        }

        _devices = BuildDefinitions();
    }

    public event EventHandler? DevicesChanged;

    public string Id => "deckhost.device.mirabox";

    public string Name => "MiraBox StreamDock";

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
            throw new InvalidOperationException("MiraBoxBridge native library not found. Build with CMake available so it is copied into the plugin's folder (plugins/bundled/deckhost-device-mirabox).", ex);
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

        if (ModelOf(deviceId).GetImageKeyId(controlId) is not { } keyId)
        {
            error = $"'{controlId}' has no image output.";
            return false;
        }

        var result = MiraBoxNative.SendKeyImage(deviceKey, keyId, rgba, width, height);
        return CheckResult(deviceId, result, out error);
    }

    public bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, out string? error) =>
        TrySetLights(deviceId, [new LightColor(controlId, red, green, blue)], out error);

    /// <summary>Sets several LEDs with one SDK call (it always sets all of them), for light effects.</summary>
    public bool TrySetLights(string deviceId, IReadOnlyList<LightColor> lights, out string? error)
    {
        byte[] colors;
        string deviceKey;
        bool allDark;
        bool restoreBrightness;
        lock (_sync)
        {
            var session = FindSession(deviceId);
            if (session is not { DeviceKey: not null })
            {
                error = "device not connected";
                return false;
            }

            foreach (var light in lights)
            {
                if (session.Model.GetLedIndex(light.ControlId) is not { } ledIndex || session.LedColors.Length < (ledIndex + 1) * 3)
                {
                    error = $"'{light.ControlId}' has no LED.";
                    return false;
                }

                session.LedColors[ledIndex * 3] = light.Red;
                session.LedColors[(ledIndex * 3) + 1] = light.Green;
                session.LedColors[(ledIndex * 3) + 2] = light.Blue;
            }

            colors = (byte[])session.LedColors.Clone();
            deviceKey = session.DeviceKey;
            allDark = Array.TrueForAll(colors, static value => value == 0);
            restoreBrightness = !allDark && session.LedsDimmed;
            session.LedsDimmed = allDark;
        }

        if (allDark)
        {
            // The N4 Pro's firmware shows its own rainbow when every LED is set to black, so "off"
            // is LED brightness 0 instead (sent every time: applying device settings can raise it).
            return CheckResult(deviceId, MiraBoxNative.SetLedBrightness(deviceKey, 0), out error);
        }

        if (restoreBrightness && !CheckResult(deviceId, MiraBoxNative.SetLedBrightness(deviceKey, MiraBoxDeviceSettings.LedBrightnessLevel(deviceId)), out error))
        {
            return false;
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

        DeviceSession? session;
        lock (_sync)
        {
            session = _sessions.FirstOrDefault(candidate => candidate.DeviceKey == deviceKey);
        }

        if (session is null)
        {
            QueueRefresh();
            return;
        }

        var deviceId = session.DeviceId;
        var model = session.Model;
        if (!model.TryMapInput(eventType, sdkControlId, out var controlId, out var inputEvent, out var value))
        {
            _context.Log("Debug", $"Ignored {model.ShortName} event {eventType} for SDK id {sdkControlId}.");
            return;
        }

        if (model.TracksLongPress(controlId))
        {
            TrackLongPress(deviceId, controlId, inputEvent);
        }

        _context.ReportInputEvent(deviceId, controlId, inputEvent, value);

        if (eventType == MiraBoxEventType.KnobPress && model.KnobsReportNoRelease)
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

            if (MiraBoxModels.For(info.OriginType) is null)
            {
                if (_reportedUnsupported.Add(deviceKey))
                {
                    _context?.Log("Warning", $"StreamDock device {info.VendorId:X4}:{info.ProductId:X4} (model {info.OriginType}) is not supported yet.");
                }

                continue;
            }

            connected.Add(info);
        }

        var statusChanges = new List<(string DeviceId, string State, string Message)>();
        bool changed;
        bool added;
        lock (_sync)
        {
            var before = _sessions.Count;
            changed = AssignSessions(connected, statusChanges);
            added = _sessions.Count != before;
            if (changed)
            {
                Volatile.Write(ref _devices, BuildDefinitions());
            }
        }

        if (added)
        {
            SaveKnown();
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
            var model = MiraBoxModels.For(info.OriginType)!;
            var sameModel = _sessions.Where(candidate => candidate.Model == model && !matched.Contains(candidate)).ToList();
            var session = sameModel.FirstOrDefault(candidate => candidate.DeviceKey == info.DeviceKey)
                ?? sameModel.FirstOrDefault(candidate => candidate.DeviceKey is null && candidate.SerialNumber == info.SerialNumber)
                ?? sameModel.FirstOrDefault(candidate => candidate.DeviceKey is null && candidate.SerialNumber is null);

            if (session is null)
            {
                session = new DeviceSession(NewDeviceId(model), model);
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
            var ledCount = Math.Min(model.LedCount, Math.Max(0, info.LedCount));
            if (session.LedColors.Length != ledCount * 3)
            {
                session.LedColors = new byte[ledCount * 3];
            }

            statusChanges.Add((session.DeviceId, "Connected", $"{model.ShortName} {info.SerialNumber}, firmware {info.FirmwareVersion}."));
        }

        foreach (var session in _sessions.Where(session => !matched.Contains(session)))
        {
            if (session.DeviceKey is not null || !session.StatusReported)
            {
                changed |= session.DeviceKey is not null;
                session.DeviceKey = null;
                statusChanges.Add((session.DeviceId, "Disconnected", $"Plug in the {session.Model.ShortName} over USB."));
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
        .. _sessions.Select(session => session.Model.Describe(
            session.DeviceId,
            session.DeviceId == session.Model.DefaultDeviceId ? $"MiraBox {session.Model.ShortName}" : $"MiraBox {session.Model.ShortName} ({session.SerialNumber})",
            session.Info,
            session.DeviceKey is not null,
            session.Info is null ? null : $"Serial {session.Info.SerialNumber}, firmware {session.Info.FirmwareVersion}"))
    ];

    /// <summary><c>mirabox.{model}</c> for the first unit of a model, then <c>-2</c>, <c>-3</c>, ...</summary>
    private string NewDeviceId(MiraBoxModel model)
    {
        var id = model.DefaultDeviceId;
        for (var number = 2; FindSession(id) is not null; number++)
        {
            id = $"{model.DefaultDeviceId}-{number}";
        }

        return id;
    }

    private DeviceSession? FindSession(string deviceId) =>
        _sessions.FirstOrDefault(session => string.Equals(session.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The model of a device; the N4 Pro for an id this plugin doesn't know.</summary>
    private MiraBoxModel ModelOf(string deviceId)
    {
        lock (_sync)
        {
            return FindSession(deviceId)?.Model ?? MiraBoxModels.N4Pro;
        }
    }

    private string KnownPath => Path.Combine(_directory, "mirabox-known.json");

    private List<KnownDevice> LoadKnown()
    {
        try
        {
            return File.Exists(KnownPath) ? JsonSerializer.Deserialize<List<KnownDevice>>(File.ReadAllText(KnownPath)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private void SaveKnown()
    {
        try
        {
            List<KnownDevice> known;
            lock (_sync)
            {
                known = [.. _sessions
                    .Where(static session => session.DeviceId != PrimaryDeviceId || session.SerialNumber is not null)
                    .Select(static session => new KnownDevice(session.DeviceId, session.Model.OriginType, session.SerialNumber ?? string.Empty))];
                if (FindSession(PrimaryDeviceId) is null)
                {
                    known.Add(new KnownDevice(PrimaryDeviceId, MiraBoxModels.N4Pro.OriginType, string.Empty) { Forgotten = true });
                }
            }

            Directory.CreateDirectory(_directory);
            File.WriteAllText(KnownPath, JsonSerializer.Serialize(known));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _context?.Log("Warning", $"Could not remember the MiraBox devices: {ex.Message}");
        }
    }

    private sealed record KnownDevice(string Id, int OriginType, string Serial)
    {
        /// <summary>For the N4 Pro that is listed before anything is plugged in: the user forgot it.</summary>
        public bool Forgotten { get; init; }
    }

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

    private sealed class DeviceSession(string deviceId, MiraBoxModel model)
    {
        public string DeviceId { get; } = deviceId;

        public MiraBoxModel Model { get; } = model;

        public string? DeviceKey { get; set; }

        public string? SerialNumber { get; set; }

        public MiraBoxDeviceInfo? Info { get; set; }

        public byte[] LedColors { get; set; } = new byte[model.LedCount * 3];

        /// <summary>The LEDs are off through brightness 0 (see <see cref="TrySetLights"/>).</summary>
        public bool LedsDimmed { get; set; }

        public bool StatusReported { get; set; }
    }
}
