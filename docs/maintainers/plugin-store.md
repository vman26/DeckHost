# The plugin catalog (the DeckHost store)

DeckHost's own plugins are listed in a public GitHub repository, **`vman26/deckhost-plugins`**. Each
plugin is one JSON file there; a workflow checks every submission and publishes all of them as one
`catalog.json` on that repository's GitHub Pages. DeckHost reads it as the **DeckHost** source in
**Plugins → Store**, next to OpenAction and the Elgato community archive. No server, no costs: GitHub
hosts it. A maintainer reviews a plugin **once**, when it's added; its later releases are listed
by themselves.

```
new plugin:  author ──issue or pull request──▶ plugins/<id>.json ──review, merge──┐
new version: author ──GitHub release──▶ update workflow (every 6 h, checks) ──────┤
                                                                                  ▼ publish
                         https://vman26.github.io/deckhost-plugins/catalog.json ──▶ DeckHost store
```

| | Author does | Maintainer does |
|---|---|---|
| New plugin | **Add a plugin** issue (or a pull request) | Review and merge once |
| New version | A GitHub release | Nothing |
| Name, description, author | Change the package's manifest, release | Nothing |
| New permissions, lower version, failed checks | A release | Approve the `review-update` issue (Actions → update → *approve*) |
| Category, icon, page, repository | A pull request | Merge |

## Setting it up (once)

