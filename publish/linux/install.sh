#!/bin/sh
# Install VoxQueue for the current user on Ubuntu / Debian.
#
# Why an installer instead of a double-clickable binary: GNOME resolves file
# types by name suffix and refuses to run an extensionless executable from the
# file manager, and a .desktop file dropped in a normal folder opens as text
# until it is marked trusted. Installing to the two standard locations and
# marking the shortcut trusted is the one path Ubuntu accepts.
#
# After this script the app is in the application grid and on the desktop, and
# both launchers work by double-click.

set -e
HERE=$(cd "$(dirname "$0")" && pwd)

APP_DIR="$HOME/.local/share/voxqueue"
BIN_DIR="$HOME/.local/bin"
ENTRY_DIR="$HOME/.local/share/applications"

mkdir -p "$APP_DIR" "$BIN_DIR" "$ENTRY_DIR"
cp "$HERE/VoxQueue" "$APP_DIR/VoxQueue"
chmod +x "$APP_DIR/VoxQueue"

echo "installed binary: $APP_DIR/VoxQueue"

# Menu entry: appears in Show Applications, launchable with one click.
ENTRY="$ENTRY_DIR/voxqueue.desktop"
cat > "$ENTRY" <<DESK
[Desktop Entry]
Type=Application
Name=VoxQueue
Comment=Batch text-to-speech
Exec=$APP_DIR/VoxQueue
Icon=audio-x-generic
Terminal=true
Categories=AudioVideo;Audio;
DESK
chmod +x "$ENTRY"

# Desktop shortcut, marked trusted so a double-click runs it instead of opening
# it in a text editor. gio is what Nautilus consults; without this the shortcut
# is inert no matter what its mode bits say.
DESKTOP_DIR=$(xdg-user-dir DESKTOP 2>/dev/null || echo "$HOME/Desktop")
if [ -d "$DESKTOP_DIR" ]; then
    cp "$ENTRY" "$DESKTOP_DIR/VoxQueue.desktop"
    chmod +x "$DESKTOP_DIR/VoxQueue.desktop"
    gio set "$DESKTOP_DIR/VoxQueue.desktop" metadata::trusted true 2>/dev/null || true
    echo "desktop shortcut: $DESKTOP_DIR/VoxQueue.desktop"
fi

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$ENTRY_DIR" 2>/dev/null || true

# Runtime check, so a missing library is reported here and not as a dead click.
echo
if ! "$APP_DIR/VoxQueue" --selftest >/tmp/voxqueue-selftest.log 2>&1; then
    echo "WARNING: the self-test failed. Log: /tmp/voxqueue-selftest.log"
fi

# Nothing to install: the xcb libraries Qt needs are inside the bundle. This is
# what "standalone" means here — no apt step, no sudo, no missing-library abort.
echo "Done. Start VoxQueue from the application grid, or double-click the"
echo "desktop shortcut."
