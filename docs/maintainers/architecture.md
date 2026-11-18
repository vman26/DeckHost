# Architecture

DeckHost connects control surfaces (keys, knobs, touch strips, lights) to actions. Hardware, actions and the editor are separate parts that only meet through `DeckHost.Contracts`.

```
                 ┌──────────────────────────┐
                 │  DeckHost.App (Avalonia) │  composition root, editor, pages,
                 │                          │  built-in action plugins, per-OS code
                 └──┬──────────┬─────────┬──┘
                    │          │         │
          ┌─────────▼───┐   ┌──▼─────────▼───────────────────────────┐
          │DeckHost.Core│   │ Device plugins                         │
          │  runtime    │   │ MiraBox (native SDK) · Elgato (HID) ·  │
          └─────────┬───┘   │ Web Deck (HTTP + WebSocket)            │
                    │       └──┬─────────────────────────────────────┘
          ┌─────────▼──────────▼───┐
          │   DeckHost.Contracts   │  what plugins see
          └────────────────────────┘
```

The rules ([0001](adr/0001-dependency-rule.md)) are checked by `ArchitectureTests`: Contracts references nothing of DeckHost; Core only Contracts; device plugins only Contracts; only the App knows the device plugins.

## The parts

| Part | What it does | Key types |
|---|---|---|
| Contracts | Interfaces and records shared with plugins | `IDevicePlugin`, `IDeviceOutput`, `IDeckAction`, `IApplicationPlugin`, `ControlBinding`, `Workflow` |
| Core | The host worker, input routing, rendering, profiles, plugins (and running them in their own process), updates, workflows, firmware, Stream Deck plugin runtime | `DeviceHostService`, `ActionRouter`, `WorkflowEngine`, `SurfaceRenderer`, `ProfileStore`, `PluginCatalog` |
| Device plugins | Find devices, report input, show images and lights | `MiraBoxDevicePlugin`, `ElgatoDevicePlugin`, `WebDeckPlugin`, `HidDevicePlugin` (user-described USB devices) |
| App | Window, editor, pages, built-in actions, OS integration | `MainViewModel`, `WorkflowEditorViewModel`, `Platform/*` |

## Flows

**Input:** device thread → `IDevicePluginContext.ReportInputEvent` → the host's queue → worker → `ActionRouter` (or the workflow engine) → action `Execute` → lights and variables → dirty devices.

**Output:** after each batch the worker renders every dirty device per control (background, appearance, the action's `Render`) and pushes only frames whose pixels changed ([0002](adr/0002-single-worker.md)).

**Edits:** every edit in the UI builds a new immutable binding and hands the host a new snapshot ([0004](adr/0004-immutable-bindings.md)).

**Profiles on disk:** `profiles.json` with a format version; older files are backed up and migrated on load ([0005](adr/0005-profile-migrations.md)).

## Third-party code

| Package | Used for | License |
|---|---|---|
| Avalonia (+ Fluent theme, ColorPicker, WebView) | The UI | MIT |
| SkiaSharp, Svg.Skia | Drawing control images, SVG icons | MIT |
| CommunityToolkit.Mvvm | View models | MIT |
| Microsoft.Extensions.DependencyInjection | Composition | MIT |
| NLog | Logging | BSD-3-Clause |
| System.Reactive | Variable store | MIT |
| NCalcSync (NCalc) | Workflow expressions, behind `WorkflowExpression` | MIT |
| HidSharp | Elgato Stream Deck over USB | Apache-2.0 |
| Markdig | Plugin READMEs in the store | BSD-2-Clause |
| Velopack (+ `vpk`, its WiX fork for the MSI) | Installers and updates (`Platform/AppUpdater`, `scripts/package-velopack.sh`); its updater ships in every package | MIT (WiX: MS-RL) |
| Microsoft.Web.WebView2 | HTML Stream Deck plugins on Windows | Microsoft (redistributable) |
| StreamDock SDK (submodule) | MiraBox N4 Pro | MIT |
| hidapi (bundled with the SDK) | USB HID for the SDK | BSD-3-Clause (one of its license options) |
| OpenCV (static on Linux) | Image encoding in the SDK | Apache-2.0 |

Versions are pinned in the project files. Add a row when you add a package, and check that its license allows redistribution in a closed or open build.
