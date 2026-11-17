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

    public IReadOnlyList<ActionSettingDefinition> Settings { get; init; } = [];

    public TimeSpan? RefreshInterval { get; init; }

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

internal sealed class FakeDevicePlugin : IDeviceWithContext, IDeviceOutput
{
    public const string DeviceId = "fake.device";

    private readonly string _deviceId;
    private DeviceDefinition[] _devices;

    public FakeDevicePlugin(bool connected = true, string deviceId = DeviceId)
    {
        _deviceId = deviceId;
        _devices = [CreateDevice(connected)];
    }

    public event EventHandler? DevicesChanged;

    public string Id => _deviceId == DeviceId ? "fake.plugin" : "fake.plugin." + _deviceId;

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

    private DeviceDefinition CreateDevice(bool connected) => new(
        _deviceId,
        "Fake device",
        [
            new PhysicalControl("key.0", "Key 1", ControlType.Key, new Rect(0, 0, 10, 10), InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.ColorImage, 8, 8),
            new PhysicalControl("key.1", "Key 2", ControlType.Key, new Rect(10, 0, 10, 10), InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.ColorImage, 8, 8),
            new PhysicalControl("encoder.0", "Knob", ControlType.Encoder, new Rect(0, 10, 10, 10), InputCapabilities.Rotate | InputCapabilities.Press, OutputCapabilities.None, LedIndex: 0),
        ],
        connected);
}

/// <summary>A device plugin that keeps the context the host gave it, so a test can report input.</summary>
internal interface IDeviceWithContext : IDevicePlugin
{
    IDevicePluginContext? Context { get; }
}

internal static class Bindings
{
    public static ActionReference Reference(string pluginId, string actionId) =>
        new(pluginId, pluginId, actionId, actionId, InputCapabilities.None, OutputCapabilities.None);

    public static ControlBinding Single(string controlId, string pluginId, string actionId, RgbFeedback? light = null) =>
        new(controlId, ProfileActionKind.Single, Reference(pluginId, actionId), new ControlAppearance(RgbFeedback: light), []);
}

/// <summary>Fills its area with one color and records how it was asked to draw.</summary>
internal sealed class SolidAction(string id, uint argbColor) : IDeckAction
{
    public string Id { get; } = id;

    public string Name => Id;

    public InputCapabilities RequiredInputs => InputCapabilities.None;

    public OutputCapabilities RequiredOutputs => OutputCapabilities.None;

    public ConcurrentQueue<(string ControlId, InputCapabilities EventType)> Executions { get; } = new();

    public ConcurrentQueue<(string ControlId, int Width, int Height, bool Compact)> Renders { get; } = new();

    public void Execute(ActionContext context) => Executions.Enqueue((context.ControlId, context.EventType));

    public void Render(IRenderContext context)
    {
        Renders.Enqueue((context.ControlId, context.Width, context.Height, context.IsCompact));
        context.FillRoundedRect(0, 0, context.Width, context.Height, 0, argbColor);
    }
}

/// <summary>
/// A device like the N4 Pro's touch strip: a touch zone with a display, a knob under it that
/// borrows that display, and a screen backdrop. Records every output call in order.
/// </summary>
internal sealed class StripDevicePlugin : IDevicePlugin, IDeviceOutput, IDeviceBackdropOutput
{
    public const string DeviceId = "strip.device";
    public const string ZoneId = "touch.0";
    public const string KnobId = "encoder.0";
    public const int ZoneWidth = 176;
    public const int ZoneHeight = 112;

    public event EventHandler? DevicesChanged
    {
        add { }
        remove { }
    }

    public string Id => "strip.plugin";

    public string Name => "Strip";

    public IReadOnlyCollection<DeviceDefinition> Devices { get; } =
    [
        new DeviceDefinition(
            DeviceId,
            "Strip device",
            [
                new PhysicalControl(ZoneId, "Touch 1", ControlType.TouchSurface, new Rect(0, 0, 20, 10), InputCapabilities.Press, OutputCapabilities.ColorImage, ZoneWidth, ZoneHeight),
                new PhysicalControl(KnobId, "Knob 1", ControlType.Encoder, new Rect(5, 20, 10, 10), InputCapabilities.Rotate | InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.None, DisplayControlId: ZoneId),
            ],
            IsConnected: true),
    ];

    public IDeviceOutput Output => this;

    public IDevicePluginContext? Context { get; private set; }

    /// <summary>"backdrop:r,g,b" and "image:controlId", in the order they were sent.</summary>
    public ConcurrentQueue<string> Calls { get; } = new();

    public Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken)
    {
        Context = context;
        return Task.CompletedTask;
    }

    public bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, out string? error)
    {
        Calls.Enqueue($"image:{controlId}");
        error = null;
        return true;
    }

    public bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, out string? error)
    {
        error = null;
        return true;
    }

    public bool TrySetBackdrop(string deviceId, byte red, byte green, byte blue, out string? error)
    {
        Calls.Enqueue($"backdrop:{red},{green},{blue}");
        error = null;
        return true;
    }

    public void Clear(string deviceId)
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>Like DeckHost's Display Content: draws nothing itself, only the control's appearance shows.</summary>
internal sealed class AppearanceOnlyAction(string id) : IDeckAction
{
    public string Id { get; } = id;

    public string Name => Id;

    public InputCapabilities RequiredInputs => InputCapabilities.None;

    public OutputCapabilities RequiredOutputs => OutputCapabilities.None;

    public bool ShowsAppearanceOnly => true;

    public ConcurrentQueue<InputCapabilities> Executions { get; } = new();

    public void Execute(ActionContext context) => Executions.Enqueue(context.EventType);

    public void Render(IRenderContext context)
    {
    }
}

/// <summary>Two touch zones, each with a knob that borrows its display: a device to check that a knob only draws its own zone.</summary>
internal sealed class TwoZoneDevicePlugin : IDeviceWithContext, IDeviceOutput
{
    public const string DeviceId = "zones.device";
    public const string FirstZone = "touch.0";
    public const string SecondZone = "touch.1";
    public const string FirstKnob = "encoder.0";
    public const string SecondKnob = "encoder.1";

    public event EventHandler? DevicesChanged
    {
        add { }
        remove { }
    }

    public string Id => "zones.plugin";

    public string Name => "Zones";

    public IReadOnlyCollection<DeviceDefinition> Devices { get; } =
    [
        new DeviceDefinition(
            DeviceId,
            "Zones device",
            [
                new PhysicalControl(FirstZone, "Touch 1", ControlType.TouchSurface, new Rect(0, 0, 10, 10), InputCapabilities.Press, OutputCapabilities.ColorImage, 8, 8),
                new PhysicalControl(SecondZone, "Touch 2", ControlType.TouchSurface, new Rect(10, 0, 10, 10), InputCapabilities.Press, OutputCapabilities.ColorImage, 8, 8),
                new PhysicalControl(FirstKnob, "Knob 1", ControlType.Encoder, new Rect(0, 10, 10, 10), InputCapabilities.Rotate | InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.None, DisplayControlId: FirstZone),
                new PhysicalControl(SecondKnob, "Knob 2", ControlType.Encoder, new Rect(10, 10, 10, 10), InputCapabilities.Rotate | InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.None, DisplayControlId: SecondZone),
            ],
            IsConnected: true),
    ];

    public IDeviceOutput Output => this;

    public IDevicePluginContext? Context { get; private set; }

    public ConcurrentQueue<string> PushedImages { get; } = new();

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
        error = null;
        return true;
    }

    public void Clear(string deviceId)
    {
    }

    public void Dispose()
    {
    }
}
