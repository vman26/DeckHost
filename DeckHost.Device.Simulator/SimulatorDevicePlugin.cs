using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeckHost.Device.Simulator;

/// <summary>
/// Simulated devices: a simulator window (<c>DeckHost --simulator</c>) connects over loopback,
/// picks one of the models the other device plugins describe (<see cref="IDeviceModelCatalog"/>) and
/// appears here as a connected device with that model's controls, displays and lights. DeckHost
/// sends it the images, lights and backdrop it would send the hardware; the window sends presses,
/// turns, touches and swipes back. Everything above the device plugin (profiles, rendering,
/// actions, lights) runs as with real hardware.
/// <para>
/// The endpoint's port and a random token go to <c>simulator.json</c> in DeckHost's data folder;
/// only a process of the same user that can read it can connect, and only while the endpoint is
/// switched on (DeckHost settings → Developer → Device simulator; <see cref="SetEnabled"/>). A
/// simulated device's id is <c>simulator.{model id}</c> (then <c>-2</c>, …), so its profiles are
/// its own; profiles copy from the real device with Profiles → Copy to. It goes away when its
/// window closes; opening the same model again brings back the id and its profiles.
/// </para>
/// </summary>
public sealed class SimulatorDevicePlugin : IDevicePlugin, IDeviceOutput, IDeviceBackdropOutput, IDeviceLightBatchOutput, IConfigurableDevicePlugin
{
    public const string IdPrefix = "simulator.";
    public const string BrightnessSetting = "brightness";
    public const string VibrateSetting = "vibrate";

    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(500);

    private readonly string _directory;
    private readonly Func<IEnumerable<IDevicePlugin>>? _plugins;
    private readonly Lock _sync = new();
    private readonly List<SimulatedDevice> _devices = [];
    private readonly ConcurrentDictionary<(string DeviceId, string ControlId), Timer> _longPressTimers = new();
    private CancellationTokenSource? _session;
    private IReadOnlyCollection<DeviceDefinition> _definitions = [];
    private IDevicePluginContext? _context;
    private TcpListener? _listener;
    private string _token = string.Empty;
    private bool _disposed;
    private bool _enabled;

