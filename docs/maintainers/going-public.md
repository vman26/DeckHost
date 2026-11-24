# Public launch checklist

DeckHost's repository is public, with everything in one repository under GPL-3.0. What's left by hand
before announcing it:

## Repository settings

- [ ] **Settings → Actions → General → Fork pull request workflows:** *Require approval for
      first-time contributors*.
- [ ] **Settings → Code security:** turn on *Private vulnerability reporting* (SECURITY.md links to
      it), *Dependabot alerts* and *Secret scanning*.
- [ ] **Settings → Rules:** a ruleset for `main` requiring a pull request and the **build** workflow.
- [ ] **Settings → Pages → Source: GitHub Actions**, then run the **docs** workflow (or push to `main`).
      The docs appear at `https://vman26.github.io/DeckHost/`, which is where the app's Help links.
- [ ] **Settings → General → Features:** Issues on; Discussions on if you want a place for questions
      that aren't bugs.
- [x] **Sponsor button:** `.github/FUNDING.yml` (GitHub Sponsors and Buy me a coffee).
- [x] **The plugin catalog:** `vman26/deckhost-plugins` ([guide](plugin-store.md)).
- [ ] Look at the repository logged out (or in a private window) and read the README as a stranger.

## Release and announce

- [ ] **First release:** tag `v0.1.0` (or whatever you want the first public version to be) on `main`.
      The release workflow builds Windows, Linux and macOS, checks each, and publishes them with
      checksums, build provenance and the OBS plugin package.
- [ ] **Windows signing:** [SignPath Foundation](https://signpath.org) (free for open-source
      projects) couldn't give DeckHost a certificate for now. Apply again later; they need the public
      repository, a release built by GitHub Actions and a description of the project. Once approved
      they give you an organization, project and signing policy, and the release workflow gets a
      SignPath signing step (ask for it; it takes their values). Until then releases are unsigned,
      with checksums and build provenance, and the docs and release notes tell users what to do
      (Windows: *More info → Run anyway*). See [Code signing](releasing.md#code-signing).
- [ ] **VirusTotal:** add the `VT_API_KEY` secret (a free VirusTotal account's API key), so every
      release is scanned and false positives show up before users hit them
      ([False positives](releasing.md#false-positives)).
- [ ] **macOS:** with the Apple Developer Program (99 USD/year) the existing `MACOS_*` secrets sign
      and notarize; without it, users right-click → Open the first time.
- [ ] **Fill in the OBS entry** in the plugin catalog with the release's package and checksum.
- [ ] **Announce**, with screenshots and a short video of the device creator and a workflow:
      - r/streamdeck, r/StreamDock (or the MiraBox community), r/opensource, r/selfhosted (the Web Deck)
      - the OpenAction / OpenDeck community (DeckHost runs their plugins)
      - MiraBox: ask whether they'd link DeckHost for the N4 Pro
      - a "Show HN" post on Hacker News

### A first announcement

> **DeckHost: a free, open-source host for Stream Decks, the MiraBox N4 Pro and any USB button box**
>
> DeckHost drives your control surfaces on Windows, Linux and macOS: the MiraBox N4 Pro (keys, touch
> strip, knobs and their lights), Elgato Stream Decks (including the + and the Mini), a Web Deck on
> your phone, and USB button boxes, pedals and jog wheels you teach it by pressing their keys.
>
> - Thousands of Stream Deck and OpenAction plugins work as they are; plugins run in their own process.
> - Multi Actions, Key Logic and a visual workflow editor (triggers, conditions, waits, loops).
> - Profiles per app, pages, folders, an icon library, light effects, undo, light and dark themes.
>
> It's GPL-3.0, built by me with a lot of help from an AI coding assistant (the README says how), and
> tested with 600+ automated tests. Feedback and bug reports are very welcome: Help and support →
> Report a problem in the app. Download: github.com/vman26/DeckHost/releases
