"""Main window: header, tabs, theme switching and language refresh."""

from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import Qt, QTimer
from PySide6.QtGui import QIcon
from PySide6.QtWidgets import (QHBoxLayout, QLabel, QMainWindow, QTabWidget,
                               QVBoxLayout, QWidget)

from .. import APP_NAME, __version__
from . import theme
from .log_tab import LogTab
from .settings_tab import SettingsTab
from .work_tab import WorkTab


class MainWindow(QMainWindow):
    def __init__(self, ctx, icon_path: str = "") -> None:
        super().__init__()
        self.ctx = ctx
        self.setWindowTitle(f"{APP_NAME} {__version__}")
        if icon_path:
            self.setWindowIcon(QIcon(icon_path))
        self.setMinimumSize(1120, 720)
        self.setAcceptDrops(True)

        # The settings tab needs a way to reach the editors it does not own.
        self.ctx.window = self
        self._build()
        self.apply_theme()
        self._restore_geometry()
        # Restoring the session switches the tab, so it runs after the geometry
        # is in place — otherwise the resize would fight the tab change.
        self.restore_session()

    # ------------------------------------------------------------------ build
    def _build(self) -> None:
        root = QWidget()
        root.setObjectName("Root")
        self.setCentralWidget(root)
        lay = QVBoxLayout(root)
        lay.setContentsMargins(18, 14, 18, 16)
        lay.setSpacing(10)

        # header
        head = QHBoxLayout()
        self.lbl_title = QLabel(APP_NAME)
        self.lbl_title.setObjectName("Title")
        self.lbl_sub = QLabel(self.ctx.tr.t("app_subtitle"))
        self.lbl_sub.setObjectName("Subtitle")
        col = QVBoxLayout()
        col.setSpacing(0)
        col.addWidget(self.lbl_title)
        col.addWidget(self.lbl_sub)
        head.addLayout(col)
        head.addStretch(1)
        self.lbl_ver = QLabel(f"v{__version__}")
        self.lbl_ver.setObjectName("Subtitle")
        head.addWidget(self.lbl_ver)
        lay.addLayout(head)

        # tabs
        self.tabs = QTabWidget()
        self.tab_train = WorkTab(self.ctx, "train")
        self.tab_dub = WorkTab(self.ctx, "dub")
        self.tab_bulk = WorkTab(self.ctx, "bulk")
        self.tab_settings = SettingsTab(self.ctx)
        self.tab_log = LogTab(self.ctx)

        self.tabs.addTab(self.tab_train, self.ctx.tr.t("tab_train"))
        self.tabs.addTab(self.tab_dub, self.ctx.tr.t("tab_dub"))
        self.tabs.addTab(self.tab_bulk, self.ctx.tr.t("tab_bulk"))
        self.tabs.addTab(self.tab_settings, self.ctx.tr.t("tab_settings"))
        self.tabs.addTab(self.tab_log, self.ctx.tr.t("tab_log"))
        lay.addWidget(self.tabs, 1)

        self.tab_settings.theme_changed.connect(lambda _: self.apply_theme())
        self.tab_settings.language_changed.connect(lambda _: self.retranslate())
        self.tab_settings.voices_reloaded.connect(self._voices_reloaded)

        last = int(self.ctx.cfg.get("last_tab") or 0)
        if 0 <= last < self.tabs.count():
            self.tabs.setCurrentIndex(last)
        self.tabs.currentChanged.connect(
            lambda i: (self.ctx.cfg.set("last_tab", i), self.ctx.cfg.save()))

    # ------------------------------------------------------------------ theme
    def apply_theme(self) -> None:
        name = self.ctx.palette_name()
        p = theme.palette(name)
        self.setStyleSheet(theme.stylesheet(p))
        for t in (self.tab_train, self.tab_dub, self.tab_bulk):
            t.table.apply_colors(name)
            # Marker colours live in the highlighter, not the stylesheet, so the
            # editor has to be told which palette is active.
            t.text.apply_theme(name)

    # --------------------------------------------------------------- language
    # ------------------------------------------------------------- drag & drop
    def dragEnterEvent(self, event) -> None:  # noqa: N802
        if self._dropped_project(event):
            event.setDropAction(Qt.CopyAction)
            event.accept()
            self._set_drop_hot(True)
        else:
            event.ignore()

    def dragLeaveEvent(self, event) -> None:  # noqa: N802
        self._set_drop_hot(False)
        super().dragLeaveEvent(event)

    def _set_drop_hot(self, hot: bool) -> None:
        """Highlights the window while a .vqproj hovers.

        A stylesheet banner rather than a layout widget: an overlay that takes
        part in layout would shift the editor underneath it.
        """
        if getattr(self, "_drop_hot", False) == hot:
            return
        self._drop_hot = hot

        banner = getattr(self, "_drop_banner", None)
        if banner is None:
            from PySide6.QtWidgets import QLabel
            banner = QLabel(self.ctx.tr.t("drop_project_hint"), self)
            banner.setObjectName("DropBanner")
            banner.setAlignment(Qt.AlignCenter)
            banner.setStyleSheet(
                "QLabel#DropBanner {"
                " border: 2px solid palette(highlight);"
                " border-radius: 8px;"
                " padding: 10px 18px;"
                " font-weight: bold;"
                " background: palette(window); }")
            banner.adjustSize()
            self._drop_banner = banner

        if hot:
            banner.move((self.width() - banner.width()) // 2,
                        (self.height() - banner.height()) // 2)
            banner.raise_()
        banner.setVisible(hot)

    def dropEvent(self, event) -> None:  # noqa: N802
        self._set_drop_hot(False)
        path = self._dropped_project(event)
        if not path:
            event.ignore()
            return
        event.accept()
        self._open_dropped_project(path)

    @staticmethod
    def _dropped_project(event) -> str:
        """Path of a .vqproj among the dropped files, or an empty string."""
        try:
            urls = event.mimeData().urls()
        except Exception:
            return ""
        for url in urls or []:
            p = url.toLocalFile()
            if p.lower().endswith(".vqproj"):
                return p
        return ""

    def _open_dropped_project(self, path: str) -> None:
        """Open a dropped project, asking first whether to replace what is open.

        Replacing silently would throw away the tab's contents, and a drop is an
        easy gesture to make by accident. The tab comes from the file's own mode,
        so a dub project does not land in the training tab.
        """
        from PySide6.QtWidgets import QMessageBox
        from ..core import workspace

        tr = self.ctx.tr.t
        try:
            proj = workspace.load(path)
        except Exception:
            proj = None

        if proj is None:
            QMessageBox.warning(self, tr("app_title"), tr("load_project"))
            return

        index = {"dub": 1, "bulk": 2}.get(proj.mode, 0)
        panel = self._panel_for(index)
        if panel is None:
            return

        if panel.has_work():
            answer = QMessageBox.question(
                self, tr("app_title"), tr("drop_replace_warn"),
                QMessageBox.Yes | QMessageBox.No, QMessageBox.No)
            if answer != QMessageBox.Yes:
                return

        if not panel.apply_workspace(proj):
            return

        if self.tabs.currentIndex() != index:
            self.tabs.setCurrentIndex(index)
        self.setWindowTitle(f"{APP_NAME} {__version__} - {Path(path).name}")

    def _panel_for(self, index: int):
        return {0: self.tab_train, 1: self.tab_dub, 2: self.tab_bulk}.get(index)

    def restore_session(self) -> None:
        """Bring back every tab from the previous session.

        The slot is left alone when it cannot be read, so a corrupt file costs one
        empty start rather than losing the text; the next close overwrites it.
        """
        if not self.ctx.cfg.get("restore_session", True):
            return

        from ..core import workspace
        loaded = workspace.load_session()
        if loaded is None:
            return

        projects, active = loaded
        for mode, proj in projects.items():
            index = {"dub": 1, "bulk": 2}.get(mode, 0)
            panel = self._panel_for(index)
            if panel is not None:
                panel.apply_workspace(proj)

        if 0 <= active <= 2 and self.tabs.currentIndex() != active:
            self.tabs.setCurrentIndex(active)


    def retranslate(self) -> None:
        tr = self.ctx.tr.t
        self.lbl_sub.setText(tr("app_subtitle"))
        self.tabs.setTabText(0, tr("tab_train"))
        self.tabs.setTabText(1, tr("tab_dub"))
        self.tabs.setTabText(2, tr("tab_bulk"))
        self.tabs.setTabText(3, tr("tab_settings"))
        self.tabs.setTabText(4, tr("tab_log"))
        for t in (self.tab_train, self.tab_dub, self.tab_bulk):
            t.retranslate()
        self.tab_settings.retranslate()

    # ---------------------------------------------------------------- geometry
    def _restore_geometry(self) -> None:
        geo = self.ctx.cfg.get("window")
        try:
            if isinstance(geo, list) and len(geo) == 4:
                self.setGeometry(*[int(x) for x in geo])
        except Exception:
            pass

        # Maximised state is restored separately: a maximised window has no
        # meaningful restore geometry of its own, so storing its size would make
        # the next start open "large but not maximised".
        try:
            if self.ctx.cfg.get("window_max") is True:
                self.setWindowState(self.windowState() | Qt.WindowMaximized)
        except Exception:
            pass

    def closeEvent(self, event) -> None:  # noqa: N802
        try:
            maximised = bool(self.windowState() & Qt.WindowMaximized)
            self.ctx.cfg.set("window_max", maximised)
            # Only the normal geometry is a size the user chose; a maximised
            # window reports the screen size.
            if not maximised:
                g = self.geometry()
                self.ctx.cfg.set("window", [g.x(), g.y(), g.width(), g.height()])
            self.ctx.cfg.save()
        except Exception:
            pass
        # Save EVERY tab, not just the one in front: the slot is rewritten on each
        # close, so saving only the active tab discarded the other two.
        try:
            from ..core import workspace
            bundle = {}
            for panel in (self.tab_train, self.tab_dub, self.tab_bulk):
                proj = panel.build_workspace() if panel else None
                if proj is not None:
                    bundle[proj.mode] = proj
            workspace.save_session(bundle, self.tabs.currentIndex())
        except Exception:
            pass

        for t in (self.tab_train, self.tab_dub, self.tab_bulk):
            if t.worker and t.worker.isRunning():
                t.worker.cancel()
                t.worker.wait(3000)
        super().closeEvent(event)

    # ------------------------------------------------------------------ voices
    def _voices_reloaded(self) -> None:
        self.ctx.notify()

    def boot(self) -> None:
        """Background start-up work: try the saved server once, quietly."""
        def attempt() -> None:
            ok, _ = self.ctx.client.ping()
            if ok:
                self.ctx.refresh_voices()
        QTimer.singleShot(250, attempt)