    /// <param name="directory">DeckHost's data folder (by default %AppData%/DeckHost): the endpoint file goes there, device settings under <c>devices/</c>.</param>
    /// <param name="plugins">
    /// The device plugins whose models can be simulated (read when a simulator connects). By default the ones the host runs
    /// (<see cref="IDevicePluginContext.GetDevicePlugins"/>), which is how DeckHost loads it: a bundled package.
    /// </param>
    /// <param name="enabled">
    /// Listen for simulators; see <see cref="SetEnabled"/>. DeckHost settings → Developer → Device simulator turns the whole
    /// plugin on and off in the Plugin Manager instead.
    /// </param>
    public SimulatorDevicePlugin(string? directory = null, Func<IEnumerable<IDevicePlugin>>? plugins = null, bool enabled = true)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost");
        _plugins = plugins;
        _enabled = enabled;
    }

    /// <summary>Whether simulators can connect.</summary>
    public bool IsEnabled => _enabled;

    public string Id => "deckhost.device.simulator";

    public string Name => "Device simulator";

    public IReadOnlyCollection<DeviceDefinition> Devices => Volatile.Read(ref _definitions);

    public IDeviceOutput Output => this;

    public event EventHandler? DevicesChanged;

    /// <summary>The port the endpoint listens on (0 before <see cref="InitializeAsync"/>).</summary>
    public int Port => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;

    public Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context = context;
        lock (_sync)
        {
            if (_enabled)
            {
                StartListening();
            }
            else
            {
                // A file left by a DeckHost that didn't close cleanly would send simulators to a dead port.
                StopListening();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Turns the endpoint on or off. Off closes every simulator window's connection (its device
    /// goes away) and removes the endpoint file, so nothing can connect.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (_enabled == enabled || _disposed)
            {
                return;
            }

            _enabled = enabled;
            if (_context is null)
            {
                return; // Started by InitializeAsync.
            }

            if (enabled)
            {
                StartListening();
            }
            else
            {
                StopListening();
            }
        }
    }

    /// <summary>Must hold <see cref="_sync"/>.</summary>
    private void StartListening()
    {
        if (_listener is not null)
        {
            return;
        }

        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _session = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        try
        {
            SimulatorWire.WriteEndpoint(_directory, new SimulatorEndpoint(Port, _token));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _context?.Log("Warning", $"The device simulator can't connect: {ex.Message}");
        }

        var listener = _listener;
        var session = _session.Token;
        _ = Task.Run(() => AcceptLoopAsync(listener, session), CancellationToken.None);
    }

    /// <summary>Must hold <see cref="_sync"/>. The connections end by themselves when the session is cancelled.</summary>
    private void StopListening()
    {
        // Cancelled, not disposed: connections still hold its token while they close.
        _session?.Cancel();
        _session = null;
        _listener?.Stop();
        _listener = null;
        try
        {
            // Only one DeckHost runs per user, so whatever file is there is ours or a stale one.
            File.Delete(Path.Combine(_directory, SimulatorWire.EndpointFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Every model the other device plugins describe.</summary>
    public IReadOnlyList<SimulatorModel> Models()
    {
        var models = new List<SimulatorModel>();
        foreach (var plugin in _plugins?.Invoke() ?? _context?.GetDevicePlugins() ?? [])
        {
            if (ReferenceEquals(plugin, this) || plugin is not IDeviceModelCatalog catalog)
            {
                continue;
            }

            try
            {
                models.AddRange(catalog.DescribeModels().Select(device => new SimulatorModel($"{plugin.Id}/{device.Id}", plugin.Name, device with { IsConnected = false })));
            }
            catch (Exception ex)
            {
                _context?.Log("Warning", $"{plugin.Name} could not describe its models: {ex.Message}");
            }
        }

        return models;
    }

    // ---------------------------------------------------------------- connections

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken session)
    {
        while (!session.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(session).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client, session), CancellationToken.None);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken session)
    {
        client.NoDelay = true;
        var connection = new SimulatorConnection(client.GetStream());
        SimulatedDevice? device = null;
        try
        {
            var hello = await ReadWithTimeoutAsync(connection, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            if (hello?.Type != SimulatorMessage.Hello || !TokenMatches(hello.Token))
            {
                connection.Send(new SimulatorMessage { Type = SimulatorMessage.Failure, Error = "DeckHost didn't accept this simulator (wrong token)." });
                return;
            }

            var models = Models();
            connection.Send(new SimulatorMessage { Type = SimulatorMessage.ModelList, Models = [.. models] });

            while (await connection.ReadAsync(session).ConfigureAwait(false) is { } message)
            {
                switch (message.Type)
                {
                    case SimulatorMessage.Attach:
                        var model = Models().FirstOrDefault(candidate => candidate.Key == message.Model);
                        if (model is null)
                        {
                            connection.Send(new SimulatorMessage { Type = SimulatorMessage.Failure, Error = $"DeckHost has no model '{message.Model}'." });
                            break;
                        }

                        if (device is not null)
                        {
                            Detach(device);
                        }

                        device = AttachDevice(model, connection);
                        break;

                    case SimulatorMessage.Input when device is not null && message.Control is { } control:
                        HandleInput(device, control, message.Event, message.Value);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
        finally
        {
            if (device is not null)
            {
                Detach(device);
            }

            // The connection first: it writes what is still queued (a refusal) before the socket closes.
            await connection.DisposeAsync().ConfigureAwait(false);
            client.Dispose();
        }
    }

    private static async Task<SimulatorMessage?> ReadWithTimeoutAsync(SimulatorConnection connection, TimeSpan timeout)
    {
        using var timer = new CancellationTokenSource(timeout);
        try
        {
            return await connection.ReadAsync(timer.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private bool TokenMatches(string? token) =>
        token is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(_token));

    private SimulatedDevice AttachDevice(SimulatorModel model, SimulatorConnection connection)
    {
        SimulatedDevice device;
        lock (_sync)
        {
            var baseId = IdPrefix + model.Device.Id;
            // The first free id: the same model opened again gets its id, and so its profiles, back.
            var id = baseId;
            for (var number = 2; _devices.Any(candidate => string.Equals(candidate.Definition.Id, id, StringComparison.OrdinalIgnoreCase)); number++)
            {
                id = $"{baseId}-{number}";
            }

            device = new SimulatedDevice(model.Key, model.Device with
            {
                Id = id,
                Name = $"Simulated {model.Device.Name}",
                Detail = $"Simulator ({model.Plugin})",
            })
            {
                Connection = connection,
            };
            _devices.Add(device);
            Publish();
        }

        var settings = GetDeviceSettings(device.Id);
        connection.Send(new SimulatorMessage { Type = SimulatorMessage.Attached, Model = model.Key, DeviceId = device.Id, Device = device.Current });
        connection.Send(SettingsMessage(device, settings));
        _context?.ReportConnectionStatus(device.Id, "Connected", "Simulator window open.");
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        return device;
    }

    /// <summary>The window closed (or became another model): the simulated device goes away; its profiles stay for next time.</summary>
    private void Detach(SimulatedDevice device)
    {
        lock (_sync)
        {
            device.Connection = null;
            _devices.Remove(device);
            Publish();
        }

        _context?.ReportConnectionStatus(device.Id, "Disconnected", "The simulator window was closed.");
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Must hold <see cref="_sync"/>.</summary>
    private void Publish() => Volatile.Write(ref _definitions, [.. _devices.Select(static device => device.Current)]);

    private void HandleInput(SimulatedDevice device, string controlId, InputCapabilities input, int value)
    {
        var control = device.Definition.FindControl(controlId);
        if (control is null || input == InputCapabilities.None || !control.Inputs.HasFlag(input) || _context is not { } context)
        {
            return;
        }

        if (control.Inputs.HasFlag(InputCapabilities.LongPress) && input is InputCapabilities.Press or InputCapabilities.Release)
        {
            TrackLongPress(device.Id, controlId, input);
        }

        context.ReportInputEvent(device.Id, controlId, input, value);
    }

    /// <summary>Raises LongPress when a key stays down for <see cref="LongPressDelay"/>, as the hardware plugins do.</summary>
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

    // ---------------------------------------------------------------- output

    public bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, out string? error)
    {
        if (!TryConnection(deviceId, out var connection, out error))
        {
            return false;
        }

        connection.Send(new SimulatorMessage { Type = SimulatorMessage.Image, Control = controlId, Width = width, Height = height, Rgba = rgba.ToArray() });
        return true;
    }

    public bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, out string? error) =>
        TrySetLights(deviceId, [new LightColor(controlId, red, green, blue)], out error);

    public bool TrySetLights(string deviceId, IReadOnlyList<LightColor> lights, out string? error)
    {
        if (!TryConnection(deviceId, out var connection, out error))
        {
            return false;
        }

        connection.Send(new SimulatorMessage
        {
            Type = SimulatorMessage.LightsChanged,
            Lights = [.. lights.Select(static light => new SimulatorLight(light.ControlId, light.Red, light.Green, light.Blue))],
        });
        return true;
    }

    public bool TrySetBackdrop(string deviceId, byte red, byte green, byte blue, out string? error)
    {
        if (!TryConnection(deviceId, out var connection, out error))
        {
            return false;
        }

        connection.Send(new SimulatorMessage { Type = SimulatorMessage.Backdrop, R = red, G = green, B = blue });
        return true;
    }

    public void Clear(string deviceId)
    {
        if (TryConnection(deviceId, out var connection, out _))
        {
            connection.Send(new SimulatorMessage { Type = SimulatorMessage.Clear });
        }
    }

    private bool TryConnection(string deviceId, out SimulatorConnection connection, out string? error)
    {
        lock (_sync)
        {
            if (Find(deviceId)?.Connection is { } open)
            {
                connection = open;
                error = null;
                return true;
            }
        }

        connection = null!;
        error = "simulator window not open";
        return false;
    }

    /// <summary>Must hold <see cref="_sync"/>.</summary>
    private SimulatedDevice? Find(string deviceId) =>
        _devices.FirstOrDefault(device => string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- settings

    public IReadOnlyList<ActionSettingDefinition> GetDeviceSettingDefinitions(string deviceId)
    {
        DeviceDefinition? definition;
        lock (_sync)
        {
            definition = Find(deviceId)?.Definition;
        }

        var definitions = new List<ActionSettingDefinition>
        {
            new(BrightnessSetting, "Screen brightness", ActionSettingKind.Number, "100") { Range = new NumberRange(0, 100) { Unit = "%" } },
        };
        if (definition?.Has(DeviceCapabilities.Haptics) == true)
        {
            definitions.Add(new(VibrateSetting, "Vibrate when a control is used", ActionSettingKind.Choice, "on", Options: [new("on", "On"), new("off", "Off")]));
        }

        return definitions;
    }

    public ActionSettings GetDeviceSettings(string deviceId)
    {
        try
        {
            var path = SettingsPath(deviceId);
            return File.Exists(path) ? JsonSerializer.Deserialize<ActionSettings>(File.ReadAllText(path)) ?? ActionSettings.Empty : ActionSettings.Empty;
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
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath(deviceId))!);
            File.WriteAllText(SettingsPath(deviceId), JsonSerializer.Serialize(settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        SimulatedDevice? device;
        lock (_sync)
        {
            device = Find(deviceId);
        }

        if (device?.Connection is { } connection)
        {
            connection.Send(SettingsMessage(device, settings));
        }

        return null;
    }

    private static SimulatorMessage SettingsMessage(SimulatedDevice device, ActionSettings settings) => new()
    {
        Type = SimulatorMessage.Settings,
        Brightness = Math.Clamp(settings.GetInt(BrightnessSetting, 100), 0, 100),
        Vibrate = device.Definition.Has(DeviceCapabilities.Haptics) && settings.Get(VibrateSetting, "on") != "off",
    };

    private string SettingsPath(string deviceId) =>
        Path.Combine(_directory, "devices", string.Concat(deviceId.Select(static c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_sync)
        {
            StopListening();
        }

        foreach (var timer in _longPressTimers.Values)
        {
            timer.Dispose();
        }
    }

    private sealed class SimulatedDevice(string modelKey, DeviceDefinition definition)
    {
        public string ModelKey { get; } = modelKey;

        public DeviceDefinition Definition { get; } = definition;

        public string Id => Definition.Id;

        public SimulatorConnection? Connection { get; set; }

        public DeviceDefinition Current => Definition with { IsConnected = Connection is not null };
    }
}
