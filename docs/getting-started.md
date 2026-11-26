# Getting started

## Install

Download the package for your system from the [Download](download.md) page (the latest release on GitHub). `SHA256SUMS.txt` lists the checksums.

| System | Package | Install |
|---|---|---|
| Windows 10/11 (x64) | `DeckHost-<version>-win-x64-install.exe` | Run it. It installs for your user, without administrator rights, with a Start menu entry and an uninstaller, and starts DeckHost. For every user of the computer, use the `.msi` instead. Another folder: see [Install in another folder](download.md#install-in-another-folder). |
| Linux (x64) | `DeckHost-<version>-linux-x64.tar.gz` | Unpack and run `./install.sh`. It installs to `~/.local/share/deckhost` with a menu entry, and (with sudo, once) the device rule that lets DeckHost use the N4 Pro and Stream Decks without root. `./install.sh --uninstall` removes it. |
| Linux (x64), no install | `DeckHost-<version>-linux-x86_64.AppImage` | `chmod +x` it and run it; it updates itself. The device rule has to be installed once by hand: see [the AppImage notes](devices.md#linux-appimage). |
| macOS 12+ (Apple Silicon) | `DeckHost-<version>-osx-arm64.zip` | Unzip and move **DeckHost.app** to Applications. |

!!! warning "The first start: DeckHost isn't code-signed yet"
    Your system asks once before it opens DeckHost ([why, and how to check the file](download.md#first-start-and-signing)):

    - **Windows** says "Windows protected your PC": choose **More info → Run anyway**.
    - **macOS 14 and older:** right-click DeckHost.app → **Open**. **macOS 15 and newer:** open it once, then **System Settings → Privacy & Security → Open Anyway**. Or run `xattr -dr com.apple.quarantine /Applications/DeckHost.app`.

    Your antivirus program blocks it? See [If your antivirus blocks DeckHost](troubleshooting.md#if-your-antivirus-blocks-deckhost).

## The welcome tour

The first start opens a short tour:

1. **Your devices**: what DeckHost found, updated as you plug devices in. If yours isn't there, it says why when it can (on Linux a missing device rule, with the commands to copy; elsewhere the Elgato or MiraBox app holding the device). No device? Open a deck on this screen, use your phone, or try the device simulator.
2. **A starting point**: pick a starter (Media, Streaming or Work), see a preview, and put it on your plugged-in deck. Controls that already have an action keep it. Coming from the Stream Deck app? Import a `.streamDeckProfile` here; DeckHost lists the plugins it needs that you don't have, with **Install**.
3. **Preferences**: start with your sign-in, keep running in the tray when you close the window, check for updates, the look, and offer to report crashes.
4. **How it works**: four short tips and a link to this documentation.

After the tour three tips point at the action list, the device and the pickers in the header. Show them again from **☰ → Show tips**.

Open it again from **☰ → Welcome tour**.

## Your first key

1. Find an action in the list on the right (search, or open a plugin's group).
2. Drag it onto a key. The device shows it right away.
3. Click the key to set it up below the device: the action's settings, the text, icon and background, and the light.
4. Double-click the key to try it.

Drag an action off a key to remove it, or right-click → **Remove assignment**.
