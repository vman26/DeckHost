# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

DeckHost is a .NET 10 / Avalonia 12 desktop host for physical control surfaces. The only device so far is the MiraBox / StreamDock **N4 Pro**. A C++ bridge (`native/MiraBoxBridge`) wraps the vendor StreamDock C++ SDK (git submodule at `native/StreamDock-SDK`) and exposes a flat C ABI that the C# device plugin P/Invokes.

## Commands

Run these from the repository root. Windows is the primary development platform.

```powershell
dotnet build .\DeckHost.slnx -c Debug                                   # C# + native bridge (via CMake)
dotnet build .\DeckHost.slnx -c Debug -p:EnableMiraBoxNativeBuild=false # C# only
dotnet run --project .\DeckHost.App
dotnet test .\DeckHost.Tests -p:EnableMiraBoxNativeBuild=false
dotnet test .\DeckHost.Tests --filter "FullyQualifiedName~N4ProLayoutTests"   # one class
```

- `DeckHost.Device.MiraBox.csproj` drives the native build. Its targets run in this order: `DetectCMake` → `ApplyStreamDockSdkPatch` (idempotent `git apply` of `native/patches/streamdock-sdk.patch`) → `ConfigureMiraBoxBridge` → `BuildMiraBoxBridge`. CMake is found through `CMakeExePath`, then Visual Studio's bundled CMake, then `PATH`. If none is found, the build only warns and the app runs without device support.
- Native output goes to `native/MiraBoxBridge/out/<Config>/bin/<Config>/`. `DeckHost.App.csproj` copies it to `bin/.../plugins/device/deckhost.device.mirabox/`. `MiraBoxNative`'s `DllImportResolver` loads `MiraBoxBridge` **only** from that folder, after preloading the other native binaries there (hidapi → transport → opencv).
- The submodule is pinned to an upstream commit with `ignore = dirty`. Never commit inside it. SDK changes go into the patch file, per the rule below about third-party code.
- `CMakePresets.json` and the root `CMakeLists.txt` are only for Visual Studio's "Open Folder" mode. Nothing copies from their `out/build/<preset>` output.

## Architecture

The dependency rule: `Contracts` ← `Core` ← `App` → `Device.MiraBox` → `Contracts`. **Core must never reference a device plugin.** All hardware access goes through `IDevicePlugin` and `IDeviceOutput`, which are defined in Contracts.

- **`DeckHost.Contracts`**: the plugin-facing surface.
  - Interfaces: `IDevicePlugin` (devices, `DevicesChanged`, `Output`, `InitializeAsync`), `IDeviceOutput` (`TryPushImage`, `TrySetLight`, `Clear`), `IDevicePluginContext` (input, connection status, log), `IApplicationPlugin`, `IDeckAction`, `IRenderContext`, `IVariableStore`.
  - Records: `PhysicalControl` carries the device pixel size (`RenderWidth`/`RenderHeight`), `HardwareId` and `LedIndex`. `DeviceDefinition` has an `IsConnected` flag.
- **`DeckHost.Core`**: the runtime.
  - `DeviceHostService`: every public method only enqueues work onto a `Channel` that **one worker task** processes in order. That worker owns all mutable state, which is why the class has almost no locks. Events are raised on background threads.
  - `ActionRouter`: dispatches input events to actions.
  - `Rendering/`: `SurfaceRenderer` (pinned buffer, one per control size, reused), `SkiaRenderContext`, `SkiaDrawingResources` (cached paints, fonts and decoded images) and `ControlRenderer`.
  - `State/VariableStore`: the Rx variable store.
  - `Profiles/ProfileStore`: profiles as JSON in `%AppData%/DeckHost/profiles.json`.
- **`DeckHost.Device.MiraBox`**:
  - `N4ProLayout`: designer layout, SDK id mapping and input mapping. It's pure and covered by tests.
  - `MiraBoxDevicePlugin`: device tracking keyed by the SDK's device key (HID path), LED state, long-press synthesis and the `[UnmanagedCallersOnly]` native callback.
  - `MiraBoxNative`: the P/Invoke layer, using function pointers and UTF-8 strings.
