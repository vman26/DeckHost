#!/usr/bin/env bash
# Packages a published Linux DeckHost (dotnet publish -r linux-x64 --self-contained) as an AppImage:
#   DeckHost-<version>-linux-x86_64.AppImage
#
#   scripts/package-appimage.sh <version> <publish-dir> <output-dir>
#
# Needs appimagetool: APPIMAGETOOL names it (CI downloads a pinned, checksum-verified copy with
# scripts/get-appimagetool.sh), else it is looked up on PATH. Without FUSE (CI containers) the tool
# and the result run through APPIMAGE_EXTRACT_AND_RUN=1.
set -euo pipefail

version="${1:?version}"
publish="$(cd "${2:?publish dir}" && pwd)"
mkdir -p "${3:?output dir}"
output="$(cd "$3" && pwd)"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tool="${APPIMAGETOOL:-$(command -v appimagetool || true)}"
if [[ -z "$tool" ]]; then
    echo "appimagetool not found: set APPIMAGETOOL or run scripts/get-appimagetool.sh." >&2
    exit 1
fi

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
appdir="$stage/DeckHost.AppDir"
mkdir -p "$appdir/usr/bin"
cp -a "$publish" "$appdir/usr/bin/DeckHost"
chmod +x "$appdir/usr/bin/DeckHost/DeckHost.App"
install -m 0755 "$root/packaging/linux/AppRun" "$appdir/AppRun"
# The menu entry starts the program by name; AppRun finds it.
sed 's|^Exec=.*|Exec=DeckHost.App|' "$root/packaging/linux/deckhost.desktop" > "$appdir/deckhost.desktop"
cp "$root/DeckHost.App/Assets/deckhost-256.png" "$appdir/deckhost.png"
ln -s deckhost.png "$appdir/.DirIcon"
mkdir -p "$appdir/usr/share/doc/deckhost"
cp "$root/LICENSE" "$appdir/usr/share/doc/deckhost/LICENSE.txt"
cp "$root/NOTICE.md" "$appdir/usr/share/doc/deckhost/NOTICE.md"
cp "$root/packaging/linux/70-deckhost-mirabox.rules" "$appdir/usr/share/doc/deckhost/"

file="$output/DeckHost-${version}-linux-x86_64.AppImage"
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$tool" --no-appstream "$appdir" "$file"
chmod +x "$file"
echo "$file"
