using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeckHost.Plugin.Obs;

/// <summary>
/// Minimal obs-websocket v5 client (built into OBS 28+): Hello / Identify with optional
/// authentication, then requests matched to responses by request id.
/// </summary>
internal sealed class ObsWebSocketClient : IAsyncDisposable
{
    private const int OpHello = 0;
    private const int OpIdentify = 1;
    private const int OpIdentified = 2;
    private const int OpRequest = 6;
    private const int OpRequestResponse = 7;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly CancellationTokenSource _closing = new();
    private Task? _receiveLoop;

    public bool IsOpen => _socket.State == WebSocketState.Open && _receiveLoop is { IsCompleted: false };

    public static async Task<ObsWebSocketClient> ConnectAsync(string host, int port, string password, CancellationToken cancellationToken)
    {
        var client = new ObsWebSocketClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);

            await client._socket.ConnectAsync(new Uri($"ws://{host}:{port}"), timeout.Token).ConfigureAwait(false);

            var hello = await client.ReceiveMessageAsync(timeout.Token).ConfigureAwait(false);
            if (hello?["op"]?.GetValue<int>() != OpHello)
            {
                throw new ObsException("OBS did not greet (is this obs-websocket 5?).");
            }

            var identify = new JsonObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = 0 };
            if (hello["d"]?["authentication"] is JsonObject authentication)
            {
                if (string.IsNullOrEmpty(password))
                {
                    throw new ObsException("OBS needs a password (Tools > WebSocket Server Settings).");
                }

                identify["authentication"] = CreateAuthentication(
                    password,
                    authentication["salt"]!.GetValue<string>(),
                    authentication["challenge"]!.GetValue<string>());
            }

            await client.SendAsync(new JsonObject { ["op"] = OpIdentify, ["d"] = identify }, timeout.Token).ConfigureAwait(false);

            var identified = await client.ReceiveMessageAsync(timeout.Token).ConfigureAwait(false);
            if (identified?["op"]?.GetValue<int>() != OpIdentified)
            {
                throw new ObsException(client._socket.CloseStatus == (WebSocketCloseStatus)4009
                    ? "Wrong OBS WebSocket password."
                    : "OBS refused the connection.");
            }

            client._receiveLoop = Task.Run(client.ReceiveLoopAsync);
            return client;
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw new ObsException($"OBS not reachable on {host}:{port}. Is OBS running with the WebSocket server on?", ex);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Sends a request and returns its responseData (or an empty object).</summary>
    public async Task<JsonObject> RequestAsync(string requestType, JsonObject? requestData, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = completion;

        try
        {
            var request = new JsonObject { ["requestType"] = requestType, ["requestId"] = requestId };
            if (requestData is not null)
            {
                request["requestData"] = requestData;
            }

            await SendAsync(new JsonObject { ["op"] = OpRequest, ["d"] = request }, cancellationToken).ConfigureAwait(false);
            var response = await completion.Task.WaitAsync(Timeout, cancellationToken).ConfigureAwait(false);

            var status = response["requestStatus"];
            if (status?["result"]?.GetValue<bool>() != true)
            {
                var comment = status?["comment"]?.GetValue<string>() ?? $"code {status?["code"]}";
                throw new ObsException(comment);
            }

            return response["responseData"] as JsonObject ?? [];
        }
        catch (TimeoutException ex)
        {
            throw new ObsException("OBS did not answer in time.", ex);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _closing.CancelAsync().ConfigureAwait(false);
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "DeckHost closing", timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
        }

        foreach (var pending in _pending.Values)
        {
            pending.TrySetException(new ObsException("Connection to OBS closed."));
        }

        _socket.Dispose();
        _sendLock.Dispose();
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!_closing.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var message = await ReceiveMessageAsync(_closing.Token).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                if (message["op"]?.GetValue<int>() == OpRequestResponse &&
                    message["d"] is JsonObject data &&
                    data["requestId"]?.GetValue<string>() is { } requestId &&
                    _pending.TryRemove(requestId, out var completion))
                {
                    completion.TrySetResult(data);
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or JsonException)
        {
        }

        foreach (var pending in _pending.Values)
        {
            pending.TrySetException(new ObsException("Connection to OBS lost."));
        }
    }

    private async Task SendAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<JsonObject?> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var result = await _socket.ReceiveAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage)
            {
                return JsonNode.Parse(buffer.ToArray()) as JsonObject;
            }
        }
    }

    /// <summary>base64(sha256(base64(sha256(password + salt)) + challenge)), as specified by obs-websocket 5.</summary>
    internal static string CreateAuthentication(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }
}

internal sealed class ObsException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>One connection per OBS endpoint, shared by all OBS actions; reconnects after a failure.</summary>
internal sealed class ObsConnectionPool : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<(string Host, int Port, string Password), ObsWebSocketClient> _clients = [];
    private bool _disposed;

    public async Task<JsonObject> RequestAsync(ObsEndpoint endpoint, string requestType, JsonObject? requestData, CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(endpoint, cancellationToken).ConfigureAwait(false);
        try
        {
            return await client.RequestAsync(requestType, requestData, cancellationToken).ConfigureAwait(false);
        }
        catch (ObsException) when (!client.IsOpen)
        {
            await DropAsync(endpoint, client).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            foreach (var client in _clients.Values)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }

            _clients.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<ObsWebSocketClient> GetClientAsync(ObsEndpoint endpoint, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = (endpoint.Host, endpoint.Port, endpoint.Password);
            if (_clients.TryGetValue(key, out var existing))
            {
                if (existing.IsOpen)
                {
                    return existing;
                }

                _clients.Remove(key);
                await existing.DisposeAsync().ConfigureAwait(false);
            }

            var client = await ObsWebSocketClient.ConnectAsync(endpoint.Host, endpoint.Port, endpoint.Password, cancellationToken).ConfigureAwait(false);
            _clients[key] = client;
            return client;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task DropAsync(ObsEndpoint endpoint, ObsWebSocketClient client)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var key = (endpoint.Host, endpoint.Port, endpoint.Password);
            if (_clients.TryGetValue(key, out var current) && ReferenceEquals(current, client))
            {
                _clients.Remove(key);
            }
        }
        finally
        {
            _lock.Release();
        }

        await client.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed record ObsEndpoint(string Host, int Port, string Password);
