#!/usr/bin/env bash
# Downloads appimagetool (a pinned release) and checks it against the hash recorded here.
#   scripts/get-appimagetool.sh <output-file>
# Updating: change the version and the hash together, after reading the release page.
set -euo pipefail

version="1.9.0"
sha256="46fdd785094c7f6e545b61afcfb0f3d98d8eab243f644b4b17698c01d06083d1"
target="${1:?output file}"

curl -fsSL --retry 3 -o "$target" "https://github.com/AppImage/appimagetool/releases/download/${version}/appimagetool-x86_64.AppImage"
echo "${sha256}  ${target}" | sha256sum -c -
chmod +x "$target"
