using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using SkiaSharp;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeckHost.Core.StreamDeck;

/// <summary>
/// Devices of OpenAction device plugins (the ones made for OpenDeck: Mirabox, Ajazz, …). Such a
/// plugin is an <c>.sdPlugin</c> with a <c>DeviceNamespace</c> in its manifest and no actions;
/// <see cref="StreamDeckRuntime"/> runs it, it connects and sends <c>registerDevice</c> (id, name,
/// rows, columns, encoders) for every device it finds, then <c>keyDown</c>/<c>keyUp</c> and
/// <c>encoderChange</c>/<c>encoderDown</c>/<c>encoderUp</c>. DeckHost answers with <c>setImage</c>
/// (a JPEG data URL per key, none to clear) and <c>setBrightness</c>.
/// <para>
/// A registered device is a DeckHost device <c>openaction.{its id}</c> with <c>key.N</c> in reading
/// order and <c>encoder.N</c> (turn and push, no screen), so profiles, rendering and actions work
/// as with any other device. Devices seen before stay listed, unplugged, until forgotten
/// (<c>devices/openaction-known.json</c>).
/// </para>
/// </summary>
public sealed class OpenActionDevicePlugin : IDevicePlugin, IDeviceOutput, IConfigurableDevicePlugin, IDeviceSettingActions, IDeviceModelCatalog
{
    public const string IdPrefix = "openaction.";
    public const string BrightnessSetting = "brightness";
    public const string ForgetActionId = "forget";
    public const string KeysDisplayId = "keys";

    /// <summary>The size of the images sent per key; the plugin scales them to the device.</summary>
    public const int KeyImageSize = 144;

    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(500);
    private const double Margin = 24;
    private const double KeySize = 100;
    private const double KeyGap = 16;
    private const double KnobSize = 80;

