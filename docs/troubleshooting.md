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
  printed since it last started; `<plugin id>.previous.log` is the run before (after a crash, what it
  printed before it ended). HTML plugins (whose program is a web page) run in DeckHost's built-in browser on Windows;
  they need the Microsoft Edge WebView2 runtime, which Windows 10 and 11 normally have.
- **Linux and macOS:** HTML plugins don't run there yet; the Plugins page says so.
- **Internet access:** a Stream Deck plugin is a program of its own, so a firewall needs an allow rule
  for it, not just for DeckHost. Its program is in
  `%AppData%\DeckHost\plugins\streamdeck\<plugin id>.sdPlugin\`. A log line with `os error 10013`
  ("access permissions") when it connects means a firewall blocked it. In Bitdefender: **Protection →
  Firewall → Settings → Rules**. Antivirus and web-protection exceptions don't change firewall rules.

## The Discord plugin keeps asking for access

The OpenAction **Discord** plugin (`me.amankhanna.oadiscord`) talks to the Discord app on your
computer and needs a Discord application of your own:

1. In the [Discord Developer Portal](https://discord.com/developers/applications), create an
   application. Under **OAuth2**, copy its **Client ID**, reset and copy the **Client Secret**, and add
   **exactly one** redirect URL, for example `http://localhost`. Click **Save Changes**.
2. In DeckHost, select a Discord key, enter the Client ID and Secret in its settings, and click **Save**.
3. Press the key, then click **Authorize** when Discord asks. It should only ask once.

When it asks again on every press, the key's settings panel or the plugin's log
(`logs\plugins\me.amankhanna.oadiscord.log`, and `.previous.log` for the run before) says why:

| Message | What it means | What to do |
|---|---|---|
| `Could not find the IPC pipe` | The plugin can't reach the Discord app. | Start the official Discord app and let it finish loading, then run the pipe check below. |
| `Client ID or Client Secret not configured` | The plugin has no Client ID or Secret. | Enter both and click **Save**. DeckHost before 0.1.0-rc.6 could lose them right after you authorized; update DeckHost. |
| `code 5000 … Missing "redirect_uri"` | Your Discord application has no redirect URL, or more than one. | Give it exactly one (e.g. `http://localhost`) and click **Save Changes** in the Developer Portal. |
| `Failed to exchange code for token: HTTP request failed: …` | The plugin's program couldn't reach `discord.com` to finish signing in. With `os error 10013` a firewall blocked it. | Allow `oadiscord-x86_64-pc-windows-msvc.exe` (in `%AppData%\DeckHost\plugins\streamdeck\me.amankhanna.oadiscord.sdPlugin\`) in your firewall, then authorize again. To check that your network is fine, run the check below. |
| `OAuth error: … invalid_client` | The Client Secret doesn't match the application. | Copy the current secret from the Developer Portal (resetting it makes the old one invalid) and click **Save**. |

The pipe check, in PowerShell with Discord running; it should list `discord-ipc-0`:

```powershell
Get-ChildItem \\.\pipe\ | Where-Object Name -like 'discord-ipc*'
```

The network check, in PowerShell (type `curl.exe`, not `curl`):

```powershell
curl.exe -sS -X POST https://discord.com/api/v10/oauth2/token -d grant_type=authorization_code -d code=test -d client_id=123 -d client_secret=test
```

`{"error": "invalid_client"}` means your computer reaches Discord fine, so something is blocking the
plugin's program in particular, usually a firewall rule for it. A `curl:` error means the network
itself is blocked.

The plugin keeps its Client ID, Secret and sign-in in
`%AppData%\DeckHost\streamdeck\settings\me.amankhanna.oadiscord.global.json`. If you share it, replace
`clientSecret` and `accessToken` first.

## Windows says "Windows protected your PC\"

DeckHost's packages aren't code-signed yet ([why](download.md#first-start-and-signing)). Choose **More info → Run anyway**. Windows only asks the first time; updates DeckHost installs itself don't ask again.

## macOS says DeckHost can't be opened

macOS 14 and older: right-click DeckHost.app → **Open**. macOS 15 and newer: open it once, then **System Settings → Privacy & Security → Open Anyway**. On any version you can also run `xattr -dr com.apple.quarantine /Applications/DeckHost.app`. Hotkeys, typing text and locking need **System Settings → Privacy & Security → Accessibility** for DeckHost.

## If your antivirus blocks DeckHost

Some antivirus programs quarantine or delete DeckHost, its installer or files in its folders. This is
almost always a *false positive*. DeckHost isn't code-signed yet, so every release is a file the
scanner hasn't seen before. It also does things that malware does too, but in the open and because
you set it up: it presses keys for your actions, starts at sign-in if you ask, and runs plugins.

**1. Check that your file is DeckHost's.** Compare it with the release
([Check a download](download.md#check-a-download)): the checksum in `SHA256SUMS.txt`, the build
provenance, and the VirusTotal report linked in the release notes. If the file you have doesn't
match, delete it and download it again from the [releases page](https://github.com/vman26/DeckHost/releases).

**2. Restore it and let DeckHost run.** Restore the files from the antivirus program's quarantine,
then add these folders as exceptions:

- the install folder: `%LocalAppData%\DeckHost` (the installer), the folder you picked for the `.msi`,
  or wherever you unpacked the zip
- `%AppData%\DeckHost`: your profiles, settings and installed plugins (Stream Deck plugins bring
  programs of their own)

In **Bitdefender**: **Protection → Antivirus → Open → Settings → Manage quarantine** to restore
([Bitdefender's steps](https://www.bitdefender.com/consumer/support/answer/2092/)), and
**Protection → Antivirus → Open → Settings → Manage exceptions → Add an exception** for each folder
([steps](https://www.bitdefender.com/consumer/support/answer/13427/)). In **Microsoft Defender**:
**Windows Security → Virus & threat protection → Protection history**, open the entry and choose
**Allow on device** or **Restore**. Then reinstall DeckHost if files are missing: your profiles and
settings in `%AppData%\DeckHost` stay.

Firewall rules are separate from these exceptions. If a plugin can't go online, see
[Internet access](#a-stream-deck-plugins-picture-doesnt-change) for plugins.

**3. Report the false positive**, so the next release isn't flagged for everyone. Send the file the
program flagged and its detection name (shown in the alert or the quarantine list):

| Antivirus | Where to report |
|---|---|
| Microsoft Defender | [Submit a file](https://www.microsoft.com/en-us/wdsi/filesubmission), "Incorrectly detected as malware" |
| Bitdefender | [Report a false positive](https://www.bitdefender.com/site/Main/automaticSampleUploader) (files up to 25 MB: send DeckHost's program, `DeckHost.App.exe`, rather than the installer) |
| Avast | [Submit a sample](https://www.avast.com/submit-a-sample) → False positive |
| Kaspersky | [OpenTip](https://opentip.kaspersky.com/): check the file, then **Submit to reanalyze** |
| ESET | [How to submit a false positive](https://support.eset.com/en/kb141-submit-a-virus-website-or-potential-false-positive-sample-to-the-eset-lab) |
| Norton | [submit.norton.com](https://submit.norton.com/) → False positive |

Please also [open an issue](https://github.com/vman26/DeckHost/issues) with the antivirus program, the
detection name and the file, so DeckHost's maintainer can report it as the author.

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
