using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Device.Simulator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DeckHost.App.Simulator;

/// <summary>
/// The simulator window's state: connects to DeckHost's simulator endpoint (and keeps trying while
/// DeckHost isn't running), lists the models DeckHost offers, attaches as the chosen one and keeps
/// what DeckHost sends: images per control, lights, the backdrop, brightness and vibration.
/// </summary>
public sealed partial class SimulatorViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly string _directory;
    private readonly CancellationTokenSource _stop = new();
    private SimulatorClient? _client;
    private string? _wantedModel;
    private string _status = "Looking for DeckHost…";
    private DeviceDefinition? _device;

    public SimulatorViewModel(string? startModel = null, string? directory = null)
    {
        _directory = directory ?? SimulatorWire.DefaultDirectory;
        _wantedModel = startModel ?? LoadLastModel();
    }

    /// <summary>The models DeckHost offers, in its order.</summary>
    public IReadOnlyList<SimulatorModel> Models { get; private set; } = [];

    /// <summary>The model this window is (its key), once attached.</summary>
    public string? CurrentModel { get; private set; }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>The device this window is, as DeckHost sees it.</summary>
    public DeviceDefinition? Device
    {
        get => _device;
        private set => SetProperty(ref _device, value);
    }

    public string? DeviceId { get; private set; }

    public Dictionary<string, WriteableBitmap> Images { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Color> Lights { get; } = new(StringComparer.Ordinal);

    public Color Backdrop { get; private set; } = Colors.Black;

    /// <summary>0–100, from DeckHost's device settings.</summary>
    public int Brightness { get; private set; } = 100;

    public bool Vibrate { get; private set; }

    /// <summary>Something to draw changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The model list changed.</summary>
    public event EventHandler? ModelsChanged;

    /// <summary>A control was used on a device that vibrates.</summary>
    public event EventHandler? Vibrated;

    /// <summary>Connects, and reconnects whenever DeckHost goes away. Runs until disposed.</summary>
    public async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var endpoint = SimulatorWire.ReadEndpoint(_directory);
            if (endpoint is null)
            {
                Post(() => Status = "Waiting for DeckHost. Start it and turn on DeckHost settings → Developer → Device simulator; this window connects by itself.");
                await DelayAsync().ConfigureAwait(false);
                continue;
            }

            SimulatorClient client;
            try
            {
                client = await SimulatorClient.ConnectAsync(endpoint, Environment.MachineName, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException)
            {
                Post(() => Status = "Waiting for DeckHost. Start it; this window connects by itself.");
                await DelayAsync().ConfigureAwait(false);
                continue;
            }

            var closed = new TaskCompletionSource();
            client.MessageReceived += (_, message) => OnMessage(message);
            client.Disconnected += (_, _) => closed.TrySetResult();
            _client = client;
            client.Start();
            Post(() => Status = "Connected to DeckHost.");
            await Task.WhenAny(closed.Task, Task.Delay(Timeout.Infinite, _stop.Token)).ConfigureAwait(false);
            _client = null;
            await client.DisposeAsync().ConfigureAwait(false);
            Post(() =>
            {
                Device = null;
                Status = "DeckHost closed the connection. Reconnecting…";
                Changed?.Invoke(this, EventArgs.Empty);
            });
            await DelayAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Become another model (from the model list).</summary>
    public void Choose(string modelKey)
    {
        _wantedModel = modelKey;
        SaveLastModel(modelKey);
        _client?.Attach(modelKey);
    }

    /// <summary>A control was used in the window.</summary>
    public void SendInput(string controlId, InputCapabilities input, int value)
    {
        _client?.SendInput(controlId, input, value);
        if (Vibrate && input is InputCapabilities.Press or InputCapabilities.Rotate or InputCapabilities.Swipe)
        {
            Vibrated?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnMessage(SimulatorMessage message)
    {
        switch (message.Type)
        {
            case SimulatorMessage.ModelList:
                var models = message.Models ?? [];
                Post(() =>
                {
                    Models = models;
                    ModelsChanged?.Invoke(this, EventArgs.Empty);
                    var pick = models.FirstOrDefault(model => model.Key == _wantedModel) ?? models.FirstOrDefault();
                    if (pick is null)
                    {
                        Status = "DeckHost offers no models to simulate.";
                        return;
                    }

                    _client?.Attach(pick.Key);
                });
                break;

            case SimulatorMessage.Attached:
                Post(() =>
                {
                    Device = message.Device;
                    DeviceId = message.DeviceId;
                    CurrentModel = message.Model;
                    Images.Clear();
                    Lights.Clear();
                    Status = $"DeckHost sees this window as “{message.Device?.Name}”. Pick it in DeckHost's device list.";
                    ModelsChanged?.Invoke(this, EventArgs.Empty);
                    Changed?.Invoke(this, EventArgs.Empty);
                });
                break;

            case SimulatorMessage.Image when message is { Control: { } control, Rgba: { } data, Width: > 0, Height: > 0 }:
                var pixels = data;
                if (pixels.Length < message.Width * message.Height * 4)
                {
                    return;
                }

                Post(() =>
                {
                    Images[control] = ToBitmap(Images.GetValueOrDefault(control), message.Width, message.Height, pixels);
                    Changed?.Invoke(this, EventArgs.Empty);
                });
                break;

            case SimulatorMessage.LightsChanged when message.Lights is { } lights:
                Post(() =>
                {
                    foreach (var light in lights)
                    {
                        Lights[light.Control] = Color.FromRgb(light.R, light.G, light.B);
                    }

                    Changed?.Invoke(this, EventArgs.Empty);
                });
                break;

            case SimulatorMessage.Backdrop:
                Post(() =>
                {
                    Backdrop = Color.FromRgb(message.R, message.G, message.B);
                    Changed?.Invoke(this, EventArgs.Empty);
                });
                break;

            case SimulatorMessage.Clear:
                Post(() =>
                {
                    Images.Clear();
                    Lights.Clear();
                    Backdrop = Colors.Black;
                    Changed?.Invoke(this, EventArgs.Empty);
                });
                break;

            case SimulatorMessage.Settings:
                Post(() =>
                {
                    Brightness = message.Brightness ?? 100;
                    Vibrate = message.Vibrate ?? false;
                    Changed?.Invoke(this, EventArgs.Empty);
                });
                break;

            case SimulatorMessage.Failure:
                Post(() => Status = message.Error ?? "DeckHost refused.");
                break;
        }
    }

    private static WriteableBitmap ToBitmap(WriteableBitmap? bitmap, int width, int height, byte[] rgba)
    {
        if (bitmap is null || bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height)
        {
            bitmap = new WriteableBitmap(new Avalonia.PixelSize(width, height), new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Premul);
        }

        using var buffer = bitmap.Lock();
        for (var row = 0; row < height; row++)
        {
            Marshal.Copy(rgba, row * width * 4, buffer.Address + (row * buffer.RowBytes), width * 4);
        }

        return bitmap;
    }

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);

    private async Task DelayAsync()
    {
        try
        {
            await Task.Delay(RetryDelay, _stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private string LastModelPath => Path.Combine(_directory, "simulator-window.json");

    private string? LoadLastModel()
    {
        try
        {
            return File.Exists(LastModelPath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(LastModelPath))?.GetValueOrDefault("model") : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void SaveLastModel(string model)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(LastModelPath, JsonSerializer.Serialize(new Dictionary<string, string> { ["model"] = model }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_client is { } client)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
