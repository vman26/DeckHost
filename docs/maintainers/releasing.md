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

## Problem reports

DeckHost's **Report a problem** (and the offer after a crash) opens a new issue, filled in by the app, in the repository it checks for updates: the public releases repository when `RELEASES_REPO` is set, else this one. The user reads it and sends it themselves; nothing is sent automatically.

With a private source repository, the **sync issues** workflow links the two (every hour, and right away on comments here):

- every new public issue gets a copy here, labeled `public-report`, with a link back; the public one gets the label `tracked`;
- comments on the public issue are copied to the copy;
- a comment here that starts with `/public` is posted to the public issue (without the `/public`), so you answer from here;
- closing the copy as **completed** thanks the reporter and closes the public issue; when the reporter closes it, the copy says so.

Nothing else from this repository reaches the public one. Setup, once:

1. Turn on **Issues** in the releases repository (Settings → General → Features).
2. Give the `RELEASES_TOKEN` token **Issues: Read and write** on the releases repository, next to Contents.
3. Copy `packaging/releases-repo/` (README and `.github/ISSUE_TEMPLATE/`) into the releases repository: issue forms for problems and ideas, with a link to the documentation.
4. Run **sync issues** by hand once (Actions → sync issues → Run workflow, with **dry run** to see what it would do).

## Code signing

Without signing the packages still work: Windows SmartScreen warns, and macOS asks to allow the app once. Add secrets to sign them; nothing else changes. A trusted certificate always means a check of who you are by a certificate authority, so no script can get one by itself. What can be automated is everything around it, and that's what's here.

### What to choose

| | Cost | Who | Automation |
|---|---|---|---|
| **Azure Artifact Signing** (Windows) | about 10 USD/month | organizations in the US, Canada, EU, UK and several other countries; individual developers only in the US and Canada | `scripts/setup-azure-signing.sh` sets it up; Microsoft renews the certificates, nothing expires on you except the app secret (2 years) |
| A **code-signing certificate** from a CA (Windows) | 150–400 USD/year | anyone, also individuals in Europe (Certum, Sectigo, SSL.com, …) | the `.pfx` secrets below; cloud-only certificates need the CA's tool |
| **SignPath Foundation** (Windows) | free | open-source projects with a public source repository | their GitHub action; not for a private source repository |
| **Apple Developer ID** (macOS) | 99 USD/year | anyone with an Apple Developer account | the macOS secrets below; signing and notarizing are automatic |
| **Build provenance** (all files) | free | public source repositories (private ones need GitHub Enterprise Cloud) | on by itself: the release job attests every file (`gh attestation verify <file> --repo <owner/repo>`) |

For a person in Europe with a private source repository: a CA certificate for Windows (or Artifact Signing through a registered business) and the Apple Developer Program for macOS.

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

`scripts/sign-windows.ps1` signs `DeckHost.App.exe`, DeckHost's libraries and the native bridge before packaging, and the setup afterwards. Certificates issued since mid-2023 live on hardware tokens or in the CA's cloud; those need the CA's signing tool instead of a `.pfx` (adapt `sign-windows.ps1`). SmartScreen trusts a new certificate after it has seen enough downloads.

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
