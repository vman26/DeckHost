using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System.Collections.Concurrent;

namespace DeckHost.Tests;

internal sealed class RecordingAction(string id, InputCapabilities requiredInputs = InputCapabilities.None) : IDeckAction
{
    public string Id { get; } = id;

    public string Name => Id;

    public InputCapabilities RequiredInputs { get; } = requiredInputs;

    public OutputCapabilities RequiredOutputs => OutputCapabilities.None;

    public List<ActionContext> Executions { get; } = [];

    public Action<ActionContext>? OnExecute { get; set; }

    public void Execute(ActionContext context)
    {
        Executions.Add(context);
        OnExecute?.Invoke(context);
    }

    /// <summary>Draws a different color depending on how often the action ran, so renders change after input.</summary>
    public void Render(IRenderContext context)
    {
        context.Variables.TryGetValue<int>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.ExecutionCount), out var count);
        context.FillRoundedRect(0, 0, context.Width, context.Height, 0, 0xFF000000u | (uint)(count * 40));
    }
}

internal sealed class TestApplicationPlugin(string id, params IDeckAction[] actions) : IApplicationPlugin
{
    public string Id { get; } = id;

    public string Name => Id;

    public IReadOnlyCollection<IDeckAction> Actions { get; } = actions;
}

internal sealed class FakeDevicePlugin : IDevicePlugin, IDeviceOutput
{
    public const string DeviceId = "fake.device";

    private DeviceDefinition[] _devices;

    public FakeDevicePlugin(bool connected = true)
    {
        _devices = [CreateDevice(connected)];
    }

    public event EventHandler? DevicesChanged;

    public string Id => "fake.plugin";

    public string Name => "Fake";

    public IReadOnlyCollection<DeviceDefinition> Devices => _devices;

    public IDeviceOutput Output => this;

    public IDevicePluginContext? Context { get; private set; }

    public ConcurrentQueue<string> PushedImages { get; } = new();

    public ConcurrentQueue<(string ControlId, byte R, byte G, byte B)> Lights { get; } = new();

    public int ClearCount;

    public bool Disposed { get; private set; }

    public Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken)
    {
        Context = context;
        return Task.CompletedTask;
    }

    public bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, out string? error)
    {
        PushedImages.Enqueue(controlId);
        error = null;
        return true;
    }

    public bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, out string? error)
    {
        Lights.Enqueue((controlId, red, green, blue));
        error = null;
        return true;
    }

    public void Clear(string deviceId) => Interlocked.Increment(ref ClearCount);

    public void SetConnected(bool connected)
    {
        _devices = [CreateDevice(connected)];
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Disposed = true;

    private static DeviceDefinition CreateDevice(bool connected) => new(
        DeviceId,
        "Fake device",
        [
            new PhysicalControl("key.0", "Key 1", ControlType.Key, new Rect(0, 0, 10, 10), InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.ColorImage, 8, 8),
            new PhysicalControl("key.1", "Key 2", ControlType.Key, new Rect(10, 0, 10, 10), InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.ColorImage, 8, 8),
            new PhysicalControl("encoder.0", "Knob", ControlType.Encoder, new Rect(0, 10, 10, 10), InputCapabilities.Rotate | InputCapabilities.Press, OutputCapabilities.None, LedIndex: 0),
        ],
        connected);
}

internal static class Bindings
{
    public static ActionReference Reference(string pluginId, string actionId) =>
        new(pluginId, pluginId, actionId, actionId, InputCapabilities.None, OutputCapabilities.None);

    public static ControlBinding Single(string controlId, string pluginId, string actionId, RgbFeedback? light = null) =>
        new(controlId, ProfileActionKind.Single, Reference(pluginId, actionId), new ControlAppearance(RgbFeedback: light), []);
}
