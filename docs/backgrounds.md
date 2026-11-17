# Backgrounds

A profile's background is a color, an image, and any number of **layers** drawn over them, bottom to top. The **Default background** (Profile Manager, its own section) is set once and used by every profile. A profile can **use a background of its own**: tick the box under **Background of this profile** and the color, image and layers editor appears; untick it and the profile shows the default again (what you set stays, ready for the next time you tick it). Pages and folders look like their profile.

The color and image are the bottom of the picture; **Layers** adds more on top:

| Layer | What it draws |
|---|---|
| **Solid** | One color over the whole layout (with its opacity; `#AARRGGBB` colors work too). |
| **Gradient** | A gradient across the whole layout through two or more colors at an angle (0° left to right, 90° top to bottom). **+ Add a color** adds one, the arrows trade the places of two colors and each color has a % that says where along the gradient it starts. Every key shows its own part, so neighbouring keys continue each other. |
| **Radial** | A gradient from a center point outwards through the same colors; set the center (across and down) and the size (100 % reaches the corners). |
| **Image** | A picture (PNG, JPG, SVG or an animated GIF) spread over the whole layout; every key shows its slice. |

Each layer has an **Opacity** and a **Show** switch (a hidden layer is kept, not drawn), and moves up or down with the arrows. A layer of a kind this version of DeckHost doesn't know (from a newer version) is kept as it is and not drawn.

![The layers of a profile's background on the Profiles page: a gradient and a solid layer that follows the CPU load](images/backgrounds-studio.png)

![An N4 Pro profile with a violet gradient and a red layer that follows the CPU load: the keys, the touch strip and the screen between them show it](images/backgrounds-keys.png)

## Layers that follow a variable

Tick **Follows a variable** on a layer to make it react:

- **Variable:** `system.cpu` and `system.memory` (the load in percent, updated every two seconds while a background follows them), or `vars.name` for a variable a workflow sets with a Set variable node. A value that can't be read counts as 0.
- **Changes its color:** the layer's color goes from one color to another as the value goes from *From* to *to* (for a gradient, its end color).
- **Changes its opacity:** the layer gets more visible as the value grows, up to the layer's own opacity.
- **Whether it shows:** the layer only shows while the value is at or above *From* (`true` counts as 1).

**+ CPU glow** adds a red layer that gets stronger as the CPU gets busier (invisible when idle, full at 60 % load). A device is drawn again when a variable a background follows changes, at most ten times a second, and only the keys whose picture changed are sent.

## Limits

- Devices that keep a screen color behind and between the keys (the N4 Pro) get the part of the background that stays the same: solid and gradient layers that don't follow a variable (a gradient counts as the color half way along it). Layers that follow a variable and images show on the keys only.
- Images and GIFs are drawn per key, so a large animation costs more on devices with many keys.
