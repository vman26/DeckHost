# Device plugins

A **device plugin** connects hardware to DeckHost: it finds devices, describes their controls,
reports what the user presses and turns, and shows the images and lights DeckHost renders.
DeckHost's own are `DeckHost.Device.MiraBox` (N4 Pro, through the vendor SDK), `DeckHost.Device.Elgato`
(Stream Decks over USB HID), `DeckHost.Device.Web` (the Web Deck in a browser) and
`DeckHost.Device.Hid` (USB devices described in the device creator).

!!! tip "No code needed for input-only USB devices"
    A button box, pedal or jog wheel that only sends input can usually be added with
    **☰ → Add a USB device**: press each key, turn each knob, save. Write a device plugin when the
    device has screens or lights, or a protocol the device creator can't learn.

!!! note "Device plugins are part of DeckHost"
    Unlike action plugins, device plugins are built into DeckHost today: you add one with a pull
    request (a `DeckHost.Device.<Name>` project, registered in `DeckHost.App/App.axaml.cs`). They
    reference only `DeckHost.Contracts`, so loading them from packages later is possible.

## The shape

```csharp
public sealed class ExampleDevicePlugin : IDevicePlugin, IDeviceOutput
{
    public string Id => "deckhost.device.example";
    public string Name => "Example decks";
    public IReadOnlyCollection<DeviceDefinition> Devices => _devices;      // every device you know, plugged in or not
    public event EventHandler? DevicesChanged;                              // raise when that list changes
    public IDeviceOutput Output => this;

    public Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken)
    {
        _context = context;    // input, connection status, log, redraw requests
        // Start watching for devices (hot-plug) and open the ones that are there.
        return Task.CompletedTask;
    }

    public bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, out string? error) { … }
    public bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, out string? error) { … }
    public void Clear(string deviceId) { … }        // DeckHost is closing: blank the device
    public void Dispose() { … }
}
```

Register it in `App.axaml.cs` next to the others
(`services.AddSingleton<IDevicePlugin>(…)`). The architecture tests check that a device plugin
references nothing of DeckHost but Contracts.

## Describing a device

A `DeviceDefinition(id, name, controls, isConnected, detail)` per device. Keep devices listed while
they're unplugged (with `isConnected: false`), so their profiles can be edited; remember devices
seen before (the Elgato plugin keeps them in `devices/elgato-known.json`). Give each device a stable
id, ideally from its serial number (`elgato.<product>.<serial>`).

Each control is a `PhysicalControl`:

| | |
|---|---|
| `Id` | `key.N` (reading order, left to right, top to bottom), `encoder.N` (knobs, left to right), `touch.N` (touch zones), `touch.swipe` (swipes over a strip). Using these lets profiles move between devices. |
| `DisplayName`, `Type` | "Key 1", and `Key`, `Encoder`, `TouchSurface`, `Display` or `Slider`. |
| `Bounds` | Where it sits in the editor, in layout units (a key is about 72). |
| `Inputs` | What it reports: `Press`, `Release`, `LongPress`, `Rotate`, `Swipe`, `Touch`. |
| `Outputs` | What it shows: `ColorImage`, `Text`, `SegmentLed`, `FullDisplay`, or `None`. |
| `RenderWidth`, `RenderHeight` | The pixel size of its own image (0 without a screen). DeckHost renders exactly that size. |
| `LedIndex` | Set when it has an RGB light. |
| `DisplayControlId` | For a control without a screen, the control whose screen belongs to it (a knob's touch zone): DeckHost draws the knob's action there. |
| `HardwareId` | The device's own number for it, for diagnostics. |

## Input

Report what happens with `context.ReportInputEvent(deviceId, controlId, event, value)`, from any
thread (it's queued for DeckHost's worker):

- keys: `Press` (value 1) and `Release` (0). If the hardware doesn't report holds, send
  `LongPress` yourself after about 500 ms of holding (the Elgato and HID plugins show how).
- knobs: `Rotate` with the steps (negative is left); `Press`/`Release` for a push.
- touch strips: `Press`/`Release` on a zone; `Swipe` on `touch.swipe` (-1 left, 1 right).

Report connection changes with `context.ReportConnectionStatus(deviceId, "Connected" | "Disconnected" | "Error", message)`
(shown in Help and support → Devices) and anything worth knowing with `context.Log`.

## Output

- `TryPushImage` gets the control's image as RGBA at `RenderWidth`×`RenderHeight`, only when its
  pixels changed. Convert it to what the device wants (rotation, JPEG/BMP, color order) and send it.
  Return false with an error message when it fails; DeckHost counts failures for diagnostics and
  tries again next time.
- `TrySetLight` sets a control's RGB light. Implement `IDeviceLightBatchOutput.TrySetLights` too when
  the device can set all lights in one go: DeckHost animates lights at about 30 frames a second.
- `IDeviceBackdropOutput.TrySetBackdrop`: devices with one screen behind all keys get the profile's
  background color there.
- If the device draws over your images by itself (a stored idle screen), call
  `context.RequestRedraw(deviceId)` and DeckHost sends every image again.

## Settings and firmware

- `IConfigurableDevicePlugin`: settings for **Device settings** (brightness, idle screen, …) as
  `ActionSettingDefinition`s; you store them and apply them again when the device reconnects.
- `IDeviceFirmware`: report the firmware version; install firmware only with the maker's documented
  protocol. DeckHost handles downloads, checksums, confirmation and pausing the device
  ([decision 0009](adr/0009-firmware-updates.md)).

## Testing

Keep the protocol pure and testable: the Elgato plugin's `StreamDeckProtocol` (report building and
input parsing) and the MiraBox `N4ProLayout` have no I/O and are covered by unit tests; only a thin
layer talks to the hardware. The editor shows every device even when unplugged, so you can check
the layout and the rendered images without hardware.
