# 0007: DeckHost plugins run in their own process

**Status:** Accepted (replaces "DeckHost plugins run in the app's process")

## Context
DeckHost's own plugin format (`plugin.json` + a .NET assembly) used to load into DeckHost's process. A plugin that crashed took DeckHost and every device down; one that hung froze the device; and it could do anything DeckHost can. Stream Deck plugins already run as separate processes.

## Decision
- By default a DeckHost plugin runs in **its own process**: DeckHost starts itself with `--plugin-host <port> <token> <folder>` (no window), the process loads the package and connects back over a loopback socket, proving itself with a one-time token. Messages are JSON lines (`Core/Plugins/Isolation/PluginWire.cs`).
- DeckHost gets stand-ins (`IsolatedApplicationPlugin`, `IsolatedDeckAction`) built from the plugin's description, so the rest of DeckHost doesn't know the difference, and plugins need no changes.
- `Execute`, `OnAppear`/`OnDisappear` are sent without waiting. `Render` can't wait for another process on DeckHost's worker: the plugin's `IRenderContext` there records the drawing calls (a display list), DeckHost replays the last list and asks for a new one when what the action draws from changed (settings, size, the control's variables), on `RequestRender` from the plugin, and at the action's refresh interval; when it arrives the device is redrawn. Variables the plugin publishes and `IActionHost` calls are forwarded.
- A process that ends or stops answering pings is restarted (after 1, 3 and 10 s); it's told again what's on the devices. After three restarts the plugin stays stopped, with a message on the Plugins page.
- The user can **opt a plugin out** (Plugins page → *Run in its own process*), after a warning; that choice is kept in `plugins/state.json`. System plugins stay inside DeckHost (they ship with it); Stream Deck plugins were always separate.

## Consequences
- A crashing or hanging plugin can't take DeckHost or the devices down, and comes back by itself.
- One picture behind: a changed key shows its new picture a few milliseconds later (one round trip). Starting a plugin takes a moment longer (a process start, about half a second).
- Each plugin process costs memory (a .NET runtime each).
- Not a sandbox: the process runs with the user's rights. Permissions in `plugin.json` are still declarations. A real sandbox (lower rights per process) can build on this.
- Values that cross are strings, numbers, booleans and times; a plugin that shares other objects through the variable store only sees them inside its own process.
