# 0008: Generic USB devices are descriptions, learned by pressing

**Status:** Accepted

## Context
People own control surfaces DeckHost will never have a driver for: button boxes, pedals, jog wheels, cheap macro pads, home-made Arduino/Pico boxes. Writing a device plugin is out of reach for most of them.

## Decision
- A generic device is **data, not code**: a JSON description (vendor and product id, the HID interface, each control's place on the layout and where its state is in the input reports). `DeckHost.Device.Hid` turns every description into a device.
- The **device creator** writes descriptions by watching the device: the user presses a key or turns a knob, and `HidLearner` compares those reports with the reports while the device was left alone (ignoring bytes that change on their own).
- Controls are numbered by where they sit (`key.N` reading order, `encoder.N` left to right), like every other device, so `LayoutMapper` can move profiles between them.
- Input only. Output (LEDs, displays) differs per device and can't be learned by watching.

## Consequences
- Many devices work without anyone writing code, and descriptions can be shared as files.
- Keyboards and mice can't be read on Windows (the system keeps them), and a device that types keys keeps typing them.
- Some devices pack states in ways the learner can't see (several bits per state, multi-byte values); those can be described by hand in the JSON.
