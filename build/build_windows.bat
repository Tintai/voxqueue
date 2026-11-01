@echo off
REM ============================================================================
REM  VoxQueue - build for Windows
REM
REM  PyInstaller cannot cross-compile: this file MUST be run on
REM  Windows. Wynik: publish\windows\VoxQueue.exe (jeden plik, bez instalacji).
REM
REM  Uzycie:  build\build_windows.bat
REM ============================================================================
setlocal enabledelayedexpansion
cd /d "%~dp0.."

echo == VoxQueue - build (Windows) ==

where python >nul 2>nul
if errorlevel 1 (
    echo ERROR: nie znaleziono python w PATH.
    echo Zainstaluj Python 3.9+ i zaznacz "Add to PATH".
    exit /b 1
)

echo -- zaleznosci --
python -m pip install --quiet --upgrade pip
python -m pip install --quiet -r build\requirements.txt
if errorlevel 1 (
    echo ERROR: instalacja zaleznosci nie powiodla sie.
    exit /b 1
)

if exist "publish\windows" rmdir /s /q "publish\windows"
if exist "build\.work-win" rmdir /s /q "build\.work-win"
mkdir "publish\windows"

echo -- PyInstaller --
python -m PyInstaller build\build.spec ^
    --noconfirm ^
    --distpath "publish\windows" ^
    --workpath "build\.work-win" ^
    --log-level WARN
if errorlevel 1 (
    echo ERROR: build nie powiodl sie.
    exit /b 1
)

if not exist "publish\windows\VoxQueue.exe" (
    echo ERROR: nie powstal plik publish\windows\VoxQueue.exe
    exit /b 1
)

> "publish\windows\README.txt" echo VoxQueue - Batch-TTS-Frontend
>>"publish\windows\README.txt" echo.
>>"publish\windows\README.txt" echo Start:      VoxQueue.exe
>>"publish\windows\README.txt" echo Wymaga:     niczego poza tym plikiem
>>"publish\windows\README.txt" echo Serwer:     Ustawienia -^> Polaczenie -^> Adres serwera
>>"publish\windows\README.txt" echo              domyslnie http://127.0.0.1:3900
>>"publish\windows\README.txt" echo ffmpeg:     opcjonalny - dla MP3/OGG/FLAC
>>"publish\windows\README.txt" echo.
>>"publish\windows\README.txt" echo Uwaga: Windows SmartScreen moze ostrzec przy
>>"publish\windows\README.txt" echo pierwszym uruchomieniu - plik nie jest podpisany
>>"publish\windows\README.txt" echo certyfikatem. "Wiecej informacji" -^> "Uruchom mimo to".

echo.
echo OK: publish\windows\VoxQueue.exe
dir /b "publish\windows"
endlocal
