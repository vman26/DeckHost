# Writing plugins

A DeckHost plugin is a .NET assembly with a public `IApplicationPlugin` (from `DeckHost.Contracts`) and a parameterless constructor, packaged as a `.zip` with a `plugin.json`:

```json
{
  "id": "deckhost.plugin.obs",
  "name": "OBS Studio",
  "version": "1.0.0",
  "author": "DeckHost",
  "description": "Scenes, mute, streaming and recording.",
  "assembly": "DeckHost.Plugin.Obs.dll",
  "hostApiVersion": 1,
  "icon": "icon.png"
}
```

- Reference `DeckHost.Contracts` without copying it into the package (`Private=false`).
- Each action is an `IDeckAction`: its `Settings` become the fields in the editor, `Execute` runs on input, `Render` draws on the control. Both run on DeckHost's worker and must return quickly: start slow work on a background task and call `RequestRender` when it's done.
- `plugins/DeckHost.Plugin.Obs` in the source is a complete example (obs-websocket 5).

Stream Deck plugins (`.sdPlugin`) and OpenAction plugins run as they are: see the Elgato Stream Deck SDK.
