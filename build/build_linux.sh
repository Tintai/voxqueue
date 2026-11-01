#!/usr/bin/env bash
# Build VoxQueue for Linux and stage the result in publish/linux/.
#
# Usage:  bash build/build_linux.sh
# Needs: a Python 3.9+ venv with requirements.txt installed.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

PY="${PYTHON:-python3}"
OUT="$ROOT/publish/linux"
WORK="$ROOT/build/.work-linux"

echo "== VoxQueue - build (Linux) =="
"$PY" -c "import PyInstaller, PySide6, numpy" 2>/dev/null || {
    echo "ERROR: missing dependencies. Install them with:"
    echo "  $PY -m pip install -r build/requirements.txt"
    exit 1
}

rm -rf "$WORK" "$OUT"
mkdir -p "$OUT"

echo "-- PyInstaller --"
"$PY" -m PyInstaller build/build.spec \
    --noconfirm \
    --distpath "$OUT" \
    --workpath "$WORK" \
    --log-level WARN

BIN="$OUT/VoxQueue"
if [[ ! -f "$BIN" ]]; then
    echo "ERROR: $BIN was not produced"
    exit 1
fi
chmod +x "$BIN"

# A launcher with an extension.
#
# The binary is called "VoxQueue" with no extension. A file manager decides what
# to do with a file from its suffix, and an extensionless file matches nothing, so
# double-clicking it reports "no application available for this file type" even
# when the execute bit is set. Every desktop registers a handler for ".sh", so the
# launcher opens in a terminal and starts the binary from its own folder.
cat > "$OUT/VoxQueue.sh" <<'SH'
#!/bin/sh
# Start VoxQueue from the folder this script sits in, so it works when the
# archive is unpacked anywhere and the desktop runs it from a different directory.
cd "$(dirname "$0")" || exit 1
exec ./VoxQueue "$@"
SH
chmod +x "$OUT/VoxQueue.sh"

cat > "$OUT/install.sh" <<'INSTALL'
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
INSTALL
chmod +x "$OUT/install.sh"

# Ship the note next to the binary - a bare executable with no server address is
# not usable on its own. Kept in English to match the repository.
cat > "$OUT/README.txt" <<'TXT'
VoxQueue - batch text-to-speech
===============================

Install:    ./install.sh            (recommended - adds a working shortcut)
Run:        ./VoxQueue.sh          (or: chmod +x VoxQueue && ./VoxQueue)

            The chmod is not optional. Copying or downloading a bare executable
            drops its +x bit, and then the shell says "Permission denied" while a
            file manager says it has no application for this file type. Use the
            .tar.gz next to it instead - tar keeps the bit.

Requires:   nothing. Python, Qt and the xcb libraries are inside the bundle.
Server:     Settings -> Connection -> Server address
            default http://127.0.0.1:3900
ffmpeg:     optional; needed for MP3/M4A/OGG/FLAC and loudness normalisation

If the window does not appear, run this to see the reason:

    ./VoxQueue

Verify the bundle without opening a window:

    ./VoxQueue --selftest

Double-clicking the bare "VoxQueue" file will not work: it has no name suffix,
so the file manager has no program registered for it. Run ./install.sh once and
use the shortcut it creates.
TXT


# ---------------------------------------------------------------- .deb package
#
# This is the answer to "I want to double-click it". A tar.gz needs a terminal
# because tar does not register the file anywhere. A .deb is handled by the
# desktop's package manager: double-click opens a graphical installer, it asks
# for the password once, installs, and the app appears in the application grid
# with an icon. No terminal at any step.
#
# Layout follows the FHS: the self-contained bundle in /opt/voxqueue, a symlink
# in /usr/bin, the menu entry in /usr/share/applications, the icon in the theme.
if command -v dpkg-deb >/dev/null 2>&1; then
    VER=$(grep -oP '__version__\s*=\s*"\K[0-9.]+' "$ROOT/voxqueue/__init__.py" 2>/dev/null || echo "1.0.0")
    DEBROOT="$WORK/deb"
    rm -rf "$DEBROOT"
    mkdir -p "$DEBROOT/DEBIAN" \
             "$DEBROOT/opt/voxqueue" \
             "$DEBROOT/usr/bin" \
             "$DEBROOT/usr/share/applications" \
             "$DEBROOT/usr/share/icons/hicolor/256x256/apps"

    cp "$BIN" "$DEBROOT/opt/voxqueue/VoxQueue"
    chmod 755 "$DEBROOT/opt/voxqueue/VoxQueue"
    ln -sf /opt/voxqueue/VoxQueue "$DEBROOT/usr/bin/voxqueue"

    ICON="$ROOT/voxqueue/assets/icon.png"
    [ -f "$ICON" ] && cp "$ICON" "$DEBROOT/usr/share/icons/hicolor/256x256/apps/voxqueue.png"

    cat > "$DEBROOT/usr/share/applications/voxqueue.desktop" <<DESK
