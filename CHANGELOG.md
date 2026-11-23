# Changelog

What changed in each DeckHost release, written for the people who use it. The newest release is at
the top. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions
follow [Semantic Versioning](https://semver.org/).

Add a line under **Unreleased** in the same pull request as the change, in the group that fits
(Added, Changed, Deprecated, Removed, Fixed, Security). The release takes its notes from here; see
[Releasing](https://github.com/vman26/DeckHost/blob/develop/docs/maintainers/releasing.md#release-notes).

## [Unreleased]

### Added

- Updates install through a new installer (Velopack): **Install and restart** shows the download from 0 to 100 %,
  then DeckHost restarts into the new version, which is put in place as a whole, never file by file. Updates are
  small (only what changed), the AppImage and the Windows zip update themselves too, and DeckHost installed for all
  users asks for administrator rights when it updates. Windows now has a per-user installer (`-install.exe`) and an
  MSI for all users; a DeckHost from the older setup offers **Switch to the new installer** once.

- Backgrounds: gradients can have more than two colors (add, reorder, set where each starts) and there are radial
  gradients; the CPU glow preset is visible at ordinary load (full at 60 %) and the CPU value is read right away.

- A **Layout** button next to Back (and Ctrl+Home) closes every open page and returns to the device, instead of
  clicking Back page after page.
- A **Variables** page (☰): DeckHost's own variables (CPU, memory, the app in front) and the ones your workflows set,
  with scope, type and live value; create, change and remove your own.
- Variable names are picked from a list (or typed) in workflow nodes, Show Variable and the background that follows a
  variable.

- USB device creator: controls snap to each other's edges, centers and even spacing while you drag (hold Alt to
  place freely), Ctrl or Shift and a click select several to line up or spread evenly, and **Arrange** keeps every
  key and knob on its own number. Right-click the device in the editor for **Add a USB device**.

- USB device creator: right-click the canvas (Add key, Add knob, Arrange, Select all) or a control (Learn push,
  Remove), and Shift and drag over the empty canvas selects what the rectangle touches.
- The action list dims the actions the selected control can't use (hover for why), and **Only actions that fit the
  selected control** hides them.

- A **Devices** page (☰): every device DeckHost knows, plugged in or not, with **Remove**; new devices can wait
  under **Found** (Add or Keep ignoring) instead of being added by themselves, and a removed device that is plugged
  in again is offered again.

- The Variables page publishes CPU and memory while it is open (they used to show nothing unless a background followed
  them), once a second and in percent, in step with the System Info action.
- When the built-in browser can't start, the plugin settings page tries again by itself (up to three times) before it says why and
  offers **Try the built-in browser again**; a view that is only torn down while it starts no longer turns the browser off; a "failed" mark left by an older DeckHost that was only a view torn down while starting is forgotten.

- Plugin store: open-source Elgato plugins can come from a checksummed copy kept in DeckHost's plugin
  repository instead of the community archive (the copying itself stays off for now).
- Linux: an AppImage next to the tar.gz, one file that runs without installing. DeckHost doesn't replace it
  by itself: the update screen offers the new file, and **Start with the system** starts the file you ran.
- Plugin updates: DeckHost checks the store for newer versions of your installed plugins, and you
  can update one, all, or let them update by themselves (Plugins page).
- DeckHost can install its own updates by itself when it closes (Settings → Updates).
- A documentation page for every plugin, with its links, actions and README: from the Plugins page
  or by right-clicking a control, an action or a plugin group.
- Install a plugin straight from a GitHub repository address (Store → Install from GitHub).
- Links from the store to the Elgato Marketplace, MiraBox Space and OpenAction.
- MiraBox StreamDock plugins run: their knob actions go on knobs, and older plugins that expect
  their start-up arguments by position get them.
- The Plugins page shows device plugins and action plugins apart, each plugin with its picture,
  and each device with its manufacturer, model, connection and what it can do.
- A Download page on the documentation site that always points at the newest release.
- For plugin authors: the plugin API as the `DeckHost.Contracts` NuGet package and a
  `dotnet new deckhost-plugin` template.
- Every StreamDock model in MiraBox's SDK, as a preview: N4, 293, 293s, M18, XL, M3, N3, N1,
  K1 Pro and Mini, with their keys, buttons, knobs, side switches and small displays. A model
  you've plugged in once stays in the device picker.
- **Forget this device** in Device settings removes an unplugged MiraBox or Stream Deck from the
  device picker (also the N4 Pro shown before anything is plugged in); its profiles are kept.
- A device simulator (a developer option in DeckHost settings, then ☰ → Device simulator): a window that acts like any device DeckHost supports,
  with its keys, knobs, touch strips, images, LED colors and vibration, so you can set up and try a
  device without having it.

- Devices from OpenAction device plugins (the Mirabox, Ajazz and other plugins made for OpenDeck):
  install the plugin like any other and the devices it finds appear in the device picker, with
  their keys and knobs, images, brightness and input.

- Workflow editor: drop a node onto a link to put it in between, right-click nodes and links for a
  menu, the actions are grouped by plugin, a Wait fills up while it waits, Repeat can run a number
  of times from a variable or while/until a variable has a value (with a pause between rounds), and
  a workflow can be named, exported to a `.deckhostworkflow` file and imported.

- Workflows can start by themselves: when the profile comes onto the device, when an app comes to
  the front, when a variable changes (optionally to a given value) and every so many seconds.
- Workflow action nodes have an **On error** output for when the action did not run, and can try
  again (up to 5 times, with a wait) before going on.
- Dropping a node into a link in the workflow editor moves the nodes after it to the right to
  make room.
- Workflow variables can belong to one profile or one control (**Shared by** on Set variable, If,
  Repeat and the variable trigger), not only to every workflow.
- Workflow expressions: **Calculate** in Set variable and an **An expression** check in If, with
  variables in brackets, text, number and time functions, and a **Store as** type (number, true /
  false or text) that refuses a value that doesn't fit.
- The Web Deck can be served over HTTPS (a certificate DeckHost makes for the computer, with its
  fingerprint shown in Device settings) and can be installed as an app where the browser allows
  it.
- Background layers (Profiles page): gradients across the whole layout, images and solid colors
  with an opacity, drawn over the profile's color and image, and layers that follow the CPU load,
  the memory load or a workflow's variable.
- A **Switch** workflow node: any number of cases, each with its own output, and Else; cases keep
  their links when you move or rename them.
- Shared workflows: save a named workflow once and run it from others with a **Call workflow**
  node; profile exports take the shared workflows they call along.
- Debugging workflows: breakpoints (F9), **Continue** and **Step** for a run that is held in front
  of a node.
- The workflow editor lists the variables that are set right now and can set one, to see why a
  workflow went the way it did.

### Changed

- A Stream Deck plugin's log no longer starts empty when DeckHost restarts the plugin after a crash: the run
  before is kept as `<plugin id>.previous.log`, and each run starts with a line saying when it started.

- The download page explains how to install DeckHost in a folder of your choice: the MSI's window only asks
  "for me or for everyone", so the folder is given on the command line (`VELOPACK_INSTALLDIR=` for the MSI,
  `--installto` for the installer).

- The download page no longer says the Windows packages are signed: they aren't yet. The download page, Getting
  started and the release notes now explain what Windows and macOS (including macOS 15's **Open Anyway**) show the
  first time, and how to check a download. Troubleshooting has a new section for when an antivirus program blocks
  DeckHost: how to check the file, restore it, allow DeckHost's folders and report the false positive. Each release's
  notes link a VirusTotal report for every Windows download.

- Moving from the older Windows setup no longer starts a hidden PowerShell window to remove the old install (a
  pattern antivirus programs distrust): the new DeckHost removes it itself before it starts.

- DeckHost uses much less CPU while it waits: lights and live keys only wake it when something has to change
  (before, it woke up about 40 times a second all day), and the app is precompiled, so it starts faster and doesn't
  spend its first minutes compiling. Every pull request and release is now measured on Windows, Linux and macOS
  ([Performance](https://vman26.github.io/DeckHost/performance/)).

- Backgrounds: the default background has its own section in the Profile Manager, and a profile only shows its background
  settings after you tick **Use a background of its own**; backups moved to their own section too.

- Workflows: debugging only works while a workflow editor is open (a held run goes on when the last editor closes);
  deleting a node links what led into it to what it led to (and asks when it has several branches); Arrange keeps
  a Switch's branches in the order of its cases; a Call workflow node can create a new shared workflow; new workflows
  start over when triggered again while running, and runs in parallel each show their own wait.

- DeckHost is one public repository under GPL-3.0-or-later; issues, releases and source are in the
  same place.
- Devices describe themselves in more detail (identity, displays, capabilities, limits), the basis
  for supporting more hardware.

### Fixed

- `DeckHost --check-plugin` checks Stream Deck plugins too (their `manifest.json`, actions and program) instead of
  failing with "No plugin.json at the root of the package"; it reads them but doesn't start them.

- Switching controls while a plugin's settings page was still loading no longer shows an "unexpected error" notice or
  writes a crash report (Windows: the web view was closed while it started, `E_ABORT`).
- macOS and Linux: DeckHost no longer closes at start when its temporary folder has a long path (the single-instance
  pipe's path was too long for the system).
- macOS: DeckHost no longer closes at random a few minutes after it starts. The StreamDock device support set up
  macOS's USB device watcher on a thread that could end, and the next device check then crashed.
- macOS: DeckHost keeps App Nap from slowing it down while it runs in the background or in the menu bar, so keys and
  knobs keep answering at once.
- DeckHost no longer closes when the built-in browser (the plugin settings page) can't start, for example without
  the Microsoft Edge WebView2 runtime: unexpected errors in the window are recovered from with a crash report and a
  notice, and the browser stays off on that computer until the runtime is updated.
- After repeated crashes at start DeckHost comes up in **safe mode** (no browser, no extra plugins, no open editor)
  with a button to restart normally, instead of crashing every time.

- Windows no longer reports DeckHost (or one of its plugins) as preventing shutdown: with DeckHost
  running in the tray its window refused every close, also the one Windows sends when shutting
  down. It now only stays when you close the window yourself, and stops its plugin processes
  together instead of one after another.
- Right-clicking a control offers **Try it** (runs its action) instead of a Customize entry that
  did nothing, since clicking a control already opens its editor.
- The Plugins page says what each device plugin supports (the Elgato one said "Supports .").
- The plugin store's search no longer freezes while you type: it waits for you to pause, shows a
  page of results at a time (**Show more** adds the next) and matches every word you type.
- Add a USB device: a key you pressed to look at what the device sends could not be added with
  **Add key** afterwards (it counted as noise, and nothing said so). It can now, and when you press
  a key right before clicking Add key the page tells you to wait a moment.
- The N3's knobs are on the right of its keys, and the editor draws a device the way the simulator
  does (a body, the screens in the profile's color, side wheels on the edge) instead of a blue box.
- Selecting a key after another one could turn its tile off or change its Run when setting, and
  merely selecting a control added an undo step.
- The text you set in Customize → Text now shows on every action, including plugins and readouts
  such as Show Variable.
- Stream Deck plugins that are web pages (such as an analog clock) no longer freeze while DeckHost's
  window is in the background (Windows).
- Outside Windows, such plugins now say why they don't run instead of staying blank.

## [0.1.0-rc.1] - 2026-11-15

The first public pre-release.

### Added

- Backgrounds: gradients can have more than two colors (add, reorder, set where each starts) and there are radial
  gradients; the CPU glow preset is visible at ordinary load (full at 60 %) and the CPU value is read right away.

- Devices: the MiraBox StreamDock N4 Pro, Elgato Stream Decks (preview, no Elgato software needed),
  generic USB HID devices you describe yourself, and the Web Deck on a phone, tablet or browser.
- An editor like the Stream Deck app: drag actions onto keys, knobs and touch zones, customize text,
  icons, colors and tiles, with undo and redo.
- Profiles, pages and folders, switching automatically with the app in front, with backups, export
  and import (including `.streamDeckProfile` files).
- Multi Actions, Key Logic (press, double press, hold) and a visual workflow editor.
- Knob lights with effects, per device or per knob.
- Plugins: DeckHost plugins (each in its own process), Elgato Stream Deck and OpenAction plugins,
  a plugin store, and icon packs with Iconify search.
- Updates from GitHub releases, verified against their checksums.
- Windows, Linux and macOS (Apple silicon) packages.

[Unreleased]: https://github.com/vman26/DeckHost/compare/v0.1.0-rc.1...HEAD
[0.1.0-rc.1]: https://github.com/vman26/DeckHost/releases/tag/v0.1.0-rc.1
