# 0009: Firmware updates go through the host, written by the device plugin

**Status:** Accepted

## Context
Device makers ship firmware fixes. Each has its own update protocol (often undocumented), and a bad write can leave a device unusable. DeckHost also keeps drawing on every connected device, which must not happen while firmware is being written.

## Decision
- Device plugins may implement `IDeviceFirmware` (Contracts): report the version, optionally check the maker's server for a newer release, and write an image.
- Everything around the write is DeckHost's (`Core/Firmware/FirmwareService`), the same for every plugin: only connected devices, a confirmation in the UI, HTTPS downloads that must match the release's SHA-256 (no checksum, no install), one update at a time, and the device **paused** in the host (no images, lights or input) until the plugin is done, then redrawn.
- A plugin installs only with the maker's documented protocol, and checks that the image is for this device before writing.
- Today's plugins only report versions: the StreamDock SDK can't write firmware, Elgato's protocol isn't public, and generic USB devices are updated with their maker's tool.

## Consequences
- A plugin for a device with an open update protocol (many QMK/VIA, Arduino or DFU-based devices) gets updates with little code, and users get the same flow and safety for every device.
- The Device settings page shows every device's firmware version.
- The checksum is only as trustworthy as where the plugin got it; plugins should take it from the maker over HTTPS.
