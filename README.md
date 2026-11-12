# DeckHost

DeckHost is a modular host for physical control surfaces (macro pads, rotary consoles, touch strips). It separates the hardware from the actions: device plugins drive the hardware, application plugins provide the actions, and the designer lets you map one onto the other.

The first supported device is the **MiraBox / StreamDock N4 Pro**: 10 LCD keys, a touch strip with 4 zones and swipe, 4 knobs and 4 RGB LEDs.

## Core architecture
- **Hardware decoupled:** the core only talks to `IDevicePlugin` / `IDeviceOutput`. Everything device-specific (SDK ids, image sizes, LEDs, native calls) lives in the device plugin.
- **Per-control rendering:** each key and touch zone is rendered on its own with SkiaSharp and pushed only when its pixels changed. Render buffers, paints and fonts are reused between frames.
- **One runtime worker:** input, actions, rendering and USB I/O run on a single background worker, so native SDK callbacks return immediately and the UI thread never waits on the device.
- **Reactive state:** a thread-safe Rx.NET variable store connects actions and renderers.
- **Saved profiles:** mappings are stored in `%AppData%/DeckHost/profiles.json` (or the platform equivalent).
- **Cross-platform:** .NET 10 and Avalonia UI on Windows, Linux and macOS (Apple Silicon). Windows is the primary development platform.

## Download and install

Releases are on the repository's **Releases** page, one package per platform:

| Platform | Package | Install |
|---|---|---|
| Windows 10/11 (x64) | `DeckHost-<version>-win-x64-setup.exe` | Run it. Installs for your user (no administrator rights), with a Start menu entry and uninstaller. A plain `.zip` is there too. |
| Linux (x64) | `DeckHost-<version>-linux-x64.tar.gz` | Unpack and run `./install.sh`: installs to `~/.local/share/deckhost` with a menu entry, and (with sudo, once) the udev rule that lets you use the N4 Pro without root. `./install.sh --uninstall` removes it. |
| macOS 12+ (Apple Silicon) | `DeckHost-<version>-osx-arm64.zip` | Unzip and move **DeckHost.app** to Applications. It isn't notarized: the first time, right-click → **Open**, or run `xattr -dr com.apple.quarantine /Applications/DeckHost.app`. |

`SHA256SUMS.txt` lists the checksums. To check that the device support is installed without opening the window, run DeckHost with `--check-native`: it loads the native bridge and lists the StreamDock devices it finds.

### What works where

| | Windows | Linux | macOS |
|---|---|---|---|
| N4 Pro (keys, touch strip, knobs, LEDs, settings) | ✓ | ✓ (udev rule) | ✓ |
| Media keys, volume | ✓ | playerctl; wpctl, pactl or amixer | ✓ (volume HUD) |
| Hotkey, Type Text | ✓ | xdotool (X11) or wtype (Wayland) | ✓ (Accessibility permission) |
| Lock computer | ✓ | loginctl or xdg-screensaver | ✓ (Accessibility permission) |
| Volume Knob level on the touch strip | ✓ | wpctl, pactl or amixer | ✓ |
| Automatic profile switching | ✓ | X11 / XWayland apps, needs xprop | ✓ |
| Start at sign-in, tray | ✓ | XDG autostart; tray needs a StatusNotifier host (KDE, most desktops; GNOME with the AppIndicator extension) | ✓ (LaunchAgent) |
| Stream Deck plugins (native and Node.js) | ✓ | ✓ when the plugin ships a Linux build | ✓ when the plugin ships a macOS build |
| Stream Deck HTML plugins, settings pages inside DeckHost | ✓ (WebView2) | settings pages open in your browser; HTML plugins don't run | same as Linux |

On Linux, "close to tray" starts off, since not every desktop shows a tray icon; turn it on in DeckHost settings if yours does. The Linux tools above are optional: an action whose tool is missing shows which one to install.

