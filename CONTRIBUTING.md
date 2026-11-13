# Contributing to DeckHost

Thanks for helping! Bug reports, ideas, device support, plugins, docs and fixes are all welcome.

## Reporting a problem

The easiest way is from DeckHost: **☰ → Help and support → Report a problem**. It opens an issue with
your version, system and devices filled in. A support bundle (same page) helps a lot; look inside
before attaching it. Security problems: see [SECURITY.md](SECURITY.md), not a public issue.

## Plugins

You don't need to change DeckHost to add actions: write a plugin
([guide](docs/maintainers/plugins.md)) and list it in the
[DeckHost plugin catalog](https://github.com/vman26/deckhost-plugins). Plugins can use any license,
free or paid: the plugin API (`DeckHost.Contracts`) is MIT.

A USB device DeckHost doesn't know can often be added without code, with **☰ → Add a USB device**.
For devices with screens or lights, see the [device plugin guide](docs/maintainers/device-plugins.md).

## Changing DeckHost

1. For anything bigger than a small fix, open an issue first, so we can agree on the approach.
2. Build and test (see [README](README.md#building-from-source)):
   ```
   dotnet build DeckHost.slnx -p:EnableMiraBoxNativeBuild=false
   dotnet test DeckHost.Tests -p:EnableMiraBoxNativeBuild=false
   ```
3. Follow what's there: the [architecture](docs/maintainers/architecture.md), the
   [decisions](docs/maintainers/adr/index.md) and `CLAUDE.md` (a detailed map of the code). In
   short: Core never references a device plugin; the host's state belongs to its one worker; bindings
   are immutable; the plugin API only grows.
4. Add tests for what you change. CI builds and tests on Windows, Linux and macOS.
5. Say how you tested it, and on which hardware if any (it's fine if you couldn't).

By contributing you agree that your contribution is licensed under the project's license
(GPL-3.0-or-later; MIT for `DeckHost.Contracts` and the plugin template) and that you have the right
to contribute it.

## AI-assisted contributions

DeckHost itself is built with a lot of help from an AI coding assistant (see the README), so
AI-assisted contributions are welcome under the same rules:

- **You** are the author: you've read every line, you understand it, and you've run it.
- Say in the pull request that AI helped and how you tested the result.
- Don't submit code you couldn't explain in the review.

## Be kind

Everyone here follows the [code of conduct](CODE_OF_CONDUCT.md).
