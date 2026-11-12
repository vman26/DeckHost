using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace DeckHost.Core.StreamDeck;

/// <summary>
/// Minimal local server on 127.0.0.1 for Stream Deck plugins: upgrades WebSocket requests (the
/// plugin and property-inspector connections) and serves static files (property inspector pages).
/// Only loopback connections are possible, so nothing is exposed to the network.
/// </summary>
internal sealed class StreamDeckServer : IDisposable
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Func<WebSocket, Task> _onWebSocket;
    private readonly Func<string, (byte[] Body, string ContentType)?> _onGet;

    public StreamDeckServer(Func<WebSocket, Task> onWebSocket, Func<string, (byte[] Body, string ContentType)?> onGet)
    {
        _onWebSocket = onWebSocket;
        _onGet = onGet;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Stop();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stopping.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var (requestLine, headers) = await ReadRequestHeadAsync(stream).ConfigureAwait(false);
                var parts = requestLine.Split(' ');
                if (parts.Length < 2 || parts[0] != "GET")
                {
                    await WriteResponseAsync(stream, 405, "text/plain", "Method Not Allowed"u8.ToArray()).ConfigureAwait(false);
                    return;
                }

                if (headers.TryGetValue("upgrade", out var upgrade) && upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase) &&
                    headers.TryGetValue("sec-websocket-key", out var key))
                {
                    var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + WebSocketGuid)));
                    var response = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response)).ConfigureAwait(false);
                    using var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(30) });
                    await _onWebSocket(socket).ConfigureAwait(false);
                    return;
                }

                var content = _onGet(Uri.UnescapeDataString(parts[1]));
                if (content is { } file)
                {
                    await WriteResponseAsync(stream, 200, file.ContentType, file.Body).ConfigureAwait(false);
                }
                else
                {
                    await WriteResponseAsync(stream, 404, "text/plain", "Not Found"u8.ToArray()).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or WebSocketException or InvalidDataException or OperationCanceledException)
            {
            }
        }
    }

    private static async Task<(string RequestLine, Dictionary<string, string> Headers)> ReadRequestHeadAsync(NetworkStream stream)
    {
        var buffer = new List<byte>();
        var single = new byte[1];
        while (buffer.Count < 16 * 1024)
        {
            if (await stream.ReadAsync(single).ConfigureAwait(false) == 0)
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

        return (lines.FirstOrDefault() ?? string.Empty, headers);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string contentType, byte[] body)
    {
        var reason = status switch { 200 => "OK", 404 => "Not Found", _ => "Error" };
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head)).ConfigureAwait(false);
        await stream.WriteAsync(body).ConfigureAwait(false);
    }
}
