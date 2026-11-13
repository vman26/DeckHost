# Writing plugins

A DeckHost plugin is a .NET assembly with a public `IApplicationPlugin` (from `DeckHost.Contracts`) and a parameterless constructor, packaged as a `.zip` with a `plugin.json`:

```json
{
  "id": "deckhost.plugin.obs",
  "name": "OBS Studio",
  "version": "1.0.0",
  "author": "DeckHost",
  "description": "Scenes, mute, streaming and recording.",
  "assembly": "DeckHost.Plugin.Obs.dll",
  "hostApiVersion": 1,
  "icon": "icon.png",
  "permissions": ["network"]
}
```

`permissions` says what the plugin does beyond drawing and reacting to input; DeckHost shows it when the plugin is installed and on the Plugins page. Known values: `network`, `processes` (starts programs), `files`, `input` (presses keys, moves the mouse), `system` (volume, power, displays), `clipboard`. They are declarations, not a sandbox: plugins run inside DeckHost ([decision 0007](adr/0007-in-process-plugins.md)).

- Reference `DeckHost.Contracts` without copying it into the package (`Private=false`).
- Each action is an `IDeckAction`: its `Settings` become the fields in the editor, `Execute` runs on input, `Render` draws on the control. Both run on DeckHost's worker and must return quickly: start slow work on a background task and call `RequestRender` when it's done.
- `plugins/DeckHost.Plugin.Obs` in the source is a complete example (obs-websocket 5).

## Check a package

```
DeckHost --check-plugin path/to/plugin.zip     # or a folder
```

checks `plugin.json` and the files it names, the version, permissions and files that don't belong (such as a copied `DeckHost.Contracts.dll`), then loads the plugin in a scratch folder (nothing is installed) and lists its actions. It exits with 1 on errors, so it fits in a plugin's CI. The Plugins page shows the same warnings for installed plugins.

## Compatibility

A plugin built for host API version 1 keeps loading in every DeckHost that supports version 1: the plugin API only grows ([decision 0003](adr/0003-plugin-api.md)). When it can't, DeckHost bumps the version and refuses older plugins with a clear message.

## Device plugins and firmware

A device plugin (`IDevicePlugin`) can also implement `IDeviceFirmware`:

```csharp
DeviceFirmwareInfo? GetFirmwareInfo(string deviceId);                    // version, CanUpdate, a line for the user
Task<FirmwareRelease?> CheckForFirmwareAsync(string deviceId, ...);      // optional: newer firmware from the maker (with its SHA-256)
Task<FirmwareUpdateResult> InstallFirmwareAsync(string deviceId, FirmwareImage image, IProgress<FirmwareProgress> progress, ...);
```

Reporting the version alone is useful: Device settings shows it. To install, set `CanUpdate` and implement `InstallFirmwareAsync`, plus `CheckForFirmwareAsync` (a release must carry a SHA-256) and/or `FileExtensions` (the user picks a file). DeckHost confirms with the user, downloads over HTTPS and checks the checksum, pauses the device in the host (no images, lights or input) while you write, and redraws it afterwards. Check that the image is meant for this device before writing, report progress, return a result instead of throwing, and handle the device dropping off and coming back. Only use the maker's documented protocol ([decision 0009](adr/0009-firmware-updates.md)).

Stream Deck plugins (`.sdPlugin`) and OpenAction plugins run as they are: see the Elgato Stream Deck SDK.