1. **Create the repository** `vman26/deckhost-plugins` (public), and copy everything from
   [`packaging/plugins-repo/`](https://github.com/vman26/DeckHost/tree/main/packaging/plugins-repo)
   in the DeckHost repository into it (README, `plugins/`, `tools/`, `.github/`).
2. **Pages:** Settings → Pages → Source: **GitHub Actions**.
3. **Workflows that open pull requests** (submit, discover): Settings → Actions → General →
   *Allow GitHub Actions to create and approve pull requests*, and create the label `add-plugin`.
   The **update** workflow opens `review-update` issues (it creates the label).
4. **Protect `main`** with a ruleset that requires pull requests and a review: then nothing new is
   listed without you. The update workflow never writes to `main`: new versions go to the
   `catalog-updates` branch (it creates it), which must stay writable for GitHub Actions, so don't
   put that branch under the ruleset.
5. **The OBS entry** points at the OBS package of a DeckHost release (`deckhost.plugin.obs.zip`,
   with its line from that release's `SHA256SUMS.txt`). It has `"auto_update": false`: its
   repository is DeckHost itself, whose releases hold the app too, so move it to a newer release by
   hand.

DeckHost already reads `https://vman26.github.io/deckhost-plugins/catalog.json`
(`PluginStore.OfficialCatalogUrl`); until the repository exists the source is simply empty.

## Getting submissions

- **Add a plugin** issues: the author gives a GitHub repository; the **submit** workflow reads the
  newest release's package, computes its SHA-256 and opens a pull request (it comments the link on
  the issue). You can run it by hand too (Actions → submit), or locally:
  `python3 tools/catalog.py from-release https://github.com/owner/repository`.
- **Found on GitHub:** every Monday the **discover** workflow searches repositories with the topics
  `deckhost-plugin`, `streamdeck-plugin`, `stream-deck-plugin` and `openaction-plugin` that have a
  plugin file in a release, and opens one pull request with the new ones.

Either way nothing new is listed until you merge.

## Updates

The **update** workflow (every six hours, or by hand) reads the newest release of each listed
plugin's repository (`tools/catalog.py update`). It updates an entry in place, without a pull
request, when the release:

- is the **same plugin id**, from the repository you reviewed,
- has a **higher version**,
- asks for **no permission** the entry doesn't already list, and
- passes the **same checks** as a pull request: download, checksum, id and version against the
  package, and loading it with `DeckHost --check-plugin`.

It also takes the name, author and description from the package's manifest. Because `main` only
takes reviewed pull requests, the new version isn't written to the entry: it goes to
`updates/<id>.json` on the `catalog-updates` branch, and **publish** applies it on top of the
reviewed entry (`catalog.py build --overrides`). A pull request that brings the entry on `main` to
that version or later makes the update file obsolete; the next run removes it. Anything else opens an issue labelled `review-update` with the reason. If it's fine, run
**Actions → update → Run workflow** with the plugin id under *approve*; if not, set
`"auto_update": false` in the entry or remove it. A different plugin id is never taken, approved or
not.

The trade-off: someone who takes over a listed plugin's GitHub repository could publish a harmful
version, which would be listed without a review, as in most app stores. The checks catch broken
packages, new permissions are held back, and removing the entry stops new installs; DeckHost's
plugin updates can be rolled back (**Back to v…**).

## The Elgato mirror

The same repository can keep a license-gated mirror of the Elgato community archive (`tools/mirror.py`, the **mirror** workflow, weekly). It writes `catalog.json` and `NOTICE.md` to the `mirror-data` branch, which **publish** puts at `/mirror/` on the Pages site, and attaches the packages to the rolling `elgato-mirror` release. A package is copied only when the plugin's GitHub repository has an allowlisted open-source license (MIT, Apache-2.0, BSD, ISC, MPL-2.0, the GPL family, Unlicense, CC0), the plugin isn't in `mirror/blocklist.json`, and the copy is a valid, small zip with a plugin manifest of the same id. The rest stay catalog-only.

DeckHost's Elgato source (`PluginStore.LoadElgatoAsync`) reads `mirror/catalog.json` next to the archive: an entry with a mirrored copy downloads from the mirror and is checked against its SHA-256; everything else keeps the archive's download, and a missing or unreadable mirror changes nothing. A plugin only the mirror has isn't added, so the archive keeps deciding what is listed.

**The workflow is off** until the repository variable `ELGATO_MIRROR_ENABLED` is `true`; set it only after Elgato's terms for the Marketplace are confirmed. **Actions → mirror → Run workflow** with *dry run* (the default for a manual run) works anyway and prints what it would do (it needs no write access; unauthenticated GitHub lookups hit the rate limit, the workflow's token doesn't). A first run can be spread over several with the *limit* input.

Takedown: add the plugin id (or `owner/repository`) to `mirror/blocklist.json` in a pull request and run the workflow; the package is deleted and the entry loses its download.

## Reviewing a submission

The **check** workflow does the mechanical part: the fields, the download, the checksum, the id and
version against the package's `plugin.json` (or a Stream Deck package's `manifest.json`), and
`DeckHost --check-plugin` (from DeckHost's latest release): it loads a DeckHost package, and for a
Stream Deck package reads its manifest, actions and program without starting it. Your part:

- Does the description match what it does? Is the author the one submitting it (or allowed to)?
- Do the `permissions` make sense? A "clock" plugin asking for `processes` deserves a question.
- Skim the source when it's open. A plugin with closed source is allowed, but say so in the
  review if anything looks off.

Merging is publishing: the catalog updates within a minute or two.

## Removing a plugin

Delete its file (or ask the author to). DeckHost stops listing it; people who installed it keep it
until they uninstall it.

## For plugin authors

1. Write and check the plugin ([guide](plugins.md)): `DeckHost --check-plugin your.plugin.zip`.
2. Release the zip (a GitHub release of your repository is best).
3. Open an **Add a plugin** issue in [`vman26/deckhost-plugins`](https://github.com/vman26/deckhost-plugins)
   with your repository's address (a pull request with the entry is made for you), or add
   `plugins/<your plugin id>.json` in a pull request yourself, following its README. The template
   (`dotnet new deckhost-plugin`) releases the package with its checksum on every tag.
4. After that, every release is listed within about six hours; nothing to submit.

| Field | |
|---|---|
| `id`, `name`, `author`, `description`, `version` | Required; `id` and `version` must match the package's `plugin.json`. |
| `download_url`, `sha256` | Where the zip is (https) and its SHA-256: DeckHost refuses a download that doesn't match. |
| `repository`, `page`, `icon` | Optional links (https); the store shows the icon and opens the page. |
| `category` | Optional: Audio, Streaming, Smart home, Productivity, … (DeckHost guesses one otherwise). |
| `permissions` | What the plugin does beyond drawing and reacting to input; shown in the store. |
| `license` | The plugin's license, e.g. `MIT`, `GPL-3.0-or-later`, `Proprietary`. |
| `auto_update` | Optional, `false` to update only through pull requests. |
| `purchase_url`, `price` | For a **paid** plugin, instead of `download_url`/`sha256`: the store shows **Buy**, which opens the page; buyers install the file they get with **Install plugin…**. |

## Selling plugins

The catalog lists paid plugins like free ones; payment and delivery are up to the author (a shop
such as Gumroad, Lemon Squeezy or Ko-fi, or your own site). The plugin API is MIT and DeckHost's
license explicitly allows plugins of any license ([NOTICE](https://github.com/vman26/DeckHost/blob/main/NOTICE.md)),
so a paid, closed plugin is fine. If DeckHost later sells plugins itself, the same `purchase_url`
field is where its checkout goes.
