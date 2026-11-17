# Status against the master plan

Where DeckHost stands against the [master plan](master-plan.md), section by section.
✅ done · 🟡 partly · ⬜ not yet. Updated with the code; the last review is at the bottom.

## 1–2. Product decisions, licensing, marketplace strategy

| Requirement | | Where / what's missing |
|---|---|---|
| GPL-3.0, free, no account or subscription | ✅ | `LICENSE`, `NOTICE.md` (section 7 permission for plugins) |
| Paid plugins permitted | ✅ | Catalog `purchase_url`/`price`, **Buy** in the store |
| Four plugin ecosystems | 🟡 | Native ✅, Elgato ✅, OpenAction 🟡 (runs on the Stream Deck runtime, no runtime of its own), MiraBox 🟡 (knobs, positional start; no LED/device extensions) |
| Simple and advanced automation | ✅ | Multi Action, Key Logic, Switch; node workflows |
| Windows, Linux, macOS | ✅ | `build.yml`, `release.yml` |
| No cloud dependency | ✅ | Everything local; store and updates optional |
| Static, animated, reactive backgrounds | 🟡 | Static ✅ (color, image, default background, per control); animated 🟡 (GIF); reactive ⬜ |
| Background Studio | ⬜ | |
| Marketplace: discovery, install/remove, updates, rollback, integrity | ✅ | Store, `PluginUpdater`, `.previous` rollback, sha256 |
| Marketplace: dependency resolution, publisher verification, signatures | ⬜ | sha256 only; no signatures, no dependencies |
| Marketplace: purchase and entitlements | 🟡 | A Buy link; no entitlements |
| Marketplace content beyond plugins (profiles, themes, icon packs, backgrounds, drivers, templates) | ⬜ | Icon packs install from files, not the store |

## 4. Product pillars

| Requirement | | Where / what's missing |
|---|---|---|
| 4.1 Profiles, pages, folders, drag and drop, undo/redo, import/export | ✅ | |
| 4.1 Device-specific adaptation, live preview, contextual profiles | ✅ | `LayoutMapper`, canvas preview, auto switching |
| 4.1 Profile inheritance, reusable components, dynamic layouts | ⬜ | |
| 4.1 Device simulation | 🟡 | Preview and simulated input in the editor; a separate simulator app is planned |
| 4.1 Themes and design systems | 🟡 | App themes, tiles; no layout themes |
| 4.1 Visual asset management | 🟡 | Icon library, `assets/` per imported profile; no asset manager |
| 4.2 Keys, encoders, touch strips, pedals, LEDs, LED rings, generic HID, virtual | ✅ | MiraBox (N4 Pro and every SDK model), Elgato, HID creator, Web Deck |
| 4.2 Touchscreens, sliders/faders, haptics, network controllers | 🟡 | Touch zones; N4 vibration setting; Web Deck; no sliders |
| 4.4 Multi Action: sequence, delays, toggles | ✅ | |
| 4.4 Multi Action: parallel, conditions, repeats, nested groups, error handling | ⬜ | Available in workflows, not in Multi Action |
| 4.4 Workflows: variables, conditions, branching, loops | ✅ | `WorkflowEngine` |
| 4.4 Workflows: events, external triggers, state machines, error handling, debugging | 🟡 | Control triggers only; run trace and log; no breakpoints |
| 4.4 One engine for both | 🟡 | `WorkflowConverter` turns Multi Actions into workflows, but Multi Actions still run on `ActionRouter.RunSteps` |
| 4.5 Vector, raster, text, gradients, transparency, fonts, icon packs, scaling, LED visualization | ✅ | `SkiaRenderContext` |
| 4.5 Layers, animations, data-bound properties, transitions | 🟡 | GIF animation; variables drive actions' drawings; no layers or transitions |
| 4.6 Virtual surfaces, browser interface | ✅ | Web Deck; device simulator (any model, `DeckHost --simulator`) |
| 4.6 Mobile clients, remote profile management, remote events | ⬜ | The Web Deck works on phones; no app, no remote editing |
| 4.8 App detection, profile switching, system monitoring, dynamic states | ✅ | `ForegroundAppWatcher`, System Info |
| 4.8 Window/process rules, game contexts, contextual backgrounds | 🟡 | By app name only |

## 6. Technical architecture

| Requirement | | Where / what's missing |
|---|---|---|
| C#, .NET 10, Avalonia, SkiaSharp, CommunityToolkit.Mvvm | ✅ | |
| Microsoft.Extensions.Logging | ✅ | NLog behind `ILogger` |
| NUnit | ✅ | NUnit 4 (constraint model), one test instance per test, classes in parallel |
| Versioned contracts and JSON | ✅ | `ContractsApi.txt` baseline, `ProfileMigrations` |
| SQLite | ⬜ | Not needed yet; JSON files |
| IPC: named pipes / Unix sockets | 🟡 | Loopback TCP with a one-time token (`PluginWire`); same isolation, different transport |
| 6.3 Rules 1–2, 4–7, 9 (core independent of vendors and Avalonia, contracts, versioned formats) | ✅ | Enforced by `ArchitectureTests` |
| 6.3 Rule 8 (compatibility systems separate) | 🟡 | Stream Deck runtime in Core; OpenAction/MiraBox share it |
| 6.3 Rule 10 (platform abstractions) | 🟡 | `Platform/` classes, not interfaces |
| 6.3 Rules 11–14 (backgrounds, reactive bindings, fallbacks) | ⬜ | Wait for the background system |
| 6.4 Repository structure | 🟡 | Flat projects (Contracts, Core, App, Device.*, Tests); the plan allows fewer assemblies |

