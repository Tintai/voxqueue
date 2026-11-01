#!/usr/bin/env bash
# ---------------------------------------------------------------------------
#  Builds the Windows release from Linux, as ONE self-contained executable.
#
#  Same approach as aether-reactor / aether-system: plain WinForms, published
#  for win-x64 from a Linux host. `EnableWindowsTargeting` is what makes that
#  possible — without it the SDK refuses to resolve the Windows desktop pack.
#
#  Single file: IncludeNativeLibrariesForSelfExtract is required or the exe
#  starts and dies looking for its own native runtime.
#
#  Usage:  bash dotnet/build-win.sh
# ---------------------------------------------------------------------------
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
SRC="$HERE/src"
IMG="mcr.microsoft.com/dotnet/sdk:10.0"
OUT="$ROOT/publish/windows"

echo "== VoxQueue — build (Windows x64, cross-compiled) =="

if ! docker image inspect "$IMG" >/dev/null 2>&1; then
    echo "-- pobieram $IMG --"
    docker pull "$IMG"
fi

# Step 1: run the cross-platform self-test. Cheap, and it stops a broken build
# before the publish step takes a minute.
echo "-- selftest (logika) --"
docker run --rm -v "$SRC":/src -w /src "$IMG" bash -c "
set -e
cd /src
dotnet run --project VoxQueue.SelfTest/VoxQueue.SelfTest.csproj 2>&1 | tail -25
"


# ---------------------------------------------------------------------------
#  Guard: size-dependent properties must not be set in an object initialiser.
#
#  A SplitContainer is 150 px wide when constructed. Setting SplitterDistance or
#  Panel*MinSize to anything larger there throws inside the constructor, so the
#  app dies before a window appears — twice now, and both times only the user
#  saw it. This check fails the build instead.
# ---------------------------------------------------------------------------
echo "-- guard: rozmiary w inicjalizatorach --"
# A literal number on the right-hand side is always the bug: the correct code
# assigns a computed value clamped to the current width. Matching a literal
# anywhere in the line also catches the inline `new SplitContainer { ... }` form.
if grep -RnE '(SplitterDistance|Panel[12]MinSize)[[:space:]]*[=:][[:space:]]*[0-9]+' \
     "$SRC"/VoxQueue.Desktop/*.cs 2>/dev/null \
     | grep -vE ':[[:space:]]*(//|\*|/\*)' ; then
    echo
    echo "ERROR: a size literal was assigned to a control property."
    echo "       A SplitContainer is 150 px wide when constructed - set it after"
    echo "       HandleCreated, wartoscia przeliczona z aktualnej szerokosci."
    exit 1
fi
echo "   ok"

# Step 2: publish
echo
echo "-- publish win-x64 --"
rm -rf "$OUT"
mkdir -p "$OUT"

docker run --rm \
    -v "$SRC":/src \
    -v "$OUT":/out \
    -w /src \
    "$IMG" bash -c "
set -e
cd /src
dotnet publish VoxQueue.Desktop/VoxQueue.Desktop.csproj \
    -c Release -r win-x64 --self-contained true \
    -p:EnableWindowsTargeting=true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=none \
    -p:PlatformTarget=x64 \
    -o /out 2>&1 | tail -25
"

EXE="$OUT/VoxQueue.exe"
if [ ! -f "$EXE" ]; then
    echo
    echo "ERROR: $EXE was not produced"
    exit 1
fi

# The name carries the version, and that is the only copy published.
#
# The Linux package has always had it in its filename, so two builds could not be
# confused after a download. The Windows binary was plain VoxQueue.exe, and "which
# version is this?" needed a Properties dialog. Shipping both names meant two
# identical 48 MB files in every release; one versioned name is enough.
VER=$(grep -oP '<Version>\K[0-9.]+' "$ROOT/dotnet/src/VoxQueue.Desktop/VoxQueue.Desktop.csproj" | head -1)
VER="${VER:-0.0.0}"
VERSIONED="$OUT/VoxQueue_${VER}_win-x64.exe"
mv "$EXE" "$VERSIONED"
echo "output: $VERSIONED"

cat > "$OUT/README.txt" <<'TXT'
VoxQueue — batch text-to-speech
===============================

Requirements
------------
Nothing. No Python, no .NET runtime. The executable is self-contained.

First run
---------
1. Windows may show a SmartScreen warning because the file is not signed.
   Click "More info" -> "Run anyway".
2. Settings -> Connection -> Server address, default: http://127.0.0.1:3900
3. Press Refresh to load the voice list.
4. Pick a mode, paste text, press Parse, then Start.

Modes
-----
Training   one line = one utterance  -> wav/0000.wav + metadata.csv
Dubbing    NAME: dialogue            -> voices/NAME/NAME_0000_2.41s.wav
Bulk       blocks split by ###       -> wav/0000.wav + 0000.txt

Pronunciation
-------------
A TTS model reads letters, not spelling: "Aether" becomes "A-e-ther", "2024" gets
spelled out. Give the engine a phonetic spelling and keep the correct one in the
transcript:

    {Eter|Aether} jest online.
    {dwa tysiące dwudziestego czwartego|2024} rok.

The first form is spoken, the second is written to metadata.csv. Full reference:
docs/PRONUNCIATION.md

Where settings live
-------------------
%APPDATA%\VoxQueue  — config.json, startup.log, selftest.log

The exe can be moved anywhere; settings follow the user, not the file.

ffmpeg
------
Optional. Without it, WAV output and all defect detection work.
MP3 / M4A / OGG / FLAC and EBU R128 loudness normalisation need ffmpeg in PATH.

Verify the package without opening a window
-------------------------------------------
    VoxQueue_<version>_win-x64.exe --selftest

The file name carries the version, so a build can be identified without opening it.

Project page and documentation: see README.md
TXT

echo
echo "Wynik: $OUT"
ls -lh "$OUT"
