#!/usr/bin/env bash
# Installs DeckHost for the current user from the unpacked release archive:
#   ~/.local/share/deckhost        the app
#   ~/.local/bin/deckhost          a launcher
#   ~/.local/share/applications    the menu entry
# and, with sudo, the udev rule that lets you use the N4 Pro and Stream Decks without root.
#   ./install.sh            install or update
#   ./install.sh --uninstall
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
target="$data/deckhost"
bin="$HOME/.local/bin"
rule="/etc/udev/rules.d/70-deckhost-mirabox.rules"

if [[ "${1:-}" == "--uninstall" ]]; then
    rm -rf "$target" "$bin/deckhost" "$data/applications/deckhost.desktop" "$data/icons/hicolor/256x256/apps/deckhost.png"
    echo "DeckHost removed. Your profiles stay in ${XDG_CONFIG_HOME:-$HOME/.config}/DeckHost."
    if [[ -f "$rule" ]]; then
        echo "To remove the device rule too: sudo rm $rule && sudo udevadm control --reload-rules"
    fi
    exit 0
fi

echo "Installing DeckHost to $target"
rm -rf "$target"
mkdir -p "$target" "$bin" "$data/applications" "$data/icons/hicolor/256x256/apps"
cp -a "$here/DeckHost/." "$target/"
chmod +x "$target/DeckHost.App"
ln -sf "$target/DeckHost.App" "$bin/deckhost"
if [[ -f "$here/deckhost.png" ]]; then
    cp "$here/deckhost.png" "$data/icons/hicolor/256x256/apps/deckhost.png"
fi
sed "s|@EXEC@|$target/DeckHost.App|" "$here/deckhost.desktop" > "$data/applications/deckhost.desktop"
command -v update-desktop-database >/dev/null && update-desktop-database "$data/applications" >/dev/null 2>&1 || true

if [[ ! -f "$rule" ]] || ! cmp -s "$here/70-deckhost-mirabox.rules" "$rule"; then
    echo "Installing the udev rule for the N4 Pro and Stream Decks (needs sudo)..."
    if sudo install -m 0644 "$here/70-deckhost-mirabox.rules" "$rule"; then
        if sudo udevadm control --reload-rules && sudo udevadm trigger; then
            echo "Unplug and replug the N4 Pro once."
        else
            echo "Couldn't reload the udev rules; they apply after a restart."
        fi
    else
        echo "Skipped. Without it, DeckHost can't open the device; install it later with:"
        echo "  sudo install -m 0644 $here/70-deckhost-mirabox.rules $rule && sudo udevadm control --reload-rules && sudo udevadm trigger"
    fi
fi

# Read the list once: "ldconfig -p | grep -q" fails under pipefail when grep stops reading early.
libraries="$(ldconfig -p 2>/dev/null || /sbin/ldconfig -p 2>/dev/null || true)"
if [[ "$libraries" != *"libwebkit2gtk-4.1"* ]]; then
    echo "Optional: install WebKitGTK (libwebkit2gtk-4.1) to see plugin settings pages inside DeckHost; without it they open in your browser."
fi
for library in libgif.so.7 libudev.so.1; do
    if [[ "$libraries" != *"$library"* ]]; then
        echo "Note: $library is missing; install giflib / libudev from your distribution for device support."
    fi
done

echo "Done. Start DeckHost from your app menu, or run: deckhost"
echo "Optional tools for the system actions: playerctl (media), wpctl/pactl (volume), xdotool or wtype (keys and text), xprop (profile auto-switching)."
