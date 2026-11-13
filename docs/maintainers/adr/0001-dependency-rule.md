# 0001: Contracts ← Core ← App → device plugins

**Status:** Accepted

## Context
DeckHost drives several kinds of hardware (MiraBox through a native SDK, Elgato over HID, a Web Deck over a socket) and runs actions from plugins it doesn't build. Hardware code changes for reasons that have nothing to do with actions, profiles or the UI.

## Decision
- **Contracts** holds what plugins see: interfaces (`IDevicePlugin`, `IDeviceOutput`, `IDeckAction`, `IApplicationPlugin`, …) and the records they exchange. It references nothing of DeckHost, no UI and no drawing library.
- **Core** is the runtime (host worker, routing, rendering, profiles, plugin loading) and references only Contracts.
- **Device plugins** reference only Contracts.
- **App** composes everything: it is the only assembly that knows the device plugins.

## Consequences
- A new device is a new assembly against Contracts; Core and the UI don't change.
- Core is tested without hardware or a window.
- Everything a device needs from the host must be expressible in Contracts, which therefore grows (default interface members, init properties; see 0003).
- `ArchitectureTests` checks these rules on the built assemblies; a pull request that breaks them fails.
