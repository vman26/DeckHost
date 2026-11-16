# MyPlugin

A [DeckHost](https://github.com/vman26/DeckHost) plugin.

## Build and try it

```
dotnet build
```

writes `bin/plugin/you.plugin.example.zip`. Check it, then install it in DeckHost
(**Plugins → Install plugin…**):

```
DeckHost --check-plugin bin/plugin/you.plugin.example.zip
```

Change `plugin.json` (name, description, `permissions`, `url`, `repository`) and the version in both
`plugin.json` and the project file when you release. The guide:
https://vman26.github.io/DeckHost/maintainers/plugins/

## Release and publish

Push a tag (`git tag v1.0.0 && git push --tags`): the workflow in `.github/workflows/build.yml`
builds the package, attaches it to a GitHub release and prints its SHA-256. DeckHost can install it
from there (**Store → Install from GitHub**), and you can list it in the DeckHost plugin catalog:
https://github.com/vman26/deckhost-plugins
