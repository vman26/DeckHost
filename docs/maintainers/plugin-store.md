# The plugin catalog (the DeckHost store)

DeckHost's own plugins are listed in a public GitHub repository, **`vman26/deckhost-plugins`**. Each
plugin is one JSON file there; a workflow checks every submission and publishes all of them as one
`catalog.json` on that repository's GitHub Pages. DeckHost reads it as the **DeckHost** source in
**Plugins → Store**, next to OpenAction and the Elgato community archive. No server, no costs: GitHub
hosts it and pull requests are the review process.

```
plugin author ──pull request──▶ deckhost-plugins/plugins/<id>.json
                                   │  check: fields, download, checksum, DeckHost --check-plugin
                                   ▼  merge → publish
                     https://vman26.github.io/deckhost-plugins/catalog.json ──▶ DeckHost store
```

## Setting it up (once)

1. **Create the repository** `vman26/deckhost-plugins` (public), and copy everything from
   [`packaging/plugins-repo/`](https://github.com/vman26/DeckHost/tree/main/packaging/plugins-repo)
   in the DeckHost repository into it (README, `plugins/`, `tools/`, `.github/`).
2. **Pages:** Settings → Pages → Source: **GitHub Actions**.
3. **Workflows that open pull requests** (submit, discover): Settings → Actions → General →
   *Allow GitHub Actions to create and approve pull requests*, and create the label `add-plugin`.
4. **Protect `main`:** Settings → Rules → a ruleset for `main` that requires a pull request and the
   **check** workflow to pass. Then nothing reaches the store without your review.
5. **Fill in the OBS entry** after DeckHost's first release (the release includes
   `deckhost.plugin.obs.zip`): set `download_url` to that release's file and `sha256` to its line in
   the release's `SHA256SUMS.txt`. Merge it; the **publish** workflow builds the catalog, and
   DeckHost's store shows the plugin.

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

Either way nothing is listed until you merge.

## Reviewing a submission

The **check** workflow does the mechanical part: the fields, the download, the checksum, the id and
version against the package's `plugin.json`, and loading it with `DeckHost --check-plugin` (from
DeckHost's latest release). Your part:

- Does the description match what it does? Is the author the one submitting it (or allowed to)?
- Do the `permissions` make sense? A "clock" plugin asking for `processes` deserves a question.
- For an update: is the new `download_url` from the same author's repository?
- Skim the source when it's open. A plugin with closed source is allowed, but say so in the
  review if anything looks off.

Merging is publishing: the catalog updates within a minute or two.

## Removing a plugin

Delete its file (or ask the author to). DeckHost stops listing it; people who installed it keep it
until they uninstall it.

## For plugin authors

1. Write and check the plugin ([guide](plugins.md)): `DeckHost --check-plugin your.plugin.zip`.
2. Release the zip (a GitHub release of your repository is best) and note its SHA-256.
3. Open an **Add a plugin** issue in [`vman26/deckhost-plugins`](https://github.com/vman26/deckhost-plugins)
   with your repository's address (a pull request with the entry is made for you), or add
   `plugins/<your plugin id>.json` in a pull request yourself, following its README. The template
   (`dotnet new deckhost-plugin`) releases the package with its checksum on every tag.

| Field | |
|---|---|
| `id`, `name`, `author`, `description`, `version` | Required; `id` and `version` must match the package's `plugin.json`. |
| `download_url`, `sha256` | Where the zip is (https) and its SHA-256: DeckHost refuses a download that doesn't match. |
| `repository`, `page`, `icon` | Optional links (https); the store shows the icon and opens the page. |
| `category` | Optional: Audio, Streaming, Smart home, Productivity, … (DeckHost guesses one otherwise). |
| `permissions` | What the plugin does beyond drawing and reacting to input; shown in the store. |
| `license` | The plugin's license, e.g. `MIT`, `GPL-3.0-or-later`, `Proprietary`. |
| `purchase_url`, `price` | For a **paid** plugin, instead of `download_url`/`sha256`: the store shows **Buy**, which opens the page; buyers install the file they get with **Install plugin…**. |

## Selling plugins

The catalog lists paid plugins like free ones; payment and delivery are up to the author (a shop
such as Gumroad, Lemon Squeezy or Ko-fi, or your own site). The plugin API is MIT and DeckHost's
license explicitly allows plugins of any license ([NOTICE](https://github.com/vman26/DeckHost/blob/main/NOTICE.md)),
so a paid, closed plugin is fine. If DeckHost later sells plugins itself, the same `purchase_url`
field is where its checkout goes.
