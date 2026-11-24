# Device plugins

A **device plugin** connects hardware to DeckHost: it finds devices, describes their controls,
reports what the user presses and turns, and shows the images and lights DeckHost renders.
DeckHost's own are `DeckHost.Device.MiraBox` (N4 Pro, through the vendor SDK), `DeckHost.Device.Elgato`
(Stream Decks over USB HID), `DeckHost.Device.Web` (the Web Deck in a browser) and
`DeckHost.Device.Hid` (USB devices described in the device creator), plus `DeckHost.Device.Simulator`.
All of them ship as bundled plugin packages (see below).

!!! tip "No code needed for input-only USB devices"
    A button box, pedal or jog wheel that only sends input can usually be added with
    **☰ → Add a USB device**: press each key, turn each knob, save. Write a device plugin when the
    device has screens or lights, or a protocol the device creator can't learn.

!!! note "Device plugins are packages, like action plugins"
    A device plugin is a plugin package whose `plugin.json` says `"kind": "device"`: its assembly has
    a public `IDevicePlugin` (a parameterless constructor, or one whose parameters are all optional)
    with the same `Id` as the manifest. Install it from the Plugin Manager like any other package.
    It runs **inside DeckHost** (devices need the live output and input callbacks; running them in their
    own process is not supported yet), takes SkiaSharp from the host as well as `DeckHost.Contracts`,
    and may be disabled and re-enabled without a restart: DeckHost blanks and unlists its devices,
    then starts it again. Its load context isn't unloaded (native callbacks, `[UnmanagedCallersOnly]`,
    can't live in a collectible one), so an old copy stays in memory until DeckHost restarts.

    Native libraries go next to the assembly in the package: a `[LibraryImport]` of the plugin is
    looked up in its folder first. A plugin that needs the folder itself (the assembly is loaded from
    memory, so it has no `Location`) reads `AppContext.GetData("DeckHost.PluginDirectory.<assembly name>")`.
    A `public static (bool Ok, string Report) CheckRuntime()` on the plugin class is run by
    `DeckHost --check-native` (MiraBox loads its native bridge and lists the devices).

    The plugins that ship with DeckHost are packages too, in `plugins/bundled/<id with dashes for dots>` next to the app (macOS codesign takes a folder with a dot in its name for a nested bundle):
    they can be disabled but not uninstalled, and an installed version of the same id replaces the
    bundled one. A project makes itself one by setting `<BundledPluginId>` and importing
    `build/BundledDevicePlugin.targets` (see `DeckHost.Device.Web.csproj`; files such as native
    libraries are added as `BundledPluginExtraFile` items, see `DeckHost.Device.MiraBox.csproj`);
    `DeckHost.App` copies them to `plugins/bundled`. The app keeps a reference to the HID and simulator
    projects for the device creator and the simulator window; those copies never run the plugin (the
    creator writes the description files the HID plugin watches).

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

Give it a `plugin.json` (`"kind": "device"`) and build it as a package: copy the Web Deck's project file
for a bundled one, or zip the plugin like the OBS plugin for an installable one. The architecture
tests check that a device plugin references nothing of DeckHost but Contracts.

## Describing a device

A `DeviceDefinition(id, name, controls, isConnected, detail)` per device. Keep devices listed while
they're unplugged (with `isConnected: false`), so their profiles can be edited; remember devices
seen before (the Elgato plugin keeps them in `devices/elgato-known.json`). Give each device a stable
id, ideally from its serial number (`elgato.<product>.<serial>`).

Describe the hardware itself too (the device descriptor, [decision 0011](adr/0011-device-descriptor.md));
all of it is optional, but the editor, the Plugins page and fallbacks use it:

```csharp
new DeviceDefinition(id, "My Deck", controls, isConnected, detail)
{
    Identity = new DeviceIdentity("Maker", "My Deck", serial, FirmwareVersion: firmware, Connection: "USB"),
    Displays = [new DeviceDisplay("main", 800, 480) { RotationDegrees = 180, ControlIds = ["key.0", "key.1"] }],
    Capabilities = DeviceCapabilities.Images | DeviceCapabilities.Brightness | DeviceCapabilities.RgbLights,
    Limits = new DeviceLimits { MaxFramesPerSecond = 30, Notes = ["The knob reports no release."] },
    PhysicalSize = new PhysicalSize(118, 84),
}
```

Controls whose images are on a display say so with `DisplayId` (and may give `PhysicalBounds` in
millimetres and a `Shape`). `definition.FindDescriptorProblems()` lists what doesn't add up; put it in
your tests.

Each control is a `PhysicalControl`:

| | |
|---|---|
| `DisplayId`, `PhysicalBounds`, `Shape` | The display it's on, where it is in millimetres, and whether it's round (a knob). |
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

## Your models in the device simulator

Implement the optional `IDeviceModelCatalog` (`DescribeModels()`): every model your plugin drives,
as an unplugged `DeviceDefinition`. The device simulator (**DeckHost settings → Developer → Device
simulator**, then **☰ → Device simulator** or `DeckHost --simulator`) then offers them; a simulator window acting as one appears as a connected
device (`simulator.<your device id>`), so you can try your layout, rendering and input mapping
without the hardware. The simulator uses only the descriptor, so a good descriptor (control bounds,
displays, `LedIndex`, `DisplayControlId`, capabilities such as `Haptics`) is what makes it look
and behave like the device.

## Testing

Keep the protocol pure and testable: the Elgato plugin's `StreamDeckProtocol` (report building and
input parsing) and the MiraBox model table (`MiraBoxModels`) have no I/O and are covered by unit tests; only a thin
layer talks to the hardware. The editor shows every device even when unplugged, so you can check
the layout and the rendered images without hardware, and the device simulator runs the whole input
and output path for any model in your `IDeviceModelCatalog`.
