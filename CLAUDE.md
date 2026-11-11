# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

DeckHost is a .NET 10 / Avalonia 12 desktop host for physical control surfaces (currently the MiraBox N4 Pro). A C++ bridge (`native/MiraBoxBridge`) wraps the vendor StreamDock C++ SDK (git submodule at `native/StreamDock-SDK`) and exposes a flat C ABI that the C# device plugin P/Invokes.

## Commands

Run from the repository root (Windows is the primary dev platform).

```powershell
dotnet build .\DeckHost.slnx -c Debug                                   # builds C# and (via CMake) the native bridge
dotnet build .\DeckHost.slnx -c Debug -p:EnableMiraBoxNativeBuild=false # C# only, skip CMake
dotnet run --project .\DeckHost.App
powershell -ExecutionPolicy Bypass -File .\scripts\Apply-StreamDockSdkPatch.ps1 [-CheckOnly]
```

- The solution file is `DeckHost.slnx` at the root. The README's `src/DeckHost.sln` and `cmake . && make` steps are out of date.
- There are no test projects yet.
- The native bridge build is driven from `DeckHost.Device.MiraBox.csproj` (targets `DetectCMake` → `ConfigureMiraBoxBridge` → `BuildMiraBoxBridge`). CMake is looked up via `CMakeExePath`, then Visual Studio's bundled CMake (`DevEnvDir` / `VSInstallDir`), then `PATH`. If none is found it only warns, and the app runs without native binaries.
- Native output goes to `native/MiraBoxBridge/out/<Config>/bin/<Config>/`. `DeckHost.App.csproj` copies it into `bin/<Config>/net10.0/plugins/device/deckhost.device.mirabox/`. `MiraBoxNative`'s `DllImportResolver` loads `MiraBoxBridge` **only** from that folder and first preloads every other native binary there (hidapi → transport → opencv).
- `CMakePresets.json` / the root `CMakeLists.txt` are the Visual Studio "Open Folder" path. They build into `out/build/<preset>`, which nothing copies from.
- After updating the StreamDock submodule, re-apply `native/patches/streamdock-sdk.patch` with the script above.

## Architecture

Projects and references:

- `DeckHost.Contracts` holds the plugin-facing interfaces and records: `IDevicePlugin`, `IApplicationPlugin`, `IDeckAction`, `IRenderContext`, `IVariableStore`, plus models such as `DeviceDefinition`, `PhysicalControl`, `DeviceProfile` and `ControlBinding`. Note that `InputCapabilities` and `OutputCapabilities` live in `Models/` but use the `DeckHost.Contracts.Enums` namespace.
- `DeckHost.Core` holds the runtime: `DeviceHostService`, `ActionRouter`, the Rx `VariableStore` and Skia rendering. It **currently references `DeckHost.Device.MiraBox` directly** and calls `MiraBoxNative` for key images, RGB and the display buffer. That breaks the intended "core knows nothing about devices" rule. Don't add more of these calls; route device output through a plugin abstraction instead.
- `DeckHost.Device.MiraBox` holds `MiraBoxDevicePlugin` (builds the hard-coded N4 Pro `DeviceDefinition` and maps native events to control IDs), `MiraBoxController` (owns the native callback delegate) and `MiraBoxNative` (the P/Invoke surface).
- `DeckHost.App` is the Avalonia MVVM UI. `App.axaml.cs` is the composition root: it wires everything by hand (no DI container) and calls `DeviceHostService.StartAsync` on `MainWindow.Opened`. First-party application plugins are compiled into `DeckHost.App/Plugins/FirstPartyApplicationPlugins.cs`. Nothing loads plugins from the `plugins/` folders at runtime yet.

Runtime flow:

- **Input:** native SDK read thread → `MiraBox_SetCallback` delegate → `MiraBoxController.InputReceived` → `MiraBoxDevicePlugin.TryMapInput` → `IDevicePluginContext.ReportInputEvent` → `DeviceHostService.DispatchInput` → `ActionRouter.Dispatch` → `IDeckAction.Execute`. After that comes RGB feedback and a full re-render of the active profile. All of this runs on the native thread, not the UI thread.
- **Output:** `RenderProfileOutputs` → `ProfilePreviewAction.GetControlRenderJobs()` gives one job per output-capable control. Keys are drawn with a per-key `SurfaceRenderer` and sent with `MiraBox_SendKeyImage` (the SDK's `setKeyImgFileStream`). `TouchSurface` goes through `DeviceDisplaySession` → `MiraBox_SendDisplayBuffer` (`setBackgroundImgStream`). Encoders are not drawn. Pixels are RGBA8888, and the bridge JPEG-encodes them with OpenCV.
- Native calls hard-code device index `0`.

Conventions that span files:

- Control IDs are strings: `key.<n>`, `encoder.<n>`, `touch.0`. Several places parse `key.` + an int, so keep that format.
- A logical key index maps to the SDK key ID as `Capabilities.MinKeyId + index` (`DeviceDefinition.GetSdkKeyId`). `Capabilities` come from the native `QueryDeviceInfo` JSON. If they are null, key rendering and RGB feedback are skipped without any error.
- An action is identified by `"{pluginId}:{actionId}"` in `ActionRouter`, and `ActionReference` stores both parts.
- `VariableStore` keys follow `devices.{deviceId}.{controlId}.{lastEvent|lastValue|lastAction|lastActionAt|executionCount|appearance.text|appearance.image|sdkKeyId}`. Actions and renderers talk to each other through these keys, so renaming one breaks the other side without any error.
- Every new `extern "C"` export in `MiraBoxBridge.cpp` needs a matching `[LibraryImport(..., EntryPoint = "...")]` in `MiraBoxNative.cs` with the exact same name. Also declare it in `MiraBoxBridge.h`. Native functions return `0` on success and negative codes on failure, and must never let C++ exceptions escape.

## Project guidelines (from `.github/copilot-instructions.md`)

- Don't modify third-party code (the StreamDock SDK) when a project-side workaround is possible. When an SDK change can't be avoided, put it in `native/patches/streamdock-sdk.patch`.
- There are two plugin categories: device plugins (hardware) and application/functional plugins (features such as media control).
- Render per control with `setKeyImgFile` / `setKeyImgFileStream`, not one 800×480 framebuffer through `setBackgroundImgStream`. The designer should show what the device would show.
- Use the SDK features for the matching hardware jobs: RGB through `setLedBrightness` / `setLedColor` / `setSingleLedColor`, input through `ReadController`, device configuration through `Configer`.
- UI behavior:
  - Drag an assignment off a control, or drop it outside a control, to remove it.
  - While dragging, show the control itself as the drag visual.
  - Right-click a control for a context menu with Customize and Remove. Customize opens a separate window with a file picker for images.
  - Multi Action is an action type, and actions are grouped by plugin in collapsible groups.
  - A hamburger menu shows the current page, and a separate Plugin Manager window lists the plugins.
