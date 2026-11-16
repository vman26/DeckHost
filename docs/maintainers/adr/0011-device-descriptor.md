# 0011: The device descriptor is DeviceDefinition, grown in place

**Status:** Accepted

## Context
The master plan (7.1) asks every device plugin for a structured, versioned description of its
hardware: identity, control topology, displays and display regions, capabilities and limitations,
physical sizes. DeckHost already had `DeviceDefinition` and `PhysicalControl` (controls, layout
position, pixel size, inputs, outputs, LEDs, linked displays), used by every plugin, the host, the
editor and installed plugins built against Contracts. The plan names the contracts `DeviceDescriptor`,
`DeviceControlDescriptor` and `DeviceDisplayDescriptor`, but says the exact interfaces are to be
validated.

## Decision
- `DeviceDefinition` **is** the descriptor and `PhysicalControl` the control descriptor; they grow
  with optional `init` properties, so their constructors and plugins built against them stay as
  they are: `DescriptorVersion` (`CurrentDescriptorVersion` = 1), `Identity` (`DeviceIdentity`:
  manufacturer, model, serial, revision, firmware, connection), `Displays` (`DeviceDisplay`: id,
  pixel size, rotation, frame-rate limit, the controls on it), `Capabilities` (`DeviceCapabilities`
  flags), `Limits` (`DeviceLimits`: frame rate, transparency, color depth, notes) and `PhysicalSize`;
  on controls `PhysicalBounds` (mm), `DisplayId` and `Shape`. `ControlType` gains `Button`,
  `Joystick` and `Led`.
- Logical coordinates stay `Bounds` (the editor's layout units); display coordinates are each
  display's pixels; physical coordinates are millimetres, optional.
- `FindDescriptorProblems()` checks a descriptor's consistency; tests run it on every built-in device.
- Topology changes keep using `DevicesChanged` with a new definition; bindings stay keyed by control
  id, so a control that disappears doesn't move others.

## Consequences
- Nothing breaks for existing plugins; a descriptor without the new fields is still valid (version
  1, no displays listed, no capabilities declared).
- The names differ from the plan's; the plan's status page maps them.
- Capabilities are declared by the plugin, next to the optional interfaces that implement them
  (backdrop, firmware, settings); the host can query either.
