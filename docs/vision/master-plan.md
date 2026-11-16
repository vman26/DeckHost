# DeckHost: Universal Control Surface Platform

**Product Vision, Technical Architecture & Master Development Roadmap**

| | |
|---|---|
| Document type | Product and Technical Master Specification |
| Status | Product direction established |
| Project | DeckHost |
| Application license | GNU General Public License v3.0 (GPL-3.0) |
| Primary language | C# |
| Framework | .NET 10 |
| Desktop UI | Avalonia |
| Target platforms | Windows, Linux and macOS |
| Development philosophy | Open-source, modular, hardware-independent and extensible |

How DeckHost measures up against this document today, section by section, is in
[Status against the master plan](status.md). What DeckHost already does beyond it is in
[Appendix A](#appendix-a-capabilities-beyond-this-document).

---

## 1. Executive Summary

### 1.1 Product Vision

DeckHost is a universal, open-source control-surface platform designed to unify physical and virtual control devices under one application and one extensible ecosystem.

The goal is to create a platform that can replace and surpass existing control-surface applications, including:

- Elgato Stream Deck.
- OpenDeck.
- MiraBox StreamDock software.
- Bitfocus Companion.
- Other programmable macro-pad and control-surface applications.

DeckHost should not be limited by a particular hardware manufacturer, operating system, plugin ecosystem or intended use case.

It should provide one unified environment where users can configure hardware, create layouts, build automations, manage integrations and control virtually any compatible device.

**Product Mission.** Make every compatible control surface programmable, intelligent, customizable and interoperable through one universal platform.

**Product Vision.** Become the preferred open-source software platform for programmable control surfaces, serving everyone from casual desktop users to professional AV operators, streamers, developers, sim racers and industrial control applications.

**Core Product Promise.** One application. Any device. Any workflow. One ecosystem.

### 1.2 Strategic Positioning

DeckHost is not intended to be another Stream Deck clone.

It should combine the strongest characteristics of existing platforms while introducing capabilities that go beyond their traditional approaches.

The strategic foundations are:

1. Universal hardware compatibility.
2. Comprehensive plugin compatibility.
3. Advanced automation and workflow capabilities.
4. A professional visual editor and rendering engine.
5. A unified ecosystem for developers and users.
6. Cross-platform operation.
7. Open-source application development with an extensible commercial plugin ecosystem.
8. Context-aware and reactive visual experiences.

### 1.3 Product Decisions

The following decisions are established and should be treated as architectural requirements.

| Decision | Direction |
|---|---|
| Application license | GPL-3.0 |
| Application pricing | Free and open-source |
| Paid plugins | Permitted |
| Plugin compatibility | All four supported ecosystems |
| Automation | Simple and advanced visual workflows |
| Platforms | Windows, Linux and macOS simultaneously |
| Product differentiation | Best-in-class hardware, ecosystem, automation and user experience |
| Architecture | Modular, hardware-independent and extensible |
| Cloud dependency | None for core functionality |
| Background support | Static, animated and reactive |
| Background editing | Dedicated Background Studio integrated into Layout Studio |

---

## 2. Licensing and Business Model

### 2.1 Application Licensing

DeckHost will be released under the GNU General Public License version 3.0 (GPL-3.0).

The intention is to ensure that DeckHost remains free and open-source software.

Users should be able to:

- Download DeckHost without paying.
- Use DeckHost without subscriptions.
- Inspect the complete source code.
- Modify the application.
- Build their own versions.
- Redistribute copies under GPL-3.0 requirements.
- Contribute improvements to the project.

The application must not require a paid account, subscription or license key to unlock its fundamental functionality.

The GPL-3.0 license also establishes obligations for distributing modified versions and derivative works. The project should publish the complete corresponding source code as required by the license.

Importantly, GPL-3.0 does not prohibit charging for copies or distribution. The project commitment is to keep the software freely available in the open-source sense, not to prohibit every commercial distribution.

### 2.2 Paid Plugins and Commercial Extensions

The DeckHost ecosystem may contain both free and paid plugins.

The application itself remains GPL-3.0, while individual extensions may have their own licensing and commercial models, subject to applicable copyright and license compatibility requirements.

Potential plugin models:

| Model | Description |
|---|---|
| Free and open-source | Public source code and free distribution |
| Freeware | Free to use, potentially proprietary |
| Paid plugin | One-time purchase |
| Subscription plugin | Recurring payment for ongoing services |
| Freemium plugin | Free basic functionality with paid features |
| Commercial integration | Paid connection to an external service |

Examples of possible commercial extensions:

- Professional OBS integrations.
- Advanced sim racing telemetry.
- Specialized industrial integrations.
- Premium rendering components.
- Advanced automation packages.
- Commercial device drivers.
- Enterprise integrations.
- Premium background effects and visual asset packages.

The plugin architecture must be designed with licensing boundaries in mind.

A separate process, API or plugin package does not automatically guarantee that a proprietary plugin is legally independent of GPL-covered code. Plugin distribution and linking arrangements must be reviewed for GPL compatibility.

### 2.3 Marketplace Strategy

The DeckHost marketplace should support:

- Free plugins.
- Paid plugins.
- Open-source plugins.
- Premium icon packs.
- Themes.
- Profiles.
- Automation templates.
- Rendering components.
- Background presets and asset packs.
- Device drivers.

The marketplace must not be mandatory.

Users should always be able to install compatible local packages manually and operate DeckHost without a marketplace account.

The marketplace should eventually provide:

- Package discovery.
- Version management.
- Dependency resolution.
- Publisher information.
- License information.
- Digital signatures and integrity verification.
- Installation and removal.
- Updates and rollback.
- Purchase and entitlement management for commercial packages.

The exact payment provider and marketplace revenue model remain open decisions.

---

## 3. Target Audiences

DeckHost should support multiple user groups through one common platform.

### 3.1 Casual Users

- Desktop shortcuts.
- Media controls.
- Application launching.
- Basic macros.
- Device customization.

### 3.2 Streamers and Content Creators

- OBS integration.
- Scene switching.
- Audio controls.
- Chat integrations.
- Streaming information.
- Dynamic visual feedback.
- Recording-aware visual states.
- Custom animated backgrounds.

### 3.3 Sim Racing and Gaming

- Game-specific profiles.
- Telemetry-driven controls.
- Rotary encoder controls.
- Shift lights and LED effects.
- Pit menus.
- Dynamic layouts.
- Context-aware controls.
- Game-state-driven backgrounds and visual effects.

### 3.4 Professional AV and Broadcast

- External system integrations.
- Feedback-driven buttons.
- Multi-device workflows.
- Advanced automation.
- State monitoring.
- Remote operation.
- Contextual status displays.

### 3.5 Developers and Power Users

- Custom plugins.
- Scripting.
- Workflow automation.
- Custom rendering components.
- Hardware integrations.
- SDK and API access.
- Custom data providers and visual effects.

### 3.6 Industrial and Specialized Applications

- Custom HID devices.
- Serial integrations.
- Network integrations.
- Physical control panels.
- Status monitoring.
- Dedicated operator interfaces.
- System-state-driven visual indicators.

---

## 4. Core Product Pillars

DeckHost consists of eight major product pillars.

### 4.1 Layout Studio

A professional visual environment for designing control surfaces.

Capabilities:

- Profiles and profile inheritance.
- Pages and nested folders.
- Drag-and-drop editing.
- Reusable components.
- Dynamic layouts.
- Device-specific layout adaptation.
- Live previews.
- Device simulation.
- Touchscreen layouts.
- Contextual profiles.
- Themes and design systems.
- Profile import and export.
- Undo and redo.
- Background Studio integration.
- Visual asset management.

### 4.2 Universal Hardware Platform

A hardware-independent system for discovering, configuring and operating control devices.

Supported device types:

- Programmable keys.
- Rotary encoders.
- Touchscreens.
- Touch strips.
- Sliders and faders.
- Pedals.
- LED indicators.
- LED rings.
- Haptic controls.
- Generic HID devices.
- Network-connected controllers.
- Virtual devices.

Hardware support must be implemented through independent device drivers and capability declarations.

### 4.3 Plugin Ecosystem

A unified plugin architecture supporting four plugin categories:

1. Native DeckHost Plugins.
2. Elgato Stream Deck Plugins.
3. OpenAction Plugins.
4. MiraBox Plugins.

Each ecosystem has its own compatibility requirements, but all should ultimately integrate with the same DeckHost action, event, state and rendering systems.

### 4.4 Automation Engine

Two complementary automation experiences.

Simple automation:

- Multi Actions.
- Sequential actions.
- Parallel actions.
- Delays.
- Toggles.
- Conditional actions.
- Repeated actions.
- Nested action groups.

Advanced automation:

- Node-based workflows.
- Variables.
- Events.
- Conditions.
- Branching.
- Loops.
- State machines.
- External triggers.
- Feedback-driven execution.
- Error handling.
- Debugging.

Both experiences must use the same underlying execution engine.

### 4.5 Rendering Engine and Background System

A shared rendering system for the editor, simulator and physical devices.

The rendering engine must support both traditional control components and complete visual environments.

Capabilities:

- Vector graphics.
- Raster images.
- Text rendering.
- Gradients.
- Transparency.
- Layers.
- Animations.
- Dynamic visual states.
- Device-specific scaling.
- LED visualization.
- Custom fonts.
- Icon packs.
- Plugin-provided components.
- Static backgrounds.
- Animated backgrounds.
- Reactive backgrounds.
- Background transitions and effects.
- Data-bound visual properties.
- Conditional visual states.

#### Background Types

DeckHost must support three fundamental background categories.

**1. Static backgrounds.** Backgrounds that remain visually unchanged unless edited or replaced.
Examples:

- Solid colours.
- Colour gradients.
- Images and photographs.
- Patterns and textures.
- Transparent backgrounds.
- Layered compositions.
- Custom overlays.

**2. Animated backgrounds.** Backgrounds that change over time without requiring a user interaction.
Examples:

- Animated images.
- Video-based backgrounds where supported.
- Moving gradients.
- Animated patterns.
- Particle effects.
- Looping animations.
- Timed visual transitions.
- Animated overlays.

**3. Reactive backgrounds.** Backgrounds that respond to external information, system state, events or user-defined conditions.

Potential data sources include:

- Weather information.
- CPU, GPU and RAM usage.
- CPU and GPU temperatures.
- Network status and activity.
- Active application or game.
- Device connection status.
- OBS recording and streaming state.
- Plugin-provided telemetry.
- Automation variables.
- Time of day.
- User-defined events.

Examples:

- A weather integration changes the background depending on whether it is sunny, raining or storming.
- A system-monitoring plugin changes the background colour according to CPU temperature.
- An active game selects a matching background theme.
- A disconnected device triggers a warning background.
- OBS recording changes the visual appearance of a control surface.
- Time of day selects a different background preset.
- Sim racing telemetry changes visual effects based on vehicle state.

Reactive backgrounds must use the shared DeckHost data-binding and event infrastructure. They must not require a separate, isolated mechanism for every integration.

#### Background Studio

A dedicated background editor integrated into Layout Studio.

It should provide:

- Background selection and management.
- Layer-based composition.
- Drag-and-drop asset placement.
- Image positioning, scaling and cropping.
- Colour and gradient editing.
- Layer ordering and visibility.
- Animation configuration.
- Playback and transition controls.
- Reactive data-source selection.
- Property and variable bindings.
- Conditional background states.
- Live preview.
- Device-specific preview.
- Performance and quality settings.
- Background preset import and export.

Background definitions should be reusable across pages, profiles and compatible devices.

The Background Studio should make it possible to create a sophisticated reactive background without requiring the user to write code. Advanced users and plugin developers should still be able to extend the system through supported APIs.

### 4.6 Remote and Virtual Devices

DeckHost should remain useful without physical hardware.

Capabilities:

- Virtual control surfaces.
- Browser-based control interface.
- Mobile clients.
- Network-connected devices.
- Remote profile management.
- Remote event communication.
- Virtual device testing.

### 4.7 Marketplace and Ecosystem

A centralized discovery and distribution system for:

- Plugins.
- Device drivers.
- Profiles.
- Themes.
- Icon packs.
- Rendering components.
- Automation templates.
- Background presets and asset packs.

The marketplace must support both free and commercial content.

### 4.8 Context Intelligence

Context-aware control surfaces.

Capabilities:

- Active application detection.
- Profile switching.
- Window and process detection.
- Game-specific contexts.
- System monitoring.
- Dynamic button states.
- Contextual layouts.
- Event-driven information displays.
- Contextual background selection.
- Visual reactions to external state changes.

Future AI-assisted configuration can be introduced as an optional capability.

---

## 5. Competitive Positioning

DeckHost should learn from and compete with several established products.

| Area | Elgato Stream Deck | OpenDeck | Bitfocus Companion | MiraBox StreamDock | DeckHost |
|---|---|---|---|---|---|
| Hardware support | Elgato-focused | Stream Deck-focused | Broad control integrations | MiraBox-focused | Universal |
| Plugin ecosystem | Mature SDK | Open ecosystem | Module ecosystem | Vendor ecosystem | Four compatibility categories |
| Automation | Multi Actions | Plugin-dependent | Advanced | Vendor-dependent | Simple and advanced |
| Visual customization | Strong | Varies | Button-oriented | Device-oriented | Shared advanced renderer |
| Background system | Device/application-dependent | Varies | Interface-dependent | Vendor-dependent | Static, animated and reactive |
| Cross-platform | Windows/macOS | Windows/Linux/macOS | Multiple platforms | Vendor-dependent | Windows/Linux/macOS |
| Remote operation | Mobile offering | Varies | Professional workflows | Varies | Native remote platform |
| Developer platform | Established SDK | Open-source APIs | Module APIs | Vendor SDK | Unified SDK |
| Licensing | Proprietary application | Open-source | Open-source | Proprietary software | GPL-3.0 |

This table describes strategic positioning, not a verified feature-by-feature audit.

**Competitive objective.** DeckHost should deliver:

- The usability of Stream Deck.
- The openness of OpenDeck.
- The integration capabilities of Companion.
- The hardware knowledge and device-specific capabilities of MiraBox.
- A unified architecture connecting all of these areas.

It should also offer a more flexible visual system in which backgrounds, controls, data and automation work together rather than existing as separate configuration features.

---

## 6. Technical Architecture

### 6.1 Technology Baseline

| Area | Technology | Responsibility |
|---|---|---|
| Language | C# | Primary implementation |
| Runtime | .NET 10 | Core runtime |
| Desktop UI | Avalonia | Cross-platform application |
| Rendering | SkiaSharp | Shared rendering engine |
| Architecture | Modular monolith | Initial architecture |
| MVVM | CommunityToolkit.Mvvm | Presentation |
| Logging | Microsoft.Extensions.Logging | Logging abstraction |
| Testing | NUnit | Automated tests |
| Plugin contracts | Versioned .NET contracts | Extension APIs |
| Configuration | Versioned JSON | Portable profiles |
| Database | SQLite where useful | Metadata and persistence |
| IPC | Named pipes / Unix domain sockets | Plugin communication |
| Packaging | Platform-specific installers | Distribution |

Dependencies must be checked for licensing, target framework support and platform compatibility.

### 6.2 Architectural Principles

DeckHost Core is the platform.

DeckHost Desktop is one application that consumes the platform.

The architecture should allow future clients, including:

- Mobile applications.
- Browser-based editors.
- Remote control clients.
- Virtual devices.
- Dedicated developer tools.

#### Architectural layers

- **Presentation:** DeckHost.Desktop, DeckHost.Editor, DeckHost.Simulator, DeckHost.UI.Components.
- **Application:** DeckHost.Application, DeckHost.Workflows, DeckHost.ProfileManagement.
- **Core:** DeckHost.Core, DeckHost.Abstractions, DeckHost.Profiles, DeckHost.Rendering, DeckHost.Automation, DeckHost.Backgrounds.
- **Infrastructure:** DeckHost.Infrastructure, DeckHost.Storage, DeckHost.Networking, DeckHost.Platform, DeckHost.Marketplace, DeckHost.DataProviders.
- **Extensions:** DeckHost.PluginSdk, DeckHost.PluginRuntime, DeckHost.DeviceSdk, DeckHost.Compatibility.Elgato, DeckHost.Compatibility.OpenAction, DeckHost.Compatibility.MiraBox.

#### Background architecture responsibilities

The background system should be separated into clear responsibilities:

- Background definitions describe the visual composition and its configuration.
- Background sources provide data such as weather, system metrics and plugin values.
- Data bindings connect source values to background properties.
- The event and state infrastructure communicates changes.
- The background renderer resolves layers, transitions and effects.
- The shared rendering pipeline produces output for the editor, simulator and physical devices.

Background sources should not be coupled directly to individual rendering components. A weather plugin should provide weather data through a supported data-provider contract; the background system decides how that data affects the visual result.

### 6.3 Architecture Rules

1. Core must not reference individual hardware vendors.
2. Core must not reference Avalonia.
3. Application services depend on abstractions.
4. Device drivers implement common device contracts.
5. Plugins use public SDK contracts.
6. Rendering is independent of physical hardware.
7. Automation is independent of UI and hardware.
8. Compatibility systems are separate subsystems.
9. Persistent formats are versioned.
10. Platform-specific functionality uses explicit abstractions.
11. Background definitions must not depend on a specific device.
12. Reactive visual properties must use shared data and event contracts.
13. Rendering must not directly own external data-source connections.
14. Device limitations must be handled through explicit rendering capabilities and fallbacks.

### 6.4 Repository Structure

```
DeckHost/
│
├── src/
│   ├── Desktop/
│   │   ├── DeckHost.Desktop
│   │   ├── DeckHost.Editor
│   │   ├── DeckHost.Simulator
│   │   └── DeckHost.UI.Components
│   │
│   ├── Core/
│   │   ├── DeckHost.Core
│   │   ├── DeckHost.Abstractions
│   │   ├── DeckHost.Profiles
│   │   ├── DeckHost.Rendering
│   │   ├── DeckHost.Backgrounds
│   │   └── DeckHost.Automation
│   │
│   ├── Application/
│   │   ├── DeckHost.Application
│   │   ├── DeckHost.Workflows
│   │   └── DeckHost.ProfileManagement
│   │
│   ├── Infrastructure/
│   │   ├── DeckHost.Infrastructure
│   │   ├── DeckHost.Storage
│   │   ├── DeckHost.Networking
│   │   ├── DeckHost.Platform
│   │   ├── DeckHost.Marketplace
│   │   └── DeckHost.DataProviders
│   │
│   ├── Plugins/
│   │   ├── DeckHost.PluginSdk
│   │   ├── DeckHost.PluginRuntime
│   │   ├── DeckHost.Compatibility.Elgato
│   │   ├── DeckHost.Compatibility.OpenAction
│   │   └── DeckHost.Compatibility.MiraBox
│   │
│   └── Devices/
│       ├── DeckHost.DeviceSdk
│       ├── DeckHost.Devices.MiraBox
│       ├── DeckHost.Devices.Elgato
│       ├── DeckHost.Devices.HID
│       └── DeckHost.Devices.Virtual
│
├── tests/
│   ├── UnitTests/
│   ├── IntegrationTests/
│   ├── ContractTests/
│   ├── CompatibilityTests/
│   ├── RenderingTests/
│   ├── BackgroundTests/
│   └── EndToEndTests/
│
├── samples/
│   ├── SamplePlugin/
│   ├── SampleDeviceDriver/
│   ├── SampleWorkflow/
│   └── SampleBackgroundProvider/
│
├── tools/
│   ├── PluginValidator/
│   ├── ProfileValidator/
│   └── PackageBuilder/
│
└── docs/
    ├── Architecture/
    ├── Developer/
    ├── User/
    └── Specifications/
```

Project boundaries should reflect real ownership and dependency requirements, not create unnecessary assemblies. The proposed project structure is a logical starting point; implementation should validate whether each responsibility warrants its own assembly.

---

## 7. Universal Device Abstraction

The device abstraction is one of the most important foundations.

DeckHost must not model every device as a fixed grid of buttons.

Instead, every device exposes capabilities, controls, input events and output surfaces.

### 7.1 Device Descriptor and Control Topology

Every device plugin must expose a structured description of the hardware it provides. DeckHost must never rely on hardcoded assumptions about a device's button count, physical arrangement, display dimensions or available input controls.

The device plugin is responsible for describing the physical hardware and its capabilities. DeckHost uses this information to construct device-independent layouts, configure the Layout Studio, adapt rendering and map user-defined actions to physical controls.

This mechanism is called the Device Descriptor and Control Topology API.

#### 7.1.1 Device Descriptor

Each connected device must expose a `DeviceDescriptor` containing:

- **Identity:** Stable device ID, manufacturer, model, hardware revision and serial information where available.
- **Metadata:** Display name, description, firmware version and connection information.
- **Physical dimensions:** Device dimensions and physical layout information where available.
- **Control topology:** A structured representation of all physical controls and their positions.
- **Capabilities:** Supported input, output, rendering and interaction features.
- **Display configuration:** Resolution, pixel density, orientation, refresh capabilities and display regions.
- **Limitations:** Hardware restrictions, unsupported operations and resource constraints.

Descriptors must be versioned and designed for future extensibility.

#### 7.1.2 Control Topology

The plugin must describe the physical controls available on a device.

Supported control types should include, but are not limited to:

- Display buttons and ordinary physical buttons.
- Touchscreens and touch-sensitive surfaces.
- Touch bars and display strips.
- Rotary encoders and push dials.
- Sliders and faders.
- Joysticks and directional controls.
- LED indicators and RGB lighting zones.
- Additional or vendor-specific controls.

Each control should expose, where applicable:

- Stable control identifier.
- Control type.
- Physical position and dimensions.
- Logical position and dimensions.
- Associated display region.
- Supported input events.
- Supported output operations.
- Hardware-specific capabilities and limitations.

The topology must support irregular arrangements, non-rectangular controls, multiple display regions and devices with different control types.

The API must not assume that all devices consist of a regular grid of identical buttons.

#### 7.1.3 Coordinate Systems and Display Regions

The device plugin must describe the coordinate systems required to position and render content correctly.

DeckHost should distinguish between:

- **Physical coordinates:** The actual position and dimensions of controls on the device.
- **Logical coordinates:** The coordinate space used by the DeckHost layout and rendering systems.
- **Display coordinates:** The pixel coordinate space of individual displays or display regions.

The plugin must provide sufficient mapping information for DeckHost to translate between these coordinate systems.

A device may expose multiple independent displays, individual button displays, a shared touchscreen or a combination of these.

Display regions must be independently identifiable and associated with their corresponding controls where applicable.

#### 7.1.4 Capability-Based Hardware Description

Device functionality must be represented through explicit capabilities rather than assumptions based on manufacturer or model.

Examples include:

- Static image rendering.
- Animated image rendering.
- Video or continuous frame rendering.
- Touch input and multi-touch.
- Button press and release events.
- Rotary encoder input.
- Brightness adjustment.
- RGB and LED control.
- Hardware feedback and status indicators.
- Per-control and per-display rendering.
- Hardware-specific actions.

Capabilities may be advertised at device level or individual control level.

DeckHost must query these capabilities before attempting to use a hardware feature. This allows the application to adapt layouts and provide sensible fallbacks when a device lacks a particular capability.

#### 7.1.5 Dynamic Discovery and Topology Changes

The architecture must support devices whose available controls or capabilities can change at runtime.

Examples include:

- Dynamically connected or disconnected control modules.
- Firmware-dependent capabilities.
- Expandable control surfaces.
- Changes in available display regions.
- Hardware modes that expose different controls.

When the topology changes, the plugin must notify DeckHost. DeckHost must refresh the device descriptor and reconcile the existing user layout with the updated hardware configuration.

Existing user assignments should be preserved wherever possible. Controls that are no longer available must not silently cause unrelated assignments to move to different physical controls.

#### 7.1.6 Separation of Hardware Description and User Layout

The device descriptor defines what physically exists. It must not dictate how users arrange their interfaces.

DeckHost owns the canonical layout model, including:

- User-created pages and profiles.
- Control assignments.
- Visual components and widgets.
- Backgrounds and visual composition.
- Automation bindings.
- Navigation and interaction behavior.

The Layout Studio uses the hardware topology to present an accurate representation of the physical device.

The rendering engine uses the descriptor to determine how the canonical layout can be rendered for the target hardware.

This separation ensures that layouts remain portable and that the same profile can be adapted to different devices.

#### 7.1.7 Device Descriptor Example

The following illustrates the intended structure. It is conceptual and does not define the final serialization contract.

```json
{
  "deviceId": "mirabox.n4pro",
  "manufacturer": "MiraBox",
  "model": "StreamDock N4 Pro",
  "descriptorVersion": 1,
  "controls": [
    {
      "id": "button-1",
      "type": "display-button",
      "position": { "x": 0, "y": 0 },
      "size": { "width": 1, "height": 1 },
      "capabilities": [
        "display",
        "press",
        "release"
      ]
    },
    {
      "id": "dial-1",
      "type": "rotary-encoder",
      "capabilities": [
        "rotate",
        "press"
      ]
    }
  ],
  "displays": [
    {
      "id": "main-display",
      "resolution": {
        "width": 1920,
        "height": 1080
      }
    }
  ]
}
```

The final model must support richer geometry, multiple display regions, nested controls, capability metadata and vendor-specific extensions.

#### 7.1.8 API and Architectural Responsibilities

The Device Descriptor and Control Topology API should be exposed through the device abstraction layer.

Relevant architectural contracts include:

- `IDeviceProvider`: Discovers and provides supported devices.
- `IDeviceSession`: Represents an active connection to a device.
- `DeviceDescriptor`: Describes device identity, metadata and topology.
- `DeviceCapabilities`: Describes supported device-level operations.
- `DeviceControlDescriptor`: Describes an individual physical control.
- `DeviceDisplayDescriptor`: Describes a display or display region.
- `IInputEvent`: Represents normalized hardware input.
- `IRenderTarget`: Represents a target for visual output.
- `IDeviceRenderer`: Handles device-specific rendering and output.

The exact interfaces and serialization model must be validated during the hardware integration spike.

The core must own the canonical descriptor model. Device plugins translate vendor-specific SDK structures into this model rather than exposing vendor SDK types directly to the rest of DeckHost.

#### 7.1.9 Design Requirements

The implementation must satisfy these requirements:

1. New hardware must be describable without modifying DeckHost core.
2. Device topology must not be hardcoded into the Layout Studio.
3. Control identifiers must remain stable across reconnects where hardware permits.
4. Multiple control types and irregular physical layouts must be supported.
5. Device capabilities must be discoverable and queryable.
6. Runtime topology changes must be supported.
7. User layouts must remain independent of hardware-specific implementations.
8. Vendor-specific functionality must be accessible through extensible capability contracts without compromising the portable core model.
9. Descriptor versions must support future schema evolution.
10. Device plugins must not be able to redefine or override DeckHost's canonical layout model.

### 7.2 Capability System

| Category | Examples |
|---|---|
| Input | Button, rotary, touch, swipe, slider, pedal |
| Output | Image, text, animation, LED, haptic |
| Display | Resolution, pixel format, refresh capability |
| Lighting | RGB, brightness, LED ring, zones |
| Feedback | Press state, encoder state, device status |
| Communication | USB, HID, serial, network |
| Advanced | Touch gestures, multi-touch, custom protocols |

Capabilities must be discoverable and extensible.

### 7.3 Device Drivers

Every hardware integration implements the common device abstraction.

Initial integrations:

1. MiraBox StreamDock N4 Pro.
2. Elgato Stream Deck family.
3. Generic HID.
4. Virtual devices.

Future integrations should not require changes to the core device model.

### 7.4 Device-Independent Profiles

Profiles describe logical controls and their capabilities rather than fixed physical coordinates.

The layout engine maps logical controls to physical device surfaces.

Unsupported capabilities should be identified and handled through defined fallbacks.

---

## 8. Plugin Architecture and Four Compatibility Categories

The plugin system is a major differentiator.

DeckHost must support four distinct plugin categories.

These are separate compatibility environments, not four names for the same API.

### 8.1 Category 1: Native DeckHost Plugins

The preferred plugin development platform.

Native plugins use the DeckHost Plugin SDK.

They can provide:

- Actions.
- Triggers.
- Feedback.
- Data providers.
- Integrations.
- Custom UI components.
- Rendering extensions.
- Device drivers.
- Background data sources.
- Background effects and rendering extensions.

Native plugins should receive access to the full supported DeckHost extension model.

### 8.2 Category 2: Elgato Stream Deck Plugins

DeckHost should support existing Elgato Stream Deck plugins wherever technically and legally feasible.

Compatibility research and implementation must cover:

- Plugin manifests.
- Plugin discovery.
- Registration and lifecycle.
- WebSocket communication.
- Action registration.
- Context handling.
- Settings and property inspectors.
- Events and callbacks.
- Button image updates.
- Device and application events.
- Supported SDK versions.

The objective is to allow users to use existing Elgato plugins without requiring developers to rewrite them.

Implementation must use an independent Elgato compatibility runtime.

### 8.3 Category 3: OpenAction Plugins

OpenAction must have its own compatibility implementation.

Investigate and support its actual:

- Manifest format.
- API.
- Execution model.
- Dependencies.
- Configuration semantics.
- Lifecycle.
- Device interaction model.

Do not assume that OpenAction and Elgato plugins are interchangeable.

The compatibility runtime should translate supported operations into DeckHost's native action, event and state model.

### 8.4 Category 4: MiraBox Plugins

MiraBox plugins are a separate first-class compatibility category.

This is important because MiraBox has its own StreamDock SDK, device ecosystem and plugin conventions.

DeckHost should support existing MiraBox extensions wherever technically feasible.

The implementation must investigate:

- MiraBox plugin package format.
- Plugin manifests.
- SDK interfaces.
- Plugin lifecycle.
- Device registration.
- Action registration.
- Hardware communication.
- Button and touch interactions.
- Image rendering.
- LED and lighting controls.
- Device-specific properties.
- Configuration and settings.
- Plugin dependencies.
- Supported SDK versions.

The goal is to let users access existing MiraBox plugin functionality from DeckHost without forcing all integrations to be rewritten.

The compatibility layer must preserve device-specific features where possible while translating common functionality into DeckHost's unified model.

MiraBox-specific capabilities must not be artificially restricted to the generic device abstraction.

For example, an advanced lighting feature should remain available through a device-specific extension when the universal abstraction cannot fully represent it.

### 8.5 Plugin Runtime Architecture

Recommended logical structure:

```
                  DeckHost
                     |
                Plugin Manager
                     |
       +-------------+-------------+
       |             |             |
       v             v             v
 Native Runtime  Compatibility Runtimes
                      |
            +---------+---------+
            |         |         |
            v         v         v
         Elgato   OpenAction  MiraBox
```

Third-party and compatibility plugins should execute outside the main UI process wherever practical.

Use versioned IPC contracts and explicit permissions.

### 8.6 Plugin Security

Plugin isolation must include:

- Separate process execution.
- Permission declarations.
- Controlled IPC.
- Timeouts.
- Crash detection.
- Automatic recovery.
- Package integrity verification.
- Explicit user approval for sensitive capabilities.

Process isolation alone is not a complete security sandbox.

Stronger restrictions should use operating-system security facilities where available.

### 8.7 Plugin SDK

Provide:

- Stable public contracts.
- NuGet packages where appropriate.
- Plugin templates.
- Documentation.
- Sample plugins.
- Debugging tools.
- Manifest validation.
- Compatibility testing.
- SDK versioning.

A developer must be able to create a working plugin without referencing internal DeckHost projects.

The SDK should document how plugins provide data sources, events and rendering extensions, including the permissions and lifecycle requirements for background-related functionality.

---

## 9. Automation Engine

DeckHost should provide two automation experiences using one shared execution engine.

### 9.1 Shared Execution Architecture

```
             Automation Engine
                    |
           Workflow Definition
                    |
            Execution Scheduler
                    |
             Action Dispatcher
                    |
             Event / State System
                    |
                Plugins
```

Both the simple Multi Action editor and the advanced workflow editor compile into the same workflow representation.

### 9.2 Simple Multi Actions

Support:

- Sequential execution.
- Parallel execution.
- Configurable delays.
- Repeated actions.
- Toggle sequences.
- Conditional actions.
- Nested action groups.
- Error handling.
- Cancellation.
- Execution status.

Example:

```
Button Pressed
    |
    +-- Set OBS Scene
    |
    +-- Wait 500 ms
    |
    +-- Mute Microphone
    |
    +-- Set LED to Red
```

### 9.3 Advanced Visual Workflow Editor

Provide a node-based editor.

Node categories:

| Category | Examples |
|---|---|
| Triggers | Button, event, timer, application change |
| Actions | Plugin, device, system |
| Logic | If, switch, comparison, branch |
| Flow | Sequence, parallel, loop, delay |
| Data | Variable, expression, transform |
| Events | Subscribe, emit, wait |
| Feedback | Update visual, LED, text, state |
| Error handling | Try, catch, retry, timeout |

Advanced capabilities:

- Workflow debugging.
- Step execution.
- Breakpoints.
- Variable inspection.
- Execution history.
- Cancellation.
- Timeouts.
- Reusable sub-workflows.
- Workflow templates.

### 9.4 Variables and State

Provide:

- Local workflow variables.
- Profile variables.
- Global variables.
- Plugin-provided variables.
- Device state.
- Application context.
- Computed values.

Variables must have defined types and scopes.

### 9.5 Event Architecture

Events should be the foundation of reactive behavior.

Examples:

- Device connected.
- Button pressed.
- Encoder rotated.
- Application activated.
- Profile changed.
- Plugin event received.
- Telemetry updated.
- Timer elapsed.
- Network message received.
- System metric changed.
- Weather condition changed.
- Background state changed.

Subscriptions must have explicit lifetimes and backpressure policies.

Reactive visual components and backgrounds should consume the same event and state infrastructure used by automation.

### 9.6 Reliability

The automation engine must support:

- Cancellation tokens.
- Execution timeouts.
- Error propagation.
- Configurable retries.
- Execution isolation.
- Logging and tracing.
- Recursive workflow protection.
- Deterministic ordering where required.

No workflow may block the UI thread or device communication loops.

---

## 10. Rendering Engine and Layout Studio

### 10.1 Shared Rendering Pipeline

The editor, simulator and physical hardware must use the same rendering pipeline.

```
Logical Layout
      |
Visual Component Tree
      |
Layout and Style Resolution
      |
Background Composition
      |
Rendering Engine
      |
Rendered Frame
      |
      +---- Desktop Preview
      |
      +---- Virtual Device
      |
      +---- Physical Device
```

The device adapter handles output conversion and transport, not visual design decisions.

Background composition is part of the rendering pipeline. It must not be implemented as a separate rendering path for physical devices.

### 10.2 Visual Components

Support:

- Images.
- Text.
- Shapes.
- Icons.
- Progress indicators.
- Gauges.
- Meters.
- Status indicators.
- Animated elements.
- Custom plugin components.

Components should be composable and reusable.

Visual components may expose bindable properties so their appearance can react to DeckHost variables, plugin data and application state.

### 10.3 Layout Editor

Provide:

- Device-aware preview.
- Drag-and-drop.
- Alignment guides.
- Layers.
- Component properties.
- Reusable styles.
- Theme management.
- Responsive layouts.
- Touchscreen design.
- Visual state editing.
- Action configuration.
- Live preview.
- Undo and redo.
- Copy and paste.
- Asset management.
- Background Studio integration.

### 10.4 Logical and Physical Layouts

Separate logical design from physical device mapping.

A profile contains:

- Logical controls.
- Pages.
- Folders.
- Components.
- Actions.
- Visual states.
- Automation bindings.
- Background definitions and references.

A device mapping describes:

- Physical control placement.
- Surface dimensions.
- Resolution.
- Orientation.
- Supported interactions.
- Output constraints.

### 10.5 Performance

The renderer must support:

- Incremental updates.
- Dirty-region tracking.
- Cached static components.
- Frame scheduling.
- Animation throttling.
- Efficient image conversion.
- Device-specific output optimization.
- Background frame caching.
- Efficient layer composition.
- Independent update scheduling for static and dynamic content.

Unchanged images must not be regenerated unnecessarily.

Background animations must not force unrelated controls or static components to be rendered again.

### 10.6 Background System and Background Studio

The background system is a first-class part of DeckHost's visual architecture.

It consists of two related but distinct responsibilities:

- The background runtime defines, evaluates and renders backgrounds.
- Background Studio provides the user interface for creating, configuring and previewing them.

Background Studio is integrated into Layout Studio, while the underlying background runtime remains independent of Avalonia and physical device implementations.

#### 10.6.1 Background Composition Model

A background is a reusable visual definition composed of one or more layers.

Potential layer types:

- Base colour layer.
- Gradient layer.
- Image layer.
- Pattern or texture layer.
- Animation layer.
- Video layer where supported.
- Particle or effect layer.
- Overlay layer.
- Reactive layer.
- Transition layer.

Example composition:

```
Background
|
+-- Base Layer
|   +-- Dark gradient
|
+-- Image Layer
|   +-- Weather-dependent image
|
+-- Animation Layer
|   +-- Rain particle effect
|
+-- Overlay Layer
|   +-- Dark translucent gradient
|
+-- Reactive Layer
    +-- CPU temperature colour adjustment
```

Layers should support:

- Ordering.
- Visibility.
- Opacity.
- Blending where supported.
- Position and scale.
- Clipping.
- Layer-specific properties.
- Data bindings.
- Conditional visibility.
- Animation and transition settings.

The model should be extensible so new layer types can be introduced without changing the profile format's fundamental structure.

#### 10.6.2 Static Backgrounds

Static backgrounds should support:

- Solid colours.
- Linear and radial gradients.
- Images.
- Patterns and textures.
- Transparency.
- Multiple image layers.
- Cropping and positioning.
- Colour overlays.
- Reusable background presets.

Image assets should be managed through the profile asset system and referenced by stable identifiers.

#### 10.6.3 Animated Backgrounds

Animated backgrounds should support:

- Animated image assets.
- Video playback where the target supports it.
- Animated gradients.
- Particle systems.
- Moving patterns.
- Timed transitions.
- Looping animations.
- Playback speed and direction where applicable.
- Start, pause, stop and reset behavior.

The renderer should expose capability checks so unsupported animation types can be replaced by a static representation or another suitable fallback.

Animation should be configurable per background and should respect global performance settings.

#### 10.6.4 Reactive Backgrounds

Reactive backgrounds respond to changing data or events.

Potential data providers:

- DeckHost system-monitoring services.
- Weather integrations.
- Application and game context providers.
- OBS integrations.
- Device drivers.
- Telemetry plugins.
- Network monitoring plugins.
- User-defined workflow variables.
- External service integrations.

Reactive properties may include:

- Background selection.
- Layer visibility.
- Colour.
- Opacity.
- Image selection.
- Animation state.
- Animation speed.
- Effect intensity.
- Transition behavior.

Examples:

| Source | Condition | Visual response |
|---|---|---|
| Weather | Rain detected | Enable rain layer |
| Weather | Sunny conditions | Select sunny background |
| CPU temperature | Above configured threshold | Change warning colour |
| GPU usage | High load | Increase visual activity |
| Application context | Game activated | Select game-specific background |
| Device status | Disconnected | Display warning overlay |
| OBS | Recording active | Enable recording indicator |
| Time | Night period | Select night background |
| Telemetry | Vehicle state changes | Update background effect |

These are examples of possible configurations, not hardcoded renderer behaviors.

#### 10.6.5 Data Binding and State Resolution

Reactive backgrounds must use the shared DeckHost data and event architecture.

A binding should describe:

- Source identifier.
- Property or variable identifier.
- Expected data type.
- Optional transformation.
- Optional condition.
- Target background property.
- Fallback value.
- Update behavior.

The system must handle:

- Missing data sources.
- Invalid values.
- Stale data.
- Provider disconnection.
- Type mismatches.
- Conflicting updates.
- Rapidly changing values.

Background evaluation should be deterministic for a given input state.

The renderer should consume resolved background state rather than perform network requests, query operating-system metrics or directly communicate with plugins.

#### 10.6.6 Background Studio Editing Experience

Background Studio should provide a visual editing environment integrated into Layout Studio.

The editor should include:

- **Layer panel:** add and remove layers; reorder layers; enable and disable layers; group related layers where supported; select a layer to edit its properties.
- **Visual canvas:** live background preview; drag-and-drop positioning; image scaling and cropping; alignment and guides; zoom and pan; device-specific aspect ratio previews.
- **Property editor:** colours and gradients; opacity and blending; position and dimensions; animation settings; layer-specific properties; conditional visibility; data bindings.
- **Reactive editor:** select available data sources; browse provider properties; bind values to visual properties; define conditions and thresholds; configure fallback values; preview different input states.
- **Animation and transition controls:** preview playback; configure looping; set playback speed; configure transitions; preview state changes.
- **Preset management:** save reusable backgrounds; import and export background definitions; package background assets; preview presets before applying them.

The editor should provide sensible defaults for ordinary users while retaining access to advanced configuration.

#### 10.6.7 Preview and Simulation

Background Studio must support multiple preview targets:

- Generic layout preview.
- Selected physical device.
- Virtual device.
- Different resolutions and aspect ratios.
- Simulated data-provider values.
- Simulated events and state changes.

Users should be able to test a reactive background without waiting for actual weather changes, temperature changes or external events.

Simulation must not alter live system state or trigger unintended automation actions.

#### 10.6.8 Device Capability Adaptation

Background rendering must respect the output capabilities of each target.

Examples:

- A device without animation support receives a suitable static frame.
- A low-resolution display receives appropriately scaled output.
- A device with limited refresh capability receives a reduced update rate.
- A device without transparency support receives a flattened composition.
- A device with limited colour depth receives suitable colour conversion.

The editor should identify unsupported features and communicate when a fallback is being used.

A profile should remain reusable even when a particular device cannot reproduce every visual effect.

#### 10.6.9 Performance and Resource Management

Background rendering must be resource-aware.

Requirements:

- Frame-rate limits.
- Animation throttling.
- Cached static layers.
- Cached rendered frames where appropriate.
- Efficient invalidation.
- Reduced update rates for expensive data sources.
- Independent scheduling for data updates and visual frames.
- Global performance settings.
- Reduced-motion mode.
- Device-specific quality settings.
- Graceful degradation under load.

Static backgrounds should have minimal runtime overhead.

Reactive data providers should not be polled continuously when event-driven updates are available.

Background rendering must never block device communication, automation execution or the UI thread.

#### 10.6.10 Transitions and State Changes

Background state changes should support configurable transitions.

Examples:

- Fade between weather states.
- Gradually change colour based on temperature.
- Crossfade between application-specific backgrounds.
- Enable an overlay when a device disconnects.
- Transition between day and night themes.

Transitions should be interruptible and should define behavior when a new state arrives before the previous transition finishes.

#### 10.6.11 Persistence and Portability

Background definitions must be versioned and portable.

They should support:

- Reuse across profiles.
- Reuse across pages.
- Asset references.
- Data-provider dependencies.
- Binding definitions.
- Device capability requirements.
- Migration between format versions.

A profile must preserve a background definition even when one of its providers is unavailable.

Missing dependencies should produce a clear warning and a defined fallback rather than silently deleting visual content.

---

## 11. Profiles and Persistence

### 11.1 Portable Profile Format

Use a versioned and documented profile format.

```
Profile
├── Metadata
├── Version
├── Dependencies
├── Logical Layouts
│   ├── Pages
│   ├── Folders
│   └── Controls
├── Actions
├── Workflows
├── Variables
├── Themes
├── Backgrounds
│   ├── Definitions
│   ├── Layers
│   ├── Bindings
│   └── Transitions
├── Assets
└── Device Mappings
```

Profile data must be independent of runtime state.

Separate:

- User configuration.
- Profile definitions.
- Device mappings.
- Plugin configuration.
- Runtime state.
- Cache data.

Background definitions and their dependencies must be represented in the portable profile model. Runtime-only information, such as cached frames and temporary provider values, must not be serialized as profile configuration.

### 11.2 Profile Portability

A profile declares dependencies and supported capabilities.

On import:

1. Validate the format.
2. Check the version.
3. Resolve dependencies.
4. Identify unavailable plugins.
5. Detect unsupported device capabilities.
6. Apply migrations.
7. Validate background definitions and asset references.
8. Identify missing background data providers.
9. Preview the resulting profile.
10. Allow the user to install or activate it.

Missing dependencies must not silently destroy profile content.

When a background provider is unavailable, DeckHost should preserve the binding and provide a clear fallback or degraded visual state.

### 11.3 Storage Strategy

Use:

- JSON for portable configuration and interchange.
- SQLite for relational metadata and indexes where useful.
- File storage for images, fonts and other assets.
- Explicit cache directories.
- Versioned migrations.

Profiles must be exportable without a live database.

Background assets should be included in profile packages when appropriate, with support for shared assets and dependency references to avoid unnecessary duplication.

---

## 12. Marketplace and Remote Ecosystem

### 12.1 Marketplace

The marketplace is integrated into DeckHost but is not a mandatory cloud dependency.

Content categories:

- Plugins.
- Device drivers.
- Profiles.
- Themes.
- Icon packs.
- Rendering components.
- Workflow templates.
- Background presets.
- Background asset packs.
- Custom visual effects.

Required functionality:

- Search.
- Categories.
- Compatibility filtering.
- Version management.
- Dependency resolution.
- Updates.
- Publisher verification.
- Package integrity.
- License information.
- Local installation.
- Uninstallation and rollback.

Paid and free content must coexist.

### 12.2 Remote Control

Remote clients connect to a DeckHost host instance.

Potential clients:

- Desktop application.
- Browser.
- Mobile application.
- Virtual device.
- Network-connected physical device.

The remote architecture should support:

- Authentication.
- Device pairing.
- Encrypted transport.
- Permission management.
- Event streaming.
- Image and state updates.
- Reconnection.
- Multiple clients.

Remote clients should be able to receive rendered output and relevant state updates without needing to recreate the host's background evaluation logic.

### 12.3 Cloud Independence

Cloud functionality enhances DeckHost but must not be required to operate it.

Users must be able to:

- Run locally.
- Store profiles locally.
- Install local plugins.
- Use offline devices.
- Import and export manually.
- Operate without an account.

External reactive sources, such as weather services, may require network connectivity, but their absence must not affect the operation of the core application or unrelated profile functionality.

---

## 13. Cross-Platform Engineering

Windows, Linux and macOS must be first-class targets from the beginning.

### 13.1 Platform Support

| Capability | Windows | Linux | macOS |
|---|---|---|---|
| Desktop application | First-class | First-class | First-class |
| Layout editor | Full | Full | Full |
| Profiles | Full | Full | Full |
| Automation | Full | Full | Full |
| Native plugins | Full | Full | Full |
| Marketplace | Full | Full | Full |
| Generic HID | Supported devices | Supported devices | Supported devices |
| Elgato compatibility | Target | Target | Target |
| OpenAction compatibility | Target | Target | Target |
| MiraBox compatibility | Target | SDK-dependent | SDK-dependent |
| Remote devices | Full | Full | Full |
| Background Studio | Full | Full | Full |
| Background rendering | Capability-dependent | Capability-dependent | Capability-dependent |

Actual hardware compatibility must be validated against vendor SDK availability and platform permissions.

### 13.2 Platform Abstraction

All platform-specific functionality must be behind interfaces.

Examples:

- IProcessService.
- IApplicationMonitor.
- IInputService.
- IHardwareDiscovery.
- IDeviceAccess.
- IWindowManager.
- IStartupManager.
- IFileDialogService.
- ISystemNotificationService.
- ISystemMetricsProvider.

Avoid platform checks scattered throughout business logic.

Background rendering should use the shared rendering abstraction. Platform-specific media decoding, hardware acceleration and system metric collection should be provided through explicit infrastructure services.

### 13.3 CI/CD

Build and test on all three operating systems.

Required pipelines:

1. Restore and build.
2. Unit tests.
3. Contract tests.
4. Integration tests.
5. Cross-platform UI smoke tests.
6. Package validation.
7. Plugin compatibility tests.
8. Rendering and background regression tests.
9. Release artifact generation.

Hardware integration tests should use virtual devices in CI, supplemented by dedicated physical hardware testing.

### 13.4 Distribution

- **Windows:** installer package; portable distribution where appropriate.
- **Linux:** AppImage; Flatpak; distribution packages where practical.
- **macOS:** signed application bundle; notarized distribution.

Updates require platform-aware integrity validation and rollback.

---

## 14. Master Development Roadmap

The roadmap consists of ten phases.

The phases are sequential in terms of architectural dependencies, but independent workstreams can overlap when their contracts are stable.

### Phase 0: Architecture Stabilization

**Objective:** Establish foundations without breaking existing functionality.

Tasks:

- Audit the repository.
- Map existing systems to the target architecture.
- Identify duplicated responsibilities.
- Define module boundaries.
- Establish dependency rules.
- Define core contracts.
- Introduce architectural tests.
- Document platform-specific dependencies.
- Establish cross-platform CI.
- Define versioning and migration policies.

Deliverables: architecture decision records; project dependency diagram; component ownership map; build and test pipelines; technical debt register.

Exit criteria:

- Core projects build on all three platforms.
- Core has no dependency on Avalonia or vendor SDKs.
- Device and plugin contracts are documented.
- Existing MiraBox functionality remains operational.

### Phase 1: Core Platform and Device Abstraction

**Objective:** Turn DeckHost into a universal control-surface host.

Tasks:

- Implement device discovery.
- Implement device lifecycle management.
- Introduce capability descriptors.
- Introduce logical controls and physical surfaces.
- Normalize input events.
- Normalize output operations.
- Implement device registration.
- Add virtual device support.
- Adapt the existing MiraBox driver.
- Integrate the device simulator.

Deliverables: DeckHost Device SDK; device manager; capability registry; virtual device driver; MiraBox driver using the new contracts.

Exit criteria:

- Multiple devices can be discovered and managed.
- Devices expose capabilities without vendor-specific core dependencies.
- Simulator and physical devices use the same contracts.
- A new device can be implemented without modifying Core.

### Phase 2: Profiles, Pages and Layouts

**Objective:** Reach fundamental feature parity with established control-surface applications.

Tasks:

- Implement profiles.
- Implement multiple pages.
- Implement folders.
- Implement page navigation actions.
- Implement Back navigation.
- Implement touch-strip navigation.
- Introduce device-independent logical layouts.
- Introduce device mappings.
- Implement profile import and export.
- Add versioned migrations.
- Implement editor undo and redo.

Deliverables: complete profile system; page navigation; folder navigation; portable layout format; profile migration system.

Exit criteria:

- Multi-page profiles work.
- Users can navigate folders.
- Profiles can be exported and imported.
- Layouts can be mapped to compatible devices.
- Unsupported capabilities are handled explicitly.

### Phase 3: Native Action and Automation Engine

**Objective:** Build a reusable action execution platform.

Tasks:

- Define action contracts.
- Define action configuration schemas.
- Implement action lifecycle.
- Implement action dispatcher.
- Implement Multi Actions.
- Implement delays.
- Implement toggles and stateful actions.
- Implement double press.
- Implement long press.
- Implement conditional execution.
- Implement variables.
- Implement cancellation and error handling.
- Implement event subscriptions.
- Add execution tracing.

Deliverables: Native Action SDK; action registry; execution scheduler; Multi Action editor; event bus; state management system.

Exit criteria:

- Actions execute independently of the UI.
- Multiple actions execute reliably.
- Long-running actions do not block the UI.
- Workflows can be cancelled.
- Action states drive visual feedback.

### Phase 4: Visual Automation

**Objective:** Deliver advanced workflows without sacrificing usability.

Tasks:

- Define workflow intermediate representation.
- Implement node-based editor.
- Implement node registry.
- Implement graph validation.
- Implement branching.
- Implement loops.
- Implement parallel execution.
- Implement variable inspection.
- Implement breakpoints.
- Implement workflow debugging.
- Implement reusable sub-workflows.
- Add workflow templates.

Deliverables: visual workflow editor; shared execution engine; workflow debugger; workflow package format.

Exit criteria:

- Simple and advanced automation share one execution engine.
- Users can debug workflows.
- Execution is observable and cancellable.
- Workflows can be imported and exported.

### Phase 5: Plugin Ecosystem and Compatibility

**Objective:** Allow users to migrate from competing applications without abandoning their existing investments.

Tasks:

- Complete native plugin SDK.
- Implement manifest validation.
- Implement plugin process isolation.
- Implement plugin lifecycle management.
- Implement permissions.
- Implement plugin settings interfaces.
- Implement Elgato compatibility.
- Implement OpenAction compatibility.
- Implement MiraBox compatibility.
- Implement compatibility test harnesses.
- Implement dependency resolution.
- Add plugin diagnostics.
- Define data-provider contracts.
- Define background extension contracts.

Deliverables: native plugin runtime; developer SDK; Elgato compatibility runtime; OpenAction compatibility runtime; MiraBox compatibility runtime; plugin validator; plugin documentation; data-provider SDK documentation.

Exit criteria:

- Native plugins can be developed independently.
- Plugin failures do not crash the desktop application.
- Supported Elgato plugins run without modification.
- Supported OpenAction extensions run through their compatibility layer.
- Supported MiraBox plugins run through their compatibility layer.
- Plugins can expose supported data sources and events.
- Unsupported features are reported clearly.

Compatibility must be measured using real-world plugin test suites.

### Phase 6: Hardware Expansion

**Objective:** Establish DeckHost as a credible universal control-surface platform.

Tasks:

- Implement Elgato device drivers.
- Expand generic HID support.
- Support multiple connected devices.
- Implement device-specific capability mappings.
- Implement firmware information.
- Improve hot-plug handling.
- Add device diagnostics.
- Improve rotary and touch handling.
- Implement output optimizations.
- Expand virtual devices.

Deliverables: MiraBox integration; Elgato integration; generic HID integration; multi-device manager; device compatibility database.

Exit criteria:

- Multiple devices operate simultaneously.
- Device disconnection does not destabilize the application.
- Device mappings can be reused.
- Supported capabilities are accurately represented.

### Phase 7: Rendering, Background Studio and Contextual Experience

**Objective:** Make the visual experience a major competitive advantage.

Tasks:

- Expand rendering components.
- Add animation support.
- Add reusable visual themes.
- Implement dynamic visual states.
- Add data-bound components.
- Implement application-aware profiles.
- Add profile switching rules.
- Add telemetry-driven visuals.
- Implement the background definition model.
- Implement static backgrounds.
- Implement animated backgrounds.
- Implement reactive backgrounds.
- Implement background layers and composition.
- Integrate Background Studio into Layout Studio.
- Implement data-source and property bindings.
- Implement conditional background states.
- Add background transitions.
- Add device-specific background adaptation.
- Implement background presets and asset management.
- Add simulated data and events for preview.
- Optimize rendering performance.

Deliverables: expanded rendering engine; theme system; dynamic component system; context manager; application-aware profiles; background runtime; Background Studio editor; background preset format; reactive background data-binding support.

Exit criteria:

- Editor and physical output produce equivalent results within device constraints.
- Dynamic updates do not require unnecessary full layout redraws.
- Context switching is predictable and configurable.
- Static, animated and reactive backgrounds can be created and reused.
- Reactive backgrounds respond to supported data sources and events.
- Background definitions survive export and import.
- Unsupported device features have predictable fallbacks.
- Background rendering does not block automation, UI or device communication.
- Performance remains acceptable on supported hardware.

### Phase 8: Remote Platform and Marketplace

**Objective:** Build the independent DeckHost ecosystem.

Tasks:

- Implement remote protocol.
- Implement secure device pairing.
- Implement browser-based client.
- Implement remote state updates.
- Implement marketplace package format.
- Implement dependency resolution.
- Implement plugin and profile discovery.
- Implement publisher verification.
- Implement package integrity validation.
- Add offline package installation.
- Implement update rollback.
- Introduce commercial plugin purchasing and entitlement management where appropriate.
- Add distribution support for themes, background presets and asset packs.

Deliverables: remote client; remote protocol; marketplace integration; marketplace client; package manager; plugin publishing documentation.

Exit criteria:

- Remote devices operate independently of desktop UI internals.
- Packages can be installed and updated safely.
- Offline installation remains possible.
- Dependencies are resolved before installation.
- Free and paid plugins can coexist.
- Background packages can be distributed and installed through the same package infrastructure.

### Phase 9: Public Release and Ecosystem Maturity

**Objective:** Deliver a reliable, documented and maintainable public product.

Tasks:

- Complete installation packages.
- Complete signing and distribution.
- Add crash reporting with privacy controls.
- Add diagnostics and support bundles.
- Add migration guides.
- Add onboarding.
- Complete developer documentation.
- Complete user documentation.
- Establish release channels.
- Establish compatibility testing.
- Establish community contribution guidelines.
- Publish GPL-3.0 licensing and source distribution information.

Deliverables: public release; stable SDK; documentation website; release infrastructure; support and contribution processes.

Exit criteria:

- Installation and updates work on all three platforms.
- Profiles survive upgrades.
- Critical workflows pass regression tests.
- Device and plugin failures are recoverable.
- Developers can build extensions using documented tooling.
- Background definitions and assets remain portable across supported upgrades.

---

## 15. Testing and Quality Strategy

NUnit will be used for automated testing.

Test projects:

- UnitTests.
- IntegrationTests.
- ContractTests.
- CompatibilityTests.
- RenderingTests.
- BackgroundTests.
- DeviceTests.
- WorkflowTests.
- EndToEndTests.

Test naming convention: `MethodName_StateUnderTest_ExpectedBehavior`.

### 15.1 Device Testing

Every driver must pass common contract tests.

Test: discovery; connection; disconnection; reconnection; input events; output rendering; unsupported capabilities; device errors; cancellation; multiple devices.

Virtual devices should simulate failures and unusual input patterns.

### 15.2 Plugin Testing

Test: manifest validation; API compatibility; plugin startup; plugin shutdown; communication failures; crashes; timeouts; permission enforcement; dependency failures; update and rollback.

Maintain separate real-world compatibility fixtures for Elgato, OpenAction and MiraBox plugins.

Data-provider plugins must also be tested for invalid values, missing dependencies, stale data, disconnection and high-frequency updates.

### 15.3 Automation Testing

Test: execution order; parallel execution; conditions; variables; nested workflows; cancellation; exceptions; timeouts; event subscriptions; recursive execution protection; recovery after failures.

### 15.4 Rendering Testing

Use deterministic test layouts and reference images.

Test: resolution changes; transparency; text alignment; font rendering; clipping; scaling; animation; layer composition; device-specific output conversion; background composition; background transitions; data-bound visual properties; reactive state changes; fallback rendering.

### 15.5 Background Testing

Background functionality requires dedicated tests in addition to general rendering tests.

- **Definitions and persistence:** background serialization and deserialization; version migrations; layer ordering; missing assets; missing providers; invalid bindings; profile import and export.
- **Static rendering:** colours and gradients; image scaling and cropping; transparency; layer opacity; composition; resolution changes.
- **Animated rendering:** playback; looping; playback speed; frame scheduling; pause and resume; animation throttling; device-specific fallback.
- **Reactive rendering:** data-source updates; event-driven changes; conditional state selection; threshold evaluation; invalid and missing data; provider disconnection; rapid state changes; conflicting updates; fallback values.
- **Background Studio:** layer editing; undo and redo; preview behavior; device simulation; simulated data inputs; import and export.
- **Performance:** static background overhead; animated frame rate; reactive update rate; cache invalidation; memory usage; resource cleanup; multiple active backgrounds.

Reactive background tests should use deterministic mock providers and simulated events rather than depend on live weather services or changing system metrics.

### 15.6 Reliability Objectives

| Area | Target |
|---|---|
| UI responsiveness | No blocking device or plugin operations on UI thread |
| Plugin failures | Isolated and recoverable |
| Device failures | Reconnection without application restart |
| Profile migration | No silent configuration loss |
| Automation | Observable and cancellable |
| Rendering | Incremental updates |
| Background rendering | Resource-aware and capability-adaptive |
| Reactive data | Handles missing and stale sources |
| Offline operation | Core functionality remains available |
| Platform parity | Shared automated test suite |

---

## 16. Risks and Architectural Decisions

### 16.1 Major Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Plugin compatibility complexity | High | Independent compatibility runtimes |
| MiraBox SDK restrictions | High | Validate SDK capabilities and platform availability |
| Vendor SDK restrictions | High | Device abstraction and independent drivers |
| GPL/plugin licensing boundaries | High | Legal review of distribution and linking models |
| Core becoming monolithic | High | Enforced dependency rules |
| Overengineering | High | Incremental milestones |
| Cross-platform UI differences | Medium | Automated smoke tests |
| Rendering performance | Medium | Caching and profiling |
| Plugin security | High | Process isolation and permissions |
| Profile format changes | High | Versioning and migrations |
| Marketplace maintenance | Medium | Begin with local packages |
| Feature scope | High | Prioritized release criteria |
| Animated background resource usage | Medium | Frame limits, caching and quality controls |
| Reactive provider reliability | Medium | Timeouts, fallback values and stale-data handling |
| Background format complexity | Medium | Versioned schema and migration tests |
| External service availability | Medium | Optional providers and local fallbacks |

### 16.2 Architectural Decisions to Lock

1. DeckHost application is GPL-3.0.
2. The application remains free and open-source.
3. Commercial plugins are permitted, subject to licensing compatibility.
4. Core is independent of UI and hardware vendors.
5. Hardware uses capability-based abstractions.
6. Profiles use portable, versioned formats.
7. Native plugins have a stable public SDK.
8. Third-party plugins run outside the main UI process wherever practical.
9. Elgato, OpenAction and MiraBox compatibility are independent subsystems.
10. Both automation editors share one execution engine.
11. Rendering is shared by editor, simulator and physical devices.
12. Windows, Linux and macOS are first-class targets.
13. Cloud services are optional.
14. All critical subsystems expose diagnostics and structured logs.
15. Compatibility is measured against real-world test fixtures.
16. Backgrounds are first-class, reusable visual definitions.
17. Static, animated and reactive backgrounds are supported.
18. Background Studio is integrated into Layout Studio.
19. Reactive backgrounds use the shared data-binding and event infrastructure.
20. Background rendering is independent of external data acquisition.
21. Device-specific limitations are handled through capability-aware fallbacks.
22. Background assets and definitions are versioned and portable.

---

## 17. First Implementation Milestone: DeckHost Foundation 1.0

The complete vision is substantial. Development should begin by stabilizing the existing application and creating the foundations for future expansion.

The first milestone should focus on architecture, device abstraction, plugin contracts and portability.

Background Studio is part of the long-term product direction, but its implementation should build on the stable rendering, profile and data-provider foundations rather than introduce premature complexity into Foundation 1.0.

### 17.1 Foundation Checklist

- Audit the existing repository.
- Map existing functionality to the target architecture.
- Document project and assembly dependencies.
- Define Device SDK contracts.
- Define native Plugin SDK contracts.
- Define Elgato compatibility boundaries.
- Define OpenAction compatibility boundaries.
- Define MiraBox compatibility boundaries.
- Define the shared Action and Event model.
- Introduce virtual device support.
- Adapt the MiraBox N4 Pro driver.
- Implement the first portable profile schema.
- Define the initial rendering abstraction boundaries.
- Identify how future background definitions and assets fit into the profile model.
- Build and test on Windows, Linux and macOS.
- Preserve existing editor, lighting and rendering functionality.
- Establish GPL-3.0 licensing and contribution documentation.

### 17.2 Suggested Initial Effort Allocation

| Area | Effort |
|---|---|
| Architecture and Core | 30% |
| Device Abstraction | 25% |
| Profiles and Layouts | 20% |
| Automation Foundations | 15% |
| Cross-platform Testing | 10% |

These are suggested engineering allocations, not delivery estimates. Background Studio implementation is planned for the rendering and contextual experience phase.

### 17.3 Foundation 1.0 Exit Criteria

- Existing functionality remains operational.
- Core architecture is independent of UI and hardware vendors.
- Device drivers use common contracts.
- Native plugin contracts are documented.
- Four compatibility categories have defined boundaries.
- Virtual devices can test core functionality.
- The application builds on all three operating systems.
- Profiles have a documented, versioned format.
- GPL-3.0 licensing is published correctly.

---

## 18. Product Success Criteria

DeckHost should be measured by whether it can replace existing control-surface software and offer capabilities beyond those applications.

| Category | Success indicator |
|---|---|
| Hardware | Multiple manufacturers supported |
| Compatibility | Real-world plugin compatibility |
| Automation | Simple and advanced workflows |
| Editor | Complete layouts without external tools |
| Visual experience | Static, animated and reactive backgrounds |
| Background editing | Users can create and preview backgrounds visually |
| Background portability | Backgrounds and assets transfer across compatible profiles and devices |
| Context awareness | Visuals respond reliably to system and application state |
| Portability | Profiles transferable across devices |
| Ecosystem | Independent developers publishing extensions |
| Commercial ecosystem | Free and paid plugins coexist |
| Licensing | Application remains GPL-3.0 |
| Reliability | Recoverable device and plugin failures |
| Performance | Responsive UI and efficient rendering |
| Cross-platform | Equivalent core functionality |
| Adoption | Active users, contributors and published packages |

The most important measurement is not the number of features.

It is whether users can replace their existing control-surface software without losing functionality, and then discover capabilities they did not have before.

---

## 19. Final Product Positioning

**DeckHost: One platform. Unlimited control.**

A universal, open-source and extensible platform for physical and virtual control surfaces.

Four competitive advantages:

1. Universal hardware support.
2. Powerful automation.
3. An open four-ecosystem plugin platform.
4. Best-in-class visual experience.

Business and licensing model:

- GPL-3.0 application.
- Free application distribution.
- Community-developed open-source extensions.
- Optional commercial plugins.
- Optional marketplace services.
- No mandatory subscription or cloud dependency for core functionality.

### Final Strategic Principle

Do not turn DeckHost into a feature race against Elgato, OpenDeck, MiraBox or Companion.

The real competitive advantage must be that everything works together through one coherent platform.

A plugin provides telemetry.
Telemetry updates variables.
Variables trigger workflow conditions.
Workflows execute actions.
Actions update device states.
Device states update rendered visuals.
Rendered visuals can include static, animated and reactive backgrounds.
A weather provider can change the background.
A system-monitoring provider can adjust visual effects.
An application context can select a new theme.
An automation workflow can trigger a visual state transition.

All of this works across multiple devices, manufacturers and operating systems.

DeckHost should not just control devices. It should become the platform that makes control surfaces universally programmable.

---

## Appendix A: Capabilities beyond this document

DeckHost already does the following, which the sections above don't describe. They belong to the
product and are kept as it grows.

**Devices**

- **Device creator:** teach DeckHost any USB HID device without code: press each key and turn each
  knob, DeckHost learns the report bits (per-bit noise, relative and absolute knobs), and you lay
  the controls out by dragging. The result is a device description file (`devices/hid/<id>.json`).
- **Linked displays:** a control without a screen (a knob) can use another control's screen (its
  touch zone): the knob's action fills the zone or shares it as a strip, and a tap on the zone
  presses the knob.
