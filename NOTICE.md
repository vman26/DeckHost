# DeckHost license notice

DeckHost — a host for control surfaces (MiraBox N4 Pro, Elgato Stream Deck, Web Deck, USB devices)
Copyright (C) 2026 Marco Veltman and DeckHost contributors

This program is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License as published by the Free Software Foundation, either version 3 of the
License, or (at your option) any later version. See [`LICENSE`](LICENSE).

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without
even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
General Public License for more details.

## The plugin API is MIT

`DeckHost.Contracts` (the interfaces and records plugins are built against) and the plugin
template in `plugins/` are licensed under the **MIT License** (see their own `LICENSE` files), so
plugins can use any license, free or paid.

## Additional permission (GPL-3.0 section 7)

As an additional permission under section 7 of the GNU GPL version 3, the copyright holders of
DeckHost give you permission to convey a covered work that is combined with, or loads at run time:

1. **plugins** (application plugins, device plugins, Stream Deck and OpenAction plugins) that
   interact with DeckHost only through the `DeckHost.Contracts` interfaces or a documented plugin
   protocol, whatever their license; and
2. **platform and vendor libraries** that DeckHost uses but doesn't ship the source of, such as
   the Microsoft Edge WebView2 runtime and SDK, the operating system's own libraries and device
   makers' SDKs,

without the terms of the GPL applying to those plugins or libraries. This permission doesn't
change the license of DeckHost itself: DeckHost, and changed versions of it, stay under the GPL.

You may remove this additional permission from your own changed versions of DeckHost.

## Third-party code

DeckHost uses third-party packages under their own licenses (MIT, BSD, Apache-2.0); see the
[architecture page](docs/maintainers/architecture.md#third-party-code). The StreamDock SDK
(`native/StreamDock-SDK`) is © MiraboxSpace, MIT License.

*This notice isn't legal advice; for questions about using DeckHost commercially, open an issue.*
