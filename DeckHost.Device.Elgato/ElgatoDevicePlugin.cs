using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using HidSharp;

namespace DeckHost.Device.Elgato;

/// <summary>
/// Elgato Stream Deck hardware over USB HID (no Elgato software needed; close the Stream Deck
/// app, which otherwise holds the device). Devices are found and followed as they are plugged in
/// and out; each one seen before stays listed while unplugged, so its profiles can be edited.
/// </summary>
public sealed class ElgatoDevicePlugin : IDevicePlugin, IDeviceOutput, IConfigurableDevicePlugin, IDeviceFirmware
{
    /// <summary>The version the deck reports (read when it connects); Elgato's update protocol isn't public.</summary>
    public DeviceFirmwareInfo? GetFirmwareInfo(string deviceId) =>
        Find(deviceId)?.Firmware is { Length: > 0 } version
            ? new DeviceFirmwareInfo(version, CanUpdate: false, "Elgato's update protocol isn't public: update the firmware with Elgato's Stream Deck app (close DeckHost while it runs).")
            : null;

    public const string Brightness = "brightness";

    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(500);
    private static readonly NumberRange Percentage = new(0, 100) { Unit = "%", UnsetValue = 70 };

    private readonly Lock _sync = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string DeviceId, string ControlId), Timer> _longPressTimers = new();
    private readonly string _directory;
    private IReadOnlyCollection<DeviceDefinition> _devices = [];
    private IDevicePluginContext? _context;
    private int _refreshQueued;
    private bool _disposed;

    public ElgatoDevicePlugin(string? directory = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost", "devices");
        foreach (var known in LoadKnown())
        {
            if (StreamDeckModel.Find(known.ProductId) is { } model)
            {
                _sessions[known.Id] = new Session(known.Id, model, known.Serial);
            }
        }

        _devices = BuildDefinitions();
    }

    public event EventHandler? DevicesChanged;

    public string Id => "deckhost.device.elgato";

    public string Name => "Elgato Stream Deck";

    public IReadOnlyCollection<DeviceDefinition> Devices => Volatile.Read(ref _devices);

    public IDeviceOutput Output => this;

    public Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context = context;
        DeviceList.Local.Changed += OnDeviceListChanged;
        RefreshDevices();
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------- settings

    public IReadOnlyList<ActionSettingDefinition> GetDeviceSettingDefinitions(string deviceId) =>
        Find(deviceId)?.Model.HasDisplays == false
            ? []
            : [new ActionSettingDefinition(Brightness, "Screen brightness", ActionSettingKind.Number, Placeholder: "70") { Range = Percentage }];

    public ActionSettings GetDeviceSettings(string deviceId)
    {
        try
        {
            var path = SettingsPath(deviceId);
            return File.Exists(path) ? JsonSerializer.Deserialize<ActionSettings>(File.ReadAllText(path)) ?? ActionSettings.Empty : ActionSettings.Empty;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return ActionSettings.Empty;
        }
    }

    public string? ApplyDeviceSettings(string deviceId, ActionSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(SettingsPath(deviceId), JsonSerializer.Serialize(settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not save the settings: {ex.Message}";
        }

        return Find(deviceId) is { Stream: not null } session && !ApplySettings(session, settings)
            ? "The device did not accept the brightness."
            : null;
    }

    // ---------------------------------------------------------------- output

    public bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, out string? error)
    {
        if (Find(deviceId) is not { Stream: not null } session)
        {
            error = "The Stream Deck is not connected.";
            return false;
        }

        var model = session.Model;
        IEnumerable<byte[]> reports;
        if (controlId.StartsWith("key.", StringComparison.Ordinal) && int.TryParse(controlId.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var key) && key < model.KeyCount && model.HasDisplays)
        {
            reports = model.Generation == 1
                ? StreamDeckProtocol.Gen1KeyImageReports(model, key, StreamDeckProtocol.EncodeBmp(rgba, width, height, model))
                : StreamDeckProtocol.KeyImageReports(key, StreamDeckProtocol.EncodeJpeg(rgba, width, height, model.KeyImageSize, model.KeyImageSize, model.FlipKeyImages));
        }
        else if (controlId.StartsWith("touch.", StringComparison.Ordinal) && int.TryParse(controlId.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var zone) && zone < model.Dials)
        {
            var zoneWidth = model.TouchStripWidth / model.Dials;
            var jpeg = StreamDeckProtocol.EncodeJpeg(rgba, width, height, zoneWidth, model.TouchStripHeight, flip: false);
            reports = StreamDeckProtocol.TouchStripReports(zone * zoneWidth, 0, zoneWidth, model.TouchStripHeight, jpeg);
        }
        else
        {
            error = null;
            return true;
        }

        return Write(session, reports, out error);
    }

    /// <summary>Stream Decks have no RGB LEDs.</summary>
    public bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, out string? error)
    {
        error = null;
        return true;
    }

    public void Clear(string deviceId)
    {
        if (Find(deviceId) is not { Stream: not null } session || !session.Model.HasDisplays)
        {
            return;
        }

        var model = session.Model;
        var black = new byte[4];
        var keyImage = model.Generation == 1
            ? StreamDeckProtocol.EncodeBmp(black, 1, 1, model)
            : StreamDeckProtocol.EncodeJpeg(black, 1, 1, model.KeyImageSize, model.KeyImageSize, flip: false);
        for (var key = 0; key < model.KeyCount; key++)
        {
            Write(session, model.Generation == 1 ? StreamDeckProtocol.Gen1KeyImageReports(model, key, keyImage) : StreamDeckProtocol.KeyImageReports(key, keyImage), out _);
        }

        if (model.TouchStripWidth > 0)
        {
            var stripJpeg = StreamDeckProtocol.EncodeJpeg(black, 1, 1, model.TouchStripWidth, model.TouchStripHeight, flip: false);
            Write(session, StreamDeckProtocol.TouchStripReports(0, 0, model.TouchStripWidth, model.TouchStripHeight, stripJpeg), out _);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DeviceList.Local.Changed -= OnDeviceListChanged;
        foreach (var timer in _longPressTimers.Values)
        {
            timer.Dispose();
        }

        lock (_sync)
        {
            foreach (var session in _sessions.Values)
            {
                session.Close();
            }
        }
    }

    // ---------------------------------------------------------------- devices

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e)
    {
        // Bursts of change events (several interfaces per device): refresh once.
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
        {
            _ = Task.Delay(300).ContinueWith(_ =>
            {
                Volatile.Write(ref _refreshQueued, 0);
                RefreshDevices();
            }, TaskScheduler.Default);
        }
    }

    private void RefreshDevices()
    {
        if (_disposed)
        {
            return;
        }

        List<HidDevice> found;
        try
        {
            found = [.. DeviceList.Local.GetHidDevices(StreamDeckModel.VendorId)];
        }
        catch (Exception ex)
        {
            _context?.Log("Error", $"Listing Stream Deck devices failed: {ex.Message}");
            return;
        }

        var connected = new List<(Session Session, string Detail)>();
        var changed = false;
        lock (_sync)
        {
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in found)
            {
                if (StreamDeckModel.Find(device.ProductID) is not { } model)
                {
                    if (StreamDeckModel.NotYetSupported.TryGetValue(device.ProductID, out var name))
                    {
                        _context?.Log("Warning", $"{name} is not supported yet.");
                    }

                    continue;
                }

                var serial = TryGet(device.GetSerialNumber) ?? device.DevicePath;
                var id = DeviceIdFor(model, serial);
                present.Add(id);
                if (!_sessions.TryGetValue(id, out var session))
                {
                    session = new Session(id, model, serial);
                    _sessions[id] = session;
                    changed = true;
                }

                if (session.Stream is not null)
                {
                    continue;
                }

                if (!device.TryOpen(out var stream))
                {
                    _context?.Log("Warning", $"Could not open {model.Name} {serial}. Close the Elgato Stream Deck app, which keeps the device to itself; on Linux install the udev rule.");
                    continue;
                }

                stream.ReadTimeout = Timeout.Infinite;
                stream.WriteTimeout = 2000;
                session.Open(stream);
                session.Firmware = ReadFirmware(stream, model);
                if (model.Generation == 1)
                {
                    // The first generation keeps a half-sent image across reconnects: start a fresh stream.
                    Write(session, [StreamDeckProtocol.Gen1ResetStreamReport(model)], out _);
                }

                changed = true;
                connected.Add((session, $"{model.Name} {serial}"));
                var reader = new Thread(() => ReadLoop(session, stream)) { IsBackground = true, Name = $"Stream Deck {serial}" };
                reader.Start();
            }

            foreach (var session in _sessions.Values.Where(session => session.Stream is not null && !present.Contains(session.DeviceId)))
            {
                session.Close();
                changed = true;
                _context?.ReportConnectionStatus(session.DeviceId, "Disconnected", $"{session.Model.Name} was unplugged.");
            }

            if (changed)
            {
                Volatile.Write(ref _devices, BuildDefinitions());
                SaveKnown();
            }
        }

        foreach (var (session, detail) in connected)
        {
            if (session.Model.HasDisplays)
            {
                ApplySettings(session, GetDeviceSettings(session.DeviceId));
            }

            _context?.ReportConnectionStatus(session.DeviceId, "Connected", detail);
            _context?.RequestRedraw(session.DeviceId);
        }

        if (changed)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ReadLoop(Session session, HidStream stream)
    {
        var buffer = new byte[Math.Max(512, stream.Device.GetMaxInputReportLength())];
        var keysDown = new bool[session.Model.KeyCount];
        var dialsDown = new bool[Math.Max(1, session.Model.Dials)];
        try
        {
            while (!_disposed && ReferenceEquals(session.Stream, stream))
            {
                var length = stream.Read(buffer, 0, buffer.Length);
                if (length <= 0)
                {
                    continue;
                }

                foreach (var input in StreamDeckProtocol.Parse(buffer.AsSpan(0, length), session.Model, keysDown, dialsDown))
                {
                    if (input.ControlId.StartsWith("key.", StringComparison.Ordinal))
                    {
                        TrackLongPress(session.DeviceId, input.ControlId, input.Event);
                    }

                    _context?.ReportInputEvent(session.DeviceId, input.ControlId, input.Event, input.Value);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException or InvalidOperationException)
        {
            // Unplugged or closed: the device list change follows.
            if (!_disposed && ReferenceEquals(session.Stream, stream))
            {
                OnDeviceListChanged(this, null!);
            }
        }
    }

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

    private bool ApplySettings(Session session, ActionSettings settings)
    {
        var percent = settings.GetInt(Brightness, 70);
        lock (session.WriteLock)
        {
            try
            {
                session.Stream?.SetFeature(StreamDeckProtocol.BrightnessReport(session.Model, percent));
                return true;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException)
            {
                _context?.Log("Warning", $"Setting the brightness of {session.Model.Name} failed: {ex.Message}");
                return false;
            }
        }
    }

    private static string? ReadFirmware(HidStream stream, StreamDeckModel model)
    {
        try
        {
            var report = StreamDeckProtocol.FirmwareRequest(model);
            stream.GetFeature(report);
            return StreamDeckProtocol.FirmwareVersion(model, report);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or NotSupportedException or ObjectDisposedException)
        {
            return null;
        }
    }

    private bool Write(Session session, IEnumerable<byte[]> reports, out string? error)
    {
        lock (session.WriteLock)
        {
            try
            {
                foreach (var report in reports)
                {
                    if (session.Stream is not { } stream)
                    {
                        error = "The Stream Deck is not connected.";
                        return false;
                    }

                    stream.Write(report);
                }

                error = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    private Session? Find(string deviceId)
    {
        lock (_sync)
        {
            return _sessions.GetValueOrDefault(deviceId);
        }
    }

    private IReadOnlyCollection<DeviceDefinition> BuildDefinitions() =>
        [.. _sessions.Values
            .OrderBy(static session => session.DeviceId, StringComparer.Ordinal)
            .Select(static session => new DeviceDefinition(
                session.DeviceId,
                session.Model.Name,
                session.Model.Controls(),
                session.Stream is not null,
                session.Firmware is { } firmware ? $"Serial {session.Serial}, firmware {firmware}" : $"Serial {session.Serial}"))];

    internal static string DeviceIdFor(StreamDeckModel model, string serial)
    {
        var clean = new string([.. serial.Where(char.IsAsciiLetterOrDigit)]);
        if (clean.Length == 0)
        {
            clean = "unknown";
        }

        return $"elgato.{model.ProductId:x4}.{clean.ToLowerInvariant()}";
    }

    private string SettingsPath(string deviceId) => Path.Combine(_directory, deviceId + ".json");

    private string KnownPath => Path.Combine(_directory, "elgato-known.json");

    private List<KnownDevice> LoadKnown()
    {
        try
        {
            return File.Exists(KnownPath) ? JsonSerializer.Deserialize<List<KnownDevice>>(File.ReadAllText(KnownPath)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void SaveKnown()
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var known = _sessions.Values.Select(static session => new KnownDevice(session.DeviceId, session.Model.ProductId, session.Serial)).ToList();
            File.WriteAllText(KnownPath, JsonSerializer.Serialize(known));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _context?.Log("Warning", $"Could not remember the Stream Deck devices: {ex.Message}");
        }
    }

    private static string? TryGet(Func<string> read)
    {
        try
        {
            var value = read();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record KnownDevice(string Id, int ProductId, string Serial);

    private sealed class Session(string deviceId, StreamDeckModel model, string serial)
    {
        public string DeviceId { get; } = deviceId;

        public StreamDeckModel Model { get; } = model;

        public string Serial { get; } = serial;

        public Lock WriteLock { get; } = new();

        public HidStream? Stream { get; private set; }

        /// <summary>The firmware version the deck reported when it connected.</summary>
        public string? Firmware { get; set; }

        public void Open(HidStream stream) => Stream = stream;

        public void Close()
        {
            var stream = Stream;
            Stream = null;
            try
            {
                stream?.Dispose();
            }
            catch (IOException)
            {
            }
        }
    }
}
