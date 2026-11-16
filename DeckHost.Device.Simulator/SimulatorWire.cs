using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeckHost.Device.Simulator;

/// <summary>A model a simulator can be: one device description from a device plugin's catalog.</summary>
/// <param name="Key"><c>{pluginId}/{deviceId}</c>.</param>
public sealed record SimulatorModel(string Key, string Plugin, DeviceDefinition Device);

/// <summary>One LED's color.</summary>
public sealed record SimulatorLight(string Control, byte R, byte G, byte B);

/// <summary>
/// One message between DeckHost (the simulator device plugin) and a simulator window: a JSON
/// object per line over a loopback TCP connection. <see cref="Type"/> says which fields count.
/// <list type="table">
/// <item><term>hello</term><description>simulator → DeckHost: <see cref="Token"/>, <see cref="Name"/>. Answered with <c>models</c>.</description></item>
/// <item><term>models</term><description>DeckHost → simulator: <see cref="Models"/>.</description></item>
/// <item><term>attach</term><description>simulator → DeckHost: become <see cref="Model"/>. Answered with <c>attached</c> (<see cref="DeviceId"/>, <see cref="Device"/>, settings) or <c>error</c>.</description></item>
/// <item><term>input</term><description>simulator → DeckHost: <see cref="Control"/>, <see cref="Event"/>, <see cref="Value"/>.</description></item>
/// <item><term>image</term><description>DeckHost → simulator: <see cref="Control"/>, <see cref="Width"/>, <see cref="Height"/>, <see cref="Rgba"/> (base64 RGBA8888).</description></item>
/// <item><term>lights</term><description>DeckHost → simulator: <see cref="Lights"/>.</description></item>
/// <item><term>backdrop</term><description>DeckHost → simulator: the screen color behind the keys, <see cref="R"/>, <see cref="G"/>, <see cref="B"/>.</description></item>
/// <item><term>clear</term><description>DeckHost → simulator: blank every image and light.</description></item>
/// <item><term>settings</term><description>DeckHost → simulator: <see cref="Brightness"/> (0–100), <see cref="Vibrate"/>.</description></item>
/// <item><term>error</term><description>DeckHost → simulator: <see cref="Error"/>.</description></item>
/// </list>
/// </summary>
public sealed class SimulatorMessage
{
    public const string Hello = "hello";
    public const string ModelList = "models";
    public const string Attach = "attach";
    public const string Attached = "attached";
    public const string Input = "input";
    public const string Image = "image";
    public const string LightsChanged = "lights";
    public const string Backdrop = "backdrop";
    public const string Clear = "clear";
    public const string Settings = "settings";
    public const string Failure = "error";

    public string Type { get; set; } = string.Empty;

    public string? Token { get; set; }

    public string? Name { get; set; }

    public List<SimulatorModel>? Models { get; set; }

    public string? Model { get; set; }

    public string? DeviceId { get; set; }

    public DeviceDefinition? Device { get; set; }

    public string? Control { get; set; }

    public InputCapabilities Event { get; set; }

    public int Value { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public string? Rgba { get; set; }

    public List<SimulatorLight>? Lights { get; set; }

    public byte R { get; set; }

    public byte G { get; set; }

    public byte B { get; set; }

    public int? Brightness { get; set; }

    public bool? Vibrate { get; set; }

    public string? Error { get; set; }
}

/// <summary>Reading and writing <see cref="SimulatorMessage"/>s, and where DeckHost listens.</summary>
public static class SimulatorWire
{
    /// <summary>Where DeckHost writes its port and token, in its data folder; the simulator reads it.</summary>
    public const string EndpointFileName = "simulator.json";

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost");

    public static string Serialize(SimulatorMessage message) => JsonSerializer.Serialize(message, Options);

    public static SimulatorMessage? Deserialize(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<SimulatorMessage>(line, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The port and token DeckHost wrote, or null when DeckHost isn't running (or has no simulator endpoint).</summary>
    public static SimulatorEndpoint? ReadEndpoint(string? directory = null)
    {
        try
        {
            var path = Path.Combine(directory ?? DefaultDirectory, EndpointFileName);
            return File.Exists(path) ? JsonSerializer.Deserialize<SimulatorEndpoint>(File.ReadAllText(path), Options) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    internal static void WriteEndpoint(string directory, SimulatorEndpoint endpoint)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, EndpointFileName), JsonSerializer.Serialize(endpoint, Options));
    }
}

/// <summary>Where DeckHost's simulator endpoint listens (loopback) and the token a simulator must send.</summary>
public sealed record SimulatorEndpoint(int Port, string Token);

/// <summary>
/// One connection: JSON lines in, JSON lines out. Sends are queued and written by one task, images
/// coalesced per control (only the newest is sent), so a slow reader never blocks DeckHost.
/// </summary>
public sealed class SimulatorConnection : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly Lock _sync = new();
    private readonly Queue<SimulatorMessage> _queue = new();
    private readonly Dictionary<string, SimulatorMessage> _images = new(StringComparer.Ordinal);
    private readonly List<string> _imageOrder = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _writing;
    private volatile bool _closing;

    public SimulatorConnection(Stream stream)
    {
        _stream = stream;
        _reader = new StreamReader(stream, System.Text.Encoding.UTF8, false, 64 * 1024, leaveOpen: true);
        _writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), 64 * 1024, leaveOpen: true) { NewLine = "\n" };
        _writing = Task.Run(WriteLoopAsync);
    }

    public async Task<SimulatorMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }

            if (SimulatorWire.Deserialize(line) is { } message)
            {
                return message;
            }
        }
    }

    public void Send(SimulatorMessage message)
    {
        lock (_sync)
        {
            if (message.Type == SimulatorMessage.Image && message.Control is { } control)
            {
                if (!_images.ContainsKey(control))
                {
                    _imageOrder.Add(control);
                }

                _images[control] = message;
            }
            else
            {
                if (message.Type == SimulatorMessage.Clear)
                {
                    // Images queued before a clear would come back after it.
                    _images.Clear();
                    _imageOrder.Clear();
                }

                _queue.Enqueue(message);
            }
        }

        _signal.Release();
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await _signal.WaitAsync(_stop.Token).ConfigureAwait(false);
                while (Next() is { } message)
                {
                    await _writer.WriteLineAsync(SimulatorWire.Serialize(message).AsMemory(), _stop.Token).ConfigureAwait(false);
                }

                await _writer.FlushAsync(_stop.Token).ConfigureAwait(false);
                if (_closing)
                {
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    private SimulatorMessage? Next()
    {
        lock (_sync)
        {
            if (_queue.TryDequeue(out var message))
            {
                return message;
            }

            if (_imageOrder.Count > 0)
            {
                var control = _imageOrder[0];
                _imageOrder.RemoveAt(0);
                _images.Remove(control, out var image);
                return image;
            }

            return null;
        }
    }

    /// <summary>Writes what is still queued (a closing error message, say), for a second at most, then closes.</summary>
    public async ValueTask DisposeAsync()
    {
        _closing = true;
        _signal.Release();
        await Task.WhenAny(_writing, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        await _stop.CancelAsync().ConfigureAwait(false);
        _stream.Dispose();
        try
        {
            await _writing.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }

        _signal.Dispose();
        _stop.Dispose();
    }
}
