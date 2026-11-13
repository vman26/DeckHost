# Devices

DeckHost shows every device it knows in the device picker at the top. Each device has its own profiles. You can set a device up while it's unplugged; it picks up its profile as soon as it's connected.

## MiraBox / StreamDock N4 Pro

Ten keys, a touch strip with four zones and swipes, four knobs with LED rings. Everything works on Windows, Linux and macOS: keys, touch zones, knobs, lights, brightness, vibration, the boot video and the idle screen (**Device settings**).

- A knob without a display of its own shows its action on the touch zone above it. Turn this off per knob in its editor (**Show this on Touch …**).
- A swipe over the strip without an action of its own flips through the profile's pages.

## Elgato Stream Deck (preview)

DeckHost talks to Stream Decks directly over USB, without Elgato's software. **Close the Stream Deck app first**: only one program can drive a deck.

| Model | Status |
|---|---|
| Stream Deck MK.2, Original v2 | supported |
| Stream Deck XL | supported |
| Stream Deck Neo | supported |
| Stream Deck + (dials and touch strip) | supported |
| Stream Deck Pedal | supported |
| First-edition Stream Deck, Stream Deck Mini | supported (older protocol, not tested on hardware yet) |

This support was written from the protocol and isn't tested on every model yet. Please report what works and what doesn't (**☰ → Help and support → Report a problem**).

## Web Deck (phone, tablet or a window)

A deck in a web browser.

1. Choose **Web Deck** in the device picker.
2. Open **Device settings** and choose **This computer only** (a deck in a browser window here) or **Phones and tablets on this network**.
3. Set the number of columns and rows, and open the link shown there.

The link carries a pairing code: without it the page shows nothing. A new code locks out every phone that had the old link. On a phone, **Add to Home Screen** makes it a full-screen app. Allow DeckHost through your firewall when the system asks, or phones can't reach it.

## Linux: the device rule

Linux only lets programs open USB devices they're allowed to. `install.sh` installs a udev rule (`70-deckhost-mirabox.rules`) for the N4 Pro and Elgato devices; unplug the device and plug it in again afterwards. Without it, DeckHost lists the device as not connected.

## Checking the device support

Run DeckHost with `--check-native`. It loads the N4 Pro's native support, lists the devices it finds and exits, without opening a window.