- **`DeckHost.App`**: Avalonia MVVM UI with one view model class per file.
  - `App.axaml.cs` is the composition root. It wires everything by hand (no DI). It calls `StartAsync` on `MainWindow.Opened` and, on `Exit`, saves profiles and runs `DisposeAsync`, which blanks the device and shuts the SDK down.
  - First-party application plugins live in `Plugins/FirstPartyApplicationPlugins.cs`. Nothing loads plugins from the `plugins/` folders at runtime yet.

Runtime flow:

- **Input:** SDK read thread → `OnNativeEvent` → `N4ProLayout.TryMapInput` → `IDevicePluginContext.ReportInputEvent` → queue → worker → `ActionRouter.Dispatch` → `IDeckAction.Execute` → LED feedback. The device is then marked dirty.
- **Output:** after each batch of work, the worker renders every dirty device. For each control with an image, it draws the background, then the appearance image, then the bound action's own `Render`. The result goes out through `IDeviceOutput.TryPushImage` **only if the pixels differ from the last frame pushed**. The bridge encodes with OpenCV, using the SDK's image helper (which applies the N4 Pro's 180° rotation), and calls `setKeyImgFileStream`. N4 Pro key IDs 6–15 are the main keys and 1–4 are the touch-strip zones.
- **UI:** `MainViewModel` marshals host events with `Dispatcher.UIThread.Post`. Never touch view-model collections from a host or SDK callback.

Conventions that span files:

- Control IDs: `key.0`–`key.9` (reading order), `touch.0`–`touch.3`, `touch.swipe`, `encoder.0`–`encoder.3`. The N4 Pro's SDK IDs are numbered from the bottom-left, so `key.0` is SDK key 11 (see `N4ProLayout.KeyHardwareIds`).
- Build variable-store keys with `DeckHost.Contracts.VariableKeys`, which produces `devices.{deviceId}.{controlId}.{suffix}`. Never write them as string literals. Actions and renderers communicate only through these keys.
- An action is identified by `"{pluginId}:{actionId}"`. Duplicates are logged, not thrown.
- Saved bindings whose plugin isn't loaded are kept and written back unchanged (`ProfileItemViewModel.AddUnresolvedBinding`).
- Native ABI: every export in `MiraBoxBridge.cpp` must be declared in `MiraBoxBridge.h` and have a `[LibraryImport(EntryPoint = "...")]` with the identical name in `MiraBoxNative.cs`. Exports address devices by device key, return `>= 0` on success or a negative `MiraBoxResult`, and never let a C++ exception escape. The managed callback must not block or throw: the SDK holds a lock while calling it.

## Project guidelines (from `.github/copilot-instructions.md`)

- Don't modify third-party code (the StreamDock SDK) when a project-side workaround is possible. Unavoidable SDK changes go into `native/patches/streamdock-sdk.patch`.
- Two plugin categories: device plugins (hardware) and application/functional plugins (features such as media control).
- Render per control (`setKeyImgFileStream`), not one 800×480 framebuffer through `setBackgroundImgStream`. The designer should show what the device would show.
- Use the SDK's feature controllers: RGB through `setLedBrightness` / `setLedColor` / `setSingleLedColor`, input through `ReadController`, device configuration through `Configer`.
- UI behavior:
  - Dragging an assignment off a control, or dropping it anywhere in the window that isn't a control, removes it. A cancelled drag keeps it.
  - The drag visual shows the control.
  - Right-click a control for Customize / Remove. Customize opens a separate window with a file picker and, for knobs, the LED settings. It previews live, and Cancel restores the previous values.
  - Multi Action is an action type, and actions are grouped by plugin in collapsible groups.
  - A hamburger menu shows the current page, and a separate Plugin Manager window lists the plugins.
