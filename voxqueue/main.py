"""Entry point.

Run as a module (`python -m voxqueue.main`) or from the frozen executable. The
frozen build resolves bundled assets relative to `sys._MEIPASS`, which is why
resource lookup goes through `resource_path()` rather than `__file__`.
"""

from __future__ import annotations

import sys
from pathlib import Path

from PySide6.QtCore import Qt
from PySide6.QtGui import QIcon
from PySide6.QtWidgets import QApplication

from . import APP_NAME, ORG_NAME, __version__
from .core.context import Context


def resource_path(rel: str) -> str:
    """Path to a bundled resource, both in source and in a PyInstaller build."""
    base = getattr(sys, "_MEIPASS", None)
    if base:
        p = Path(base) / rel
        if p.exists():
            return str(p)
    return str(Path(__file__).resolve().parent / rel)


def main() -> int:
    QApplication.setHighDpiScaleFactorRoundingPolicy(
        Qt.HighDpiScaleFactorRoundingPolicy.PassThrough)
    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)
    app.setApplicationDisplayName(APP_NAME)
    app.setOrganizationName(ORG_NAME)
    app.setApplicationVersion(__version__)

    icon = resource_path("assets/icon.png")
    if Path(icon).exists():
        app.setWindowIcon(QIcon(icon))

    ctx = Context()
    from .ui.main_window import MainWindow
    win = MainWindow(ctx, icon)
    win.show()
    win.boot()
    return app.exec()


if __name__ == "__main__":
    sys.exit(main())