    private readonly StreamDeckRuntime _runtime;
    private readonly Action<string, JsonObject> _send;
    private readonly string _directory;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Registered> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string DeviceId, string ControlId), Timer> _longPressTimers = new();
    private IReadOnlyCollection<DeviceDefinition> _definitions = [];
    private IDevicePluginContext? _context;
    private bool _disposed;

    /// <param name="directory">DeckHost's <c>devices</c> folder: the remembered devices and their settings.</param>
    public OpenActionDevicePlugin(StreamDeckRuntime runtime, string directory)
        : this(runtime, directory, null)
    {
    }

    /// <param name="send">Where messages for a plugin go (UUID, message); the runtime's, unless a test catches them.</param>
    internal OpenActionDevicePlugin(StreamDeckRuntime runtime, string directory, Action<string, JsonObject>? send)
    {
        _runtime = runtime;
        _send = send ?? runtime.SendToDevicePlugin;
        _directory = directory;
        foreach (var known in LoadKnown())
        {
            var device = new Registered(known.PluginUuid, known.Id, known.Name, known.Rows, known.Columns, known.Encoders);
            _devices[device.DeviceId] = device;
        }

        _definitions = BuildDefinitions();
        _runtime.DevicePluginMessage += OnMessage;
        _runtime.DevicePluginDisconnected += OnPluginDisconnected;
    }

    public string Id => "deckhost.device.openaction";

    public string Name => "OpenAction devices";

    public IReadOnlyCollection<DeviceDefinition> Devices => Volatile.Read(ref _definitions);

    public IDeviceOutput Output => this;

    public event EventHandler? DevicesChanged;

    public Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------- the plugin's messages

    internal void OnMessage(string pluginUuid, string eventName, JsonObject message)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var payload = message["payload"];
            switch (eventName)
            {
                case "registerDevice" when payload is JsonObject info:
                    Register(pluginUuid, info);
                    break;
                case "deregisterDevice":
                    SetConnected(DeviceIdOf(payload?.GetValueKind() == JsonValueKind.String ? payload.GetValue<string>() : null), connected: false, pluginUuid);
                    break;
                case "rerenderImages":
                    if (payload?.GetValueKind() == JsonValueKind.String && DeviceIdOf(payload.GetValue<string>()) is { } rerender)
                    {
                        _context?.RequestRedraw(rerender);
                    }

                    break;
                case "keyDown" or "keyUp" when payload is JsonObject key:
                    Report(key, "key", eventName == "keyDown" ? InputCapabilities.Press : InputCapabilities.Release, eventName == "keyDown" ? 1 : 0);
                    break;
                case "encoderDown" or "encoderUp" when payload is JsonObject knob:
                    Report(knob, "encoder", eventName == "encoderDown" ? InputCapabilities.Press : InputCapabilities.Release, eventName == "encoderDown" ? 1 : 0);
                    break;
                case "encoderChange" when payload is JsonObject turn:
                    var ticks = Math.Clamp(turn["ticks"]?.GetValue<int>() ?? 0, -32, 32);
                    for (var step = 0; step < Math.Abs(ticks); step++)
                    {
                        Report(turn, "encoder", InputCapabilities.Rotate, Math.Sign(ticks));
                    }

                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        {
            _context?.Log("Warning", $"OpenAction plugin {pluginUuid} sent a {eventName} message DeckHost couldn't read: {ex.Message}");
        }
    }

    private void Register(string pluginUuid, JsonObject info)
    {
        var id = info["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var device = new Registered(
            pluginUuid,
            id,
            info["name"]?.GetValue<string>() is { Length: > 0 } name ? name : id,
            Math.Clamp(info["rows"]?.GetValue<int>() ?? 1, 1, 16),
            Math.Clamp(info["columns"]?.GetValue<int>() ?? 1, 1, 16),
            Math.Clamp(info["encoders"]?.GetValue<int>() ?? 0, 0, 16))
        {
            Connected = true,
        };
        lock (_sync)
        {
            _devices[device.DeviceId] = device;
            Volatile.Write(ref _definitions, BuildDefinitions());
        }

        SaveKnown();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        _context?.ReportConnectionStatus(device.DeviceId, "Connected", $"{device.Name} (OpenAction plugin).");
        ApplyBrightness(device);

        // Images sent before the device existed went nowhere: send everything again.
        _context?.RequestRedraw(device.DeviceId);
    }

    internal void OnPluginDisconnected(string pluginUuid)
    {
        List<Registered> gone;
        lock (_sync)
        {
            gone = [.. _devices.Values.Where(device => device.Connected && string.Equals(device.PluginUuid, pluginUuid, StringComparison.OrdinalIgnoreCase))];
        }

        foreach (var device in gone)
        {
            SetConnected(device.DeviceId, connected: false, pluginUuid);
        }
    }

    private void SetConnected(string? deviceId, bool connected, string pluginUuid)
    {
        if (deviceId is null)
        {
            return;
        }

        lock (_sync)
        {
            if (!_devices.TryGetValue(deviceId, out var device) || device.Connected == connected)
            {
                return;
            }

            device.Connected = connected;
            Volatile.Write(ref _definitions, BuildDefinitions());
        }

        _context?.ReportConnectionStatus(deviceId, connected ? "Connected" : "Disconnected", connected ? "Connected." : "The OpenAction plugin reports the device is gone, or the plugin stopped.");
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Report(JsonObject payload, string prefix, InputCapabilities input, int value)
    {
        var deviceId = DeviceIdOf(payload["device"]?.GetValue<string>());
        var position = payload["position"]?.GetValue<int>();
        if (deviceId is null || position is null || _context is not { } context)
        {
            return;
        }

        var controlId = $"{prefix}.{position.Value.ToString(CultureInfo.InvariantCulture)}";
        if (prefix == "key" && input is InputCapabilities.Press or InputCapabilities.Release)
        {
            TrackLongPress(deviceId, controlId, input);
        }

        context.ReportInputEvent(deviceId, controlId, input, value);
    }

    /// <summary>The DeckHost device id for the plugin's id of a device we know; null for others.</summary>
    private string? DeviceIdOf(string? pluginDeviceId)
    {
        if (string.IsNullOrEmpty(pluginDeviceId))
        {
            return null;
        }

        var deviceId = IdPrefix + pluginDeviceId;
        lock (_sync)
        {
            return _devices.ContainsKey(deviceId) ? deviceId : null;
        }
    }

    private void TrackLongPress(string deviceId, string controlId, InputCapabilities input)
    {
        var key = (deviceId, controlId);
        if (_longPressTimers.TryRemove(key, out var previous))
        {
            previous.Dispose();
        }

        if (input != InputCapabilities.Press)
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

    // ---------------------------------------------------------------- the descriptor

    private DeviceDefinition[] BuildDefinitions() =>
        [.. _devices.Values.OrderBy(static device => device.DeviceId, StringComparer.Ordinal).Select(Describe)];

    /// <summary>The device as DeckHost shows it: keys in rows, knobs below.</summary>
    internal static DeviceDefinition Describe(Registered device)
    {
        var controls = new List<PhysicalControl>();
        for (var index = 0; index < device.Rows * device.Columns; index++)
        {
            controls.Add(new PhysicalControl(
                $"key.{index}",
                $"Key {index + 1}",
                ControlType.Key,
                new Rect(Margin + (index % device.Columns * (KeySize + KeyGap)), Margin + (index / device.Columns * (KeySize + KeyGap)), KeySize, KeySize),
                InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress,
                OutputCapabilities.ColorImage | OutputCapabilities.Text,
                KeyImageSize,
                KeyImageSize,
                index.ToString(CultureInfo.InvariantCulture))
            {
                DisplayId = KeysDisplayId,
            });
        }

        var knobTop = Margin + (device.Rows * (KeySize + KeyGap)) + 10;
        var knobSpacing = Math.Max(KnobSize + KeyGap, ((device.Columns * (KeySize + KeyGap)) - KeyGap) / Math.Max(1, device.Encoders));
        for (var index = 0; index < device.Encoders; index++)
        {
            controls.Add(new PhysicalControl(
                $"encoder.{index}",
                $"Knob {index + 1}",
                ControlType.Encoder,
                new Rect(Margin + (index * knobSpacing), knobTop, KnobSize, KnobSize),
                InputCapabilities.Rotate | InputCapabilities.Press | InputCapabilities.Release,
                OutputCapabilities.None,
                HardwareId: index.ToString(CultureInfo.InvariantCulture))
            {
                Shape = ControlShape.Round,
            });
        }

        var identity = new DeviceIdentity(null, device.Name, null, null, null, "OpenAction device plugin");
        return new DeviceDefinition(device.DeviceId, device.Name, controls, device.Connected, $"Plugin {device.PluginUuid}")
        {
            Identity = identity,
            Displays = [new DeviceDisplay(KeysDisplayId, device.Columns * KeyImageSize, device.Rows * KeyImageSize) { ControlIds = [.. controls.Where(static control => control.DisplayId == KeysDisplayId).Select(static control => control.Id)] }],
            Capabilities = DeviceCapabilities.Images | DeviceCapabilities.Brightness | DeviceCapabilities.Settings,
            Limits = new DeviceLimits { Notes = ["Driven by an OpenAction device plugin: the plugin decides the real key size and what the device can do."] },
        };
    }

    public IReadOnlyList<DeviceDefinition> DescribeModels() => [.. Devices.Select(static device => device with { IsConnected = false })];

    // ---------------------------------------------------------------- output

    public bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, out string? error)
    {
        if (!TryGet(deviceId, out var device, out error))
        {
            return false;
        }

        if (!controlId.StartsWith("key.", StringComparison.Ordinal) || !int.TryParse(controlId.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var position))
        {
            error = $"'{controlId}' has no image output.";
            return false;
        }

        var jpeg = EncodeJpeg(rgba, width, height);
        if (jpeg is null)
        {
            error = "the image could not be encoded";
            return false;
        }

        _send(device.PluginUuid, new JsonObject
        {
            ["event"] = "setImage",
            ["device"] = device.PluginDeviceId,
            ["controller"] = "Keypad",
            ["position"] = position,
            ["image"] = "data:image/jpeg;base64," + Convert.ToBase64String(jpeg),
        });
        return true;
    }

    public bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, out string? error)
    {
        error = "this device has no lights DeckHost can drive";
        return false;
    }

    public void Clear(string deviceId)
    {
        if (!TryGet(deviceId, out var device, out _))
        {
            return;
        }

        // No position, no image: every key is cleared.
        _send(device.PluginUuid, new JsonObject
        {
            ["event"] = "setImage",
            ["device"] = device.PluginDeviceId,
            ["controller"] = null,
            ["position"] = null,
            ["image"] = null,
        });
    }

    /// <summary>The image as a JPEG, flattened on black (the plugins take JPEG, which has no transparency).</summary>
    internal static byte[]? EncodeJpeg(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0 || rgba.Length < width * height * 4)
        {
            return null;
        }

        using var source = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(rgba[..(width * height * 4)].ToArray(), 0, source.GetPixels(), width * height * 4);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        surface.Canvas.Clear(SKColors.Black);
        surface.Canvas.DrawBitmap(source, 0, 0);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data?.ToArray();
    }

    private bool TryGet(string deviceId, out Registered device, out string? error)
    {
        lock (_sync)
        {
            if (_devices.TryGetValue(deviceId, out var found) && found.Connected)
            {
                device = found;
                error = null;
                return true;
            }
        }

        device = null!;
        error = "device not connected";
        return false;
    }

    // ---------------------------------------------------------------- settings

    public IReadOnlyList<ActionSettingDefinition> GetDeviceSettingDefinitions(string deviceId) =>
    [
        new(BrightnessSetting, "Screen brightness", ActionSettingKind.Number, "100") { Range = new NumberRange(0, 100) { Unit = "%" } },
    ];

    public ActionSettings GetDeviceSettings(string deviceId)
    {
        try
        {
            return File.Exists(SettingsPath(deviceId)) ? JsonSerializer.Deserialize<ActionSettings>(File.ReadAllText(SettingsPath(deviceId))) ?? ActionSettings.Empty : ActionSettings.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
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
            return ex.Message;
        }

        if (TryGet(deviceId, out var device, out _))
        {
            ApplyBrightness(device);
        }

        return null;
    }

    private void ApplyBrightness(Registered device)
    {
        var brightness = Math.Clamp(GetDeviceSettings(device.DeviceId).GetInt(BrightnessSetting, 100), 0, 100);
        _send(device.PluginUuid, new JsonObject
        {
            ["event"] = "setBrightness",
            ["device"] = device.PluginDeviceId,
            ["brightness"] = brightness,
        });
    }

    public IReadOnlyList<DeviceSettingAction> GetSettingActions(string deviceId)
    {
        lock (_sync)
        {
            return _devices.TryGetValue(deviceId, out var device) && !device.Connected
                ? [new DeviceSettingAction(ForgetActionId, "Forget this device", "Removes it from the device list until the plugin reports it again. Its profiles are kept for then.", $"Forget {device.Name}?")]
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
            if (!_devices.TryGetValue(deviceId, out var device) || device.Connected)
            {
                return "Only an unplugged device can be forgotten.";
            }

            _devices.Remove(deviceId);
            Volatile.Write(ref _definitions, BuildDefinitions());
        }

        SaveKnown();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        return "Forgotten. It comes back when its plugin reports it.";
    }

    private string SettingsPath(string deviceId) =>
        Path.Combine(_directory, string.Concat(deviceId.Select(static c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json");

    // ---------------------------------------------------------------- remembered devices

    private string KnownPath => Path.Combine(_directory, "openaction-known.json");

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
                known = [.. _devices.Values.Select(static device => new KnownDevice(device.PluginUuid, device.PluginDeviceId, device.Name, device.Rows, device.Columns, device.Encoders))];
            }

            Directory.CreateDirectory(_directory);
            File.WriteAllText(KnownPath, JsonSerializer.Serialize(known));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _context?.Log("Warning", $"Could not remember the OpenAction devices: {ex.Message}");
        }
    }

    private sealed record KnownDevice(string PluginUuid, string Id, string Name, int Rows, int Columns, int Encoders);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtime.DevicePluginMessage -= OnMessage;
        _runtime.DevicePluginDisconnected -= OnPluginDisconnected;
        foreach (var timer in _longPressTimers.Values)
        {
            timer.Dispose();
        }
    }

    /// <summary>A device a plugin registered (or one remembered from before).</summary>
    internal sealed class Registered(string pluginUuid, string pluginDeviceId, string name, int rows, int columns, int encoders)
    {
        public string PluginUuid { get; } = pluginUuid;

        /// <summary>The id the plugin uses (<c>{DeviceNamespace}-{serial}</c>).</summary>
        public string PluginDeviceId { get; } = pluginDeviceId;

        public string DeviceId { get; } = IdPrefix + pluginDeviceId;

        public string Name { get; } = name;

        public int Rows { get; } = rows;

        public int Columns { get; } = columns;

        public int Encoders { get; } = encoders;

        public bool Connected { get; set; }
    }
}
