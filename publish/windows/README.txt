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
