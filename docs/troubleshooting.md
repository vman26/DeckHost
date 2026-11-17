# Troubleshooting

## My device isn't found

- Close other programs that drive it: the Elgato Stream Deck app, the MiraBox app. Only one program can use a device at a time.
- Plug it in directly, not through a hub in a monitor.
- **Linux:** run `./install.sh` from the download once (it installs the device rule), then unplug the device and plug it in again.
- Run DeckHost with `--check-native` to see whether the N4 Pro support loads and which devices it finds.

## Is the device talking to DeckHost?

**☰ → Help and support → Devices** shows, per device, whether it's connected, how many inputs DeckHost received (and when the last one came), how many images it sent and the last error. Press a key and click **Refresh**: if the inputs don't go up, the device isn't reaching DeckHost (another program has it, the Linux device rule is missing, or the cable or hub). The same lines go into a support bundle.

## An action doesn't work

The key shows the reason in small red text when an action fails. On Linux, actions use common tools: media keys need `playerctl`, volume `wpctl`, `pactl` or `amixer`, hotkeys and typing `xdotool` (X11) or `wtype` (Wayland). Install the one the key names.

## A Stream Deck plugin's picture doesn't change

Plugins that redraw by themselves (a clock, a timer, a counter from a website) send a new picture
every second or so.

- **Plugins page:** is the plugin running? Its status says so, or why not (Node.js missing, made for
  another system, stopped after crashing).
- **Its log:** `logs/plugins/<plugin id>.log` (Help and support → Open logs) shows what the plugin
  printed. HTML plugins (whose program is a web page) run in DeckHost's built-in browser on Windows;
  they need the Microsoft Edge WebView2 runtime, which Windows 10 and 11 normally have.
- **Linux and macOS:** HTML plugins don't run there yet; the Plugins page says so.

## Windows says "Windows protected your PC"

The installer isn't signed yet. Choose **More info → Run anyway**.

## macOS says DeckHost can't be opened

Right-click DeckHost.app → **Open**, or run `xattr -dr com.apple.quarantine /Applications/DeckHost.app`. Hotkeys, typing text and locking need **System Settings → Privacy & Security → Accessibility** for DeckHost.

## Reporting a problem

**☰ → Help and support**:

- **Report a problem** opens a GitHub issue with DeckHost's version, your system and your devices filled in. Describe what happened.
- **Save support bundle…** makes a zip with the logs of the last days, crash reports, settings and the plugin list (your home folder and user name are replaced). Profiles are only included when you tick the box: actions keep passwords in them. Look inside before you share it.
- **Open log folder** shows the logs: one file per day, kept 14 days.

## After a crash

DeckHost writes a crash report on your computer. On the next start, **Help and support** opens and offers to report it; nothing is sent unless you do. You can turn this offer off there.

### Safe mode

If DeckHost closes unexpectedly again and again right after it starts (two failed starts in a row, or one within a minute), the next start is in **safe mode**: the built-in browser (plugin settings pages, HTML plugins), Stream Deck, OpenAction and installed plugins are switched off, and no control is opened in the editor, so whatever crashed can't run again. Your devices, profiles and the built-in actions keep working. A banner says so; **Restart normally** forgets the failed starts and starts DeckHost again, and **Crash report** opens Help and support. You can also start in safe mode yourself with `DeckHost --safe-mode`.

When something in the window goes wrong unexpectedly (a web page that can't start, a bug), DeckHost now carries on instead of closing: it writes a crash report, shows a notice with **Report…**, and a web view that failed is left off for the rest of that run. If the WebView2 runtime itself can't be used (not registered or not found), it stays off on that computer until the Microsoft Edge WebView2 runtime is updated (`%LocalAppData%/DeckHost/webview-disabled.txt`; delete it to try again). Without the WebView2 runtime the plugin settings page shows **Open in browser** instead.

## Where DeckHost keeps its files

`%AppData%` is `~/.config` on Linux and `~/Library/Application Support` on macOS.

| What | Where |
|---|---|
| Profiles (and daily backups) | `%AppData%/DeckHost/profiles.json`, `backups/` |
| Settings | `%AppData%/DeckHost/settings.json`, `devices/` |
| Logs | `%AppData%/DeckHost/logs` (plugins: `logs/plugins`) |
| Crash reports | `%AppData%/DeckHost/crashes` |
| Plugins | `%AppData%/DeckHost/plugins` |
| Icon packs | `%AppData%/DeckHost/icons` |
