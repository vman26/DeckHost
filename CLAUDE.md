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
- The tests run on Linux too: `DeckHost.Tests` adds `SkiaSharp.NativeAssets.Linux` there. Wait for the host with `WhenIdleAsync`, not with thread ids or delays: the worker and the test both run on pool threads.

- `DeckHost.Device.MiraBox.csproj` drives the native build. Its targets run in this order: `DetectCMake` → `ApplyStreamDockSdkPatch` (idempotent `git apply` of `native/patches/streamdock-sdk.patch`) → `ConfigureMiraBoxBridge` → `BuildMiraBoxBridge`. CMake is found through `CMakeExePath`, then Visual Studio's bundled CMake, then `PATH`. If none is found, the build only warns and the app runs without device support.
- `native/MiraBoxBridge/CMakeLists.txt` declares hidapi from the SDK's bundled `third_party/hidapi-master.zip` before adding the SDK (the first declaration wins); otherwise the SDK, which looks for it relative to the top-level project, would download hidapi's moving master branch. It also builds everything position-independent so the SDK's static libraries link into the shared library on Linux/macOS.
- Native output goes to `native/MiraBoxBridge/out/<Config>/bin/<Config>/`. `DeckHost.App.csproj` copies it to `bin/.../plugins/device/deckhost.device.mirabox/`. `MiraBoxNative`'s `DllImportResolver` loads `MiraBoxBridge` **only** from that folder, after preloading the other native binaries there (hidapi → transport → opencv).
- The submodule is pinned to an upstream commit with `ignore = dirty`. Never commit inside it. SDK changes go into the patch file, per the rule below about third-party code.
- `CMakePresets.json` and the root `CMakeLists.txt` are only for Visual Studio's "Open Folder" mode. Nothing copies from their `out/build/<preset>` output.

## Architecture

The dependency rule: `Contracts` ← `Core` ← `App` → `Device.MiraBox` → `Contracts`. **Core must never reference a device plugin.** All hardware access goes through `IDevicePlugin` and `IDeviceOutput`, which are defined in Contracts.