## 7. Device abstraction

| Requirement | | Where / what's missing |
|---|---|---|
| Devices as controls with capabilities, not a grid | ✅ | `DeviceDefinition`, `PhysicalControl` (inputs, outputs, bounds, render size, LED, linked display) |
| 7.1.1 Identity, metadata | ✅ | `DeviceIdentity` (manufacturer, model, serial, revision, firmware, connection); shown on the Plugins page |
| 7.1.1 Physical dimensions, limitations, descriptor version | ✅ | `PhysicalSize`, `DeviceLimits`, `DescriptorVersion` ([ADR 0011](../maintainers/adr/0011-device-descriptor.md)); not every device fills its size yet |
| 7.1.2 Control types, shapes | ✅ | Key, button, encoder, touch surface, display, slider, joystick, LED; round or rectangular |
| 7.1.3 Physical / logical / display coordinates, display regions | 🟡 | Layout `Bounds`, `PhysicalBounds` (mm, optional), `DeviceDisplay` regions with the controls on them; the host doesn't use physical coordinates yet |
| 7.1.4 Capabilities queried before use | 🟡 | `DeviceCapabilities` flags and `Has()`, next to the optional interfaces; the host doesn't adapt to limits (frame rate, transparency) yet |
| 7.1.5 Topology changes | ✅ | `DevicesChanged` (Web Deck grid size, hot-plug); bindings keyed by control id |
| 7.1.6 Hardware description apart from user layout | ✅ | |
| 7.1.8 Canonical contracts | ✅ | The existing contracts are the descriptor (no second model): `IDevicePlugin`, `DeviceDefinition` (+ `DeviceDisplay`, `DeviceCapabilities`), `PhysicalControl`, `IDeviceOutput`; `FindDescriptorProblems()` validates |
| 7.1.10 Linked displays, backdrop, light batches, device redraws | ✅ | `DisplayControlId`, `IDeviceBackdropOutput`, `IDeviceLightBatchOutput`, `RequestRedraw` |
| 7.1.11.1 Physical coordinates in use | ⬜ | `PhysicalBounds` optional and unused by the Layout Studio and `LayoutMapper` |
| 7.1.11.2 Display density, pixel format, host-side rotation | ⬜ | Plugins rotate images themselves |
| 7.1.11.3 Host adapts to limits (frame rate, transparency) | ⬜ | |
| 7.1.11.4 Control-level capabilities | ⬜ | Per control only `Inputs`, `Outputs`, `LedIndex` |
| 7.1.11.5 Whole-display rendering | ⬜ | `OutputCapabilities.FullDisplay` exists; every device gets an image per control |
| 7.1.11.6 Touch coordinates, multi-touch | ⬜ | Taps on zones and swipes only |
| 7.1.11.7 Hardware modes and modules | 🟡 | `DevicesChanged` with new controls; no modes described |
| 7.1.11.8 Nested controls, vendor extensions | ⬜ | |
| 7.1.11.9 Serialized descriptor | 🟡 | Generic HID devices are described in JSON (`HidDeviceDescription`); `DeviceDefinition` itself has no file form |
| 7.3 N4 Pro, Elgato family, generic HID, virtual | ✅ | Also the other StreamDock models (N4, 293, 293s, M18, XL, M3, N3, N1, K1 Pro, Mini) from the SDK, not yet checked on hardware |
| 7.4 Device-independent profiles | 🟡 | Conventional control ids and `LayoutMapper`; no logical-control layer |

## 8. Plugins

| Requirement | | Where / what's missing |
|---|---|---|
| 8.1 Native actions, integrations, settings | ✅ | |
| 8.1 Triggers, data providers, custom UI components, device drivers, background sources | ⬜ | Settings pages via URL only; device plugins are built in |
| 8.2 Elgato: manifests, lifecycle, WebSocket, contexts, property inspectors, images, dials | ✅ | `StreamDeckRuntime`; HTML plugins on Windows only |
| 8.3 OpenAction runtime of its own | ⬜ | OpenAction plugins run on the Stream Deck runtime; OpenAction-only events aren't supported |
| 8.4 MiraBox compatibility | 🟡 | Format, knobs, older argument order; no LED or device-specific extensions |
| 8.5 Plugins outside the UI process | ✅ | Own process by default (ADR 0007); Stream Deck plugins always |
| 8.6 Timeouts, crash detection, recovery, integrity | ✅ | Pings, restarts 1/3/10 s, sha256 |
| 8.6 Permissions enforced, user approval, OS sandboxing | ⬜ | Declared and shown only |
| 8.7 Stable contracts, NuGet, template, docs, samples, validation | ✅ | `DeckHost.Contracts` package, `dotnet new deckhost-plugin`, `--check-plugin` |
| 8.7 Debugging tools, compatibility test fixtures | 🟡 | Logs and the checker; no real-world fixture suite |

