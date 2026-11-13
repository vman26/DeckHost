#!/usr/bin/env bash
# Packages a published DeckHost (dotnet publish -r <rid> --self-contained) for release:
#   linux-*  DeckHost-<version>-<rid>.tar.gz  the app, install.sh, desktop entry, udev rule
#   osx-*    DeckHost-<version>-<rid>.zip     DeckHost.app (ad-hoc signed)
#
#   scripts/package.sh <rid> <version> <publish-dir> <output-dir> [display-version]
#
# <version> goes into file names (a pre-release: 1.3.0-alpha.3-4b09090); the display version
# is what users see (1.3.0-alpha.3+4b09090), by default the same.
set -euo pipefail

rid="${1:?rid}"
version="${2:?version}"
publish="$(cd "${3:?publish dir}" && pwd)"
mkdir -p "${4:?output dir}"
output="$(cd "$4" && pwd)"
display="${5:-$version}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
name="DeckHost-${version}-${rid}"

case "$rid" in
    linux-*)
        folder="$stage/deckhost-${version}"
        mkdir -p "$folder"
        cp -a "$publish" "$folder/DeckHost"
        cp "$root/packaging/linux/install.sh" "$root/packaging/linux/deckhost.desktop" "$root/packaging/linux/70-deckhost-mirabox.rules" "$folder/"
        cp "$root/LICENSE" "$folder/LICENSE.txt"
        chmod +x "$folder/install.sh" "$folder/DeckHost/DeckHost.App"
        cp "$root/DeckHost.App/Assets/deckhost-256.png" "$folder/deckhost.png"
        cat > "$folder/README.txt" <<TEXT
DeckHost ${display} for Linux (${rid})

Install for your user (asks for sudo once, for the device rule):
    ./install.sh
Remove again:
    ./install.sh --uninstall

Or run it in place: ./DeckHost/DeckHost.App
Check device support without opening the app: ./DeckHost/DeckHost.App --check-native

Needs giflib (libgif.so.7) and libudev (libudev.so.1), present on most desktops.
Optional, for the system actions: playerctl, wpctl or pactl, xdotool (X11) or wtype (Wayland), xprop.
Optional, for Stream Deck plugin settings pages inside DeckHost: WebKitGTK (libwebkit2gtk-4.1).
TEXT
        tar -C "$stage" -czf "$output/${name}.tar.gz" "deckhost-${version}"
        echo "$output/${name}.tar.gz"
        ;;
    osx-*)
        app="$stage/DeckHost.app"
        mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
        cp -a "$publish/." "$app/Contents/MacOS/"
        # CFBundleShortVersionString and CFBundleVersion must be plain numbers (1.2.1); the full
        # version, with a pre-release's commit, goes in DeckHostVersion.
        short="${version%%-*}"
        sed -e "s|@SHORT_VERSION@|${short}|" -e "s|@FULL_VERSION@|${display}|" "$root/packaging/macos/Info.plist" > "$app/Contents/Info.plist"
        chmod +x "$app/Contents/MacOS/DeckHost.App"
        # Apple Silicon only runs signed code; an ad-hoc signature is enough without a developer ID.
        # codesign counts every file in Contents/MacOS as code (the .NET .dll files too), so each
        # one is signed, then the bundle with its main executable (not --deep, which Apple advises
        # against).
        while IFS= read -r -d '' file; do
            codesign --force --sign - "$file"
        done < <(find "$app/Contents/MacOS" -type f ! -path "$app/Contents/MacOS/DeckHost.App" -print0)
        codesign --force --sign - "$app"
        codesign --verify --strict "$app"
        (cd "$stage" && ditto -c -k --sequesterRsrc --keepParent DeckHost.app "$output/${name}.zip")
        echo "$output/${name}.zip"
        ;;
    *)
        echo "Unsupported runtime identifier: $rid" >&2
        exit 1
        ;;
esac
