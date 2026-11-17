# Updates

DeckHost checks for a new release now and then (turn it off in **DeckHost settings**). When one is out, a notice appears at the top: **What's new** shows the release notes (the same as the [changelog](changelog.md)), and the install button downloads the package for your system, checks it against the release's checksums, closes DeckHost, installs it and starts the new version. Your profiles and settings stay.

- **Skip this version** stops the notice for that release.
- **Include pre-releases** (DeckHost settings) offers alpha, beta and release-candidate builds too; they are offered anyway when you already run one.
- The update is never installed without you choosing it.

## Installing updates automatically

With **DeckHost settings → Updates → Install updates automatically** on, a new version downloads in
the background as soon as DeckHost finds it, and installs when you close DeckHost (it isn't started
again then; the next time you open it, it's the new version). **Install and restart** still installs
it right away. The setting is there for the Windows setup and for Linux and macOS installs DeckHost
can replace; a Windows zip is updated by hand, and so is a Linux AppImage (the update screen offers the new
`.AppImage` to download; you replace the file).

Plugins have their own setting: Plugins page → Plugin updates → **Install plugin updates
automatically** ([Updating plugins](plugins.md#updating-plugins)).
