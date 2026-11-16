# Getting started

## Install

Download the package for your system from the [Download](download.md) page (the latest release on GitHub). `SHA256SUMS.txt` lists the checksums.

| System | Package | Install |
|---|---|---|
| Windows 10/11 (x64) | `DeckHost-<version>-win-x64-setup.exe` | Run it. It installs for your user, without administrator rights, with a Start menu entry and an uninstaller. |
| Linux (x64) | `DeckHost-<version>-linux-x64.tar.gz` | Unpack and run `./install.sh`. It installs to `~/.local/share/deckhost` with a menu entry, and (with sudo, once) the device rule that lets DeckHost use the N4 Pro and Stream Decks without root. `./install.sh --uninstall` removes it. |
| macOS 12+ (Apple Silicon) | `DeckHost-<version>-osx-arm64.zip` | Unzip and move **DeckHost.app** to Applications. |

!!! warning "First start of an unsigned build"
    Until DeckHost's packages are signed, Windows SmartScreen says "Windows protected your PC": choose **More info → Run anyway**. On macOS, right-click DeckHost.app → **Open** the first time, or run `xattr -dr com.apple.quarantine /Applications/DeckHost.app`.

## The welcome tour

The first start opens a short tour:

1. **Your devices**: what DeckHost found, and what to do if your device isn't there.
2. **A starting point**: put everyday actions on the empty keys (media keys, volume, clock, system info, lock, switch profile; the volume on the first knob and track skipping on the touch strip), or start empty.
3. **Preferences**: start with your sign-in, check for updates, and offer to report crashes.
4. **How it works**: the editor in five lines.

Open it again from **☰ → Welcome tour**.

## Your first key

1. Find an action in the list on the right (search, or open a plugin's group).
2. Drag it onto a key. The device shows it right away.
3. Click the key to set it up below the device: the action's settings, the text, icon and background, and the light.
4. Double-click the key to try it.

Drag an action off a key to remove it, or right-click → **Remove assignment**.
