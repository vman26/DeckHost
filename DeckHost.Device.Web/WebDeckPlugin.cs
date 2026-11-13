using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using SkiaSharp;

namespace DeckHost.Device.Web;

/// <summary>
/// A deck in a web browser: a phone or tablet on the same network, or a browser window on this
/// computer (an on-screen deck). DeckHost serves a page with the keys over HTTP and keeps it up to
/// date over a WebSocket. Off until switched on in Device Settings; every page needs the pairing
/// code in its link, so others on the network can't press keys.
/// </summary>
public sealed class WebDeckPlugin : IDevicePlugin, IDeviceOutput, IConfigurableDevicePlugin
{
    public const string DeviceId = "web.deck";
    public const string Sharing = "sharing";
    public const string Columns = "columns";
    public const string Rows = "rows";
    public const string Port = "port";
    public const string Code = "code";

    public const string SharingOff = "off";
    public const string SharingLocal = "local";
    public const string SharingNetwork = "network";

    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private const int DefaultPort = 9123;
    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(500);

    private readonly Lock _sync = new();
    private readonly string _settingsPath;
    private readonly ConcurrentDictionary<string, byte[]> _images = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Client, byte> _clients = new();
    private readonly ConcurrentDictionary<string, Timer> _longPressTimers = new(StringComparer.Ordinal);
    private readonly byte[] _page;
    private IReadOnlyCollection<DeviceDefinition> _devices;
    private IDevicePluginContext? _context;
    private TcpListener? _listener;
    private CancellationTokenSource? _stopping;
    private ActionSettings _settings;
    private bool _disposed;

    public WebDeckPlugin(string? directory = null)
    {
        _settingsPath = Path.Combine(
            directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost", "devices"),
            DeviceId + ".json");
        _settings = LoadSettings();
        using var resource = typeof(WebDeckPlugin).Assembly.GetManifestResourceStream("deck.html")!;
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        _page = buffer.ToArray();
        _devices = [Definition()];
    }

    public event EventHandler? DevicesChanged;

    public string Id => "deckhost.device.web";

    public string Name => "Web Deck";

    public IReadOnlyCollection<DeviceDefinition> Devices => Volatile.Read(ref _devices);

    public IDeviceOutput Output => this;

    /// <summary>The port the server listens on while it runs.</summary>
    public int? ListeningPort { get; private set; }

    /// <summary>The link to open on a phone (or here), with the pairing code; null while sharing is off.</summary>
    public string? Link => ListeningPort is { } port
        ? $"http://{(SharingMode == SharingNetwork ? LocalAddress() ?? "localhost" : "localhost")}:{port}/?code={Uri.EscapeDataString(PairingCode)}"
        : null;

    private string SharingMode => _settings.Get(Sharing, SharingOff);

    private string PairingCode => _settings.Get(Code);

    public Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        Restart();
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------- settings

    public IReadOnlyList<ActionSettingDefinition> GetDeviceSettingDefinitions(string deviceId) =>
    [
        new(Sharing, "Show the deck in a browser", ActionSettingKind.Choice, SharingOff,
            Description: Link is { } link
                ? $"Open this link on your phone or in a browser: {link}"
                : "\"This computer\" gives an on-screen deck in a browser window; \"Phones and tablets\" lets devices on your network open it with the pairing link.",
            Options:
            [
                new(SharingOff, "Off"),
                new(SharingLocal, "This computer only"),
                new(SharingNetwork, "Phones and tablets on this network"),
            ]),
        new(Columns, "Columns", ActionSettingKind.Number, "5") { Range = new NumberRange(WebDeckLayout.MinSize, WebDeckLayout.MaxColumns) },
        new(Rows, "Rows", ActionSettingKind.Number, "3") { Range = new NumberRange(WebDeckLayout.MinSize, WebDeckLayout.MaxRows) },
        new(Port, "Port", ActionSettingKind.Number, DefaultPort.ToString(CultureInfo.InvariantCulture)),
        new(Code, "Pairing code", ActionSettingKind.Text, PairingCode,
            Description: "Part of the link. Change it to lock out phones that had the old link."),
    ];

    public ActionSettings GetDeviceSettings(string deviceId) => _settings;