## 9. Automation

| Requirement | | Where / what's missing |
|---|---|---|
| 9.3 Triggers: control input | ✅ | down, up, tap, double, hold, rotate, swipe |
| 9.3 Triggers: events, timers, app changes | 🟡 | Profile shown, app in front, variable changed, timer (`WorkflowEvents`, host worker); no device-connected, hotkey or external (webhook) triggers |
| 9.3 Logic, flow (if, sequence, parallel, repeat, wait) | ✅ | |
| 9.3 Switch, expressions, transforms, events, error handling nodes | ⬜ | |
| 9.3 History, cancellation | ✅ | Run log, Stop |
| 9.3 Step execution, breakpoints, variable inspection, sub-workflows, templates | ⬜ | |
| 9.4 Global and plugin variables, device and app context | ✅ | `VariableStore`, `VariableKeys` |
| 9.4 Local and profile scopes, typed variables, computed values | ⬜ | |
| 9.6 Isolation, tracing, loop protection, ordering, never blocking | ✅ | `MaxNodesPerRun`, `WouldCycle`, scheduler |
| 9.6 Timeouts, retries | ⬜ | |

## 10. Rendering and Layout Studio

| Requirement | | Where / what's missing |
|---|---|---|
| 10.1 One pipeline for editor, preview and devices | ✅ | `ControlRenderer` → `ControlFrameRendered` and devices |
| 10.2 Images, text, icons, progress, meters | ✅ | `DrawReading`, `DrawTile` |
| 10.2 Shapes, gauges, composable components, bindable properties | 🟡 | Through actions' drawing code, not as components |
| 10.3 Preview, drag and drop, properties, live preview, undo/redo, action configuration | ✅ | |
| 10.3 Alignment guides, layers, reusable styles, copy and paste, asset manager | ⬜ | |
| 10.5 Incremental updates, caches | ✅ | Pixel-diff per frame, image/paint caches |
| 10.5 Dirty regions, independent scheduling | 🟡 | Per device, not per region |
| 10.6 Background system and Studio | ⬜ | Static profile backgrounds only (color, image slice over the layout, default background) |

## 11. Profiles

| Requirement | | Where / what's missing |
|---|---|---|
| Versioned format, migrations, backups | ✅ | `ProfileDocument.CurrentVersion`, `ProfileMigrations`, daily backups |
| Export/import with assets | ✅ | `.deckhostprofile` |
| Unavailable plugins don't destroy content | ✅ | Unresolved bindings are kept |
| Declared dependencies, import preview | ⬜ | |
| Runtime state apart from configuration | ✅ | Variables aren't saved in profiles |

## 12. Marketplace and remote

| Requirement | | Where / what's missing |
|---|---|---|
| Search, categories, updates, integrity, licenses, local install, rollback | ✅ | |
| Compatibility filtering | 🟡 | Unsupported plugins say why; no filter |
| Remote: pairing, authentication, events, images, reconnection, multiple clients | ✅ | Web Deck |
| Remote: encrypted transport, permissions | ⬜ | HTTP on the local network |

## 13. Cross-platform

| Requirement | | Where / what's missing |
|---|---|---|
| CI builds and tests on all three | ✅ | |
| Package validation | ✅ | `--check-native` per package |
| UI smoke tests, rendering regression tests | ⬜ | |
| Windows installer and zip; macOS app; Linux tarball | ✅ | |
| AppImage, Flatpak | ⬜ | |
| Update integrity and rollback | ✅ | SHA256SUMS, provenance, swap with rollback |

## 15. Testing

| Requirement | | Where / what's missing |
|---|---|---|
| Automated tests | ✅ | About 640 tests |
| NUnit | ✅ | `DeckHost.Tests` on NUnit 4 |
| `MethodName_State_Expected` names, separate test projects | ⬜ | One project with sentence names (`A_knob_fills_its_touch_zone…`) |

## 17. Foundation 1.0 checklist

| Item | |
|---|---|
| Audit, dependency rules, architecture tests, ADRs | ✅ |
| Device and plugin contracts documented | ✅ |
| Elgato, OpenAction and MiraBox boundaries defined | 🟡 |
| Shared action and event model | 🟡 |
| Virtual devices | ✅ (Web Deck; device simulator) |
| MiraBox N4 Pro driver on the contracts | ✅ |
| Portable, versioned profile schema | ✅ |
| Rendering boundaries; where backgrounds fit in profiles | 🟡 |
| Builds and tests on all three | ✅ |
| GPL-3.0 and contribution documentation | ✅ |

Last reviewed: November 2026.
