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
dotnet build .\plugins\DeckHost.Plugin.Obs                              # -> artifacts\plugins\deckhost.plugin.obs.zip
```

- The plugin catalog tests install the real OBS package, so `DeckHost.Tests` builds the OBS project first (`ReferenceOutputAssembly=false`).

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
  - `Plugins/PluginCatalog`: system plugins plus installed plugin packages. Packages (`.zip` with `plugin.json`, see `PluginManifest`) install to `%AppData%/DeckHost/plugins/functional/<id>`. They load into a collectible `PluginLoadContext` **from memory**, so the files are never locked and uninstall can delete them immediately. `DeckHost.Contracts` is always shared from the host. Enabled/disabled state lives in `plugins/state.json`. It is not thread-safe, so use it from the UI thread.
- **`DeckHost.App`**: Avalonia MVVM UI (dark theme) with view models in `ViewModels/`.
  - `App.axaml.cs` is the composition root. It wires everything by hand (no DI). It calls `StartAsync` on `MainWindow.Opened` and, on `Exit`, saves profiles and runs `DisposeAsync`, which blanks the device and shuts the SDK down.
  - `SystemPlugins/`: the plugins that ship with DeckHost: Built-ins (required), Media Controls and System Controls. They derive from `DeckActionBase`. `Platform/` holds the Windows `SendInput` code (`WindowsInput`, `Hotkey`) and `SystemMetrics`. These plugins can be disabled but not uninstalled.
  - Bindings are immutable `ControlBinding` models. Every edit (drop, inspector, Customize) builds a new model and calls `MainViewModel.ApplyBinding`. `ControlBindingViewModel` only links a model to the loaded actions.
- **`plugins/DeckHost.Plugin.Obs`**: an *installable* plugin (obs-websocket v5) and the template for third-party plugins. It references Contracts with `Private=false`, and building it writes `artifacts/plugins/deckhost.plugin.obs.zip`. The app does not reference it.

Actions:

- An `IDeckAction` declares `Settings` (`ActionSettingDefinition`s). The Customize window generates an editor from them, and the values are stored per binding (and per Multi Action step) in `ActionReference.Settings`. Execute/Render read them through `ActionContext.Settings` / `IRenderContext.Settings`.
- `ActionContext.Host` (`IActionHost`) lets an action request a re-render after async work or switch profiles. `RefreshInterval` makes the host redraw live actions (clock, system info).
- `Execute` and `Render` run on the host worker and must stay fast. Process starts, network calls and similar work go to a background task, which then calls `RequestRender`. Show errors on the control through `VariableKeys.LastError`.
- Bump `PluginManifest.CurrentHostApiVersion` when `IDeckAction` or `IApplicationPlugin` change incompatibly for installed plugins.

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
  - Dragging a mapped control onto another moves its whole binding (`MainViewModel.MoveBinding`): swap when the target is taken, Ctrl copies. A drag only starts after the pointer moves a few pixels, so click and double-click still work.
  - Double-click a control (or right-click → Customize) to customize it. Hotkey settings (`ActionSettingKind.Hotkey`) can be recorded from a key press in Customize.
  - Profiles: the Profile Manager window creates, duplicates, renames, reorders and deletes them. `DeviceProfile.AutoSwitchApps` and `ProfileDocument.AutoSwitchEnabled` drive automatic switching. `Platform/ForegroundAppWatcher` (Windows, 500 ms polling, ignores DeckHost itself) feeds `AutoProfileSelector`. When no app matches, it returns to the manually chosen profile (`_manualProfileId`, which is what `selectedProfileId` saves).
  - Right-click a control for Customize / Remove. Customize opens a separate window with the action's settings, the Multi Action step editor (add, remove, reorder, per-step settings), text and image (file picker) and, for knobs, the LED. It previews live, and Cancel restores the previous binding.
  - Multi Action is an action type, and actions are grouped by plugin in collapsible groups. Display Content is hidden from the list: setting a text or image on an empty control creates it.
  - A hamburger menu shows the current page. The Plugin Manager window (same dark style) installs .zip packages, enables/disables plugins, uninstalls installed ones and shows the devices with their firmware version.
- Firmware: the StreamDock SDK can only *read* the firmware version (`transport_get_firmware_version`); it has no update/flash API. Don't add update logic without an official MiraBox protocol.
