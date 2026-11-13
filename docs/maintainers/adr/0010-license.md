# 0010: GPL-3.0 for DeckHost, MIT for the plugin API

**Status:** Accepted

## Context
DeckHost goes public. It should stay free for everyone, nobody should be able to take it and sell
it as a closed product, and its developer wants to be able to earn something (donations, paid
plugins, sponsorship). Plugins should be possible under any license, free or paid.

## Decision
- DeckHost is **GPL-3.0-or-later**: anyone may use, study, share and change it; changed versions
  that are shared stay under the GPL.
- **`DeckHost.Contracts`** (the plugin API) and the plugin template are **MIT**.
- `NOTICE.md` adds a **GPL section 7 permission**: plugins that talk to DeckHost only through the
  Contracts or a documented plugin protocol (DeckHost, Stream Deck and OpenAction plugins), and
  platform and vendor libraries (WebView2, OS libraries, device SDKs), may be combined with DeckHost
  whatever their license.
- Contributions come in under the same licenses (CONTRIBUTING). If DeckHost ever needs another
  license (dual licensing), contributors after this point would have to agree, so that decision
  stays open only while the copyright is in few hands.

## Consequences
- Paid and closed plugins are allowed and can be listed in the catalog with a `purchase_url`.
- A fork must stay open; a vendor can't ship a closed DeckHost.
- The plugin API's license is separate; keep it in `DeckHost.Contracts` only (no GPL code there).
