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

**Install plugin…** takes a DeckHost plugin package (`.zip`, `.dhplugin`) or a `.streamDeckPlugin`. Icon packs are installed from the icon picker. Plugins run with your user's rights: only install what you trust.
