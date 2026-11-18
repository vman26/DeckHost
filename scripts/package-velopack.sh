#!/usr/bin/env bash
# Velopack packages for one system: the installer, a self-updating portable package and the update
# feed DeckHost's updater reads (DeckHost.App/Platform/AppUpdater.cs).
#
#   scripts/package-velopack.sh <rid> <version> <file-version> <pack-dir> <output-dir>
#
# <version> is the SemVer without the commit (1.3.0-alpha.3); <file-version> what file names carry
# (1.3.0-alpha.3-4b09090). <pack-dir> is the published folder, or on macOS the DeckHost.app bundle
# (scripts/package.sh, DECKHOST_BUNDLE_ONLY=1).
#
# Into <output-dir>: the feed, never renamed because releases.<channel>.json names the packages
# (releases.<channel>.json, assets.<channel>.json, RELEASES*, DeckHost-<version>-<channel>-full.nupkg
# and -delta.nupkg), and the downloads, renamed to DeckHost's pattern:
#   win-x64    DeckHost-<file>-win-x64-install.exe   per user, no administrator rights (Setup.exe)
#              DeckHost-<file>-win-x64.msi           per user or for all users (Program Files)
#              DeckHost-<file>-win-x64.zip           portable, updates itself
#   linux-x64  DeckHost-<file>-linux-x86_64.AppImage replaces itself on update (pkexec when needed)
#   osx-arm64  DeckHost-<file>-osx-arm64.zip         DeckHost.app, updates itself
#              DeckHost-<file>-osx-arm64.pkg         only with MACOS_INSTALLER_IDENTITY
#
# Deltas: with VELOPACK_REPOSITORY (owner/repo) the channel's previous release is downloaded first,
# so the new release also has a delta package. Signing, all optional, from the environment:
#   Windows  WINDOWS_CERTIFICATE (+ _PASSWORD, WINDOWS_TIMESTAMP_URL): through scripts/sign-windows.ps1;
#            else AZURE_SIGNING_ENDPOINT/_ACCOUNT/_PROFILE with AZURE_TENANT_ID/_CLIENT_ID/_CLIENT_SECRET.
#   macOS    MACOS_SIGN_IDENTITY (Developer ID Application; else unsigned), MACOS_INSTALLER_IDENTITY
#            (Developer ID Installer, for the pkg), MACOS_NOTARY_PROFILE + MACOS_KEYCHAIN (notarization).
set -euo pipefail

rid="${1:?rid}"
version="${2:?version}"
file="${3:?file version}"
pack="${4:?pack dir}"
mkdir -p "${5:?output dir}"
output="$(cd "$5" && pwd)"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
feed="$(mktemp -d)"
trap 'rm -rf "$feed"' EXIT

case "$rid" in
    win-*) channel=win ;;
    linux-*) channel=linux ;;
    osx-*) channel=osx ;;
    *) echo "Unsupported runtime identifier: $rid" >&2; exit 1 ;;
esac

if [[ -n "${VELOPACK_REPOSITORY:-}" ]]; then
    pre=()
    if [[ "$version" == *-* ]]; then pre=(--pre); fi
    # Nothing published for the channel yet (the first Velopack release): a full package only.
    vpk download github --repoUrl "https://github.com/$VELOPACK_REPOSITORY" --channel "$channel" "${pre[@]}" -o "$feed" || true
fi

args=(pack -u DeckHost -v "$version" -p "$pack" --channel "$channel" -o "$feed"
      --packTitle DeckHost --packAuthors "Marco Veltman")

