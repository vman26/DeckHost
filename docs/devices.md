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

## Firmware

**Device settings → Firmware** shows the version each device runs. DeckHost installs firmware for devices whose plugin supports it, after you confirm: it checks the download, and the device doesn't respond until it's done. The N4 Pro, Stream Decks and other USB devices only show their version: MiraBox has no update protocol, Elgato's isn't public (use Elgato's app with DeckHost closed), and other devices use their maker's tool.

## Other USB devices (device creator)

Button boxes, foot pedals, jog wheels, game controllers and other USB devices DeckHost doesn't know can be made into a deck: **☰ → Add a USB device**.

1. **Pick the device.** The list shows every USB HID interface on the computer; a device can have several. Pick one and press a key: **What it sends** shows the device's reports. If nothing changes when you press, try the device's other interface.
2. **Add its keys and knobs.** Click **Add key** and press the key; DeckHost works out where it shows up in the reports. **Add knob**, turn it to the right a few clicks and click **Done turning**. For a knob you can push, select it and click **Learn push**. Pressing a key you already added says so. Keys light up on the canvas as you press them, and knobs show their turns.
3. **Place them** where they sit on the device: drag them, or **Arrange** them in rows. Keys are numbered by where they sit (left to right, top to bottom) and knobs left to right, so profiles copy cleanly between devices.
4. **Save device.** It joins the device picker with its own profiles, like any deck. **Edit** and **Remove** are on the same page.

What to know:

- These devices only send input. They have no screens or lights DeckHost could drive, so keys show their action's name on the canvas.
- **Windows keeps keyboards and mice to itself**: DeckHost can't read those interfaces. Many macro pads also have a vendor-defined interface (often listed as *vendor-defined*), which does work. A device that types keys will also keep typing them; DeckHost can't stop that.
- **Linux** needs a device rule, as for Stream Decks. The page shows the line and where to put it.
- **macOS** may ask to allow input monitoring for some devices.
- Descriptions are kept in `devices/hid/<name>.json`; they can be shared.

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
