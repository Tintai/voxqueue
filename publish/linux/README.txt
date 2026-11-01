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
