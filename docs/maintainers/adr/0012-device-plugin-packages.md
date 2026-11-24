# 0012: Device plugins are plugin packages

**Status:** Accepted

## Context
Action plugins load from packages (`plugin.json`, a collectible load context, [decision 0007](0007-in-process-plugins.md)
for their own process), but device plugins were compiled into DeckHost and registered in `App.axaml.cs`, so
supporting a new device took a pull request. The device plugins only reference `DeckHost.Contracts`, which made
loading them from packages possible; what was missing was a kind in the manifest, a loader, and a host that
accepts device plugins while it runs.

## Decision
- `plugin.json` gets `kind` (`application`, the default, or `device`). `PluginCatalog` loads a device package
  into its own load context, in this process, and announces it (`DevicePluginLoaded`, `DevicePluginUnloading`)
  instead of listing actions. Enabling, disabling, updating and uninstalling work as for any package.
- `DeviceHostService` accepts plugins while it runs (`AddDevicePluginAsync`, `RemoveDevicePluginAsync`, on its
  worker like everything else); it disposes only the plugins it was constructed with. The catalog disposes a package
  plugin, after the host has blanked and let go of it. `LiveDevicePlugins` gives the services that look devices up
  (firmware, device settings, the simulator) a current list.
- The plugins that ship with DeckHost are bundled packages in `plugins/bundled/<id>` (read-only: they can be
  disabled, not uninstalled; an installed version of the same id wins). They load in safe mode too.
- Device plugins run in the DeckHost process: their live output and input callbacks would need a wire protocol
  that [decision 0007](0007-in-process-plugins.md) doesn't have. SkiaSharp is shared with the host like Contracts.
  Their load context is not collectible: MiraBox's native callbacks (`[UnmanagedCallersOnly]`) can't live in one.
- Native libraries: the load context resolves a plugin's imports from its folder and publishes the folder as the
  AppContext value `DeckHost.PluginDirectory.<assembly>` (a memory-loaded assembly has no location); MiraBox reads it
  to preload its native dependencies. `--check-native` loads the bundled device plugins and runs their
  `CheckRuntime()`.
- All of DeckHost's device plugins are bundled: MiraBox, Elgato, Web Deck, generic USB (HID) and the simulator.
  The app still references the HID and simulator projects for the device creator and the simulator window, but those
  copies never run the plugin: the creator writes description files the HID plugin watches, and the simulator finds
  the other plugins' models through `IDevicePluginContext.GetDevicePlugins()` (a default member). DeckHost settings →
  Developer → Device simulator turns the simulator plugin on and off in the catalog.

## Consequences
- New hardware support can ship, update and be turned off like any plugin, without a DeckHost release.
- A device plugin has the same trust as DeckHost itself (it runs in the process); the manifest's permissions
  are declarations, as for other plugins.
- Turning a device plugin off removes its devices from the editor's list until it is turned on again.
- Updating a device plugin with native files (MiraBox) may need a restart on Windows, where loaded libraries are locked.
