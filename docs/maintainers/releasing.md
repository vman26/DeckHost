# Releasing and signing

## A release

Push a tag; the tag is the version (`v1.4.0`, or `v1.4.0-rc.1` for a pre-release). The **release** workflow tests, publishes DeckHost for Windows (x64), Linux (x64) and macOS (Apple Silicon), checks the native bridge in each package, packages them and creates the GitHub release with notes and `SHA256SUMS.txt`. Run the workflow by hand to get the packages as build artifacts without releasing.

Versions come from git (GitVersion, git-flow): `develop` builds alpha versions of the next minor, `release/x.y.z` builds release candidates, `hotfix/*` betas of the next patch.

## A private source repository

The updater reads releases without signing in, so they must be public. With a private source repository:

1. Create a public repository for releases, for example `vman26/DeckHost-releases`, with one commit (a README).
2. Create a fine-grained token: resource owner you, **only** that repository, **Contents: Read and write**, with an expiry date. Add it to the source repository as the Actions **secret** `RELEASES_TOKEN`.
3. Add the Actions **variable** `RELEASES_REPO` = `vman26/DeckHost-releases`.

Each release then goes to both repositories, and the apps check the releases repository. The **docs** workflow publishes this documentation to the releases repository's `gh-pages` branch: turn on **Settings → Pages → Deploy from a branch → gh-pages** there once. The documentation is then at `https://vman26.github.io/DeckHost-releases/`, which is where DeckHost's **Help** links.

## Code signing

Without certificates the packages are unsigned: Windows SmartScreen warns, and macOS asks to allow the app once. Add these secrets to sign them; nothing else changes.

### Windows

| Secret / variable | |
|---|---|
| `WINDOWS_CERTIFICATE` (secret) | The code-signing certificate as a `.pfx`, base64-encoded (`base64 -w0 cert.pfx`) |
| `WINDOWS_CERTIFICATE_PASSWORD` (secret) | Its password |
| `WINDOWS_TIMESTAMP_URL` (variable, optional) | Your CA's timestamp server (default DigiCert's) |

`scripts/sign-windows.ps1` signs `DeckHost.App.exe`, DeckHost's libraries and the native bridge before packaging, and the setup afterwards.

!!! tip "Getting a certificate"
    An OV or EV certificate from a CA (Sectigo, DigiCert, Certum, …) works when it can be exported as `.pfx`. Certificates issued since mid-2023 live on hardware tokens or in the CA's cloud; those need the CA's signing tool instead of a `.pfx` (adapt `sign-windows.ps1`). Open-source projects can apply to [SignPath Foundation](https://signpath.org) for free signing. SmartScreen trusts a new certificate after it has seen enough downloads.

### macOS

| Secret | |
|---|---|
| `MACOS_CERTIFICATE` | A **Developer ID Application** certificate with its key, as `.p12`, base64-encoded |
| `MACOS_CERTIFICATE_PASSWORD` | Its password |
| `MACOS_NOTARY_APPLE_ID` | The Apple ID of the developer account |
| `MACOS_NOTARY_TEAM_ID` | The team id |
| `MACOS_NOTARY_PASSWORD` | An app-specific password for that Apple ID |

With the certificate, `scripts/package.sh` signs every file for the hardened runtime (`packaging/macos/DeckHost.entitlements`: JIT, and loading device and plugin libraries). With the notary secrets too, it notarizes the app and staples the ticket, so it opens without any warning. A Developer ID needs the Apple Developer Program.

### Linux

Linux packages aren't signed; `SHA256SUMS.txt` lets users check a download.