- **Screen backdrop:** devices with one screen behind all keys get the profile's background color
  there.
- **Firmware API:** device plugins can report firmware and, with the maker's protocol, install it;
  DeckHost does downloads, checksums, confirmation and pausing the device.
- **Device settings buttons:** a device plugin can add buttons to its settings page (the Web Deck's
  New pairing code) with a confirmation.
- **Device statistics:** inputs, frames, failures and the last error per device, on the Help page
  and in support bundles.
- **N4 Pro specifics:** vibration, boot video, idle screen, LED brightness, key lights.

**Lights**

- **Light effects:** steady, breathe, pulse, color cycle, custom step sequences, per-LED offsets;
  a device-wide light, per-knob overrides, and each LED its own effect; a live preview editor.

**Editor and look**

- **Themes:** dark, light or the system's.
- **Icon library:** Stream Deck icon packs, image folders, and a search of the open Iconify icon
  sets, saved locally with their license; single-color SVG icons follow the text color.
- **Color picker:** spectrum, hue, opacity, HEX/RGB, presets and a screen eyedropper.
- **Tiles:** optional rounded gradient tiles per action, with their own colors and corners.
- **Own text on every action**, in the user's text style (font, size, alignment, wrap).
- **Per-input settings:** one control can do different things on press, hold, turn left and turn
  right, and swipe.
