"""Settings: connection, appearance, language, defaults, data removal."""

from __future__ import annotations

from PySide6.QtCore import Qt, Signal
from PySide6.QtWidgets import (QCheckBox, QComboBox, QFileDialog, QHBoxLayout,
                               QLabel, QLineEdit, QMessageBox, QPushButton,
                               QVBoxLayout, QWidget)

from ..i18n import LANGS
from .widgets import Card, Texts


class SettingsTab(QWidget):
    theme_changed = Signal(str)
    language_changed = Signal(str)
    voices_reloaded = Signal()

    def __init__(self, ctx, parent=None) -> None:
        super().__init__(parent)
        self.ctx = ctx
        self._texts = Texts()
        self._build()
        self._load()

    def _build(self) -> None:
        tr = self.ctx.tr.t
        root = QVBoxLayout(self)
        root.setContentsMargins(0, 0, 0, 0)
        root.setSpacing(12)

        # ---------------- connection ----------------
        conn = Card()
        conn.add(self._texts.section(tr, "connection"))

        row = QHBoxLayout()
        self.ed_url = QLineEdit()
        self.ed_url.setPlaceholderText("http://127.0.0.1:3900")
        row.addWidget(self._texts.label(tr, "base_url"))
        row.addWidget(self.ed_url, 1)
        conn.body().addLayout(row)

        row2 = QHBoxLayout()
        self.ed_key = QLineEdit()
        self.ed_key.setEchoMode(QLineEdit.Password)
        self.ed_key.setPlaceholderText("…")
        self.btn_show = QPushButton("👁")
        self.btn_show.setObjectName("Ghost")
        self.btn_show.setFixedWidth(38)
        self.btn_show.setCheckable(True)
        self.btn_show.toggled.connect(
            lambda on: self.ed_key.setEchoMode(
                QLineEdit.Normal if on else QLineEdit.Password))
        row2.addWidget(self._texts.label(tr, "api_key"))
        row2.addWidget(self.ed_key, 1)
        row2.addWidget(self.btn_show)
        conn.body().addLayout(row2)

        row3 = QHBoxLayout()
        self.btn_test = QPushButton(tr("test_connection"))
        self.btn_test.clicked.connect(self._test)
        self.btn_voices = QPushButton(tr("refresh_voices"))
        self.btn_voices.clicked.connect(self._reload_voices)
        self.lbl_conn = QLabel("—")
        self.lbl_conn.setObjectName("Dim")
        row3.addWidget(self.btn_test)
        row3.addWidget(self.btn_voices)
        row3.addWidget(self.lbl_conn, 1)
        conn.body().addLayout(row3)
        root.addWidget(conn)

        # ---------------- appearance ----------------
        look = Card(alt=True)
        look.add(self._texts.section(tr, "appearance"))
        row = QHBoxLayout()
        self.cmb_theme = QComboBox()
        self.cmb_theme.addItem(tr("theme_dark"), "dark")
        self.cmb_theme.addItem(tr("theme_light"), "light")
        self.cmb_theme.addItem(tr("theme_system"), "system")
        self.cmb_theme.currentIndexChanged.connect(self._theme_changed)
        row.addWidget(self._texts.label(tr, "theme"))
        row.addWidget(self.cmb_theme, 1)
        look.body().addLayout(row)

        row = QHBoxLayout()
        self.cmb_lang = QComboBox()

        # "System" first and selected by default: most people never pick a
        # language, and following the operating system is the right answer for
        # them. It was previously added lazily inside _load, only when the saved
        # value was already "system" and did not match an entry — so a fresh
        # install had no such option in the list at all, and the app looked like
        # it had no way to follow the system locale.
        self.cmb_lang.addItem(tr("language_system"), "system")
        for code, name in LANGS:
            self.cmb_lang.addItem(name, code)
        self.cmb_lang.currentIndexChanged.connect(self._language_changed)
        row.addWidget(self._texts.label(tr, "language"))
        row.addWidget(self.cmb_lang, 1)
        # The row has to be added before `row` is reused below. It was not, so the
        # language selector was built, wired to its handler, and then dropped: the
        # variable was reassigned to the colouring row and only that one reached the
        # card. The combo existed and worked, but was never part of any layout, so
        # it was invisible — which reads as "there is no language setting".
        look.body().addLayout(row)

        # Colouring is the most expensive thing the editor does; switching it off
        # is a real speed trade for very large scripts.
        self.chk_colour = QCheckBox(tr("colour_text"))
        self.chk_colour.setChecked(bool(self.ctx.cfg.get("colour_text", True)))
        self.chk_colour.toggled.connect(self._on_colour_toggled)
        row = QHBoxLayout()
        row.addWidget(self.chk_colour, 1)
        look.body().addLayout(row)
        root.addWidget(look)

        # ---------------- defaults ----------------
        dflt = Card(alt=True)
        dflt.add(self._texts.section(tr, "defaults"))
        row = QHBoxLayout()
        self.ed_out = QLineEdit()
        btn = QPushButton(tr("browse"))
        self.btn_browse = btn
        btn.clicked.connect(self._browse_out)
        row.addWidget(self.ed_out, 1)
        row.addWidget(btn)
        dflt.body().addLayout(row)

        row = QHBoxLayout()
        self.cmb_fmt = QComboBox()
        for f in ("wav", "mp3", "ogg", "flac"):
            self.cmb_fmt.addItem(f.upper(), f)
        self.cmb_rate = QComboBox()
        for r in (22050, 24000, 44100, 48000):
            self.cmb_rate.addItem(f"{r} Hz", r)
        row.addWidget(self._texts.label(tr, "audio_format"))
        row.addWidget(self.cmb_fmt)
        row.addSpacing(14)
        row.addWidget(self._texts.label(tr, "sample_rate"))
        row.addWidget(self.cmb_rate)
        row.addStretch(1)
        dflt.body().addLayout(row)

        row = QHBoxLayout()
        self.ed_template = QLineEdit("{index}")
        self.ed_template.setFixedWidth(140)
        row.addWidget(self._texts.label(tr, "filename_template"))
        row.addWidget(self.ed_template)
        row.addWidget(QLabel("{index} → 0000"))
        row.addStretch(1)
        dflt.body().addLayout(row)
        dflt.add(self._texts.hint(tr, "metadata_note"))
        dflt.add(self._texts.hint(tr, "resume_note"))
        root.addWidget(dflt)

        # ---------------- actions ----------------
        actions = QHBoxLayout()
        self.btn_save = QPushButton(tr("save_settings"))
        self.btn_save.setObjectName("Primary")
        self.btn_save.clicked.connect(self._save)
        self.btn_wipe = QPushButton(tr("delete_data"))
        self.btn_wipe.setObjectName("Danger")
        self.btn_wipe.clicked.connect(self._wipe)
        actions.addWidget(self.btn_save)
        actions.addStretch(1)
        actions.addWidget(self.btn_wipe)
        root.addLayout(actions)
        root.addStretch(1)

    # ------------------------------------------------------------------ state
    def _load(self) -> None:
        c = self.ctx.cfg
        self.ed_url.setText(str(c.get("base_url") or ""))
        self.ed_key.setText(str(c.get("api_key") or ""))
        self.ed_out.setText(str(c.get("output_dir") or ""))
        self.ed_template.setText(str(c.get("train_template") or "{index}"))

        i = self.cmb_theme.findData(c.get("theme"))
        self.cmb_theme.setCurrentIndex(i if i >= 0 else 0)

        # "system" is a real entry in the list now, so an unknown saved code just
        # falls back to it instead of appending an English label at the bottom.
        lang = c.get("language") or "system"
        j = self.cmb_lang.findData(lang)
        if j < 0:
            j = self.cmb_lang.findData("system")
        self.cmb_lang.setCurrentIndex(max(0, j))

        k = self.cmb_fmt.findData(c.get("audio_format") or "wav")
        self.cmb_fmt.setCurrentIndex(max(0, k))
        m = self.cmb_rate.findData(int(c.get("sample_rate") or 22050))
        self.cmb_rate.setCurrentIndex(max(0, m))

    def _save(self) -> None:
        c = self.ctx.cfg
        c.update(
            base_url=self.ed_url.text().strip(),
            api_key=self.ed_key.text().strip(),
            output_dir=self.ed_out.text().strip(),
            train_template=self.ed_template.text().strip() or "{index}",
            audio_format=self.cmb_fmt.currentData(),
            sample_rate=self.cmb_rate.currentData(),
        )
        c.save()
        self.ctx.invalidate_client()
        self.lbl_conn.setText(self.ctx.tr.t("saved"))

    # -------------------------------------------------------------- callbacks
    def _test(self) -> None:
        self._save()
        ok, msg = self.ctx.client.ping()
        self.lbl_conn.setText(("✓ " if ok else "✕ ") + msg)
        self.lbl_conn.setStyleSheet("color:#4ade80;" if ok else "color:#f87171;")

    def _reload_voices(self) -> None:
        self._save()
        ok, msg = self.ctx.refresh_voices()
        self.lbl_conn.setText(("✓ " if ok else "✕ ") + msg)
        self.lbl_conn.setStyleSheet("color:#4ade80;" if ok else "color:#f87171;")
        if ok:
            self.voices_reloaded.emit()

    def _theme_changed(self) -> None:
        val = self.cmb_theme.currentData()
        self.ctx.theme_name = val
        self.ctx.cfg.set("theme", val)
        self.ctx.cfg.save()
        self.theme_changed.emit(val)

    def _language_changed(self) -> None:
        code = self.cmb_lang.currentData()
        self.ctx.cfg.set("language", code)
        self.ctx.cfg.save()
        self.ctx.tr.set_language(code)
        self.language_changed.emit(code)

    def _browse_out(self) -> None:
        d = QFileDialog.getExistingDirectory(self, self.ctx.tr.t("output_dir"),
                                             self.ed_out.text())
        if d:
            self.ed_out.setText(d)

    def _wipe(self) -> None:
        tr = self.ctx.tr.t
        if QMessageBox.question(self, tr("app_title"), tr("delete_warn"),
                                QMessageBox.Yes | QMessageBox.No) != QMessageBox.Yes:
            return
        self.ctx.cfg.wipe()
        QMessageBox.information(self, tr("app_title"), tr("saved"))

    def _on_colour_toggled(self, checked: bool) -> None:
        self.ctx.cfg.set("colour_text", bool(checked))
        try:
            self.ctx.cfg.save()
        except Exception:
            pass
        applog.info("colour text: " + ("on" if checked else "off"))
        apply_colouring(self.ctx)

    def retranslate(self) -> None:
        """Re-reads every visible string in the active language.

        The labels, section headers and hints come from the registry, so a new
        one cannot be added without being translated. Buttons are set here
        because they carry no key of their own.
        """
        tr = self.ctx.tr.t

        self._texts.apply(tr)

        self.btn_test.setText(tr("test_connection"))
        self.btn_voices.setText(tr("refresh_voices"))
        self.btn_browse.setText(tr("browse"))
        self.btn_save.setText(tr("save_settings"))
        self.btn_wipe.setText(tr("delete_data"))
        self.chk_colour.setText(tr("colour_text"))

        # The combo box holds a code and a label; the label has to be replaced
        # without changing which entry is selected.
        # Entry 0 of the language list is "System", which is a normal translated
        # string, unlike the language names themselves.
        self.cmb_lang.blockSignals(True)
        self.cmb_lang.setItemText(0, tr("language_system"))
        self.cmb_lang.blockSignals(False)

        self.cmb_theme.blockSignals(True)
        self.cmb_theme.setItemText(0, tr("theme_dark"))
        self.cmb_theme.setItemText(1, tr("theme_light"))
        self.cmb_theme.setItemText(2, tr("theme_system"))
        self.cmb_theme.blockSignals(False)


def apply_colouring(ctx) -> None:
    """Pushes the colouring setting into every editor that is already built."""
    enabled = bool(ctx.cfg.get("colour_text", True))
    main = ctx.window if hasattr(ctx, "window") else None
    for attr in ("tab_train", "tab_dub", "tab_bulk"):
        panel = getattr(main, attr, None) if main else None
        editor = getattr(panel, "text", None) if panel else None
        if editor is not None and hasattr(editor, "set_colouring"):
            editor.set_colouring(enabled)