- **`DeckHost.Contracts`**: the plugin-facing surface.
  - Interfaces: `IDevicePlugin` (devices, `DevicesChanged`, `Output`, `InitializeAsync`), `IDeviceOutput` (`TryPushImage`, `TrySetLight`, `Clear`), optional `IDeviceBackdropOutput` (`TrySetBackdrop`), `IDevicePluginContext` (input, connection status, log), `IApplicationPlugin`, `IDeckAction`, `IRenderContext`, `IVariableStore`.
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
  - `Plugins/PluginCatalog`: system plugins plus installed plugin packages. Packages (`.zip` with `plugin.json`, see `PluginManifest`) install to `%AppData%/DeckHost/plugins/functional/<id>`. They load into a collectible `PluginLoadContext` **from memory**, so the files are never locked and uninstall can delete them immediately. `DeckHost.Contracts` is always shared from the host. Enabled/disabled state and each plugin's origin live in `plugins/state.json`. It is thread-safe (one re-entrant lock) and reports steps through `IProgress<string>`; the UI calls it from `Task.Run` and raises `Changed` on that thread, so view models post to the UI thread.
  - `N4ProDeviceSettings` (via `IConfigurableDevicePlugin`): vibration, boot video, LED/key-light config (`MiraBox_SetDeviceConfig`), brightness, and the idle screen (`MiraBox_SendBackgroundFile`; whatever `setBackgroundImgStream` writes persists as the device's standby screen). Stored in `%AppData%/DeckHost/devices/<id>.json` and reapplied on connect. "default" values are never sent.
- **`DeckHost.App`**: Avalonia MVVM UI (dark theme) with view models in `ViewModels/`.
  - `App.axaml.cs` is the composition root: a Microsoft.Extensions.DependencyInjection container exposed through CommunityToolkit's `Ioc.Default`, with NLog behind `ILogger` (`Platform/Logging.CreateConfiguration`). Windows resolve their view models from `Ioc.Default`. It calls `StartAsync` on `MainWindow.Opened` and, on `Exit`, saves profiles and runs `DisposeAsync`, which blanks the device and shuts the SDK down.
  - `SystemPlugins/`: the plugins that ship with DeckHost: Built-ins (required), Media Controls and System Controls. They derive from `DeckActionBase`. `Platform/` holds the Windows `SendInput` code (`WindowsInput`, `Hotkey`) and `SystemMetrics`. These plugins can be disabled but not uninstalled.
  - Bindings are immutable `ControlBinding` models. Every edit (drop, inspector, Customize) builds a new model and calls `MainViewModel.ApplyBinding`. `ControlBindingViewModel` only links a model to the loaded actions.
- **`DeckHost.Core/StreamDeck`**: the Elgato Stream Deck SDK compatibility layer, used for Elgato and OpenAction plugins.
  - `StreamDeckRuntime` starts each `.sdPlugin` as a process (native or `node`) with `-port -pluginUUID -registerEvent -info` and speaks the SD WebSocket protocol through `StreamDeckServer`, a loopback TcpListener that also serves property-inspector pages at `/pi/<context>` and `/plugins/<uuid>/...`.
  - A context is `{deviceId}|{profileId}|{controlId}|{actionUuid}`. Property inspectors are shown in the Customize window through `Views/WebView2Host` (WebView2 in a `NativeControlHost`); `IDeckAction.GetSettingsPageUri` supplies the URL. Images may be PNG/JPG/GIF or SVG (`Rendering/SvgRasterizer`, Svg.Skia). HTML-based plugins (`StreamDeckManifest.IsHtml`) run through `IHtmlPluginHost`, implemented by `Platform/WebView2PluginHost` (hidden WebView2 page per plugin that calls `connectElgatoStreamDeckSocket` after load). Instance settings live in `%AppData%/DeckHost/streamdeck/settings/instances.json`.
  - `StreamDeckApplicationPlugin` adapts a plugin to `IApplicationPlugin`. `IDeckAction.OnAppear/OnDisappear` (called by the host's `UpdateAppearances`) map to willAppear/willDisappear.
  - `PluginCatalog` installs `.streamDeckPlugin` packages into `plugins/streamdeck` (nested archives too). `PluginStore` reads the DeckHost sources (URLs, local catalog files or folders scanned for packages and built plugin folders; listed in `%AppData%/DeckHost/store.json`), OpenAction and the Elgato community archive. Catalogs carry no categories: `PluginCategories.Resolve` derives one. `PluginCatalog.RecordUnknownOrigins` backfills the origin of plugins installed before origins were recorded.
- **Linked displays (knob on the touch strip):** `PhysicalControl.DisplayControlId` links a control without a display to one that has one (N4 Pro: `encoder.N` → `touch.N`). `DeviceHostService.FindLinkedBinding` draws the knob's binding there through `ControlRenderer.Draw(..., LinkedBinding)`: the whole zone when the zone has no binding, else a strip (`ControlRenderer.LinkedStripHeight`) on the edge nearest the knob, drawn with `SkiaRenderContext.DrawRegion(compact: true)`, which gives the action its own `ControlId`/settings and `IRenderContext.IsCompact`. Actions draw one line when `IsCompact` (`DeckActionBase.DrawCompact`, `RenderContextExtensions.DrawReading`). `ControlAppearance.ShowOnLinkedDisplay` (an init property, so the record's constructor stays binary-compatible) turns it off. A tap on a zone that only shows the knob is dispatched as the knob's Press + Release (`FindTapTarget`). Stream Deck dial actions' `setFeedback` is kept as `DialFeedback`.
- **Screen backdrop:** devices that implement `IDeviceBackdropOutput` get the profile's background color (`ProfileBackdrop.GetColor`) behind the control images; the host sends it before the images and re-sends the images after it. On the N4 Pro this is the SDK's temporary frame background (`MiraBox_SetTemporaryBackground` → `setFrameBackgroundStream`), which covers the stored idle screen (`setBackgroundImgStream`) until `Clear` (`MiraBox_ClearTemporaryBackground`). Without it the idle image showed between the keys of every profile.
- **Preview / test mode:** the host renders every device even when unplugged and raises `ControlFrameRendered` (the canvas images) and `InputReceived` (press feedback). `SimulateInput` runs a fake input through the normal path. Profile backgrounds (`DeviceProfile.Background`) are drawn per control as a slice of one image over the layout (`SkiaRenderContext.DrawLayoutSlice`). GIFs animate through `SkiaDrawingResources` frames at `AnimationInterval` (100 ms).
- **N4 Pro start-up:** the bridge must start the SDK heartbeat, `wakeupScreen()`, and `refresh()` after images. Without the heartbeat the device ignores the host: key presses only toggle its own standby screen.
- **Log:** NLog writes the runtime log, including every input, to `%AppData%/DeckHost/logs/deckhost-<date>.log`; Stream Deck plugin output goes to `logs/plugins`.
- **Text and triggers:** `ControlAppearance.TextStyle` (color, font, size, bold/italic, alignment, wrap) is applied by `IRenderContext.DrawLabel`; actions should draw titles with it. `DeckActionBase` adds a "Run when" trigger setting to actions whose inputs were press-only; each option sets `ActionSettingOption.Requires`, and the editor hides options the control's `Inputs` can't produce.
- **Single instance and tray:** `Program.Main` holds a named mutex (a second launch signals the first to show its window). The app uses `ShutdownMode.OnExplicitShutdown` with a tray icon; `Platform/AppSettings` stores start-with-Windows (HKCU Run key, `--startup`), start-minimized and close-to-tray.
- **Device redraw:** a device plugin that overwrites the device screen (the N4 Pro idle screen) calls `IDevicePluginContext.RequestRedraw`, which makes the host forget its pushed frames and send every image again.- **`plugins/DeckHost.Plugin.Obs`**: an *installable* plugin (obs-websocket v5) and the template for third-party plugins. It references Contracts with `Private=false`, and building it writes `artifacts/plugins/deckhost.plugin.obs.zip`. The app does not reference it.

Actions:

- An `IDeckAction` declares `Settings` (`ActionSettingDefinition`s). The Customize window generates an editor from them, and the values are stored per binding (and per Multi Action step) in `ActionReference.Settings`. Execute/Render read them through `ActionContext.Settings` / `IRenderContext.Settings`.
- Give every `Number` setting a `Range` (`NumberRange`: minimum, maximum, step, optional `Unit`, `ShowInput`, and `UnsetValue` for settings that are empty by default). With a range the editor (`Views/NumberSettingEditor`, shared by Customize and Device Settings) shows a slider plus a number box; without one only a number box (fine for a port). Values are stored as invariant-culture text (`NumberRange.Format`); read them with `GetInt` / `GetDouble`. `Range` is an init property so the record's constructor stays binary-compatible for installed plugins.
- `ActionContext.Host` (`IActionHost`) lets an action request a re-render after async work or switch profiles. `RefreshInterval` makes the host redraw live actions (clock, system info).
- `Execute` and `Render` run on the host worker and must stay fast. Process starts, network calls and similar work go to a background task, which then calls `RequestRender`. Show errors on the control through `VariableKeys.LastError`.
- Bump `PluginManifest.CurrentHostApiVersion` when `IDeckAction` or `IApplicationPlugin` change incompatibly for installed plugins.

Runtime flow:

- **Input:** SDK read thread → `OnNativeEvent` → `N4ProLayout.TryMapInput` → `IDevicePluginContext.ReportInputEvent` → queue → worker → `ActionRouter.Dispatch` → `IDeckAction.Execute` → LED feedback. The device is then marked dirty.
- **Output:** after each batch of work, the worker renders every dirty device. For each control with an image, it draws the background, then the appearance image, then the bound action's own `Render`. The result goes out through `IDeviceOutput.TryPushImage` **only if the pixels differ from the last frame pushed**. The bridge encodes with OpenCV, using the SDK's image helper (which applies the N4 Pro's 180° rotation), and calls `setKeyImgFileStream`. N4 Pro key IDs 6–15 are the main keys and 1–4 are the touch-strip zones.
- **Pixel format:** the host renders RGBA; OpenCV (the SDK's `RawCanvas`) is BGRA. `EncodeRgba` in the bridge swaps R and B while copying. Copying the bytes unchanged shows red as blue on the device.
- **Knob release:** the N4 Pro reports a knob press but no release; `MiraBoxDevicePlugin` reports a Release `KnobReleaseDelay` after each press (Stream Deck plugins get dialDown and dialUp).
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
  - `Views/ColorField` compares with the text it last showed, not only `_syncing`: `TextChanged` and `ColorChanged` arrive after `Sync` returns, and used to write the shown default (#000000, #FFFFFF) into every control (and profile) whose editor was opened.
  - Right-click a control for Customize / Remove. Customize opens a separate window with the action's settings, the Multi Action step editor (add, remove, reorder, per-step settings), text and image (file picker) and, for knobs, the LED. It previews live, and Cancel restores the previous binding.
  - Multi Action is an action type, and actions are grouped by plugin in collapsible groups. Display Content is hidden from the list: setting a text or image on an empty control creates it.
  - A hamburger menu shows the current page. The Plugin Manager window (same dark style) installs .zip packages, enables/disables plugins, uninstalls installed ones and shows the devices with their firmware version.
- Firmware: the StreamDock SDK can only *read* the firmware version (`transport_get_firmware_version`); it has no update/flash API. Don't add update logic without an official MiraBox protocol.
