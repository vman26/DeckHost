# Updates

DeckHost checks for a new release now and then (turn it off in **DeckHost settings**). When one is out, a notice appears at the top: **What's new** shows the release notes (the same as the [changelog](changelog.md)), and **Install and restart** does the rest inside DeckHost, with its progress in the notice and on the update page: it downloads the package for your system, checks it against the release's checksums, installs it while DeckHost keeps running and then restarts DeckHost into the new version. If DeckHost was installed for all users (in Program Files, `/opt` or a folder you can't write), your system asks for an administrator's permission the usual way (the Windows UAC prompt, the macOS password dialog, or the Linux one from pkexec); saying no leaves DeckHost as it was. If anything goes wrong during the install, DeckHost puts the old files back. Your profiles and settings stay.

- **Skip this version** stops the notice for that release.
- **Include pre-releases** (DeckHost settings) offers alpha, beta and release-candidate builds too; they are offered anyway when you already run one.
- The update is never installed without you choosing it.

## Installing updates automatically

With **DeckHost settings → Updates → Install updates automatically** on, a new version downloads in
the background as soon as DeckHost finds it, and installs when you close DeckHost (it isn't started
again then; the next time you open it, it's the new version). **Install now and restart** (next to
the notice in DeckHost settings) installs it right away. The setting is there when DeckHost can write
its own folder, so no administrator permission is needed; a Linux AppImage is updated by hand (the
update screen offers the new `.AppImage` to download; you replace the file).

Plugins have their own setting: Plugins page → Plugin updates → **Install plugin updates
automatically** ([Updating plugins](plugins.md#updating-plugins)).
