using DeckHost.Contracts.Enums;
using System.Net;
using System.Net.Sockets;

namespace DeckHost.Device.Simulator;

/// <summary>
/// The simulator window's side of the connection: says hello with the token from DeckHost's
/// endpoint file, then raises <see cref="MessageReceived"/> for everything DeckHost sends (models,
/// attached, images, lights, backdrop, clear, settings, error) on a background thread.
/// </summary>
public sealed class SimulatorClient : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly SimulatorConnection _connection;
    private readonly CancellationTokenSource _stop = new();
    private Task? _reading;

    private SimulatorClient(TcpClient client)
    {
        _client = client;
        _connection = new SimulatorConnection(client.GetStream());
    }

    public event EventHandler<SimulatorMessage>? MessageReceived;

    /// <summary>Raised once when DeckHost closes the connection or goes away.</summary>
    public event EventHandler? Disconnected;

    /// <summary>Connects to DeckHost at <paramref name="endpoint"/> and starts reading.</summary>
    public static async Task<SimulatorClient> ConnectAsync(SimulatorEndpoint endpoint, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            await tcp.ConnectAsync(IPAddress.Loopback, endpoint.Port, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }

        var client = new SimulatorClient(tcp);
        client._connection.Send(new SimulatorMessage { Type = SimulatorMessage.Hello, Token = endpoint.Token, Name = name });
        return client;
    }

    /// <summary>Starts raising <see cref="MessageReceived"/>; call after subscribing.</summary>
    public void Start() => _reading ??= Task.Run(ReadLoopAsync);

    /// <summary>Become one of the models DeckHost listed.</summary>
    public void Attach(string modelKey) => _connection.Send(new SimulatorMessage { Type = SimulatorMessage.Attach, Model = modelKey });

    public void SendInput(string controlId, InputCapabilities input, int value) =>
        _connection.Send(new SimulatorMessage { Type = SimulatorMessage.Input, Control = controlId, Event = input, Value = value });

    private async Task ReadLoopAsync()
    {
        try
        {
            while (await _connection.ReadAsync(_stop.Token).ConfigureAwait(false) is { } message)
            {
                MessageReceived?.Invoke(this, message);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }

        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
        _client.Dispose();
        if (_reading is not null)
        {
            try
            {
                await _reading.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
            {
            }
        }

        _stop.Dispose();
    }
}
