# Built-in actions

The actions that come with DeckHost, by plugin, with their settings. Plugins add more (**Plugins → Store**).

*Generated from the code (`ReferenceDocsTests`); don't edit by hand.*

## DeckHost Built-ins

### Back

Leaves the folder: back to the page or folder it was opened from. Every new folder gets one in its first key.

Goes on: keys, knobs. Not available as a Multi Action step.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |

### Clock

Shows the current time and date.

Goes on: keys, knobs. Redraws every 1 s. Not available as a Multi Action step.

| Setting | Kind | |
|---|---|---|
| Format | choice | Options: 24-hour (14:05), 24-hour with seconds, 12-hour (2:05 PM), 12-hour with seconds. Default: 24-hour (14:05). |

### Counter

Counts presses (or knob steps). Long-press a key to reset it to zero.

Goes on: keys, knobs. Not available as a Multi Action step.

| Setting | Kind | |
|---|---|---|
| Step | number 1–100 | Added per press or knob step. Default: 1. |

### Folder

A key that opens a layout of its own, with a Back key to return. Double-click it (or use Open folder below) to fill it.

Goes on: keys, knobs. Not available as a Multi Action step.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |
| Folder name | text | Shown on the key and in the editor. Default: Folder. |

### Key ID (Diagnostic)

Shows which control and SDK id this is, to check the key layout.

Goes on: keys, knobs. Not available as a Multi Action step.

### Key Logic

Up to three things on one key: one for a press, one for a double press and one for holding it down.

Goes on: keys, knobs. Not available as a Multi Action step.

### Multi Action

Runs several actions in order. Add steps in Customize, or drop actions onto the control.

Goes on: keys, knobs. Not available as a Multi Action step.

### Multi Action Switch

Two sets of actions: the first press runs the first set, the next press the second, and so on. For toggles such as mute and unmute.

Goes on: keys, knobs. Not available as a Multi Action step.

| Setting | Kind | |
|---|---|---|
| Title before the first press | text |  |
| Title before the second press | text |  |

### Pages

Shows another page of this profile: the next, the previous or a specific one. On a knob, turning flips through the pages. Add pages with + under the device.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Go to | choice | Options: Next page, Previous page, A specific page, Do nothing. Default: Next page. Can differ per input. |
| Page | number 1–20 | Default: 1. Can differ per input. |

### Show Variable

Shows a workflow variable's value; workflows set it with a Set variable node.

Goes on: keys, knobs. Redraws every 500 ms. Not available as a Multi Action step.

| Setting | Kind | |
|---|---|---|
| Variable | text |  |
| Shown while it's empty | text |  |

### Switch Profile

Switches the device to another profile. On a knob each input can do something else: for example pressed a specific profile, turned left the previous one, turned right the next.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Switch to | choice | Options: Next profile, Previous profile, A specific profile, Do nothing. Default: Next profile. Can differ per input. |
| Profile name | text | The profile's name as shown in the profile list. Can differ per input. |

### Workflow

Build what the control does from linked nodes: what starts it (press, tap, double press, hold, turn), actions, waits, If (a variable, the app in front, the time), toggles, repeats and variables.

Goes on: keys, knobs. Not available as a Multi Action step.

## Media Controls

### Mute

Sends the Mute media key to the app that is playing.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |

### Next Track

Sends the Next Track media key to the app that is playing.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |

### Play / Pause

Sends the Play / Pause media key to the app that is playing.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |

### Previous Track

Sends the Previous Track media key to the app that is playing.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |

### Stop

Sends the Stop media key to the app that is playing.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |

### Track Swipe

Swipe left for the previous track, right for the next.

Goes on: touch strips (swipe). Not available as a Multi Action step.

### Volume Down

Sends the Volume Down media key to the app that is playing.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |

### Volume Knob

Turn to change the system volume, press to mute. Shows the volume on the touch strip.

Goes on: keys, knobs. Redraws every 1 s. Not available as a Multi Action step.

| Setting | Kind | |
|---|---|---|
| Volume steps per click | number 1–10 | One step is one press of a volume key: 2% on Windows and Linux, 1/16 on macOS. Default: 1. |

### Volume Up

Sends the Volume Up media key to the app that is playing.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |

## System Controls

### Hotkey

Presses a key combination in the active app.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |
| Keys | hotkey | Click Record and press the combination, or type it: Ctrl, Alt, Shift, Win plus one key (A-Z, 0-9, F1-F24, Enter, Tab, Esc, Space, arrows, Home, End, PageUp, PageDown, Delete, PrintScreen, media keys). |

### Launch App

Starts a program, script or shortcut.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |
| Program | file | An.exe, a shortcut (.lnk) or any file Windows can open. |
| Arguments | text |  |
| Start in | text |  |

### Lock Computer

Locks the computer (Win+L on Windows).

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |

### Open Website or File

Opens a web page, file or folder with its default app.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |
| Address or path | text |  |

### System Info

Shows CPU, memory, disk or uptime, updated every second.

Goes on: keys, knobs. Redraws every 1 s. Not available as a Multi Action step.

| Setting | Kind | |
|---|---|---|
| Show | choice | Options: CPU usage, Memory usage, Free disk space, Uptime. Default: CPU usage. |
| Drive | text | Used with 'Free disk space'. |

### Type Text

Types a piece of text into the active app.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |
| Text | text |  |
