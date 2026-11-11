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

## Plugins

DeckHost ships with three **system plugins**. You can switch them off in the Plugin Manager, but you can't uninstall them:

| Plugin | Actions |
|---|---|
| DeckHost Built-ins (always on) | Multi Action, Switch Profile, Clock, Counter, Key ID (Diagnostic) |
| Media Controls | Play / Pause, Next, Previous, Stop, Volume Up / Down, Mute, Volume Knob, Track Swipe |
| System Controls | Launch App, Open Website or File, Hotkey, Type Text, Lock Computer, System Info (CPU, memory, disk, uptime) |

Media keys, hotkeys, typing and locking use Windows `SendInput`; on other systems those actions show "Windows only".

Actions with settings (the app to launch, a hotkey, a scene name, ...) are set up with **double-click** or **right-click → Customize**. Multi Action steps are added, removed, reordered and configured there too, or added by dropping more actions onto the control. For a Hotkey, click **Record** and press the combination.

Drag a mapped control onto another control to move it (the two swap if the target is taken), hold **Ctrl** to copy it, or drop it on an empty area to remove it.

## Profiles

Profiles are the pages of a device. **☰ → Open Profile Manager** (or **Manage…** next to the profile list) creates, duplicates, renames, reorders and deletes them. The order is also what the Next/Previous Profile actions follow.

**Automatic switching (Windows):** give a profile one or more apps (process names such as `obs64` or `chrome`, or pick a running app) and it becomes active while that app's window is in front. When the focused app isn't listed anywhere, DeckHost returns to the profile you picked yourself. DeckHost's own windows never trigger a switch. Turn it off with the switch in the Profile Manager or the ☰ menu.

### Installable plugins

Other plugins are installed from a package with **☰ → Open Plugin Manager → Install plugin…**. A package is a `.zip` with a `plugin.json` and the plugin's assemblies:

```json
{
  "id": "deckhost.plugin.obs",
  "name": "OBS Studio",
  "version": "1.0.0",
  "author": "DeckHost",
  "description": "…",
  "assembly": "DeckHost.Plugin.Obs.dll",
  "hostApiVersion": 1
}
```

The assembly must contain a public `IApplicationPlugin` with a parameterless constructor whose `Id` matches the manifest. It must not ship its own copy of `DeckHost.Contracts`. Installed plugins live in `%AppData%/DeckHost/plugins/functional/<id>`. Plugins run with your user's rights, so only install packages you trust.

`plugins/DeckHost.Plugin.Obs` is an example: OBS Studio control through obs-websocket 5 (OBS 28+). It can switch scenes, toggle mute, toggle streaming and recording, and save the replay buffer. Build it with `dotnet build .\plugins\DeckHost.Plugin.Obs` and install `artifacts\plugins\deckhost.plugin.obs.zip`. Then enter the OBS WebSocket password in each OBS action's settings (OBS: Tools → WebSocket Server Settings).

### Firmware

The Plugin Manager shows each device's serial number and firmware version. The StreamDock SDK can read the firmware version but has no function to update it, so firmware updates still go through MiraBox's own Stream Dock software.

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
