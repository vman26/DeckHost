# DeckHost

DeckHost is a low-latency, modular host environment for physical control surfaces (macro pads, rotary consoles, touchscreen controllers). It separates the hardware from the actions—allowing you to use Elgato Stream Deck plugins on a MiraBox N4 Pro, or route native telemetry to any compatible device.

## Core Architecture
- **Hardware Decoupled:** DeckHost core knows nothing about specific devices. Support is injected via Device Plugins.
- **Zero-Allocation Rendering:** High-performance, GC-friendly framebuffer generation over USB.
- **Reactive State:** Powered by Rx.NET for high-frequency telemetry and state management.
- **Cross-Platform:** Built on .NET 10 and Avalonia UI for Windows, Linux, and macOS.

## Building from Source
### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Avalonia UI Templates (`dotnet new install Avalonia.Templates`)
- C++ Compiler (MSVC for Windows, GCC/Clang for Linux/macOS)

### Steps
1. Clone the repository: `git clone https://github.com/yourusername/DeckHost.git`
2. Build the native bridge: `cd native/MiraBoxBridge && cmake . && make`
3. Build the core solution: `dotnet build src/DeckHost.sln`
4. Run the application: `dotnet run --project src/DeckHost.App`

## StreamDock SDK patch workflow

The `native/StreamDock-SDK` submodule currently needs a small local compatibility fix after updates.

After updating the submodule, re-apply the patch from the repository root:

- `powershell -ExecutionPolicy Bypass -File .\scripts\Apply-StreamDockSdkPatch.ps1`

To verify that the patch still applies cleanly without changing files:

- `powershell -ExecutionPolicy Bypass -File .\scripts\Apply-StreamDockSdkPatch.ps1 -CheckOnly`

Then validate the workspace build:

- `dotnet build .\DeckHost.slnx -c Debug`

If the patch no longer applies, refresh `native/patches/streamdock-sdk.patch` against the updated submodule contents.

## License
MIT License. See `LICENSE` for details.
