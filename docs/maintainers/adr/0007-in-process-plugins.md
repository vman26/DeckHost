# 0007: DeckHost plugins run in the app's process

**Status:** Accepted, revisit

## Context
DeckHost's own plugin format (`plugin.json` + a .NET assembly) loads into a collectible `AssemblyLoadContext`. Stream Deck plugins already run as separate processes (their SDK is a WebSocket protocol).

## Decision
DeckHost plugins stay in process for now: they are fast (render on the worker), simple to write, and few. To limit the risk:
- the package declares what it needs (`permissions` in `plugin.json`), shown before install and in the Plugins page;
- the loader checks the manifest and reports problems per plugin instead of failing the app;
- the previous version of an updated plugin is kept, so it can be rolled back;
- an action that throws is caught and shown on its control.

## Consequences
- A plugin can still crash or slow down the app, and permissions are declarations, not enforcement.
- Revisit when third-party DeckHost plugins become common: run them in a helper process over the same contracts (the Stream Deck runtime shows the shape).
