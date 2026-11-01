# publish/ — prebuilt packages

```
publish/
├── windows/   VoxQueue_<version>_win-x64.exe   C# / WinForms, cross-compiled from Linux
└── linux/     VoxQueue       92 MB   Python / Qt (PyInstaller)
```

Both binaries are built from the same source tree and carry the same feature set.
The C# side runs 33 self-tests, the Python side 28.

## Which one to use

| Platform | File | Run |
|---|---|---|
| **Windows 10/11 x64** | `publish/windows/VoxQueue_1.27.3_win-x64.exe` | double-click |
| **Linux x64 — Ubuntu/Debian** | `publish/linux/VoxQueue_1.27.3_amd64.deb` | double-click, or `sudo apt install ./...deb` |
| **Linux x64 — other** | `publish/linux/VoxQueue-linux-x64.tar.gz` | `tar xzf ... && ./VoxQueue.sh` |

These are two separate implementations of the same application. WinForms is
Windows-only and Qt cannot produce a native `.exe`, so neither can replace the
other. Both are current: version 1.27.1, feature parity.

Binary artefacts are **not tracked by git** — rebuild them with the scripts at
the bottom of this file.

On Ubuntu and Debian take the `.deb`: the desktop's package manager handles it
and the app ends up in the application grid. On other distributions take the
`.tar.gz`, never the bare binary — tar stores the file mode, while a browser
download or a file-manager copy does not, and an extensionless file with no
execute bit is exactly what the desktop reports as "no application available for
this file type".

## Windows — verified

```
file VoxQueue_1.27.3_win-x64.exe
-> PE32+ executable (GUI) x86-64, for MS Windows, 9 sections

MZ header:    ok
PE signature: PE\0\0
machine:      0x8664  (x86-64)
subsystem:    2       (WINDOWS_GUI)
```

Built on Linux with `dotnet publish -r win-x64 -p:EnableWindowsTargeting=true`.
Requires only Docker with the `mcr.microsoft.com/dotnet/sdk:10.0` image.

Verify the package on Windows without opening a window:

```bat
VoxQueue_1.27.3_win-x64.exe --selftest
```

The test covers the embedded icon, window construction, four tabs, a light/dark
theme switch, an interface rebuild in five languages, and construction of all
four panels plus a resize sweep — the last one exists because a size-dependent
property set before layout throws in the constructor, which is how this app died
repeatedly before a window ever appeared.

## Linux — verified

```bash
./VoxQueue --selftest
```

```
OK   assets bundled       (icon.png=26455B, icon-128.png=8939B)
OK   module imports       (20 langs)
OK   text splitters       (dub+train)
OK   phonetic markers     (4 marker shapes)
OK   workspace save/load  (save+load+corrupt)
OK   translations         (20 langs, 6 columns)
OK   audio pipeline       (sr=22050 dur=1.00s)
OK   qt + icon            (qt run.py)
OK   main window          (4 tabs)
ALL OK
```

Requires glibc 2.39+ (built on Ubuntu 24.04). On X11 the xcb libraries are
needed; if the window does not appear:

```bash
sudo apt install libxcb-cursor0 libxcb-icccm4 libxcb-image0 \
                 libxcb-keysyms1 libxcb-render-util0 libxcb-util1 libxkbcommon-x11-0
```

## What the binaries do not include

- **ffmpeg** — optional. Without it, WAV output and all defect detection work
  (built-in peak normalisation). MP3/M4A/OGG/FLAC and EBU R128 `loudnorm` need
  ffmpeg on PATH.
- **a TTS server** — this is a client. The address is set in
  Settings -> Connection, default `http://127.0.0.1:3900`.

## Rebuilding

```bash
# Windows (from Linux, through Docker)
bash dotnet/build-win.sh

# Linux (PyInstaller)
python3 -m venv .venv && . .venv/bin/activate
pip install -r build/requirements.txt
bash build/build_linux.sh
```

Both scripts run their tests first and abort on failure, so a broken build is
never published.