[Desktop Entry]
Type=Application
Name=VoxQueue
Comment=Batch text-to-speech
Exec=/opt/voxqueue/VoxQueue
Icon=voxqueue
Terminal=false
Categories=AudioVideo;Audio;
DESK

    # Only libGL is a dependency, and it is present on every desktop already: the
    # GNOME shell itself links against it. Everything else Qt needs — the xcb
    # family, xkbcommon — is inside the bundle, so this pulls nothing in practice
    # but still installs if libGL happens to be missing on a minimal system.
    INSTALLED_KB=$(du -sk "$DEBROOT" | cut -f1)
    cat > "$DEBROOT/DEBIAN/control" <<CTRL
Package: voxqueue
Version: $VER
Section: sound
Priority: optional
Architecture: amd64
Depends: libgl1, libegl1
Installed-Size: $INSTALLED_KB
Maintainer: VoxQueue <noreply@localhost>
Description: Batch text-to-speech for OpenAI-compatible servers
 Paste a whole script and get a numbered audio dataset: clips, transcripts and
 a metadata.csv that piper_train accepts as-is. Includes pronunciation markers,
 speaker splitting, resume, defect detection and 20 interface languages.
CTRL

    # dpkg-deb refuses a DEBIAN directory that is group- or world-writable:
    # "control directory has bad permissions 777 (must be >=0755 and <=0775)".
    # The workspace here is 0777, so the modes are set explicitly rather than
    # inherited.
    # Every directory 755 and every file 644, so the installed tree does not
    # inherit the 0777 of the build workspace.
    find "$DEBROOT" -type d -exec chmod 755 {} +
    find "$DEBROOT" -type f -exec chmod 644 {} +
    chmod 755 "$DEBROOT/opt/voxqueue/VoxQueue"
    chmod 755 "$DEBROOT/usr/bin/voxqueue" 2>/dev/null || true
    chmod 644 "$DEBROOT/usr/share/applications/voxqueue.desktop"
    chmod 644 "$DEBROOT/usr/share/icons/hicolor/256x256/apps/voxqueue.png" 2>/dev/null || true

    DEB="$OUT/VoxQueue_${VER}_amd64.deb"
    rm -f "$DEB"
    dpkg-deb --build --root-owner-group "$DEBROOT" "$DEB" >/dev/null
    rm -rf "$DEBROOT"
    [ -f "$DEB" ] && echo "deb: $DEB  ($(du -h "$DEB" | cut -f1))"
fi

# A tarball, because the execute bit does not survive a plain file download.
#
# The binary is a single extensionless file. Copied through a browser, a chat
# client or a file manager it arrives as 0644: the shell reports "Permission
# denied" and a double-click reports that no application handles this file type.
# tar stores the mode, so extracting it gives a runnable program with no chmod.
ARCHIVE="$OUT/VoxQueue-linux-x64.tar.gz"
rm -f "$ARCHIVE"
tar -czf "$ARCHIVE" -C "$OUT" VoxQueue VoxQueue.sh install.sh README.txt

SIZE=$(du -h "$BIN" | cut -f1)
PACK=$(du -h "$ARCHIVE" | cut -f1)
echo
echo "OK: $BIN  ($SIZE)"
echo "    $OUT/README.txt"
echo "    $ARCHIVE  ($PACK)  <- ship this one"
