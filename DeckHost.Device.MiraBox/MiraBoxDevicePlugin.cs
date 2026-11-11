using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.Device.MiraBox;

public sealed class MiraBoxDevicePlugin : IDevicePlugin, IDisposable
{
    private const string DeviceId = "mirabox.n4pro";

    private readonly MiraBoxController _controller;
    private readonly List<DeviceDefinition> _devices;
    private IDevicePluginContext? _context;
    private bool _initialized;
    private bool _disposed;

    public MiraBoxDevicePlugin()
    {
        _controller = new MiraBoxController();
        _controller.InputReceived += OnInputReceived;
        _devices = [CreateDeviceDefinition()];
    }

    public string Id => "deckhost.device.mirabox";

    public string Name => "MiraBox Device Plugin";

    public IReadOnlyCollection<DeviceDefinition> Devices => _devices;

    public Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (_initialized)
        {
            return Task.CompletedTask;
        }

        _context = context;
        _controller.Start();

        // Query actual device capabilities from the connected device
        QueryAndUpdateDeviceCapabilities();

        _initialized = true;
        return Task.CompletedTask;
    }

    private void QueryAndUpdateDeviceCapabilities()
    {
        try
        {
            // Query device info from the native bridge (device 0)
            var deviceInfoJson = MiraBoxNative.QueryDeviceInfo(0);
            if (!string.IsNullOrWhiteSpace(deviceInfoJson))
            {
                var capabilities = DeviceCapabilities.FromJson(deviceInfoJson);
                if (capabilities is not null)
                {
                    // Log the key mapping for debugging
                    System.Diagnostics.Debug.WriteLine($"=== MiraBox Device Info ===");
                    System.Diagnostics.Debug.WriteLine($"Device: {capabilities.DeviceName}");
                    System.Diagnostics.Debug.WriteLine($"Display: {capabilities.DisplayWidth}x{capabilities.DisplayHeight}");
                    System.Diagnostics.Debug.WriteLine($"Key Size: {capabilities.KeyWidth}x{capabilities.KeyHeight}");
                    System.Diagnostics.Debug.WriteLine($"Key Range: {capabilities.MinKeyId} to {capabilities.MaxKeyId}");
                    System.Diagnostics.Debug.WriteLine($"Key Count: {capabilities.KeyCount}");
                    System.Diagnostics.Debug.WriteLine($"Key ID Mapping:");
                    for (int i = 0; i < capabilities.KeyCount; i++)
                    {
                        var sdkKeyId = capabilities.MinKeyId + i;
                        System.Diagnostics.Debug.WriteLine($"  Logical Key {i} => SDK Key {sdkKeyId}");
                    }
                    System.Diagnostics.Debug.WriteLine($"===========================");

                    // Update the device definition with actual capabilities
                    UpdateDeviceWithCapabilities(capabilities);
                }
            }
        }
        catch (Exception ex)
        {
            // If query fails, use defaults (already set in CreateDeviceDefinition)
            System.Diagnostics.Debug.WriteLine($"Failed to query device info: {ex.Message}");
        }
    }

    private void UpdateDeviceWithCapabilities(DeviceCapabilities capabilities)
    {
        // Reconstruct the devices collection with updated capabilities
        for (var index = 0; index < _devices.Count; index++)
        {
            var device = _devices[index];
            if (string.Equals(device.Id, DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                // Create new device definition with capabilities
                var updated = device with { Capabilities = capabilities };
                _devices[index] = updated;
                break;
            }
        }
    }


    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _controller.InputReceived -= OnInputReceived;
        _controller.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnInputReceived(object? sender, MiraBoxInputEventArgs e)
    {
        if (_context is null)
        {
            return;
        }

        if (!TryMapInput(e, out var controlId, out var eventType, out var value))
        {
            return;
        }

        _context.ReportInputEvent(DeviceId, controlId, eventType, value);
    }

    private static bool TryMapInput(MiraBoxInputEventArgs input, out string controlId, out InputCapabilities eventType, out int value)
    {
        switch (input.EventType)
        {
            case MiraBoxEventType.KeyPress:
                controlId = $"key.{input.ControlId}";
                eventType = InputCapabilities.Press;
                value = 1;
                return true;
            case MiraBoxEventType.KeyRelease:
                controlId = $"key.{input.ControlId}";
                eventType = InputCapabilities.Release;
                value = 0;
                return true;
            case MiraBoxEventType.KnobLeft:
                controlId = $"encoder.{input.ControlId}";
                eventType = InputCapabilities.Rotate;
                value = -1;
                return true;
            case MiraBoxEventType.KnobRight:
                controlId = $"encoder.{input.ControlId}";
                eventType = InputCapabilities.Rotate;
                value = 1;
                return true;
            case MiraBoxEventType.KnobPress:
                controlId = $"encoder.{input.ControlId}";
                eventType = InputCapabilities.Press;
                value = 1;
                return true;
            case MiraBoxEventType.KnobRelease:
                controlId = $"encoder.{input.ControlId}";
                eventType = InputCapabilities.Release;
                value = 0;
                return true;
            case MiraBoxEventType.SwipeLeft:
            case MiraBoxEventType.SwipeRight:
            case MiraBoxEventType.ToggleUp:
            case MiraBoxEventType.ToggleDown:
            case MiraBoxEventType.DipLeft:
            case MiraBoxEventType.DipLeftEnd:
            case MiraBoxEventType.DipRight:
            case MiraBoxEventType.DipRightEnd:
            case MiraBoxEventType.DipPress:
            case MiraBoxEventType.DipRelease:
                controlId = "touch.0";
                eventType = InputCapabilities.Touch;
                value = input.X;
                return true;
            default:
                controlId = string.Empty;
                eventType = InputCapabilities.None;
                value = 0;
                return false;
        }
    }

    private static DeviceDefinition CreateDeviceDefinition()
    {
        var controls = new List<PhysicalControl>();
        const double keySize = 72;
        const double keyGap = 12;
        const double keyStartX = 32;
        const double topRowY = 32;
        const double secondRowY = 116;

        for (var index = 0; index < 10; index++)
        {
            var row = index < 5 ? 0 : 1;
            var column = index % 5;
            var y = row == 0 ? topRowY : secondRowY;
            var x = keyStartX + (column * (keySize + keyGap));

            controls.Add(new PhysicalControl(
                $"key.{index}",
                $"Key {index + 1}",
                ControlType.Key,
                new Rect(x, y, keySize, keySize),
                InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress,
                OutputCapabilities.ColorImage | OutputCapabilities.Text));
        }

        controls.Add(new PhysicalControl(
            "touch.0",
            "Touch Display",
            ControlType.TouchSurface,
            new Rect(32, 224, 520, 128),
            InputCapabilities.Touch | InputCapabilities.Swipe,
            OutputCapabilities.FullDisplay | OutputCapabilities.Text));

        const double encoderSize = 64;
        const double encoderGap = 28;
        const double encoderStartX = 66;
        const double encoderY = 376;

        for (var index = 0; index < 4; index++)
        {
            var x = encoderStartX + (index * (encoderSize + encoderGap));
            controls.Add(new PhysicalControl(
                $"encoder.{index}",
                $"Encoder {index + 1}",
                ControlType.Encoder,
                new Rect(x, encoderY, encoderSize, encoderSize),
                InputCapabilities.Rotate | InputCapabilities.Press | InputCapabilities.Release,
                OutputCapabilities.None));
        }

        return new DeviceDefinition(DeviceId, "MiraBox N4 Pro", controls, 800, 480);
    }
}
