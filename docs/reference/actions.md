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
| Variable | variable |  |
| Shown while it's empty | text |  |

### Switch Profile

Switches the device to another profile. On a knob each input can do something else: for example pressed a specific profile, turned left the previous one, turned right the next.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Switch to | choice | Options: Next profile, Previous profile, A specific profile, Do nothing. Default: Next profile. Can differ per input. |
| Profile name | text | The profile's name as shown in the profile list. Can differ per input. |

### Timer

A countdown or a stopwatch: press to start or pause, hold to reset.

Goes on: keys. Redraws every 500 ms. Not available as a Multi Action step.

| Setting | Kind | |
|---|---|---|
| Counts | choice | Options: Down (countdown), Up (stopwatch). Default: Down (countdown). |
| Minutes | number 0–180 | Default: 5. |
| Seconds | number 0–59 | Default: 0. |
| Sound at zero | file |  |

### Workflow

Build what the control does from linked nodes: what starts it (press, tap, double press, hold, turn), actions, waits, If (a variable, the app in front, the time), toggles, repeats and variables.

Goes on: keys, knobs. Not available as a Multi Action step.

## Media Controls

### Microphone Mute

Mutes or unmutes the default microphone, or unmutes it only while you hold the key (push to talk).

Goes on: keys. Redraws every 2 s.

| Setting | Kind | |
|---|---|---|
| Press | choice | Options: Mutes or unmutes, Mutes, Unmutes, Unmutes while held (push to talk). Default: Mutes or unmutes. |

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

### Play Sound

Plays a sound file (WAV, MP3, …), like a soundboard. Press again to stop it.

Goes on: keys, knobs.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |
| Sound file | file |  |
| Volume | number 0–100 % | Default: 100. |
| Pressed while it plays | choice | Options: Stops it, Plays it again from the start. Default: Stops it. |
| Output device (Linux, optional) | text | The PipeWire or PulseAudio sink name (wpctl status, pactl list short sinks). Windows and macOS play on the default device. |

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

### Stop All Sounds

Stops every sound that Play Sound keys are playing.

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
| Shortcut | choice | A common shortcut, with the right keys on every system (Ctrl on Windows and Linux, Cmd on macOS). Options: My own keys, Copy, Cut, Paste, Undo, Redo, Select all, Save, Find, Screenshot (area), New tab, Close tab, Reopen closed tab, Switch app, Show desktop, Emoji picker. Default: My own keys. |
| Keys | hotkey | Click Record and press the combination, or type it: Ctrl, Alt, Shift, Win plus one key (A-Z, 0-9, F1-F24, Enter, Tab, Esc, Space, arrows, Home, End, PageUp, PageDown, Delete, PrintScreen, media keys). |

### Launch App

Starts a program, script or shortcut.

Goes on: keys, knobs. Redraws every 2 s.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |
| Program | file | An.exe, a shortcut (.lnk) or any file Windows can open. |
| Arguments | text |  |
| Start in | text |  |
| Show a green dot while it runs | toggle | Default: true. |
| Hold the key to close it | toggle | A short press then starts it when you let go; holding asks the app to close. Default: false. |

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

### Run Command

Runs a program or script in the background and shows whether it worked; can save its output in a variable.

Goes on: keys, knobs. Redraws every 1 s.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |
| Program | file | Runs the program itself, not a shell. For shell features (pipes, &&) run the shell: cmd with /c …, or sh with -c … |
| Arguments | text | Separated by spaces; put an argument with spaces in double quotes. |
| Start in | text |  |
| Stop it after | number 1–600 s | Default: 30. |
| Save the output in | variable | The first 4000 characters of what it prints, for Show Variable and workflows. |

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

### Web Request

Sends a GET, POST, PUT or DELETE to an address (webhooks, Home Assistant, local APIs) and can save the answer in a variable.

Goes on: keys, knobs. Redraws every 1 s.

| Setting | Kind | |
|---|---|---|
| Run when | choice | Which input on this control runs the action. Options: Pressed, Released, Held down (long press), Turned left, Turned right, Swiped left, Swiped right. Default: Pressed. |
| Method | choice | Options: GET, POST, PUT, PATCH, DELETE. Default: POST. |
| Address | text |  |
| Body | text | Sent with POST, PUT and PATCH. |
| Content type | choice | Options: JSON, Text, Form. Default: JSON. |
| Headers | secret | One per line, or several separated by ';;'. Kept like a password. |
| Save the answer in | variable |  |