- **Key Logic:** press, double press and hold on one key; **Multi Action Switch** for toggles.
- **Undo and redo** with merged quick edits; **drag a control onto another** to move, swap or copy.
- **Welcome tour** and a **debug panel**.

**Profiles**

- **Automatic daily backups** and restore; **Stream Deck profile import** (`.streamDeckProfile`);
  **copy a profile to another device** with the layout mapped by rows, columns and dial numbers.

**Plugins**

- **Plugin documentation pages:** each plugin's actions, links and README, from the Plugins page
  and right-click menus.
- **Plugin updates:** checked against the store; update one, all, or automatically; **roll back** to
  the previous version.
- **Install from GitHub:** a repository's newest release, checked against GitHub's checksum.
- **Plugin catalog repository:** pull-request based, with checks, an Add a plugin issue form and
  weekly discovery of plugins on GitHub.
- **Plugin template** (`dotnet new deckhost-plugin`) and the plugin checker
  (`DeckHost --check-plugin`).
- **Stream Deck plugin images:** their black backgrounds become transparent, so DeckHost's
  backgrounds show around them.

**Remote and safety**

- **Web Deck:** a connect page with a short pairing code and an optional PIN, rate limiting of
  wrong tries, and a new code that disconnects every phone.

**Application**

- **Updates:** checked against checksums and build provenance; install now, or automatically when
  DeckHost closes.
- **Crash reports** offered (never sent without asking) and **support bundles**.
- **Single instance, tray icon, start at sign-in, start minimized.**
- **Issues on command:** `@claude` on an issue runs Claude Code to propose a fix as a pull request.
