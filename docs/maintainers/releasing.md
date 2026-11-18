# Releasing and signing

## A release

Push a tag; the tag is the version (`v1.4.0`, or `v1.4.0-rc.1` for a pre-release). The **release** workflow tests, publishes DeckHost for Windows (x64), Linux (x64) and macOS (Apple Silicon), checks the native bridge in each package, packages them and creates the GitHub release with notes and `SHA256SUMS.txt`. Run the workflow by hand to get the packages as build artifacts without releasing.

Versions come from git (GitVersion, git-flow): `develop` builds alpha versions of the next minor, `release/x.y.z` builds release candidates, `hotfix/*` betas of the next patch.

## Packages and updates (Velopack)

DeckHost installs and updates through [Velopack](https://velopack.io) (the `Velopack` package in `DeckHost.App.csproj` and the `vpk` tool in the workflow, the same version; update both together). `scripts/package-velopack.sh` makes, per system, the installer, a self-updating portable package and the **update feed**: `releases.<channel>.json`, `assets.<channel>.json`, `RELEASES*` and `DeckHost-<version>-<channel>-full.nupkg` / `-delta.nupkg` (channels `win`, `linux`, `osx`). The feed files keep vpk's names, because the index names the packages; the installers get DeckHost's names:

| System | Files |
|---|---|
| Windows | `DeckHost-<v>-win-x64-install.exe` (per user, no administrator rights), `DeckHost-<v>-win-x64.msi` (per user or all users, `--instLocation Either`), `DeckHost-<v>-win-x64.zip` (portable) |
| Linux | `DeckHost-<v>-linux-x86_64.AppImage` (updates itself, pkexec when it sits in a protected folder) and `DeckHost-<v>-linux-x64.tar.gz` (`scripts/package.sh`, install.sh; updated by hand) |
| macOS | `DeckHost-<v>-osx-arm64.zip` (DeckHost.app), and `DeckHost-<v>-osx-arm64.pkg` when the `.p12` also holds a Developer ID Installer identity |

Before packing, the script downloads the channel's previous release (`vpk download github`), so every release after the first also has a small delta package. The app reads the feed from the GitHub releases of the repository the build names (`GithubSource`, pre-releases when the user includes them); `DECKHOST_UPDATE_FEED=<folder or URL>` points a DeckHost at a local feed for testing (`vpk pack -o <folder>` twice, start the older one, Install and restart). The runner needs `squashfs-tools` and `zstd` for the AppImage and the deltas.

**The older Windows setup** (`packaging/windows/DeckHost.iss`, `*-win-x64-setup.exe`) is still built so that DeckHost versions from before Velopack, whose updater runs that file, reach the first Velopack version. That version then offers **Switch to the new installer** (`UpdateMode.MigrateWindows`): it installs the Velopack setup (or the MSI for an install in Program Files), removes the old install with its uninstaller after exiting and starts the new one, carrying start at sign-in over through `%AppData%\DeckHost\migration.json`. Stop building the Inno setup (and remove the step from `release.yml`) a few releases later, once nobody runs a pre-Velopack DeckHost. macOS and AppImage users move by themselves: the old updater swaps in the zip's app, which is now Velopack's, and AppImage users replace the file as before.

## Performance

The **performance** workflow (`.github/workflows/performance.yml`) measures every pull request and every tag on Windows, Linux and macOS with `benchmarks/DeckHost.Benchmark` (what it measures: [Performance](../performance.md)). A pull request gets a check per system, which fails when a number is over `benchmarks/budgets.json`, and one comment with every number next to the last release's. A tag's results go to the **`perf-data`** branch (`releases/<tag>/<system>.json` and `index.json`, written by `scripts/performance-page.py index`); the workflow then starts the docs workflow, which turns them into the numbers on the Performance page (`scripts/performance-page.py page` → `benchmarks/performance-data.md`, included by `docs/performance.md`).

Raising a budget is a change to `benchmarks/budgets.json` in its own reviewed pull request that says why. A number that is over budget on one system only can get its own limit there (`"windows": {…}`) instead of loosening it everywhere.

## Release notes

The notes come from [`CHANGELOG.md`](https://github.com/vman26/DeckHost/blob/develop/CHANGELOG.md),
written for users ([Keep a Changelog](https://keepachangelog.com/en/1.1.0/)): every pull request that
changes something people notice adds a line under `## [Unreleased]`, in Added, Changed, Deprecated,
Removed, Fixed or Security. CI checks the format (`python3 scripts/release-notes.py check`).

Before tagging:

- **A release** (`v1.4.0`): on the release branch, rename `## [Unreleased]` to
  `## [1.4.0] - 2026-12-01`, add a new empty `## [Unreleased]` above it and update the links at the
  bottom.
- **A release candidate** (`v1.4.0-rc.1`) uses the `[1.4.0]` section when there is one, else
  `[Unreleased]`. A section of its own (`## [1.4.0-rc.1]`) wins when you want different notes.

The release workflow checks this before it builds anything (a stable tag without its section, or
an empty section, stops the release), then publishes `scripts/release-notes.py compose <tag>`:

1. the changelog section,
2. the download table,
3. how to check the files (checksums, build provenance), and
4. the pull requests since the previous release, folded and grouped by label (`.github/release.yml`;
   label a pull request `skip-changelog` to leave it out).

DeckHost's update screen shows the notes up to the `<!-- deckhost:end-of-notes -->` marker after
the download table, so people see what changed, not the checks for maintainers. Try it locally:
`python3 scripts/release-notes.py compose v1.4.0`.

## Where releases, docs and reports go

Everything happens in this repository: releases, the in-app update check (it reads the public
releases without signing in), the documentation (GitHub Pages, **Settings → Pages → Source: GitHub
Actions**) and problem reports.

DeckHost's **Report a problem** (and the offer after a crash) opens a new issue here, filled in by
the app. The user reads it and sends it themselves; nothing is sent automatically. A fork can point
its builds at its own releases with the build property `DeckHostUpdateRepository` (the release
workflow sets it to the repository it runs in).

## The NuGet package

Each release also packs the plugin API (`DeckHost.Contracts`) and the plugin template
(`DeckHost.Templates`) as NuGet packages, attached to the release. They go to nuget.org with
**Trusted Publishing**: no API key is stored; the release workflow trades its GitHub sign-in (OIDC)
for a key that lasts only that run. Set it up once:

1. On nuget.org: your profile → **Trusted Publishing** → add a policy for GitHub Actions with
   repository owner `vman26`, repository `DeckHost` and workflow file `release.yml` (no environment).
2. In this repository: Settings → Secrets and variables → Actions → **Variables** → `NUGET_USER` =
   your nuget.org username (a variable, not a secret: it's not sensitive).

The next tagged release pushes both packages (a version that's already there is skipped). Without
`NUGET_USER` the packages are only attached to the release. Plugin authors then reference
`DeckHost.Contracts` (`ExcludeAssets="runtime"`, DeckHost supplies it) and install the template with
`dotnet new install DeckHost.Templates`.

## Code signing

Without signing the packages still work: Windows SmartScreen warns, and macOS asks to allow the app once. Add secrets to sign them; nothing else changes. A trusted certificate always means a check of who you are by a certificate authority, so no script can get one by itself. What can be automated is everything around it, and that's what's here.

### What to choose

| | Cost | Who | Automation |
|---|---|---|---|
| **Azure Artifact Signing** (Windows) | about 10 USD/month | organizations in the US, Canada, EU, UK and several other countries; individual developers only in the US and Canada | `scripts/setup-azure-signing.sh` sets it up; Microsoft renews the certificates, nothing expires on you except the app secret (2 years) |
| A **code-signing certificate** from a CA (Windows) | 150–400 USD/year | anyone, also individuals in Europe (Certum, Sectigo, SSL.com, …) | the `.pfx` secrets below; cloud-only certificates need the CA's tool |
| **SignPath Foundation** (Windows) | free | open-source projects with a public source repository, like DeckHost | their GitHub action, added to the release workflow after they approve the project ([checklist](going-public.md#release-and-announce)) |
| **Apple Developer ID** (macOS) | 99 USD/year | anyone with an Apple Developer account | the macOS secrets below; signing and notarizing are automatic |
| **Build provenance** (all files) | free | public source repositories | on by itself: the release job attests every file (`gh attestation verify <file> --repo <owner/repo>`) |

For DeckHost: SignPath Foundation for Windows (free for open source) and the Apple Developer Program for macOS.

### Windows: Azure Artifact Signing

1. Get an Azure subscription and install the [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli); sign in with `az login` (and `gh auth login`, to store the results in the repository).
2. Run `scripts/setup-azure-signing.sh vman26/DeckHost`. It creates the resource group and the signing account and tells you what to do in the portal.
3. In the portal, request a **public identity validation** on the signing account. Microsoft checks it by hand; this can take a few days.
4. When it is **Completed**, run `scripts/setup-azure-signing.sh vman26/DeckHost <validation id>`. It creates the certificate profile and an app that may sign with it (and nothing else), and stores these in the repository:

| Variable / secret | |
|---|---|
| `AZURE_SIGNING_ENDPOINT` (variable) | The account's endpoint, e.g. `https://weu.codesigning.azure.net/` |
| `AZURE_SIGNING_ACCOUNT` (variable) | The signing account |
| `AZURE_SIGNING_PROFILE` (variable) | The certificate profile |
| `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` (secrets) | The signing app |

The release workflow then signs DeckHost's programs, libraries and the setup with `azure/artifact-signing-action`. The app's secret lasts two years; run the script again (step 4) to replace it. Settings: `RESOURCE_GROUP`, `LOCATION`, `ACCOUNT`, `PROFILE` environment variables.

!!! note
    The script and the signing steps haven't been run against a real Azure account yet. If the Azure CLI's `trustedsigning` commands have been renamed, create the account and profile in the portal and set the variables and secrets above by hand.

### Windows: a certificate file

| Secret / variable | |
|---|---|
| `WINDOWS_CERTIFICATE` (secret) | The code-signing certificate as a `.pfx`, base64-encoded (`base64 -w0 cert.pfx`) |
| `WINDOWS_CERTIFICATE_PASSWORD` (secret) | Its password |
| `WINDOWS_TIMESTAMP_URL` (variable, optional) | Your CA's timestamp server (default DigiCert's) |

`scripts/sign-windows.ps1` signs `DeckHost.App.exe`, DeckHost's libraries and the native bridge before packaging, the older setup afterwards, and (as vpk's `--signTemplate`) Velopack's `Setup.exe`, `Update.exe` and the MSI. With Azure Artifact Signing vpk signs those itself (`--azureTrustedSignFile`, the same variables and secrets). Certificates issued since mid-2023 live on hardware tokens or in the CA's cloud; those need the CA's signing tool instead of a `.pfx` (adapt `sign-windows.ps1`). SmartScreen trusts a new certificate after it has seen enough downloads.

### macOS

| Secret | |
|---|---|
| `MACOS_CERTIFICATE` | A **Developer ID Application** certificate with its key, as `.p12`, base64-encoded |
| `MACOS_CERTIFICATE_PASSWORD` | Its password |
| `MACOS_NOTARY_APPLE_ID` | The Apple ID of the developer account |
| `MACOS_NOTARY_TEAM_ID` | The team id |
| `MACOS_NOTARY_PASSWORD` | An app-specific password for that Apple ID |

With the certificate, vpk signs the app for the hardened runtime (`packaging/macos/DeckHost.entitlements`: JIT, and loading device and plugin libraries) after adding Velopack's updater to it; `scripts/package.sh` only assembles the bundle. With the notary secrets too, the workflow stores a `notarytool` profile in the signing keychain and vpk notarizes and staples, so the app opens without any warning. A **Developer ID Installer** identity in the same `.p12` also signs the `.pkg`; without one only the zip is made. A Developer ID needs the Apple Developer Program.

### Linux

Linux packages aren't signed; `SHA256SUMS.txt` lets users check a download.

The AppImage is Velopack's (vpk builds it from the published folder). The job runs `--check-native` from the finished file, so a bridge that doesn't load inside the image fails the release. The glibc floor is the Ubuntu 22.04 runner's. The tar.gz is installed and updated by hand (`install.sh`); DeckHost downloads the new one for the user. Flatpak isn't built (it needs offline NuGet sources, OpenCV as its own module and holes in the sandbox for the host tools).
