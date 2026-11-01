"""The Log tab: everything the app recorded, with the actions a user needs.

It reads the in-memory ring rather than the file. The file is the durable copy;
parsing a ten-megabyte log to fill a text box would stall the UI for seconds
every time the tab is opened.
"""

from __future__ import annotations

import os
import subprocess
import sys

from PySide6.QtCore import Qt
from PySide6.QtWidgets import (QHBoxLayout, QLabel, QMessageBox, QPlainTextEdit,
                               QPushButton, QVBoxLayout, QWidget)

from ..core import applog


class LogTab(QWidget):
    def __init__(self, ctx, parent=None) -> None:
        super().__init__(parent)
        self.ctx = ctx

        root = QVBoxLayout(self)
        root.setContentsMargins(12, 12, 12, 12)
        root.setSpacing(8)

        row = QHBoxLayout()
        self.btn_refresh = QPushButton()
        self.btn_open = QPushButton()
        self.btn_copy = QPushButton()
        self.btn_clear = QPushButton()
        self.btn_clear.setObjectName("Danger")
        for b in (self.btn_refresh, self.btn_open, self.btn_copy, self.btn_clear):
            row.addWidget(b)
        row.addStretch(1)
        root.addLayout(row)

        self.text = QPlainTextEdit()
        self.text.setReadOnly(True)
        self.text.setLineWrapMode(QPlainTextEdit.NoWrap)
        root.addWidget(self.text, 1)

        self.count = QLabel()
        self.count.setObjectName("Dim")
        root.addWidget(self.count)

        self.btn_refresh.clicked.connect(self.reload)
        self.btn_open.clicked.connect(self.open_file)
        self.btn_copy.clicked.connect(self.copy_all)
        self.btn_clear.clicked.connect(self.clear_log)

        self.retranslate()

    def retranslate(self) -> None:
        tr = self.ctx.tr.t
        self.btn_refresh.setText(tr("log_refresh"))
        self.btn_open.setText(tr("log_open"))
        self.btn_copy.setText(tr("log_copy"))
        self.btn_clear.setText(tr("log_clear"))
        self._update_count()

    def apply_theme(self) -> None:
        pass

    def reload(self) -> None:
        """Re-reads the ring and scrolls to the end — the newest line matters."""
        self.text.setPlainText("\n".join(applog.lines()))
        self._update_count()

        bar = self.text.verticalScrollBar()
        bar.setValue(bar.maximum())

    def _update_count(self) -> None:
        tr = self.ctx.tr.t
        self.count.setText(f"{tr('log_lines')}: {len(applog.lines())}   ·   {applog.log_path()}")

    def open_file(self) -> None:
        try:
            path = applog.log_path()
            path.parent.mkdir(parents=True, exist_ok=True)
            if not path.exists():
                # Create it so the button opens an empty file rather than failing.
                path.write_text("", encoding="utf-8")

            if sys.platform == "win32":
                os.startfile(str(path))  # noqa: S606
            elif sys.platform == "darwin":
                subprocess.run(["open", str(path)], check=False)
            else:
                subprocess.run(["xdg-open", str(path)], check=False)
        except Exception as exc:
            applog.error("log: cannot open file", exc)
            QMessageBox.warning(self, self.ctx.tr.t("app_title"), str(exc))

    def copy_all(self) -> None:
        from PySide6.QtWidgets import QApplication
        QApplication.clipboard().setText(self.text.toPlainText())

    def clear_log(self) -> None:
        tr = self.ctx.tr.t
        answer = QMessageBox.question(
            self, tr("app_title"), self.ctx.tr.hint("log_clear_warn"),
            QMessageBox.Yes | QMessageBox.No, QMessageBox.No)

        if answer != QMessageBox.Yes:
            return

        applog.clear()
        applog.info("log cleared by user")
        self.reload()
