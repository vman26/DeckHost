# Going public: checklist

What's left to do by hand to make DeckHost's repository public and announce it. Everything that could
be prepared in the code is done (license, notices, docs, issue forms, the plugin catalog, the release
workflow).

## Before flipping the switch

- [ ] **Merge the work to `main`.** The pull request stack (#4 → #5 → #6) holds everything; the
      release, docs and catalog workflows run from the default branch.
- [ ] **Your email on old commits.** Six commits carry your personal email address; it becomes
      visible when the repository is public. Either accept that, or (before going public) rewrite
      those commits' author email, which needs a force-push of every branch: ask for it if you want it.
      For new commits, use GitHub's private address: Settings → Emails → *Keep my email addresses
      private*, then `git config --global user.email <id>+vman26@users.noreply.github.com`.
- [ ] **History size.** Old OpenCV debug DLLs (about 38 MB) are in the history from before the native
      build moved to CMake. Harmless (OpenCV is Apache-2.0), only makes clones bigger. Leave them,
      unless you want to rewrite history anyway.
- [ ] **Remove the releases-repo settings** if you made them: the `RELEASES_REPO` variable and
      `RELEASES_TOKEN` secret. Without them the release goes to this repository, the apps check it
      for updates, the docs are published here, and the issue sync turns itself off.

A scan of the whole history found **no secrets** (tokens, keys, certificates) and no other personal data.

## Step 3: public, quietly

- [ ] **Settings → General → Danger zone → Change visibility → Public.**
- [ ] **Settings → Actions → General → Fork pull request workflows:** *Require approval for
      first-time contributors*.
- [ ] **Settings → Code security:** turn on *Private vulnerability reporting* (SECURITY.md links to
      it), *Dependabot alerts* and *Secret scanning*.
- [ ] **Settings → Rules:** a ruleset for `main` requiring a pull request and the **build** workflow.
- [ ] **Settings → Pages → Source: GitHub Actions**, then run the **docs** workflow (or push to `main`).
      The docs appear at `https://vman26.github.io/DeckHost/`, which is where the app's Help links.
- [ ] **Settings → General → Features:** Issues on; Discussions on if you want a place for questions
      that aren't bugs.
- [ ] **Sponsor button:** `.github/FUNDING.yml` shows Buy me a coffee; set up GitHub Sponsors
      (github.com/sponsors) and add `github: vman26` to that file.
- [ ] **The plugin catalog:** create `vman26/deckhost-plugins` ([guide](plugin-store.md)).
- [ ] Look at the repository logged out (or in a private window) and read the README as a stranger.

## Step 4: release and announce

- [ ] **First release:** tag `v0.1.0` (or whatever you want the first public version to be) on `main`.
      The release workflow builds Windows, Linux and macOS, checks each, and publishes them with
      checksums, build provenance and the OBS plugin package.
- [ ] **Windows signing:** apply to [SignPath Foundation](https://signpath.org) (free for open-source
      projects). They need: the public repository, a release built by GitHub Actions, and a
      description of the project. Once approved they give you an organization, project and signing
      policy; the release workflow then gets a SignPath signing step (ask for it; it takes their
      values). Until then, releases are unsigned (Windows shows *More info → Run anyway*).
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