    public string? ApplyDeviceSettings(string deviceId, ActionSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Get(Code)))
        {
            settings = settings.With(Code, NewCode());
        }

        _settings = settings;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not save the settings: {ex.Message}";
        }

        var error = Restart();
        return error;
    }

    // ---------------------------------------------------------------- output

    public bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, out string? error)
    {
        error = null;
        var png = EncodePng(rgba, width, height);
        _images[controlId] = png;
        Broadcast(WebDeckLayout.ImageMessage(controlId, png));
        return true;
    }

    public bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, out string? error)
    {
        error = null;
        return true;
    }

    public void Clear(string deviceId)
    {
        _images.Clear();
        Broadcast("{\"type\":\"clear\"}");
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
        foreach (var timer in _longPressTimers.Values)
        {
            timer.Dispose();
        }
    }

    // ---------------------------------------------------------------- server

    /// <summary>Starts, restarts or stops the server for the current settings. Returns an error, or null.</summary>
    private string? Restart()
    {
        Stop();
        string? error = null;
        var mode = SharingMode;
        if (mode != SharingOff && !_disposed)
        {
            var port = Math.Clamp(_settings.GetInt(Port, DefaultPort), 1, 65535);
            try
            {
                var listener = new TcpListener(mode == SharingNetwork ? IPAddress.Any : IPAddress.Loopback, port);
                listener.Start();
                var stopping = new CancellationTokenSource();
                lock (_sync)
                {
                    _listener = listener;
                    _stopping = stopping;
                    ListeningPort = port;
                }

                _ = Task.Run(() => AcceptLoopAsync(listener, stopping.Token));
                _context?.Log("Info", $"Web deck: {Link}");
            }
            catch (SocketException ex)
            {
                error = $"Port {port} can't be used ({ex.Message}). Choose another port.";
                _context?.Log("Warning", $"Web deck: {error}");
            }
        }

        Volatile.Write(ref _devices, [Definition()]);
        _context?.ReportConnectionStatus(DeviceId, ListeningPort is null ? "Disconnected" : "Connected", Link ?? "Switched off in Device Settings.");
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        if (ListeningPort is not null)
        {
            _context?.RequestRedraw(DeviceId);
        }

        return error;
    }

    private void Stop()
    {
        lock (_sync)
        {
            _stopping?.Cancel();
            _listener?.Stop();
            _listener = null;
            _stopping = null;
            ListeningPort = null;
        }

        foreach (var client in _clients.Keys)
        {
            client.Abort();
        }

        _clients.Clear();
    }

    private DeviceDefinition Definition() => new(
        DeviceId,
        "Web Deck",
        WebDeckLayout.Controls(ColumnCount, RowCount),
        ListeningPort is not null,
        Link ?? "Off");

    private int ColumnCount => Math.Clamp(_settings.GetInt(Columns, 5), WebDeckLayout.MinSize, WebDeckLayout.MaxColumns);

    private int RowCount => Math.Clamp(_settings.GetInt(Rows, 3), WebDeckLayout.MinSize, WebDeckLayout.MaxRows);

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stopping).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client, stopping));
        }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken stopping)
    {
        using (tcp)
        {
            try
            {
                var stream = tcp.GetStream();
                var (method, target, headers) = await ReadRequestHeadAsync(stream, stopping).ConfigureAwait(false);
                if (method != "GET")
                {
                    await WriteResponseAsync(stream, 405, "text/plain", "Method Not Allowed"u8.ToArray()).ConfigureAwait(false);
                    return;
                }

                var path = target.Split('?')[0];
                if (!IsPaired(target))
                {
                    await WriteResponseAsync(stream, 403, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(
                        "<!doctype html><meta name=viewport content=\"width=device-width\"><body style=\"font-family:sans-serif;background:#0b0f17;color:#e2e8f0;padding:24px\"><h2>DeckHost</h2><p>Open the link from DeckHost → Device Settings → Web Deck (it contains the pairing code).</p>")).ConfigureAwait(false);
                    return;
                }

                if (path == "/ws" && headers.TryGetValue("sec-websocket-key", out var key))
                {
                    var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + WebSocketGuid)));
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), stopping).ConfigureAwait(false);
                    using var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(20) });
                    await RunClientAsync(new Client(socket), stopping).ConfigureAwait(false);
                    return;
                }

                if (path is "/" or "/index.html")
                {
                    await WriteResponseAsync(stream, 200, "text/html; charset=utf-8", _page).ConfigureAwait(false);
                    return;
                }

                await WriteResponseAsync(stream, 404, "text/plain", "Not Found"u8.ToArray()).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or WebSocketException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private bool IsPaired(string target)
    {
        var query = target.Contains('?', StringComparison.Ordinal) ? target[(target.IndexOf('?', StringComparison.Ordinal) + 1)..] : string.Empty;
        var given = query.Split('&')
            .Select(static pair => pair.Split('=', 2))
            .Where(static pair => pair.Length == 2 && pair[0] == "code")
            .Select(static pair => Uri.UnescapeDataString(pair[1]))
            .FirstOrDefault() ?? string.Empty;
        var expected = PairingCode;
        return expected.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));
    }

    private async Task RunClientAsync(Client client, CancellationToken stopping)
    {
        _clients[client] = 0;
        try
        {
            client.Send(WebDeckLayout.LayoutMessage("DeckHost", ColumnCount, RowCount));
            foreach (var (controlId, png) in _images)
            {
                client.Send(WebDeckLayout.ImageMessage(controlId, png));
            }

            var buffer = new byte[4096];
            var message = new MemoryStream();
            while (client.Socket.State == WebSocketState.Open && !stopping.IsCancellationRequested)
            {
                var result = await client.Socket.ReceiveAsync(buffer, stopping).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Write(buffer, 0, result.Count);
                if (message.Length > 64 * 1024)
                {
                    break;
                }

                if (!result.EndOfMessage)
                {
                    continue;
                }

                var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                if (WebDeckLayout.TryParseInput(text, ColumnCount * RowCount, out var controlId, out var input))
                {
                    TrackLongPress(controlId, input);
                    _context?.ReportInputEvent(DeviceId, controlId, input, input == InputCapabilities.Press ? 1 : 0);
                }
            }
        }
        finally
        {
            _clients.TryRemove(client, out _);
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void Broadcast(string message)
    {
        foreach (var client in _clients.Keys)
        {
            client.Send(message);
        }
    }

    private void TrackLongPress(string controlId, InputCapabilities input)
    {
        if (_longPressTimers.TryRemove(controlId, out var previous))
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
            if (_longPressTimers.TryRemove(new KeyValuePair<string, Timer>(controlId, timer!)))
            {
                timer!.Dispose();
                _context?.ReportInputEvent(DeviceId, controlId, InputCapabilities.LongPress, 1);
            }
        });
        _longPressTimers[controlId] = timer;
        timer.Change(LongPressDelay, Timeout.InfiniteTimeSpan);
    }

    private ActionSettings LoadSettings()
    {
        ActionSettings settings;
        try
        {
            settings = File.Exists(_settingsPath) ? JsonSerializer.Deserialize<ActionSettings>(File.ReadAllText(_settingsPath)) ?? ActionSettings.Empty : ActionSettings.Empty;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            settings = ActionSettings.Empty;
        }

        return string.IsNullOrWhiteSpace(settings.Get(Code)) ? settings.With(Code, NewCode()) : settings;
    }

    internal static string NewCode() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));

    /// <summary>This computer's address on the local network (the first up, non-loopback IPv4).</summary>
    private static string? LocalAddress()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(static adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(static adapter => adapter.GetIPProperties().UnicastAddresses)
                .Select(static address => address.Address)
                .FirstOrDefault(static address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                ?.ToString();
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    internal static byte[] EncodePng(ReadOnlySpan<byte> rgba, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        rgba[..Math.Min(rgba.Length, info.BytesSize)].CopyTo(bitmap.GetPixelSpan());
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<(string Method, string Target, Dictionary<string, string> Headers)> ReadRequestHeadAsync(NetworkStream stream, CancellationToken stopping)
    {
        var buffer = new List<byte>();
        var single = new byte[1];
        while (buffer.Count < 16 * 1024)
        {
            if (await stream.ReadAsync(single, stopping).ConfigureAwait(false) == 0)
            {
                throw new InvalidDataException("Connection closed.");
            }

            buffer.Add(single[0]);
            if (buffer.Count >= 4 && buffer[^4] == '\r' && buffer[^3] == '\n' && buffer[^2] == '\r' && buffer[^1] == '\n')
            {
                break;
            }
        }

        var lines = Encoding.ASCII.GetString([.. buffer]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        var parts = (lines.FirstOrDefault() ?? string.Empty).Split(' ');
        return (parts.Length > 0 ? parts[0] : string.Empty, parts.Length > 1 ? parts[1] : "/", headers);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string contentType, byte[] body)
    {
        var reason = status switch { 200 => "OK", 403 => "Forbidden", 404 => "Not Found", _ => "Error" };
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head)).ConfigureAwait(false);
        await stream.WriteAsync(body).ConfigureAwait(false);
    }

    /// <summary>One open page. Messages go out in order through one queue, so a slow phone never blocks the host.</summary>
    private sealed class Client : IAsyncDisposable
    {
        private readonly System.Threading.Channels.Channel<string> _outgoing =
            System.Threading.Channels.Channel.CreateBounded<string>(new System.Threading.Channels.BoundedChannelOptions(256) { FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest });

        private readonly Task _sender;

        public Client(WebSocket socket)
        {
            Socket = socket;
            _sender = Task.Run(SendLoopAsync);
        }

        public WebSocket Socket { get; }

        public void Send(string message) => _outgoing.Writer.TryWrite(message);

        public void Abort() => Socket.Abort();

        public async ValueTask DisposeAsync()
        {
            _outgoing.Writer.TryComplete();
            try
            {
                await _sender.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or IOException)
            {
            }
        }

        private async Task SendLoopAsync()
        {
            await foreach (var message in _outgoing.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (Socket.State != WebSocketState.Open)
                {
                    return;
                }

                await Socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
