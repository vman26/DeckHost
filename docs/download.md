# Download

DeckHost is free and open source (GPL-3.0). It runs on Windows, Linux and macOS.

<div class="deckhost-download" markdown>

[Download the latest release](https://github.com/vman26/DeckHost/releases){ .md-button .md-button--primary #deckhost-latest }

<span id="deckhost-release-name"></span>

</div>

| System | Package | |
|---|---|---|
| **Windows** 10 and 11 (x64) | Installer (`-install.exe`, for you, no administrator rights); for all users the `.msi`; or a zip to run without installing | <a id="deckhost-win" href="https://github.com/vman26/DeckHost/releases">Download</a> |
| **Linux** (x64), no install | `.AppImage`; make it executable and run it; it updates itself ([notes](devices.md#linux-appimage)) | <a id="deckhost-appimage" href="https://github.com/vman26/DeckHost/releases">Download</a> |
| **Linux** (x64) | `.tar.gz`; unpack and run `install.sh` ([device rule](devices.md#linux-the-device-rule)); updated by hand | <a id="deckhost-linux" href="https://github.com/vman26/DeckHost/releases">Download</a> |
| **macOS** (Apple Silicon) | App in a zip | <a id="deckhost-mac" href="https://github.com/vman26/DeckHost/releases">Download</a> |

All releases, release notes and pre-releases are on the
[releases page](https://github.com/vman26/DeckHost/releases). Then see
[Getting started](getting-started.md). DeckHost tells you when an update is available
([Updates](updates.md)).

## Install in another folder

Neither installer asks for a folder. The installer puts DeckHost in `%LocalAppData%\DeckHost`. The MSI's
window only asks whether DeckHost is for you or for everyone, then uses `%LocalAppData%\DeckHost` or
`Program Files`. To choose the folder yourself, start them from a command prompt or PowerShell in your
Downloads folder:

- Just for you: `.\DeckHost-<version>-win-x64-install.exe --installto "D:\Apps\DeckHost"`
- For everyone (asks for administrator rights): `msiexec /i DeckHost-<version>-win-x64.msi VELOPACK_INSTALLDIR="D:\Apps\DeckHost"`

Updates go to the same folder.

## First start and signing

DeckHost's packages aren't code-signed yet. A code-signing certificate has to be bought or sponsored,
and DeckHost doesn't have one for now. Because of that your system asks once before it opens
DeckHost. That is all it means: every package is built by GitHub Actions from the public source, and
you can check that the file you have is the one that was built (see [Check a download](#check-a-download)).
Updates that DeckHost installs itself don't ask again.

**Windows**

1. Run the installer. Windows says **"Windows protected your PC"**.
2. Choose **More info**, check that the file name starts with `DeckHost-`, and choose **Run anyway**.

Using the zip? Before you unpack it, right-click it → **Properties** → tick **Unblock** → **OK**.
Otherwise Windows asks for every program inside it.

**macOS**

- macOS 14 and older: right-click (or Control-click) DeckHost.app → **Open**, then **Open** again.
- macOS 15 and newer: open DeckHost.app once and close the message. Then go to **System Settings →
  Privacy & Security**, scroll down to *"DeckHost" was blocked…* and choose **Open Anyway**.
- Or in Terminal, on any version: `xattr -dr com.apple.quarantine /Applications/DeckHost.app`

**Linux:** nothing to allow. Make the AppImage executable (`chmod +x`) and run it.

Your antivirus program blocks or removes DeckHost? See
[If your antivirus blocks DeckHost](troubleshooting.md#if-your-antivirus-blocks-deckhost).

## Check a download

- **Checksum:** every release has `SHA256SUMS.txt` with the checksum of each file. On Linux and
  macOS: `sha256sum -c SHA256SUMS.txt --ignore-missing`. On Windows, in PowerShell:
  `Get-FileHash .\DeckHost-<version>-win-x64-install.exe`, then compare the hash with the file's line
  in `SHA256SUMS.txt`.
- **Build provenance:** every file has an attestation from GitHub Actions. It proves the file was
  built by this repository's release workflow from the tagged commit and hasn't changed since. With
  the [GitHub CLI](https://cli.github.com): `gh attestation verify <file> --repo vman26/DeckHost`.
  (It isn't a code signature, so Windows and macOS still ask once.)
- **VirusTotal:** the release notes link a VirusTotal report for each Windows download, made when the
  release was built.
- DeckHost's own updater (Velopack) checks each update package against the checksum in the release's update index before installing it.

## Build it yourself

The source is on [GitHub](https://github.com/vman26/DeckHost); the README explains how to build it
with the .NET 10 SDK.