case "$channel" in
    win)
        args+=(-e DeckHost.App.exe -i "$root/DeckHost.App/Assets/deckhost.ico"
               --msi --instLocation Either --instLicense "$root/LICENSE")
        if [[ -n "${WINDOWS_CERTIFICATE:-}" ]]; then
            # vpk runs the template as a Windows command: a Windows path, not Git Bash's /d/a/...
            signer="$root/scripts/sign-windows.ps1"
            if command -v cygpath >/dev/null; then signer="$(cygpath -w "$signer")"; fi
            args+=(--signTemplate "pwsh -NoProfile -File \"$signer\" {{file}}")
        elif [[ -n "${AZURE_SIGNING_ACCOUNT:-}" && -n "${AZURE_CLIENT_SECRET:-}" ]]; then
            metadata="$feed/azure-signing.json"
            printf '{ "Endpoint": "%s", "CodeSigningAccountName": "%s", "CertificateProfileName": "%s" }\n' \
                "$AZURE_SIGNING_ENDPOINT" "$AZURE_SIGNING_ACCOUNT" "$AZURE_SIGNING_PROFILE" > "$metadata"
            args+=(--azureTrustedSignFile "$metadata")
        fi
        ;;
    linux)
        # The device rule (DeckHost can't install it from the read-only image; docs/devices.md says how
        # to take it out) and the licenses go into the image with the app.
        staged="$feed/pack/DeckHost"
        mkdir -p "$staged"
        cp -a "$pack/." "$staged/"
        cp "$root/packaging/linux/70-deckhost-mirabox.rules" "$root/NOTICE.md" "$staged/"
        cp "$root/LICENSE" "$staged/LICENSE.txt"
        args[6]="$staged"
        args+=(-e DeckHost.App -i "$root/DeckHost.App/Assets/deckhost-256.png" --categories "Utility;AudioVideo")
        ;;
    osx)
        args+=(-e DeckHost.App)
        # Without a Developer ID nothing is signed here: the .NET programs and Velopack's updater carry
        # their own ad-hoc signatures, and macOS asks the user to allow the app once (as before).
        if [[ -n "${MACOS_SIGN_IDENTITY:-}" ]]; then
            args+=(--signAppIdentity "$MACOS_SIGN_IDENTITY" --signEntitlements "$root/packaging/macos/DeckHost.entitlements")
        fi
        if [[ -n "${MACOS_INSTALLER_IDENTITY:-}" ]]; then
            args+=(--signInstallIdentity "$MACOS_INSTALLER_IDENTITY" --instLicense "$root/LICENSE")
        else
            args+=(--noInst)
        fi
        if [[ -n "${MACOS_NOTARY_PROFILE:-}" ]]; then
            args+=(--notaryProfile "$MACOS_NOTARY_PROFILE" --keychain "${MACOS_KEYCHAIN:?MACOS_KEYCHAIN}")
        fi
        ;;
esac

vpk "${args[@]}"

# The feed as vpk wrote it; the previous release's packages downloaded above are already published.
for item in "$feed"/releases."$channel".json "$feed"/assets."$channel".json "$feed"/RELEASES*; do
    [[ -e "$item" ]] && cp "$item" "$output/"
done
cp "$feed"/DeckHost-"$version"-"$channel"-*.nupkg "$output/"

# The downloads, named like every other DeckHost package (matched by kind: vpk's own names carry the
# pack id and channel, e.g. DeckHost-win-Setup.exe, DeckHost-win-Portable.zip).
move() {
    local found
    found="$(find "$feed" -maxdepth 1 -type f -name "$1" | head -n 1)"
    if [[ -n "$found" ]]; then
        mv "$found" "$output/$2"
        echo "$output/$2"
    elif [[ "${3:-}" == required ]]; then
        echo "vpk made no $1 in $feed:" >&2
        ls -la "$feed" >&2
        exit 1
    fi
}
case "$channel" in
    win)
        move '*Setup.exe' "DeckHost-$file-win-x64-install.exe" required
        move '*.msi' "DeckHost-$file-win-x64.msi" required
        move '*Portable.zip' "DeckHost-$file-win-x64.zip" required
        ;;
    linux)
        move '*.AppImage' "DeckHost-$file-linux-x86_64.AppImage" required
        ;;
    osx)
        move '*Portable.zip' "DeckHost-$file-osx-arm64.zip" required
        move '*.pkg' "DeckHost-$file-osx-arm64.pkg"
        ;;
esac

ls -la "$output"
