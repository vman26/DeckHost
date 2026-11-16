# Devices: research and plan

What other hardware DeckHost could support next, what similar apps do, and the plan that follows.
Facts come from the sources linked; what still needs checking says so.

## MiraBox / StreamDock models in the vendor SDK

The StreamDock C++ SDK in `native/StreamDock-SDK` (the submodule DeckHost's bridge builds on) has a
device class per model. DeckHost describes only the N4 Pro's layout so far (`N4ProLayout`); the
bridge already opens any device the SDK knows.

| Model (SDK class) | USB ids (VID:PID) | Screen (SDK) | Rotation | Extras the SDK reports |
|---|---|---|---|---|
| N4 Pro (`StreamDockN4Pro`) | supported today | 800×480 | 180° | 4 knobs, touch strip (4 zones), RGB rings |
| N4 (`StreamDockN4`) | | 800×480 | 180° | second screen keys 1–4 (176×112), 4 knobs (rotate 16–23, press 24–27) |
| 293 V2 / V3, 293s V2 / V3 | 5500:1001, 6603:1005/1006, 5548:6670, 6603:1014 | 800×480 | 180° | keys only |
| M18 (+ V2, V2.5, V3) | 6603:1009/1012 | 480×272 | 0° | 24 RGB LEDs |
| M3 (`StreamDockM3`) | 5548:1020 | 854×480 | −90° | 3 knobs (rotate 16–21) |
| N3 V2 / V2.5 / V3 | 6602:1000/1002, 6603:1002/1003 | 320×240 | 90° | 3 knobs (press 33–35, rotate 50/51, 60/61, 90/91) |
| N1 (`StreamDockN1`) | 6603:1011/1000 | 480×854 | 0° | second screen keys 16–18 (64×64), knob |
| Mini (`StreamDockMini`) | 5548:1036/1037 | 320×240 | −90° | 12 RGB LEDs |
| XL (`StreamDockXL`) | 5548:1028/1031 | 1024×600 | 180° | 6 RGB LEDs, background GIF |
| K1 Pro (`K1Pro`) | 6603:1015 (+ EU) | | 90° | 3 knobs (press 7–9, rotate 10–15), a keyboard with keys |

Still to read per model, from its device class: the number of keys, their SDK ids and their image
size (the SDK resizes key images per model), and the knob ids. That's what a layout like
`N4ProLayout` needs.

**Plan:** turn `N4ProLayout` into a table of model layouts (controls, SDK ids, image sizes, knobs,
touch zones, LEDs, rotation), picked by the device the SDK reports, starting with the models people
ask for (N4, 293 V3, M18, XL, N3). Each model gets the same layout tests as the N4 Pro; real
hardware checks the key order.

## Loupedeck and Razer Stream Controller

- No official protocol is published. The unofficial **`loupedeck`** Node.js library (MIT,
  [github.com/foxxyz/loupedeck](https://github.com/foxxyz/loupedeck)) drives the Loupedeck Live,
  Live S, CT, Razer Stream Controller and Stream Controller X without Loupedeck's software: a serial
  port with firmware 0.2.x, WebSocket with 0.1.x (the Loupedeck software must not be running).
- Screens in RGB565: Live and Stream Controller have a 360×270 center plus 60×270 sides, the Live S
  480×270, the CT a 240×240 knob screen too, the Stream Controller X 480×288; keys are about 90×90
  (96×96 on the X); 12 or 15 keys; knobs send ±1 steps and press/release; touch with coordinates;
  colored buttons (not on the X); haptics and brightness.
- Its source (`connections/`, `parser.js`, `constants.js`) documents the messages; the README
  doesn't. Loupedeck/Logitech's own plugin SDK wasn't found in this research.

**Plan:** a `DeckHost.Device.Loupedeck` plugin that ports the protocol from that MIT library
(credited), over a serial port: one display region split into key and side zones (DeckHost's
linked-display model fits the side strips and the CT's knob screen), knobs, buttons with colors.
Needs a device to test; the simulator below can stand in for the layout work.

## Bitfocus Companion

- MIT licensed ([github.com/bitfocus/companion](https://github.com/bitfocus/companion)); Stream
  Decks and other controllers as a "shotbox surface", 700+ integration modules (listed on
  bitfocus.io).
- **Satellite protocol:** remote surfaces connect to Companion with a line-based text protocol
  (`KEY-PRESS KEY=… PRESSED=…`), published as `@companion-app/satellite-protocol` with JSON schemas
  and conformance cases for clients in other languages. Its default port wasn't in the pages read.

**Idea:** DeckHost could speak the Satellite protocol as a client, so a DeckHost device (N4 Pro,
Web Deck, a HID box) can also be a Companion surface, or as a server, so Companion's satellite
clients show up as DeckHost devices. Either is a device plugin; worth it once someone asks.

## Macro Deck (web and mobile)

- Apache-2.0 ([github.com/Macro-Deck-App/Macro-Deck](https://github.com/Macro-Deck-App/Macro-Deck));
  Windows, macOS, Linux; version 3 in beta.
- **Web client:** any browser on the network works as the macro pad, no app needed; **with HTTPS it
  installs as a PWA**. Native companion apps (Android, iOS, paid) find the PC on the network or over
  USB.
- **Widgets** besides buttons: sliders, clocks, history graphs, weather, a music player; buttons
  have short, long and double press.
- **Plugins** in C# out of process with a NuGet SDK; a **store whose packages are reviewed and
  cryptographically signed**, and the host warns on unsigned ones.
- The client protocol (port, pairing) wasn't in the pages read.

**Ideas for the Web Deck:** HTTPS (a self-signed certificate DeckHost makes, with its fingerprint
shown for pairing) so it installs as a PWA and the PIN and pictures travel encrypted (the master
plan's 12.2 asks for encrypted transport); long and double press from the page; folders and pages
on the phone; later sliders and readouts as live widgets. **For the store:** signing catalog
packages, which the master plan (2.3, 12.1) asks for too.

## A device simulator

**Goal:** try DeckHost with every supported device without the hardware, and see it as a connected
device.

- **Separate app:** `DeckHost.Simulator`, an Avalonia window that draws any supported model (N4 Pro,
  each Stream Deck, the MiraBox models once described, Loupedeck later) from the same layouts the
  device plugins use: keys, knobs you can turn with the mouse wheel, touch zones, LED rings.
- **Seen as a device:** a `DeckHost.Device.Simulator` plugin in DeckHost listens on loopback; each
  running simulator window connects (token in a file in DeckHost's data folder) and appears as a
  connected device with its model's controls. DeckHost sends it the same frames and lights it sends
  hardware; the simulator sends presses, turns and touches back.
- **Not USB:** looking like real USB hardware would need a virtual USB/HID driver on each system
  (kernel drivers, signing), which isn't worth it. DeckHost treats the simulator like any other
  device plugin, so everything above the plugin (profiles, rendering, actions, lights) is exercised.
- **Also for tests:** the simulator protocol doubles as an end-to-end test device in CI.

**Plan:** shared model descriptions first (so the simulator and the plugins use one source), then
the simulator plugin and app.
