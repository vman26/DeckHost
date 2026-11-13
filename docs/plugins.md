# Plugins and the store

**Plugins** (the puzzle icon at the top right) lists what's installed: DeckHost's own plugins, installed DeckHost plugins, and Stream Deck / OpenAction plugins. Enable, disable and uninstall them there.

## The store

The **Store** tab lists plugins from DeckHost sources, OpenAction, and the Elgato community archive, with their icons and README (**More ▾**). Install with one click.

## Stream Deck and OpenAction plugins

DeckHost runs Stream Deck plugins in their own processes and speaks their protocol, so they work as in the Elgato app: their actions, their images, and their settings pages, which show next to the editor.

- Native and Node.js plugins run on every system they ship a build for.
- HTML plugins (that run in a web page) run on Windows.
- Settings pages need WebView2 (Windows, built in), WKWebView (macOS) or WebKitGTK (Linux, `libwebkit2gtk-4.1`; else they open in your browser).

## Installing a package

**Install plugin…** takes a DeckHost plugin package (`.zip`, `.dhplugin`) or a `.streamDeckPlugin`. Icon packs are installed from the icon picker. Plugins run with your user's rights: only install what you trust. A DeckHost plugin says what it may do (use the network, start programs, …); that is shown when it's installed and on the Plugins page.

## Plugins in their own process

DeckHost plugins run in their own process: if one crashes or hangs, DeckHost and your devices keep working, and DeckHost restarts it (up to three times; then the Plugins page says it stopped). The Plugins page shows this as **Run in its own process**, with the process it runs in.

You can turn it off for a plugin, after a warning: it then runs inside DeckHost, which is a little faster but means a crash or hang takes DeckHost and your devices with it. Only do that for a plugin you trust, or when its author asks you to. Stream Deck and OpenAction plugins always run as programs of their own, so they don't have the setting; the plugins that come with DeckHost always run inside it.

Stream Deck and OpenAction plugins are restarted the same way when their program ends unexpectedly, and get back what they had on your devices; the Plugins page shows their process too. One that keeps stopping (three times in a row) is left stopped with a message: disable and enable it to try again. A plugin that runs for a few minutes between crashes starts the count again.

## Going back to an earlier version

When a plugin is updated, DeckHost keeps the version before it. If the new one misbehaves, **Back to v…** on the Plugins page brings the earlier one back (and keeps the newer one, so you can switch again).
