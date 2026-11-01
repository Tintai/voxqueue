# -*- mode: python ; coding: utf-8 -*-
"""PyInstaller spec for VoxQueue.

Build with:
    pyinstaller build/build.spec --noconfirm

Output goes to publish/<platform>/. PyInstaller cannot cross-compile: run this
on Windows to get VoxQueue.exe and on Linux to get the Linux binary.

The excludes matter. PySide6 ships every module and Qt pulls in a lot that this
app never touches; without them the bundle is ~250 MB instead of ~60 MB.
"""

import sys
from pathlib import Path

from PyInstaller.utils.hooks import collect_submodules

ROOT = Path(SPECPATH).parent
APP = ROOT / "voxqueue"

# Qt modules we actually import. Anything else is dead weight.
EXCLUDES = [
    "PySide6.QtWebEngineCore", "PySide6.QtWebEngineWidgets",
    "PySide6.QtWebEngineQuick", "PySide6.QtQuick", "PySide6.QtQuick3D",
    "PySide6.QtQml", "PySide6.Qt3DCore", "PySide6.Qt3DRender",
    "PySide6.QtCharts", "PySide6.QtDataVisualization", "PySide6.QtGraphs",
    "PySide6.QtBluetooth", "PySide6.QtNfc", "PySide6.QtPositioning",
    "PySide6.QtLocation", "PySide6.QtSerialPort", "PySide6.QtSerialBus",
    "PySide6.QtSql", "PySide6.QtTest", "PySide6.QtDesigner",
    "PySide6.QtHelp", "PySide6.QtUiTools", "PySide6.QtSvgWidgets",
    "PySide6.QtPdf", "PySide6.QtPdfWidgets", "PySide6.QtSpatialAudio",
    "PySide6.QtRemoteObjects", "PySide6.QtScxml", "PySide6.QtStateMachine",
    "PySide6.QtTextToSpeech", "PySide6.QtWebChannel", "PySide6.QtWebSockets",
    "PySide6.QtHttpServer", "PySide6.QtNetworkAuth", "PySide6.QtConcurrent",
    "tkinter", "unittest", "pydoc", "doctest", "test", "distutils",
    "setuptools", "pip", "PyInstaller", "matplotlib", "scipy", "pandas",
    "PIL", "pytest", "IPython", "jedi",
]

datas = [
    (str(APP / "assets" / "icon.png"), "voxqueue/assets"),
    (str(APP / "assets" / "icon-128.png"), "voxqueue/assets"),
]
# The .ico is only meaningful on Windows, but harmless elsewhere.
ico = APP / "assets" / "icon.ico"
if ico.exists():
    datas.append((str(ico), "voxqueue/assets"))

hiddenimports = (
    collect_submodules("voxqueue")
    + [
        "PySide6.QtMultimedia",
        "PySide6.QtMultimediaWidgets",
        "PySide6.QtNetwork",
        "numpy",
    ]
)

icon_file = str(ico) if (ico.exists() and sys.platform == "win32") else None

# Linux: bundle the xcb libraries Qt's platform plugin links against.
#
# Qt 6.5+ refuses to load its xcb plugin unless libxcb-cursor and friends are
# present, and on a plain Ubuntu install they are not. The symptom is brutal and
# silent: the process aborts with "Could not load the Qt platform plugin xcb" and
# no window ever appears.
#
# Shipping them makes the package genuinely standalone — no apt step, no sudo.
# The files are the Ubuntu 22.04 (jammy) builds, chosen because they need no
# newer than glibc 2.33; the 24.04 builds need 2.38 and would refuse to load on
# 22.04 with the same class of error. PyInstaller's bootloader puts the bundle
# directory on LD_LIBRARY_PATH, so files at the root are found automatically.
binaries = []
if sys.platform.startswith("linux"):
    shim = ROOT / "build" / "linux-libs"
    if shim.is_dir():
        for so in sorted(shim.glob("*.so.*")):
            binaries.append((str(so), "."))
        print(f"bundling {len(binaries)} xcb libraries from {shim}")

a = Analysis(
    [str(ROOT / "run.py")],
    pathex=[str(ROOT)],
    binaries=binaries,
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=EXCLUDES,
    noarchive=False,
    optimize=0,
)

pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name="VoxQueue",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,          # UPX breaks Qt plugins more often than it saves space
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,      # windowed app: no console on Windows
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=icon_file,
)
