# Changelog

What changed in each DeckHost release, written for the people who use it. The newest release is at
the top. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions
follow [Semantic Versioning](https://semver.org/).

Add a line under **Unreleased** in the same pull request as the change, in the group that fits
(Added, Changed, Deprecated, Removed, Fixed, Security). The release takes its notes from here; see
[Releasing](https://github.com/vman26/DeckHost/blob/develop/docs/maintainers/releasing.md#release-notes).

## [Unreleased]

### Added

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

### Changed

- DeckHost is one public repository under GPL-3.0-or-later; issues, releases and source are in the
  same place.
- Devices describe themselves in more detail (identity, displays, capabilities, limits), the basis
  for supporting more hardware.

### Fixed

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
