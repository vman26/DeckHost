# Download

DeckHost is free and open source (GPL-3.0). It runs on Windows, Linux and macOS.

<div class="deckhost-download" markdown>

[Download the latest release](https://github.com/vman26/DeckHost/releases){ .md-button .md-button--primary #deckhost-latest }

<span id="deckhost-release-name"></span>

</div>

| System | Package | |
|---|---|---|
| **Windows** 10 and 11 (x64) | Setup program, or a zip to run without installing | <a id="deckhost-win" href="https://github.com/vman26/DeckHost/releases">Download</a> |
| **Linux** (x64) | `.tar.gz`; unpack and run `DeckHost` ([device rule](devices.md#linux-the-device-rule)) | <a id="deckhost-linux" href="https://github.com/vman26/DeckHost/releases">Download</a> |
| **macOS** (Apple Silicon) | App in a zip | <a id="deckhost-mac" href="https://github.com/vman26/DeckHost/releases">Download</a> |

All releases, release notes and pre-releases are on the
[releases page](https://github.com/vman26/DeckHost/releases). Then see
[Getting started](getting-started.md). DeckHost tells you when an update is available
([Updates](updates.md)).

## Code signing

**Windows:** Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org). DeckHost uses the SignPath Foundation for code signing:
the Windows program and setup are signed by SignPath from the builds GitHub Actions makes of this
repository. Releases made before the signing was set up are unsigned; Windows then shows
*More info → Run anyway*.

**macOS:** signed and notarized with an Apple Developer ID when available; otherwise right-click
the app → **Open** the first time.

## Check a download

- Every release has `SHA256SUMS.txt` with the checksum of each file
  (`sha256sum -c SHA256SUMS.txt --ignore-missing`, or `Get-FileHash` on Windows).
- Every file has a build provenance attestation from GitHub Actions:
  `gh attestation verify <file> --repo vman26/DeckHost`.
- DeckHost's own updater checks both the checksum and the source before installing.

## Build it yourself

The source is on [GitHub](https://github.com/vman26/DeckHost); the README explains how to build it
with the .NET 10 SDK.
