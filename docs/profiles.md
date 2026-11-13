# Profiles, pages and folders

- A **profile** is what the whole device shows. Each device has its own profiles. Create, duplicate, rename, reorder and delete them on the **Profiles** page (the grid icon at the top right).
- A profile has **pages**: the page bar under the device adds and removes them. Swipe the touch strip, or use a **Page** action, to flip through them.
- A **folder** is a key that opens a set of keys, with a Back key in the first place. Drop the **Folder** action on a key, then double-click it (or **Open folder**) to fill it.

## Switching by app

Give a profile one or more apps (Profiles page): it turns on by itself while one of them is in front, and DeckHost goes back to the profile you chose by hand when you leave it. Turn this off in the **☰** menu. On Linux this works for X11 and XWayland apps and needs `xprop`.

## Backgrounds

A profile can have a background color and an image or animated GIF that spans all keys. **Default background for all profiles** applies to every profile without one of its own.

## Export, import and backups

- **Export…** saves a profile with its pages, folders and images as a `.deckhostprofile` file. **Import…** adds one as a new profile.
- **Import…** also takes `.streamDeckProfile` files from the Elgato app. Pages, folders, Multi Actions and Multi Action Switches come along; actions of plugins you haven't installed are kept and work once you install them.
- DeckHost keeps a copy of your profiles from each of the last days; **Backups** on the Profiles page lists them: **Restore** brings one back, **Back up now** makes one.
