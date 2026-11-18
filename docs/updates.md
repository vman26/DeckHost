# Updates

DeckHost checks for a new release now and then (turn it off in **DeckHost settings**). When one is out, a notice appears at the top: **What's new** shows the release notes (the same as the [changelog](changelog.md)), and **Install and restart** downloads the update inside DeckHost, with its progress from 0 to 100 % in the notice and on the update page (usually only what changed, so it's small). DeckHost then closes, the new version is put in place of the old one as a whole (nothing is changed while DeckHost runs, so an update can't leave a half-installed DeckHost) and the new version starts. Your profiles and settings stay.

DeckHost updates itself when it was installed with the Windows installer (`-install.exe` or `.msi`) or the Windows zip, as the macOS app, or as the Linux AppImage. A DeckHost installed **for all users** (the MSI in Program Files, the app in `/Applications`, an AppImage in a protected folder) asks for administrator permission the usual way when it updates. The Linux tar.gz is updated by hand: the button downloads the new one and shows it; run `install.sh` from it again.

**Coming from an older DeckHost on Windows?** Versions before the new installer used a different setup. After updating once more the old way, DeckHost offers **Switch to the new installer**: one click installs the new DeckHost, removes the old one and starts the new one, with your profiles, settings and start-with-Windows kept. From then on updates work as described above.

- **Skip this version** stops the notice for that release.
- **Include pre-releases** (DeckHost settings) offers alpha, beta and release-candidate builds too; they are offered anyway when you already run one.
- The update is never installed without you choosing it.

## Installing updates automatically

With **DeckHost settings → Updates → Install updates automatically** on, a new version downloads in
the background as soon as DeckHost finds it, and installs when you close DeckHost (it isn't started
again then; the next time you open it, it's the new version). **Install now and restart** (in
DeckHost settings, next to that notice) installs it right away. The setting is there for every
DeckHost that updates itself (not for the Linux tar.gz).

Plugins have their own setting: Plugins page → Plugin updates → **Install plugin updates
automatically** ([Updating plugins](plugins.md#updating-plugins)).