## Building from source

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- CMake 3.21+ and a C++20 compiler (MSVC on Windows; Visual Studio's bundled CMake is detected automatically; GCC or Clang on Linux; Xcode command line tools on macOS)
- Linux only: OpenCV, giflib and libudev development packages (Ubuntu: `libopencv-dev libgif-dev libudev-dev`), or a static OpenCV from `scripts/build-opencv-static.sh` (what releases use).

### Steps
```powershell
git clone --recursive <repository-url>
cd DeckHost
dotnet build .\DeckHost.slnx -c Debug
dotnet run --project .\DeckHost.App
dotnet test .\DeckHost.Tests
```

`dotnet build` also configures and builds the native bridge (`native/MiraBoxBridge`) with CMake and copies it to `plugins/device/deckhost-device-mirabox/` next to the app. To build only the C# code, add `-p:EnableMiraBoxNativeBuild=false`; the app then starts without device support.

The tests run on all three systems. On Linux, to build against the static OpenCV releases use:

```bash
scripts/build-opencv-static.sh ~/opencv-static
dotnet build DeckHost.slnx -p:MiraBoxCMakeArgs=-DOpenCV_DIR=$HOME/opencv-static/lib/cmake/opencv4
```

### Versions and releases

Versions come from git ([GitVersion](https://gitversion.net), rules in `GitVersion.yml`) and follow [SemVer](https://semver.org). Tags are releases; between releases, **the branch you work on** says what comes next, so nothing is guessed:

| Where you work | Builds as | Meaning |
|---|---|---|
| tagged `v1.3.0` | `1.3.0` | a release |
| `develop`, `feature/*` | `1.4.0-alpha.N+4b09090` | work towards the next minor |
| `bugfix/*` | like the branch it starts from: from `develop` `1.4.0-alpha.N`, from a release `1.3.1-alpha.N` | a fix |
| `hotfix/*`, from a release | `1.3.1-beta.N`; `hotfix/1.3.5` builds `1.3.5-beta.N` | the next patch |
| `release/1.4.0` | `1.4.0-rc.N` | getting 1.4.0 ready |
| a commit message with `+semver: major` (or `minor`, `patch`) | `2.0.0-alpha.N` on `develop` | a bigger (or smaller) step than the branch implies |
| any other branch | like the branch it starts from, label `alpha` | |
| before the first tag | `0.1.0-alpha.N+…` | |

- `N` counts the commits since the version's starting point (the last tag, or where a release branch began). It only counts; two branches can show the same number, which is why the commit is added.
- Once a `release/*` branch exists, `develop` moves on to the minor after it (`release/1.4.0` → `develop` builds `1.5.0-alpha.N`).
- Every version that isn't a release ends in `+<commit>`: the first 7 characters of the commit it was built from (`git show 4b09090`). Release versions don't need it, the tag points at the commit.
- Package file names use `-` instead of `+` (`DeckHost-1.4.0-alpha.3-4b09090-linux-x64.tar.gz`), since GitHub renames some characters in release files.
- The app shows its version in **☰ → DeckHost settings** and in the log. Builds need the full git history (not a shallow clone); a source archive without `.git` builds as 1.0.0.

To release, tag and push:

```bash
git tag v1.4.0
git push origin v1.4.0
```

The tag is the version. The **release** workflow then tests and publishes DeckHost self-contained for Windows, Linux and macOS, checks the native bridge in each package (`--check-native`), packages them (Windows installer and zip, Linux tarball, macOS app) and creates the GitHub release with notes and checksums. A tag with a suffix (`v1.4.0-rc.1`, `v1.3.1-beta.1`) becomes a pre-release; use the label of the branch you tag on (`rc` on `release/*`, `beta` on `hotfix/*`), so the builds after it count on from it. Run the workflow by hand to get the packages as build artifacts without releasing.

The usual flow (git-flow):

- **Features:** branch `feature/*` from `develop`, merge back. For a release: branch `release/1.4.0` from `develop`, fix and tag `v1.4.0-rc.1`, … as needed, merge it into `main`, tag `v1.4.0` there, and merge `main` back into `develop`.
- **Urgent fix for a release:** branch `hotfix/*` from `main` (or from the tag, `git switch -c hotfix/usb v1.3.0`), merge into `main`, tag `v1.3.1`, merge `main` back into `develop`.
- **Next major:** put `+semver: major` in a commit message on `develop` when the breaking work lands.

## Plugins

DeckHost ships with three **system plugins**. You can switch them off in the Plugin Manager, but you can't uninstall them:

| Plugin | Actions |
|---|---|
| DeckHost Built-ins (always on) | Multi Action, Switch Profile, Clock, Counter, Key ID (Diagnostic) |
| Media Controls | Play / Pause, Next, Previous, Stop, Volume Up / Down, Mute, Volume Knob, Track Swipe |
| System Controls | Launch App, Open Website or File, Hotkey, Type Text, Lock Computer, System Info (CPU, memory, disk, uptime) |

Media keys, hotkeys, typing and locking use Windows `SendInput`, the tools listed under "What works where" on Linux, and system key events on macOS.

Actions with settings (the app to launch, a hotkey, a scene name, ...) are set up with **double-click** or **right-click → Customize**. Multi Action steps are added, removed, reordered and configured there too, or added by dropping more actions onto the control. For a Hotkey, click **Record** and press the combination.

Drag a mapped control onto another control to move it (the two swap if the target is taken), hold **Ctrl** to copy it, or drop it on an empty area to remove it.

## Profiles

Profiles are the pages of a device. **☰ → Open Profile Manager** (or **Manage…** next to the profile list) creates, duplicates, renames, reorders and deletes them. The order is also what the Next/Previous Profile actions follow.

**Automatic switching (Windows):** give a profile one or more apps (process names such as `obs64` or `chrome`, or pick a running app) and it becomes active while that app's window is in front. When the focused app isn't listed anywhere, DeckHost returns to the profile you picked yourself. DeckHost's own windows never trigger a switch. Turn it off with the switch in the Profile Manager or the ☰ menu.

Duplicating a profile copies everything: bindings, action settings, Stream Deck plugin settings, apps and background.

### Customizing a control

**Customize** (double-click a control) sets:

- **Run when** (for actions that normally run on a press): only the inputs the control has, so a key offers press, release and long press, a knob also turned left/right, and the swipe bar swiped left/right.
- **Per input** (actions that support it, such as Switch Profile): on a control with more than one input, a group of settings for each input: **When pressed**, **When held down**, **When turned left/right**, **When swiped left/right**. For example a knob that goes to a specific profile when pressed and steps back and forth through the profiles when turned. "Do nothing" leaves an input unused.
- **Text**: multi-line, wrapped or not, with color (picker or hex), font, size, bold, italic, and horizontal (left/center/right) and vertical (top/middle/bottom) alignment.
- **Image**: PNG, JPG, animated GIF or SVG, with a **Clear** button; and a background color.
- The action's own settings, and for knobs the LED color. Numeric settings have a slider with a number box next to it for exact values, and **↺** to go back to the default.

### Knobs on the touch strip

The N4 Pro's knobs have no screen of their own, so like on a Stream Deck +, each knob shows its action on the touch zone above it:

- **Touch zone empty:** the knob's action fills the zone, for example the Volume Knob shows the volume and a level bar. Tapping the zone then works like pressing the knob.
- **Touch zone has its own action, text or image:** the zone keeps it, and the knob gets a one-line strip along its bottom edge. A tap stays with the zone. A background color alone doesn't count: the knob then fills the zone on that color.
- **Show this on Touch N** (Customize, on the knob) turns this off for a knob, leaving the zone to its own action.

Text, image and text style set on a knob are used there too. Actions that show a value (Volume Knob, Clock, Counter, System Info, and Stream Deck dial actions through `setFeedback`) draw it as a compact one-liner in the strip.

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
- `setTitle`, `setImage` (PNG, JPG and SVG), `setState`, settings and global settings work. Dial actions' `setFeedback` (title, value and indicator bar) is shown on the knob's touch zone.
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

- **Start DeckHost when I sign in**: Windows: the `HKCU\…\Run` key; Linux: `~/.config/autostart/deckhost.desktop`; macOS: a LaunchAgent (`~/Library/LaunchAgents/app.deckhost.DeckHost.plist`). No admin rights needed.
- **Start in the tray when started at sign-in**: the device works without the window.
- **Closing the window keeps DeckHost running in the tray**: use **Exit** in the tray menu to quit.

The settings window also shows DeckHost's version.

Only one DeckHost runs at a time: starting it again shows the running one. (Two instances would both read the device and run every action twice.)

### Folders and logs

`%AppData%` is `~/.config` on Linux and `~/Library/Application Support` on macOS.

| What | Where |
|---|---|
| Log files (NLog, one per day, kept 14 days) | `%AppData%/DeckHost/logs/deckhost-<date>.log` |
| Stream Deck plugin output | `%AppData%/DeckHost/logs/plugins` |
| DeckHost plugins | `%AppData%/DeckHost/plugins/functional/<id>` |
| Stream Deck plugins | `%AppData%/DeckHost/plugins/streamdeck/<uuid>.sdPlugin` |
| Profiles | `%AppData%/DeckHost/profiles.json` |

The Plugin Manager shows these paths and has **Plugins folder** and **Logs folder** buttons.

### Backgrounds, live preview and test mode

- **Backgrounds:** a profile can have a background color and an image or animated GIF that spans all keys (Profile Manager). Each control can have its own background color, and its image can be an animated GIF (Customize). GIFs play at about 10 frames per second. The profile's background color also fills the screen between the keys, as a temporary background that covers the device's idle screen while DeckHost runs; the idle screen comes back when DeckHost exits. A background *image* is drawn on the keys and zones only.
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
