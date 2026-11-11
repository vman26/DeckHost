# DeckHost

DeckHost is a modular host for physical control surfaces (macro pads, rotary consoles, touch strips). It separates the hardware from the actions: device plugins drive the hardware, application plugins provide the actions, and the designer lets you map one onto the other.

The first supported device is the **MiraBox / StreamDock N4 Pro**: 10 LCD keys, a touch strip with 4 zones and swipe, 4 knobs and 4 RGB LEDs.

## Core architecture
- **Hardware decoupled:** the core only talks to `IDevicePlugin` / `IDeviceOutput`. Everything device-specific (SDK ids, image sizes, LEDs, native calls) lives in the device plugin.
- **Per-control rendering:** each key and touch zone is rendered on its own with SkiaSharp and pushed only when its pixels changed. Render buffers, paints and fonts are reused between frames.
- **One runtime worker:** input, actions, rendering and USB I/O run on a single background worker, so native SDK callbacks return immediately and the UI thread never waits on the device.
- **Reactive state:** a thread-safe Rx.NET variable store connects actions and renderers.
- **Saved profiles:** mappings are stored in `%AppData%/DeckHost/profiles.json` (or the platform equivalent).
- **Cross-platform:** .NET 10 and Avalonia UI. The native bridge builds on Windows, Linux and macOS; Windows is the primary development platform.

## Building from source

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- CMake 3.21+ and a C++20 compiler (MSVC on Windows; Visual Studio's bundled CMake is detected automatically)

### Steps
```powershell
git clone --recursive <repository-url>
cd DeckHost
dotnet build .\DeckHost.slnx -c Debug
dotnet run --project .\DeckHost.App
dotnet test .\DeckHost.Tests
```

`dotnet build` also configures and builds the native bridge (`native/MiraBoxBridge`) with CMake and copies it to `plugins/device/deckhost.device.mirabox/` next to the app. To build only the C# code, add `-p:EnableMiraBoxNativeBuild=false`; the app then starts without device support.

## StreamDock SDK patch

`native/StreamDock-SDK` is pinned to an upstream commit. The local fixes it needs are kept in `native/patches/streamdock-sdk.patch`, and the build applies them automatically (it checks first, so applying twice is harmless).

To apply or check the patch by hand:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Apply-StreamDockSdkPatch.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\Apply-StreamDockSdkPatch.ps1 -CheckOnly
```

When you update the submodule and the patch no longer applies, regenerate it against the new upstream commit with `git -C native/StreamDock-SDK diff > native/patches/streamdock-sdk.patch` after fixing the files.

## N4 Pro key mapping

The SDK numbers the N4 Pro's keys from the bottom-left, so the top row is SDK keys 11–15 and the bottom row 6–10. If an image appears on the wrong key on your unit, assign the **Key ID (Diagnostic)** action to the keys and adjust `KeyHardwareIds` in `DeckHost.Device.MiraBox/N4ProLayout.cs`.

## License
MIT License. See `LICENSE` for details.
