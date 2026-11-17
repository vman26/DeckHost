# DeckHost.Contracts

The plugin API of [DeckHost](https://github.com/vman26/DeckHost), the open-source host for control
surfaces (MiraBox N4 Pro, Elgato Stream Decks, a Web Deck, USB devices): `IApplicationPlugin`,
`IDeckAction`, `IRenderContext`, settings, variables and drawing helpers.

Reference it without copying it into your plugin; DeckHost supplies it at run time:

```xml
<PackageReference Include="DeckHost.Contracts" Version="x.y.z" ExcludeAssets="runtime" PrivateAssets="all" />
```

Start from the template: `dotnet new install DeckHost.Templates`, then `dotnet new deckhost-plugin`.
The guide is at https://vman26.github.io/DeckHost/maintainers/plugins/ and the API reference at
https://vman26.github.io/DeckHost/reference/api/.

MIT licensed, so plugins can use any license, free or paid.
