# DeckHost

DeckHost is a modular host for physical control surfaces (macro pads, rotary consoles, touch strips). It separates the hardware from the actions: device plugins drive the hardware, application plugins provide the actions, and the designer lets you map one onto the other.

The first supported device is the **MiraBox / StreamDock N4 Pro**: 10 LCD keys, a touch strip with 4 zones and swipe, 4 knobs and 4 RGB LEDs.

## Core architecture
- **Hardware decoupled:** the core only talks to `IDevicePlugin` / `IDeviceOutput`. Everything device-specific (SDK ids, image sizes, LEDs, native calls) lives in the device plugin.
- **Per-control rendering:** each key and touch zone is rendered on its own with SkiaSharp and pushed only when its pixels changed. Render buffers, paints and fonts are reused between frames.
- **One runtime worker:** input, actions, rendering and USB I/O run on a single background worker, so native SDK callbacks return immediately and the UI thread never waits on the device.
- **Reactive state:** a thread-safe Rx.NET variable store connects actions and renderers.
- **Saved profiles:** mappings are stored in `%AppData%/DeckHost/profiles.json` (or the platform equivalent).
- **Cross-platform:** .NET 10 and Avalonia UI. The native bridge builds on Windows, Linux and macOS; Windows is the primary development platform.

## Building from source

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- CMake 3.21+ and a C++20 compiler (MSVC on Windows; Visual Studio's bundled CMake is detected automatically)

### Steps
```powershell
git clone --recursive <repository-url>
cd DeckHost
dotnet build .\DeckHost.slnx -c Debug
dotnet run --project .\DeckHost.App
dotnet test .\DeckHost.Tests
```

`dotnet build` also configures and builds the native bridge (`native/MiraBoxBridge`) with CMake and copies it to `plugins/device/deckhost.device.mirabox/` next to the app. To build only the C# code, add `-p:EnableMiraBoxNativeBuild=false`; the app then starts without device support.

## Plugins

DeckHost ships with three **system plugins**. You can switch them off in the Plugin Manager, but you can't uninstall them:

| Plugin | Actions |
|---|---|
| DeckHost Built-ins (always on) | Multi Action, Switch Profile, Clock, Counter, Key ID (Diagnostic) |
| Media Controls | Play / Pause, Next, Previous, Stop, Volume Up / Down, Mute, Volume Knob, Track Swipe |
| System Controls | Launch App, Open Website or File, Hotkey, Type Text, Lock Computer, System Info (CPU, memory, disk, uptime) |

Media keys, hotkeys, typing and locking use Windows `SendInput`; on other systems those actions show "Windows only".

Actions with settings (the app to launch, a hotkey, a scene name, ...) are set up with **double-click** or **right-click → Customize**. Multi Action steps are added, removed, reordered and configured there too, or added by dropping more actions onto the control. For a Hotkey, click **Record** and press the combination.

Drag a mapped control onto another control to move it (the two swap if the target is taken), hold **Ctrl** to copy it, or drop it on an empty area to remove it.

## Profiles

Profiles are the pages of a device. **☰ → Open Profile Manager** (or **Manage…** next to the profile list) creates, duplicates, renames, reorders and deletes them. The order is also what the Next/Previous Profile actions follow.

**Automatic switching (Windows):** give a profile one or more apps (process names such as `obs64` or `chrome`, or pick a running app) and it becomes active while that app's window is in front. When the focused app isn't listed anywhere, DeckHost returns to the profile you picked yourself. DeckHost's own windows never trigger a switch. Turn it off with the switch in the Profile Manager or the ☰ menu.

Duplicating a profile copies everything: bindings, action settings, Stream Deck plugin settings, apps and background.

### Customizing a control

**Customize** (double-click a control) sets:

- **Run when** (for actions that normally run on a press): only the inputs the control has, so a key offers press, release and long press, a knob also turned left/right, and the swipe bar swiped left/right.
- **Text**: multi-line, wrapped or not, with color (picker or hex), font, size, bold, italic, and horizontal (left/center/right) and vertical (top/middle/bottom) alignment.
- **Image**: PNG, JPG, animated GIF or SVG, with a **Clear** button; and a background color.
- The action's own settings, and for knobs the LED color.

### Plugin store and Stream Deck plugins

**☰ → Open Plugin Manager** (or **Plugins…** next to the action search) → **Store** shows one list, which you can filter by source, category, status and search text:

- **DeckHost**: plugins from the sources under **Store → Sources**, one per line. A source is a catalog URL, a local catalog file, or a local folder such as `E:\Projects\DeckHost\plugins`. Folders are searched for packages (`.zip`, `.dhplugin`, `.streamDeckPlugin`) and built plugin folders (a `plugin.json` next to its assembly, e.g. a project's `bin` output), so plugins you are developing show up without packaging them. A catalog is a JSON object keyed by plugin id (or an array) with `name`, `author`, `description`, `version`, `category`, `download_url`, `repository` and `icon`. There is no official DeckHost catalog yet. The list is saved in `%AppData%/DeckHost/store.json`.
- **OpenAction**: the open-source Stream Deck plugin catalog (`openactionapi.github.io/plugins`). Packages come from the plugin's download link or its latest GitHub release.
- **Elgato (community archive)**: an archive of Elgato Marketplace plugins (`plugins.amankhanna.me`). Elgato has no public store API.

The catalogs have no categories, so DeckHost derives one from each plugin's name and description (a category from the catalog or the plugin's manifest wins). **Page** opens the plugin's page: the repository for OpenAction plugins, the Elgato Marketplace for Elgato plugins. Stream Deck plugins from both sources install into the same Stream Deck folder, since they are the same kind of plugin.

Installing, uninstalling and enabling run in the background; the Plugin Manager shows each step (downloading with a percentage, unpacking, starting, stopping, removing files). The Installed tab shows where each plugin came from.

Stream Deck plugins run through DeckHost's Stream Deck compatibility layer:

- Each plugin starts as its own process (native `.exe` or Node.js) and talks to DeckHost over the standard Stream Deck SDK WebSocket protocol.
- N4 Pro keys act as Stream Deck keys and the knobs as Stream Deck+ dials.
- `setTitle`, `setImage` (PNG, JPG and SVG), `setState`, settings and global settings work.
- HTML-based plugins (whose code is a web page) run in a hidden built-in browser page, as in the Stream Deck app. Their console output goes to the DeckHost log.
- A plugin's own settings page (property inspector) opens inside the Customize window through the built-in browser (Microsoft Edge WebView2, which Windows 10/11 include). **Open in browser** is there as a fallback.

Limits:

- Node.js plugins need Node.js 20 or newer.
- Settings are kept per device, profile and control.

You can also install a `.streamDeckPlugin` file with **Install plugin…**.

### Device settings

**☰ → Device settings** (or **Settings…** next to a device in the Plugin Manager) holds the device plugin's own settings. For the N4 Pro: vibration, the start-up animation, LED and key-light behavior, screen and LED brightness, and the **idle / start-up screen** the device shows when DeckHost isn't running. Settings start at their default (switches on, brightness 100), which DeckHost doesn't send until you change something; setting a value back to its default sends the default once, so the device really returns to it. After writing the idle screen DeckHost sends all key images again. If an old image is stuck on the display when DeckHost is closed, set the idle screen to **Black** (or pick an image) and save. Settings are stored in `%AppData%/DeckHost/devices/<device>.json` and applied again whenever the device connects.

### DeckHost settings and the tray

**☰ → DeckHost settings** (or **Settings…** in the tray menu):

- **Start DeckHost when I sign in to Windows**: adds DeckHost to your user's start-up programs (the `HKCU\…\Run` key; no admin rights needed).
- **Start in the tray when started with Windows**: the device works without the window.
- **Closing the window keeps DeckHost running in the tray**: use **Exit** in the tray menu to quit.

Only one DeckHost runs at a time: starting it again shows the running one. (Two instances would both read the device and run every action twice.)

### Folders and logs

| What | Where |
|---|---|
| Log files (NLog, one per day, kept 14 days) | `%AppData%/DeckHost/logs/deckhost-<date>.log` |
| Stream Deck plugin output | `%AppData%/DeckHost/logs/plugins` |
| DeckHost plugins | `%AppData%/DeckHost/plugins/functional/<id>` |
| Stream Deck plugins | `%AppData%/DeckHost/plugins/streamdeck/<uuid>.sdPlugin` |
| Profiles | `%AppData%/DeckHost/profiles.json` |

The Plugin Manager shows these paths and has **Plugins folder** and **Logs folder** buttons.

### Backgrounds, live preview and test mode

- **Backgrounds:** a profile can have a background color and an image or animated GIF that spans all keys (Profile Manager). Each control can have its own background color, and its image can be an animated GIF (Customize). GIFs play at about 10 frames per second.
- **Live preview:** the canvas shows the exact images that are on the device, even when it's unplugged, and lights up controls as you press them.
- **Test mode** (canvas header): click controls to press them, use the mouse wheel to turn knobs, and click the left or right half of the swipe bar to swipe. The real actions run.

### Installable plugins

Other plugins are installed from a package with **☰ → Open Plugin Manager → Install plugin…**. A package is a `.zip` with a `plugin.json` and the plugin's assemblies:

```json
{
  "id": "deckhost.plugin.obs",
  "name": "OBS Studio",
  "version": "1.0.0",
  "author": "DeckHost",
  "description": "…",
  "assembly": "DeckHost.Plugin.Obs.dll",
  "hostApiVersion": 1
}
```

The assembly must contain a public `IApplicationPlugin` with a parameterless constructor whose `Id` matches the manifest. It must not ship its own copy of `DeckHost.Contracts`. Installed plugins live in `%AppData%/DeckHost/plugins/functional/<id>`. Plugins run with your user's rights, so only install packages you trust.

`plugins/DeckHost.Plugin.Obs` is an example: OBS Studio control through obs-websocket 5 (OBS 28+). It can switch scenes, toggle mute, toggle streaming and recording, and save the replay buffer. Build it with `dotnet build .\plugins\DeckHost.Plugin.Obs` and install `artifacts\plugins\deckhost.plugin.obs.zip`. Then enter the OBS WebSocket password in each OBS action's settings (OBS: Tools → WebSocket Server Settings).

### Firmware

The Plugin Manager shows each device's serial number and firmware version. The StreamDock SDK can read the firmware version but has no function to update it, so firmware updates still go through MiraBox's own Stream Dock software.

## StreamDock SDK patch

`native/StreamDock-SDK` is pinned to an upstream commit. The local fixes it needs are kept in `native/patches/streamdock-sdk.patch`, and the build applies them automatically (it checks first, so applying twice is harmless).

To apply or check the patch by hand:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Apply-StreamDockSdkPatch.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\Apply-StreamDockSdkPatch.ps1 -CheckOnly
```

When you update the submodule and the patch no longer applies, regenerate it against the new upstream commit with `git -C native/StreamDock-SDK diff > native/patches/streamdock-sdk.patch` after fixing the files.

## N4 Pro key mapping

The SDK numbers the N4 Pro's keys from the bottom-left, so the top row is SDK keys 11–15 and the bottom row 6–10. If an image appears on the wrong key on your unit, assign the **Key ID (Diagnostic)** action to the keys and adjust `KeyHardwareIds` in `DeckHost.Device.MiraBox/N4ProLayout.cs`.

## License
MIT License. See `LICENSE` for details.
