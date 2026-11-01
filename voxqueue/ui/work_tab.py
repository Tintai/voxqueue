"""The working tab. One class, three modes — train / dub / bulk.

They differ only in how the paste is parsed and where the files land, so making
three near-identical tabs would guarantee they drift apart. The mode decides:

    train  line -> clip, output wav/0000.wav + metadata.csv
    dub    NAME: line -> clip, output voices/<NAME>/<NAME>_0000_2.4s.wav
    bulk   marker -> clip, output wav/0000.wav + 0000.txt
"""

from __future__ import annotations

import webbrowser
from pathlib import Path

from PySide6.QtCore import Qt, QTimer
from PySide6.QtWidgets import (QAbstractItemView, QCheckBox, QComboBox, QFileDialog,
                               QScrollArea,
                               QHBoxLayout, QHeaderView, QLabel, QLineEdit,
                               QMessageBox, QProgressBar, QPushButton, QSpinBox,
                               QSplitter, QTableWidget, QTableWidgetItem,
                               QVBoxLayout, QWidget)

from ..core import splitter as sp
from ..core.state import Item, Job
from ..core.worker import QueueWorker, write_metadata
from .widgets import Card, FlowLayout, ItemTable, ScriptEdit, Texts, hint, section


def tr_t(ctx, key: str) -> str:
    """Shorthand for a translated string, used by the handlers below."""
    return ctx.tr.t(key)

MODE_INFO = {
    "train": ("tab_train", "train_hint"),
    "dub": ("tab_dub", "dub_hint"),
    "bulk": ("tab_bulk", "bulk_hint"),
}


class WorkTab(QWidget):
    def __init__(self, ctx, mode: str, parent=None) -> None:
        super().__init__(parent)
        self.ctx = ctx
        self.mode = mode
        self._texts = Texts()
        self.job: Job | None = None
        self.worker: QueueWorker | None = None
        self._run_total = 0
        self._run_done = 0
        self._showing_playback = False
        self._speaker_rows: dict[str, QComboBox] = {}
        self._build()
        ctx.subscribe(self._on_context_changed)

    # ------------------------------------------------------------------ build
    def _build(self) -> None:
        tr = self.ctx.tr.t
        outer = QVBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)

        split = QSplitter(Qt.Horizontal)
        outer.addWidget(split)

        # ---------------- left: text input ----------------
        left = Card()
        title_key, hint_key = MODE_INFO[self.mode]
        head = self._texts.label(tr, title_key)
        head.setObjectName("Title")
        left.add(head)
        left.add(self._texts.hint(tr, hint_key))

        self.text = ScriptEdit(tr("paste_hint"))
        self.text.file_dropped.connect(self._load_file_path)
        left.add(self.text)

        row = QHBoxLayout()
        self.btn_load = QPushButton(tr("load_file"))
        self.btn_load.clicked.connect(self._browse_file)
        self.btn_save_proj = QPushButton(tr("save_project"))
        self.btn_load_proj = QPushButton(tr("load_project"))
        self.btn_save_proj.clicked.connect(self._save_project)
        self.btn_load_proj.clicked.connect(self._load_project)

        self.btn_parse = QPushButton(tr("build_plan"))
        self.btn_parse.setObjectName("Primary")
        self.btn_parse.clicked.connect(self._parse)
        self.btn_clear = QPushButton(tr("clear"))
        self.btn_clear.setObjectName("Ghost")
        self.btn_clear.clicked.connect(lambda: self.text.clear())
        row.addWidget(self.btn_load)
        row.addStretch(1)
        row.addWidget(self.btn_clear)
        self.btn_bulk_pron = QPushButton(tr("bulk_pron"))
        self.btn_bulk_pron.clicked.connect(self._bulk_pronunciation)
        row.addWidget(self.btn_bulk_pron)
        row.addWidget(self.btn_parse)
        left.body().addLayout(row)

        self.lbl_parsed = QLabel("")
        self.lbl_parsed.setObjectName("Dim")
        left.add(self.lbl_parsed)

        split.addWidget(left)

        # ---------------- right: settings + queue ----------------
        right = QWidget()
        rlay = QVBoxLayout(right)
        rlay.setContentsMargins(0, 0, 0, 0)
        rlay.setSpacing(10)

        top = QWidget()
        top.setObjectName("TopBlock")
        tlay = QVBoxLayout(top)
        tlay.setContentsMargins(0, 0, 0, 0)
        tlay.setSpacing(10)

        opts = Card(alt=True)
        opts.add(self._texts.section(tr, "connection"))

        grid = QHBoxLayout()
        self.cmb_voice = QComboBox()
        self.cmb_voice.setMinimumWidth(240)
        self.cmb_voice.setMinimumHeight(34)
        self.cmb_voice.currentIndexChanged.connect(self._remember_voice)
        grid.addWidget(self._texts.label(tr, "voice"))
        grid.addWidget(self.cmb_voice, 1)
        opts.body().addLayout(grid)

        outrow = QHBoxLayout()
        self.ed_out = QLineEdit(str(self.ctx.cfg.get("output_dir")))
        self.btn_browse = QPushButton(tr("browse"))
        btn_out = self.btn_browse
        btn_out.clicked.connect(self._browse_out)
        outrow.addWidget(self._texts.label(tr, "output_dir"))
        outrow.addWidget(self.ed_out, 1)
        outrow.addWidget(btn_out)
        opts.body().addLayout(outrow)

        checks = QHBoxLayout()
        self.chk_norm = QCheckBox(tr("normalize"))
        self.chk_norm.setChecked(bool(self.ctx.cfg.get("normalize", True)))
        self.chk_def = QCheckBox(tr("detect_defects"))
        self.chk_def.setChecked(bool(self.ctx.cfg.get("detect_defects", True)))
        self.chk_ext = QCheckBox(tr("add_extension"))
        self.chk_ext.setChecked(bool(self.ctx.cfg.get("add_extension", True)))
        self.chk_meta = QCheckBox(tr("metadata_spoken"))
        self.chk_meta.setChecked(bool(self.ctx.cfg.get("metadata_spoken", True)))
        checks.addWidget(self.chk_norm)
        checks.addWidget(self.chk_def)
        # Shutdown is opt-in and never persisted: a setting that silently powers
        # the machine off after the next run is a trap.
        self.chk_shutdown = QCheckBox(tr("shutdown_when_done"))

        checks.addWidget(self.chk_ext)
        checks.addWidget(self.chk_meta)
        checks.addWidget(self.chk_shutdown)
        checks.addStretch(1)
        opts.body().addLayout(checks)

        extra = QHBoxLayout()
        if self.mode == "bulk":
            self.ed_marker = QLineEdit(str(self.ctx.cfg.get("split_marker") or "###"))
            self.ed_marker.setFixedWidth(70)
            self.chk_txt = QCheckBox(tr("write_transcript"))
            self.chk_txt.setChecked(True)
            extra.addWidget(self._texts.label(tr, "split_marker"))
            extra.addWidget(self.ed_marker)
            extra.addWidget(self.chk_txt)
        self.spn_start = QSpinBox()
        self.spn_start.setRange(0, 999000)
        # Numbering always begins at 0000. index_start is deliberately not
        # persisted: a value nudged once used to leak into the next dataset.
        self.spn_start.setValue(0)
        extra.addWidget(self._texts.label(tr, "index_start"))
        extra.addWidget(self.spn_start)
        extra.addStretch(1)
        opts.body().addLayout(extra)

        # speaker -> voice mapping (dub only).
        # Collapsed by default: on an 800 px window the settings block, the
        # speaker map, the controls and the work list do not all fit, and a
        # layout that silently overlaps its own buttons is worse than one
        # extra click.
        self.map_box = Card(alt=True)
        head_row = QHBoxLayout()
        self.btn_map_toggle = QPushButton("▸ " + tr("voice_map"))
        self.btn_map_toggle.setObjectName("Ghost")
        self.btn_map_toggle.setCheckable(True)
        self.btn_map_toggle.setChecked(False)
        self.btn_map_toggle.clicked.connect(self._toggle_map)
        head_row.addWidget(self.btn_map_toggle, 1)
        self.map_box.body().addLayout(head_row)
        self.tbl_map = QTableWidget(0, 2)
        self.tbl_map.setHorizontalHeaderLabels([tr("speaker"), tr("voice")])
        self.tbl_map.verticalHeader().setVisible(False)
        self.tbl_map.verticalHeader().setDefaultSectionSize(36)
        self.tbl_map.setSelectionMode(QAbstractItemView.NoSelection)
        self.tbl_map.setShowGrid(False)
        self.tbl_map.setFrameShape(QTableWidget.NoFrame)
        self.tbl_map.horizontalHeader().setSectionResizeMode(0, QHeaderView.Stretch)
        self.tbl_map.horizontalHeader().setSectionResizeMode(1, QHeaderView.Stretch)
        self.tbl_map.horizontalHeader().setHighlightSections(False)
        self.map_box.add(self.tbl_map)
        self.btn_autoassign = QPushButton(tr("auto_assign"))
        self.btn_autoassign.clicked.connect(self._auto_assign)
        self.map_box.add(self.btn_autoassign)
        self.tbl_map.setVisible(False)
        self.btn_autoassign.setVisible(False)
        if self.mode == "dub":
            tlay.addWidget(opts)
            tlay.addWidget(self.map_box)
        else:
            tlay.addWidget(opts)
        # Absorb leftover height here, not in the cards: without this the last
        # card is stretched and shows a large empty area under its header.
        tlay.addStretch(1)
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QScrollArea.NoFrame)
        scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        scroll.setWidget(top)
        # Hard cap. The settings block never needs more than this, and an
        # uncapped scroll area is what let the cards overlap the buttons: the
        # layout cannot shrink it below its sizeHint, so it stole the space the
        # table needed. Capped, it scrolls internally and the table keeps the
        # remainder.
        scroll.setFixedHeight(330)
        rlay.addWidget(scroll, 0)

        # controls
        #
        # QHBoxLayout does not wrap: on a narrow window the trailing buttons get
        # squeezed until the text is unreadable, and "Clear project" effectively
        # disappears. A FlowLayout wraps instead, so every button stays usable at
        # any window width.
        ctrl_host = QWidget()
        ctrl = FlowLayout(ctrl_host)
        self.btn_start = QPushButton(tr("start"))
        self.btn_start.setObjectName("Primary")
        self.btn_start.clicked.connect(self._start)
        self.btn_pause = QPushButton(tr("pause"))
        self.btn_pause.clicked.connect(self._toggle_pause)
        self.btn_pause.setEnabled(False)
        self.btn_cancel = QPushButton(tr("cancel"))
        self.btn_cancel.setObjectName("Danger")
        self.btn_cancel.clicked.connect(self._cancel)
        self.btn_cancel.setEnabled(False)
        ctrl.addWidget(self.btn_start)
        ctrl.addWidget(self.btn_pause)
        ctrl.addWidget(self.btn_cancel)
        self.btn_regen = QPushButton(tr("regenerate_selected"))
        self.btn_regen.clicked.connect(self._regen_selected)
        self.btn_retry = QPushButton(tr("regenerate_failed"))
        self.btn_retry.clicked.connect(self._retry_failed)
        ctrl.addWidget(self.btn_retry)
        ctrl.addWidget(self.btn_regen)
        self.btn_one = QPushButton(tr("generate_one"))
        self.btn_one.clicked.connect(self._generate_selected)
        ctrl.addWidget(self.btn_one)

        self.btn_open = QPushButton(tr("open_folder"))
        self.btn_open.clicked.connect(self._open_folder)
        ctrl.addWidget(self.btn_open)

        self.btn_clear_project = QPushButton(tr("clear_project"))
        self.btn_clear_project.setObjectName("Danger")
        self.btn_clear_project.clicked.connect(self._clear_project)
        ctrl.addWidget(self.btn_clear_project)
        # Widget z FlowLayout musi byc w drzewie, inaczej nie dostaje geometrii.
        rlay.addWidget(ctrl_host, 0)

        self.bar = QProgressBar()
        self.bar.setTextVisible(False)
        self.lbl_time = QLabel("")
        self.lbl_time.setObjectName("Dim")
        bar_row = QHBoxLayout()
        bar_row.addWidget(self.bar, 1)
        bar_row.addWidget(self.lbl_time, 0)
        rlay.addLayout(bar_row)

        # Follow-the-queue toggle on the same line as the status, so it stays
        # reachable while the queue is working — turning it off mid-run is the
        # whole point.
        self.chk_follow = QCheckBox(tr("auto_scroll"))
        self.chk_follow.setChecked(bool(self.ctx.cfg.get("auto_scroll", True)))
        self.chk_follow.toggled.connect(self._on_follow_toggled)

        status_row = QHBoxLayout()
        # Registered like every other label: the status line holds a translatable
        # string too, and leaving it out meant it kept the previous language after
        # a switch while everything around it changed.
        self.lbl_status = self._texts.label(tr, "status_ready")
        self.lbl_status.setObjectName("Dim")
        # Sorting is a three-way choice, so it is a combo rather than two
        # checkboxes whose combination would need its own rules.
        self.cmb_sort = QComboBox()
        self.cmb_sort.addItems([tr("sort_source"), tr("sort_errors"),
                                tr("sort_pron"), tr("sort_stale")])
        self.cmb_sort.setCurrentIndex(int(self.ctx.cfg.get("sort_mode") or 0))
        self.cmb_sort.currentIndexChanged.connect(self._on_sort_changed)

        # Filter to pinned rows. Separate from the sort dropdown because it is a
        # filter, not an order — mixing the two in one control makes neither clear.
        self.chk_stale_only = QCheckBox(tr("stale_only"))
        self.chk_stale_only.setChecked(bool(self.ctx.cfg.get("stale_only", False)))
        self.chk_stale_only.toggled.connect(self._on_stale_only_toggled)

        self.chk_pinned_only = QCheckBox(tr("pinned_only"))
        self.chk_pinned_only.setChecked(bool(self.ctx.cfg.get("pinned_only", False)))
        self.chk_pinned_only.toggled.connect(self._on_pinned_only_toggled)

        # Search filters as you type: text, pronunciation, speaker or index. A
        # list of ten thousand rows is unusable without it.
        self.ed_search = QLineEdit()
        self.ed_search.setPlaceholderText(tr("search"))
        self.ed_search.setMaximumWidth(200)
        self.ed_search.textChanged.connect(self._apply_sort)

        status_row.addWidget(self.ed_search, 0)
        status_row.addWidget(self.lbl_status, 1)
        status_row.addWidget(self.chk_pinned_only, 0)
        status_row.addWidget(self.chk_stale_only, 0)
        status_row.addWidget(self.cmb_sort, 0)
        status_row.addWidget(self.chk_follow, 0)
        rlay.addLayout(status_row)

        self.table = ItemTable()
        self.table.play_requested.connect(self._play)
        self.table.regen_requested.connect(self._regen_indices)
        self.table.edit_requested.connect(self._edit_pronunciation)
        self.table.pin_requested.connect(self._set_pinned)
        self.table.jump_requested.connect(self._jump_to_index)
        self.table.setMinimumHeight(140)
        rlay.addWidget(self.table, 1)

        rlay.setStretch(0, 0)
        split.addWidget(right)
        split.setStretchFactor(0, 3)
        split.setStretchFactor(1, 4)
        split.setSizes([480, 760])

    # -------------------------------------------------------------- callbacks
    def retranslate(self) -> None:
        tr = self.ctx.tr.t

        # Registered labels, headings and hints first: they carry their own key,
        # so a new one cannot be forgotten. The rest below are widgets whose text
        # is set from a key at the call site rather than at construction.
        self._texts.apply(tr)

        # Registered labels were re-read above, including the status line. A live
        # message ("Running", "Cancel") is transient, so anything that is not a
        # finished run falls back to a neutral Ready rather than staying in the
        # language the run started in.
        if not (self.worker and self.worker.isRunning()):
            self.lbl_status.setText(tr("status_ready"))

        self.btn_browse.setText(tr("browse"))
        self.btn_load.setText(tr("load_file"))
        self.btn_parse.setText(tr("build_plan"))
        self.btn_bulk_pron.setText(tr("bulk_pron"))
        self.btn_clear.setText(tr("clear"))
        self.btn_start.setText(tr("start"))
        self.btn_pause.setText(tr("pause"))
        self.btn_cancel.setText(tr("cancel"))
        self.btn_regen.setText(tr("regenerate_selected"))
        self.btn_retry.setText(tr("regenerate_failed"))
        self.btn_open.setText(tr("open_folder"))
        self.btn_one.setText(tr("generate_one"))
        self.btn_clear_project.setText(tr("clear_project"))
        self.btn_save_proj.setText(tr("save_project"))
        self.btn_load_proj.setText(tr("load_project"))
        self.chk_norm.setText(tr("normalize"))
        self.chk_def.setText(tr("detect_defects"))
        self.chk_ext.setText(tr("add_extension"))
        self.chk_meta.setText(tr("metadata_spoken"))
        self.chk_shutdown.setText(tr("shutdown_when_done"))
        self.chk_follow.setText(tr("auto_scroll"))
        self.chk_pinned_only.setText(tr("pinned_only"))
        self.chk_stale_only.setText(tr("stale_only"))

        # Only exists in Bulk mode, hence the guard — but it was never relabelled,
        # so "Zapisuj transkrypt .txt" survived a switch to any other language.
        if getattr(self, "chk_txt", None) is not None:
            self.chk_txt.setText(tr("write_transcript"))
        self.ed_search.setPlaceholderText(tr("search"))
        sort_idx = self.cmb_sort.currentIndex()
        self.cmb_sort.blockSignals(True)
        self.cmb_sort.clear()
        self.cmb_sort.addItems([tr("sort_source"), tr("sort_errors"),
                                tr("sort_pron"), tr("sort_stale")])
        self.cmb_sort.setCurrentIndex(max(0, min(3, sort_idx)))
        self.cmb_sort.blockSignals(False)

        # Column headers follow the language; the defect names in Notes read the
        # language off the table, so it has to be told which one is active.
        self.table.setProperty("lang", self.ctx.tr.code)
        self.table.set_headers(self.ctx.tr)
        self.btn_autoassign.setText(tr("auto_assign"))
        self.text.setPlaceholderText(tr("paste_hint"))
        self._on_context_changed()

        # Honour the colouring setting from the start, not only after it is
        # toggled — otherwise a user who switched it off sees it back on every
        # launch until they touch the checkbox.
        try:
            self.text.set_colouring(bool(self.ctx.cfg.get("colour_text", True)))
        except Exception:
            pass

    def _on_context_changed(self) -> None:
        """Voices changed — rebuild the pickers without losing the selection."""
        tr = self.ctx.tr
        cur = self.cmb_voice.currentData()
        self.cmb_voice.clear()
        for v in self.ctx.voices:
            self.cmb_voice.addItem(v.label(), v.id)
        # Prefer the current selection, then the remembered one. Without the
        # second step the remembered voice was loaded into the combo as item 0
        # only by accident of ordering.
        want = cur or self.ctx.cfg.get("voice") or ""
        if want:
            i = self.cmb_voice.findData(want)
            if i >= 0:
                self.cmb_voice.setCurrentIndex(i)
        if not self.cmb_voice.count() and not self.ctx.voices:
            self.cmb_voice.addItem(tr.t("no_voice"), "")
        for speaker, combo in self._speaker_rows.items():
            self._fill_combo(combo)
        self.table.apply_colors(self.ctx.palette_name())

    def _fill_combo(self, combo: QComboBox) -> None:
        keep = combo.currentData()
        combo.clear()
        for v in self.ctx.voices:
            combo.addItem(v.label(), v.id)
        if keep:
            i = combo.findData(keep)
            if i >= 0:
                combo.setCurrentIndex(i)

    # ------------------------------------------------------------------ files
    def _browse_file(self) -> None:
        path, _ = QFileDialog.getOpenFileName(
            self, self.ctx.tr.t("load_file"), "",
            "Text (*.txt *.csv *.srt *.md);;All files (*)")
        if path:
            self._load_file_path(path)

    def _load_file_path(self, path: str) -> None:
        try:
            data = Path(path).read_text(encoding="utf-8", errors="replace")
        except Exception as e:
            QMessageBox.warning(self, self.ctx.tr.t("app_title"), str(e))
            return
        existing = self.text.toPlainText().strip()
        self.text.setPlainText((existing + "\n" + data) if existing else data)

    def _browse_out(self) -> None:
        d = QFileDialog.getExistingDirectory(self, self.ctx.tr.t("output_dir"),
                                             self.ed_out.text())
        if d:
            self.ed_out.setText(d)

    def _open_folder(self) -> None:
        p = Path(self.ed_out.text())
        p.mkdir(parents=True, exist_ok=True)
        try:
            webbrowser.open(p.as_uri())
        except Exception:
            pass

    # ------------------------------------------------------------------ parse
    def _parse(self, ask_first: bool = True) -> bool:
        tr = self.ctx.tr
        raw = self.text.toPlainText()
        if not raw.strip():
            QMessageBox.information(self, tr.t("app_title"), tr.t("no_text"))
            return False

        # Re-parsing throws away per-row state: what was rendered, pinned, edited.
        # That is right when the text changed and wrong when nothing did, because
        # a second click on "Process text" would silently reset a finished list.
        if ask_first and not self._confirm_reparse(raw):
            return False

        # Splitting runs off the UI thread: a ten-thousand-line script froze the
        # window for seconds, with no repaint and no scroll.
        from concurrent.futures import ThreadPoolExecutor

        marker_text = (getattr(self, "ed_marker", None).text()
                       if getattr(self, "ed_marker", None) else "###")
        mode = self.mode

        def _split():
            if mode == "train":
                return sp.split_lines(raw)
            if mode == "bulk":
                return sp.split_blocks(raw, marker_text)
            return sp.split_dub(raw)

        self.lbl_status.setText(tr.t("parsing"))
        self.btn_parse.setEnabled(False)
        try:
            with ThreadPoolExecutor(max_workers=1) as pool:
                lines = pool.submit(_split).result()
        finally:
            self.btn_parse.setEnabled(True)

        if not lines:
            QMessageBox.information(self, tr.t("app_title"), tr.t("no_items"))
            return False

        for i, ln in enumerate(lines):
            ln.index = i

        voice = self.cmb_voice.currentData() or ""
        out_dir = self.ed_out.text().strip() or str(Path.home() / "VoxQueue")
        opts = {
            "normalize": self.chk_norm.isChecked(),
            "detect_defects": self.chk_def.isChecked(),
            "index_start": self.spn_start.value(),
            "audio_format": str(self.ctx.cfg.get("audio_format") or "wav"),
            "sample_rate": int(self.ctx.cfg.get("sample_rate") or 22050),
            "train_template": str(self.ctx.cfg.get("train_template") or "{index}"),
            "add_extension": self.chk_ext.isChecked(),
            "metadata_spoken": self.chk_meta.isChecked(),
        }
        if self.mode == "bulk":
            opts["split_marker"] = getattr(self, "ed_marker", None).text() \
                if getattr(self, "ed_marker", None) else "###"
            opts["write_transcript"] = getattr(self, "chk_txt", None).isChecked() \
                if getattr(self, "chk_txt", None) else True

        job = Job(mode=self.mode, out_dir=out_dir, voice=voice, options=opts,
                  items=self._make_items(lines))

        if self.mode == "dub":
            self._build_speaker_map(lines, voice)
            job.voice_map = {s: (c.currentData() or voice)
                             for s, c in self._speaker_rows.items()}

        # Pick up results from an earlier run into the same folder, so an
        # interrupted night job resumes instead of regenerating from zero.
        restored = job.restore_progress()

        self.job = job
        self.table.clear_rows()
        self.table.add_rows([{
            "index": i.index, "text": i.text, "speaker": i.speaker,
            "phonetic": i.phonetic, "spoken": i.spoken,
            "status": "pending", "duration": 0.0, "defects": [], "file": "",
        } for i in job.items])
        self.table.apply_colors(self.ctx.palette_name())

        self._apply_sort()

        self.lbl_parsed.setText(
            f"{tr.t('items_found')}: {len(job.items)}"
            + (f"   ·   {tr.t('resumed')}: {restored}" if restored else ""))
        self.bar.setRange(0, len(job.items))
        self.bar.setValue(0)
        self.lbl_status.setText(tr.t("status_ready"))
        job.save()
        return True

    @staticmethod
    def _make_items(lines) -> list[Item]:
        """Turn parsed lines into job items, resolving {spoken|written} once.

        The grid shows the written form, the engine gets the phonetic one, and
        neither has to look at a marker again.
        """
        from ..core import pronunciation

        out: list[Item] = []
        for ln in lines:
            spoken, written = pronunciation.split(ln.text)
            out.append(Item(
                index=ln.index,
                text=written,
                spoken=spoken,
                # Only the substituted words, not a repeated sentence.
                phonetic=pronunciation.summary(ln.text),
                source_line=ln.raw,
                speaker=ln.speaker,
            ))
        return out

    def _build_speaker_map(self, lines, default_voice: str) -> None:
        names = sp.speakers(lines)
        self._speaker_rows.clear()
        self.tbl_map.setRowCount(0)
        if not names:
            self.tbl_map.setFixedHeight(0)
            self.map_box.setVisible(False)
            self.btn_autoassign.setEnabled(False)
            return
        self.btn_autoassign.setEnabled(True)
        # Size the table to its content: a scrollbar inside a four-row table
        # hides the rows the user is meant to map.
        rows_h = 36 * len(names) + 34
        # maxHeight has to be lifted before setFixedHeight, otherwise the
        # earlier setMaximumHeight clips the new value and the rows stay hidden.
        self.map_box.setVisible(True)
        self.tbl_map.setMaximumHeight(16777215)
        tbl_h = min(rows_h, 300)
        self.tbl_map.setFixedHeight(tbl_h)
        self.tbl_map.setRowCount(len(names))
        for r, name in enumerate(names):
            item = QTableWidgetItem(name)
            item.setFlags(Qt.ItemIsEnabled)
            self.tbl_map.setItem(r, 0, item)
            combo = QComboBox()
            for v in self.ctx.voices:
                combo.addItem(v.label(), v.id)
            if default_voice:
                i = combo.findData(default_voice)
                if i >= 0:
                    combo.setCurrentIndex(i)
            self.tbl_map.setCellWidget(r, 1, combo)
            self._speaker_rows[name] = combo

    def _toggle_map(self) -> None:
        on = self.btn_map_toggle.isChecked()
        self.tbl_map.setVisible(on)
        self.btn_autoassign.setVisible(on)
        arrow = "▾ " if on else "▸ "
        self.btn_map_toggle.setText(arrow + self.ctx.tr.t("voice_map"))

    def _auto_assign(self) -> None:
        voice = self.cmb_voice.currentData()
        if not voice:
            return
        for combo in self._speaker_rows.values():
            i = combo.findData(voice)
            if i >= 0:
                combo.setCurrentIndex(i)

    # ------------------------------------------------------------------ queue
    def _current_options(self) -> dict:
        """Options as they stand in the UI, before a plan is built."""
        opts = {
            "normalize": self.chk_norm.isChecked(),
            "detect_defects": self.chk_def.isChecked(),
            "index_start": self.spn_start.value(),
            "audio_format": str(self.ctx.cfg.get("audio_format") or "wav"),
            "sample_rate": int(self.ctx.cfg.get("sample_rate") or 22050),
            "train_template": str(self.ctx.cfg.get("train_template") or "{index}"),
            "add_extension": self.chk_ext.isChecked(),
            "metadata_spoken": self.chk_meta.isChecked(),
        }
        if getattr(self, "ed_marker", None):
            opts["split_marker"] = self.ed_marker.text()
        if getattr(self, "chk_txt", None):
            opts["write_transcript"] = self.chk_txt.isChecked()
        return opts

    def _start(self, only: set[int] | None = None) -> None:
        tr = self.ctx.tr

        # Stop playback first: the player keeps the loaded clip open, so
        # regenerating the item that is currently playing fails until some other
        # clip is played.
        try:
            from .widgets import AudioPreview
            AudioPreview.stop()
        except Exception:
            pass

        if self.worker and self.worker.isRunning():
            return
        if not self.job or not self.job.items:
            if not self._parse():
                return
        if not self.cmb_voice.currentData() and self.mode != "dub":
            QMessageBox.information(self, tr.t("app_title"), tr.t("no_voice"))
            return

        # Refresh the mapping in case the user changed it after parsing.
        if self.mode == "dub":
            self.job.voice_map = {s: (c.currentData() or self.job.voice)
                                  for s, c in self._speaker_rows.items()}
        else:
            self.job.voice = self.cmb_voice.currentData() or self.job.voice
        self.job.out_dir = self.ed_out.text().strip() or self.job.out_dir
        self.job.options.update({
            "normalize": self.chk_norm.isChecked(),
            "detect_defects": self.chk_def.isChecked(),
            "index_start": self.spn_start.value(),
            "add_extension": self.chk_ext.isChecked(),
            "metadata_spoken": self.chk_meta.isChecked(),
        })
        Path(self.job.out_dir).mkdir(parents=True, exist_ok=True)
        self.job.save()

        # The bar follows THIS run, not lifetime totals: filled with the done count
        # on load it reads 9999/10000 — visually full and motionless.
        self._run_total = sum(
            1 for i in self.job.items
            if (only is None or i.index in only) and i.status != "done")
        self._run_done = 0
        self.bar.setRange(0, max(1, self._run_total))
        self.bar.setValue(0)

        self.worker = QueueWorker(self.job, self.ctx.client)
        # Narrowing happens on the runner, not on the job: numbering, filenames
        # and state stay identical to a full run.
        self.worker.only_indices = only
        self.worker.item_started.connect(self._on_started)
        self.worker.item_finished.connect(self._on_finished)
        self.worker.item_failed.connect(self._on_failed)
        self.worker.progress.connect(self._on_progress)
        self.worker.all_finished.connect(self._on_all_done)
        self.worker.start()

        self.chk_shutdown.setEnabled(False)
        self.btn_start.setEnabled(False)
        self.btn_pause.setEnabled(True)
        self.btn_cancel.setEnabled(True)
        self.lbl_status.setText(tr.t("status_running"))

    def _toggle_pause(self) -> None:
        if not self.worker:
            return
        if self.worker.paused:
            self.worker.resume()
            self.btn_pause.setText(self.ctx.tr.t("pause"))
            self.lbl_status.setText(self.ctx.tr.t("status_running"))
        else:
            self.worker.pause()
            self.btn_pause.setText(self.ctx.tr.t("resume"))
            self.lbl_status.setText(self.ctx.tr.t("status_ready"))

    def _cancel(self) -> None:
        if self.worker:
            self.worker.cancel()
            self.lbl_status.setText(self.ctx.tr.t("cancel"))

    def _on_started(self, index: int, text: str) -> None:
        self.table.update_row(index, status="running")
        self.table.apply_colors(self.ctx.palette_name())
        self.scroll_to_active()

    def _on_finished(self, index: int, rel: str, dur: float, defects: list) -> None:
        self._run_done += 1
        self.bar.setValue(self._run_done)
        full = str(Path(self.job.out_dir) / rel) if self.job else rel
        self.table.update_row(index, status="done", duration=dur,
                              defects=defects, file=full, error="")
        self.table.apply_colors(self.ctx.palette_name())
        self.scroll_to_active()

    def _on_failed(self, index: int, error: str) -> None:
        self._run_done += 1
        self.bar.setValue(self._run_done)
        self.table.update_row(index, status="failed", error=error)
        self.table.apply_colors(self.ctx.palette_name())
        self.scroll_to_active()

    def _on_progress(self, done: int, total: int, review: int) -> None:
        # The bar is driven by _on_finished/_on_failed so it shows this run.
        # Lifetime totals would snap it back to a full bar immediately.
        tr = self.ctx.tr.t
        self.lbl_status.setText(
            f"{tr('progress')}: {done}/{total}   ·   {tr('review')}: {review}")

    def _on_all_done(self, cancelled: bool) -> None:
        tr = self.ctx.tr.t
        self.btn_start.setEnabled(True)
        self.btn_pause.setEnabled(False)
        self.btn_pause.setText(tr("pause"))
        self.btn_cancel.setEnabled(False)
        if self.job:
            try:
                if self.mode in ("train", "bulk"):
                    write_metadata(self.job)
            except Exception:
                pass
            self.job.save()
        self.chk_shutdown.setEnabled(True)
        self._apply_sort()

        if cancelled:
            self.lbl_status.setText(tr("cancel"))
        else:
            done = self.job.done if self.job else 0
            failed = self.job.failed if self.job else 0
            self.lbl_status.setText(
                f"{tr('done')}: {done}   ·   {tr('failed')}: {failed}")

            # Decided here, after the run is over and the state file is written —
            # never from inside the worker, which cannot tell "finished" from
            # "succeeded".
            self._maybe_shutdown(done)

    def _on_stale_only_toggled(self, checked: bool) -> None:
        self.ctx.cfg.set("stale_only", bool(checked))
        try:
            self.ctx.cfg.save()
        except Exception:
            pass
        self._apply_sort()

    def _on_pinned_only_toggled(self, checked: bool) -> None:
        self.ctx.cfg.set("pinned_only", bool(checked))
        try:
            self.ctx.cfg.save()
        except Exception:
            pass
        self._apply_sort()

    def _set_pinned(self, indices: list, pinned: bool) -> None:
        """Pin or unpin the selected rows."""
        if not self.job:
            return
        for it in self.job.items:
            if it.index in indices:
                it.pinned = bool(pinned)
        self.table.set_pinned(list(indices), bool(pinned))
        self._apply_sort()
        self._update_pinned_label()

    def _update_pinned_label(self) -> None:
        """Show the pinned count, so an empty filtered list is explained."""
        tr = self.ctx.tr.t
        pinned = sum(1 for i in (self.job.items if self.job else []) if i.pinned)
        self.chk_pinned_only.setText(
            f"{tr('pinned_only')} ({pinned})" if pinned else tr("pinned_only"))

    def _jump_to_index(self) -> None:
        """Jump to a row by its number.

        Matched against the item's own Index, not the row position: with a sort
        applied the two differ, and the user types the number they saw.
        """
        from PySide6.QtWidgets import QInputDialog

        tr = self.ctx.tr.t
        value, ok = QInputDialog.getInt(self, tr("jump_to_index"),
                                        tr("jump_to_index"), 0, 0, 9_999_999, 1)
        if not ok:
            return

        for row in range(self.table.rowCount()):
            it = self.table._rows.get(row)
            if it and it.get("index") == value:
                self.table.selectRow(row)
                self.table.scrollToItem(self.table.item(row, 1),
                                        QAbstractItemView.PositionAtCenter)
                self.lbl_status.setText(f"{tr('index_found')}: {value}")
                return
        self.lbl_status.setText(f"{tr('index_not_found')}: {value}")

    def _on_sort_changed(self, index: int) -> None:
        self.ctx.cfg.set("sort_mode", int(index))
        try:
            self.ctx.cfg.save()
        except Exception:
            pass
        self._apply_sort()

    def _confirm_reparse(self, raw: str) -> bool:
        """Asks before discarding a work list that still has results.

        Returns False only when the user backs out of a destructive re-parse, or
        when the list already matches the text — re-parsing would change nothing
        and reset every status.
        """
        from ..core import pronunciation, splitter as sp

        if not self.job or not self.job.items:
            return True

        has_work = any(i.status in ("done", "failed", "stale") for i in self.job.items)
        if not has_work:
            return True

        marker = (getattr(self, "ed_marker", None).text()
                  if getattr(self, "ed_marker", None) else "###")
        if self.mode == "train":
            lines = sp.split_lines(raw)
        elif self.mode == "bulk":
            lines = sp.split_blocks(raw, marker)
        else:
            lines = sp.split_dub(raw)

        before = [i.text for i in self.job.items]
        after = [pronunciation.split(l.text)[1] for l in lines]

        if before == after:
            self.lbl_status.setText(self.ctx.tr.t("list_up_to_date"))
            return False

        rendered = sum(1 for i in self.job.items
                       if i.status in ("done", "failed", "stale"))
        tr = self.ctx.tr.t
        answer = QMessageBox.question(
            self, tr("app_title"),
            self.ctx.tr.hint("reparse_warn") + "\n\n"
            + tr("reparse_detail").format(rendered),
            QMessageBox.Yes | QMessageBox.No, QMessageBox.No)

        if answer != QMessageBox.Yes:
            return False

        applog.info(f"re-parse confirmed: {rendered} rendered row(s) reset")
        return True

    def _bulk_pronunciation(self) -> None:
        """Replace a phrase everywhere and mark affected clips as out of date.

        Three fields answer three separate questions: what to look for, how the
        engine should say it, and what the transcript should keep. "Count
        matches" comes first because replacing across a thousand lines without
        knowing whether it hits three times or three hundred is a button nobody
        dares press.
        """
        from PySide6.QtWidgets import (QCheckBox, QDialog, QDialogButtonBox,
                                       QFormLayout, QHBoxLayout, QLabel,
                                       QLineEdit, QPushButton)
        from ..core import pronunciation

        source = self.text.toPlainText()
        if not source.strip():
            self.lbl_status.setText(self.ctx.tr.t("no_text"))
            return

        tr = self.ctx.tr.t
        dlg = QDialog(self)
        dlg.setWindowTitle(tr("bulk_pron"))
        dlg.setMinimumWidth(560)
        form = QFormLayout(dlg)

        ed_find = QLineEdit()
        ed_spoken = QLineEdit()
        ed_written = QLineEdit()
        for e in (ed_find, ed_spoken, ed_written):
            e.setStyleSheet("font-family: monospace;")

        state = {"spoken": False, "written": False}
        ed_spoken.textEdited.connect(lambda _: state.__setitem__("spoken", True))
        ed_written.textEdited.connect(lambda _: state.__setitem__("written", True))

        result = QLabel("")
        result.setObjectName("Dim")

        btn_replace = None  # resolved after the button box exists

        def invalidate(_text: str = "") -> None:
            result.setText("")
            result.setObjectName("Dim")
            result.setStyleSheet("")
            if btn_replace is not None:
                btn_replace.setEnabled(False)

        def sync(_text: str = "") -> None:
            """Mirror the search term, but show the RECORDED form for written.

            Searching the phonetic form "i-de" while every match sits inside
            {i-de|ID} used to prefill i-de, so leaving the field alone looked like
            "record i-de" even though that is not what happens. Prefilling with ID
            makes the field honest.
            """
            if not state["spoken"]:
                ed_spoken.setText(ed_find.text())

            if not state["written"]:
                recorded = pronunciation.written_form_for(
                    source, ed_find.text(),
                    chk_whole.isChecked(), chk_case.isChecked())
                ed_written.setText(recorded or ed_find.text())

        ed_find.textChanged.connect(sync)

        chk_whole = QCheckBox(tr("whole_word"))
        chk_whole.setChecked(True)
        chk_case = QCheckBox(tr("case_sensitive"))

        for w in (ed_find, ed_spoken, ed_written):
            w.textChanged.connect(invalidate)
        chk_whole.toggled.connect(invalidate)
        chk_case.toggled.connect(invalidate)
        chk_whole.toggled.connect(lambda _: sync())
        chk_case.toggled.connect(lambda _: sync())

        count_row = QHBoxLayout()
        btn_count = QPushButton(tr("bulk_count"))
        count_row.addWidget(btn_count)
        count_row.addWidget(result, 1)

        def do_count() -> None:
            find = ed_find.text().strip()
            if not find:
                result.setText("")
                if btn_replace is not None:
                    btn_replace.setEnabled(False)
                return

            n = pronunciation.count_matches(
                source, find, chk_whole.isChecked(), chk_case.isChecked())

            if n == 0:
                # Distinguish "not here" from "already marked" — the second explains
                # why the list visibly shows the phrase, which read as a bug.
                inside = pronunciation.count_inside_markers(
                    source, find, chk_case.isChecked())

                result.setText(
                    tr("bulk_all_marked").format(inside) if inside
                    else tr("bulk_none_found"))
                result.setStyleSheet("color: palette(mid);")
                if btn_replace is not None:
                    btn_replace.setEnabled(False)
                return

            result.setText(f"{n} {tr('bulk_found_n')}")
            result.setStyleSheet("color: palette(link);")
            if btn_replace is not None:
                btn_replace.setEnabled(True)

        btn_count.clicked.connect(do_count)

        def note(key: str) -> QLabel:
            lbl = QLabel(self.ctx.tr.hint(key))
            lbl.setWordWrap(True)
            lbl.setObjectName("Dim")
            return lbl

        form.addRow(tr("bulk_find") + ":", ed_find)
        form.addRow("", note("bulk_find_note"))
        form.addRow(tr("bulk_spoken") + ":", ed_spoken)
        form.addRow("", note("bulk_spoken_note"))
        form.addRow(tr("bulk_written") + ":", ed_written)
        form.addRow("", note("bulk_written_note"))
        form.addRow(chk_whole)
        form.addRow(chk_case)

        hint = QLabel(self.ctx.tr.hint("bulk_spoken_hint"))
        hint.setWordWrap(True)
        hint.setObjectName("Dim")
        form.addRow(hint)
        form.addRow(count_row)

        buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        btn_replace = buttons.button(QDialogButtonBox.Ok)
        btn_replace.setText(tr("bulk_replace"))
        btn_replace.setEnabled(False)
        buttons.button(QDialogButtonBox.Cancel).setText(tr("cancel"))
        buttons.accepted.connect(dlg.accept)
        buttons.rejected.connect(dlg.reject)
        form.addRow(buttons)

        def accept_if_valid() -> None:
            if btn_replace.isEnabled():
                dlg.accept()

        buttons.accepted.disconnect(dlg.accept)
        buttons.accepted.connect(accept_if_valid)
        btn_replace.setDefault(True)

        # Count once up front so the dialog opens with an answer, not a question.
        do_count()

        if dlg.exec() != QDialog.Accepted:
            return

        find = ed_find.text().strip()
        if not find:
            return

        # Full snapshot: a re-parse rebuilds every row, so a replace of ten lines
        # would otherwise wipe the render status of the other ten thousand.
        # Snapshot includes the SPOKEN form. Changing a pronunciation leaves the
        # written side identical, so comparing text alone reports every edited row as
        # untouched and restores it as done — the list then claims its audio matches
        # the text while the audio was made from the previous pronunciation.
        before = {i.index: (i.text, i.spoken or i.text, i.status, i.file,
                            i.duration, list(i.defects), i.error, i.needs_regen)
                  for i in (self.job.items if self.job else [])}

        replaced, count = pronunciation.replace_all(
            source, find, ed_spoken.text(), ed_written.text(),
            chk_whole.isChecked(), chk_case.isChecked())

        if count == 0:
            self.lbl_status.setText(f"{tr('bulk_none')}: {find}")
            return

        self.text.setPlainText(replaced)

        # ask_first False: the user asked for a replacement, not a re-parse. A
        # confirmation quoting ten thousand reset clips is alarming and beside the
        # point.
        self._parse(ask_first=False)

        if self.job:
            marked = 0
            for it in self.job.items:
                old = before.get(it.index)
                if old is None:
                    continue

                text_same = old[0] == it.text
                spoken_same = old[1] == (it.spoken or it.text)

                if text_same and spoken_same:
                    # Untouched: the clip on disk still says what this row says.
                    it.needs_regen = old[7]
                    it.status = old[2]
                    it.file = old[3]
                    it.duration = old[4]
                    it.defects = list(old[5])
                    it.error = old[6]
                    continue

                # The audio was rendered from the old text or pronunciation.
                if old[2] in ("done", "failed", "stale"):
                    it.status = "stale"
                    it.needs_regen = True
                    marked += 1
            self._apply_sort()

        self.lbl_status.setText(
            f"{tr('bulk_done')}: {count}"
            + (f"   ·   {tr('needs_regen')}: {marked}" if marked else ""))
        applog.info(f"bulk pronunciation: {count} replacement(s) of '{find}'")

    def _edit_pronunciation(self, index: int) -> None:
        """Edit the pronunciation of one line and rewrite the source text.

        Writing the change into the source box is the whole point: the item is
        rebuilt from that box on the next parse, so an edit that only touched the
        item would silently vanish.
        """
        from PySide6.QtWidgets import QDialog, QDialogButtonBox, QFormLayout, QLineEdit
        from ..core import pronunciation

        it = self._find_item(index)
        if it is None:
            return
        tr = self.ctx.tr.t

        dlg = QDialog(self)
        dlg.setWindowTitle(f"{tr('edit_pronunciation')} — {tr('dialog_line')} {index + 1}")
        dlg.setMinimumWidth(480)
        form = QFormLayout(dlg)

        ed_spoken = QLineEdit(it.spoken_or_text)
        ed_written = QLineEdit(it.text)
        ed_written.setReadOnly(True)

        from PySide6.QtWidgets import QCheckBox
        chk_written = QCheckBox(tr("dialog_change_spelling"))
        chk_written.setChecked(False)

        # Read-only alone changes nothing on screen, so ticking the box looked
        # like a dead control. The field now gets an active background and a
        # readable text colour while it is editable.
        def sync_written(editable: bool) -> None:
            ed_written.setReadOnly(not editable)
            ed_written.setStyleSheet(
                "background: palette(base);" if editable else
                "background: palette(window); color: palette(mid);")

        chk_written.toggled.connect(sync_written)
        sync_written(False)

        form.addRow(tr("dialog_engine_says") + ":", ed_spoken)
        form.addRow(chk_written)
        form.addRow(tr("dialog_transcript_keeps") + ":", ed_written)

        buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        buttons.button(QDialogButtonBox.Ok).setText(tr("ok"))
        buttons.button(QDialogButtonBox.Cancel).setText(tr("cancel"))
        buttons.accepted.connect(dlg.accept)
        buttons.rejected.connect(dlg.reject)
        form.addRow(buttons)

        if dlg.exec() != QDialog.Accepted:
            return

        spoken = ed_spoken.text().strip()
        written = ed_written.text().strip()
        if not spoken or not written:
            return

        marker = written if spoken == written else f"{{{spoken}|{written}}}"

        line_index = self._rewrite_source_line(it, marker)
        if line_index is None:
            self.lbl_status.setText(tr("source_line_missing"))
            return

        was_rendered = it.status in ("done", "failed", "stale")

        it.text = written
        it.spoken = spoken
        # Only the WORDS that changed, not the sentence — the written form is
        # already in the Text column.
        it.phonetic = pronunciation.summary(marker)

        # The clip on disk was made from the old line, so it is out of date.
        # Leaving "done" would claim the audio matches the text when it does not.
        if was_rendered:
            it.status = "stale"

        self._apply_sort()
        self.lbl_status.setText(
            tr("pronunciation_saved") + (" · " + tr("needs_regen") if was_rendered else ""))
        applog.info(f"pronunciation edited: line {it.index}"
                    + (" (marked stale)" if was_rendered else ""))

    def _rewrite_source_line(self, it, replacement: str):
        """Replace the item's source line in the editor.

        Matching is by the cleaned form of the raw line, not by position: the
        user may have edited the box since the last parse, and a positional
        rewrite would then corrupt a different line.
        """
        from ..core import pronunciation, splitter

        text = self.text.toPlainText()
        lines = text.replace("\r\n", "\n").split("\n")

        # Fall back to the written form when the raw line is missing; matching the
        # visible text is weaker than matching the raw line, but doing nothing at
        # all is worse.
        raw = it.source_line or it.text
        target = splitter.clean(pronunciation.to_written(raw))
        if not target:
            return False

        for i, line in enumerate(lines):
            # Compare the WRITTEN form of the candidate against the written form of
            # the target. Comparing the raw line to a marker-stripped target meant
            # every line that already carried a {spoken|written} marker failed to
            # match — which made the edit look like a no-op on exactly those lines.
            if splitter.clean(pronunciation.to_written(line)) != target:
                continue

            # Replace the LINE, not the document: assigning the whole text re-runs
            # the highlighter over every line, which is what made the editor flash
            # on every pronunciation edit.
            if self.text.replace_line(i, replacement):
                it.source_line = replacement
                return i
            return None

        return None

    def _apply_sort(self) -> None:
        """Reorder the list so the rows that need attention come first.

        A thousand finished clips bury the dozen that failed, which is the whole
        reason this option exists. Stable within each bucket by index, so
        unticking restores the original order.
        """
        if not self.job or not self.job.items:
            return

        # Filter first, then order: filtering after sorting would leave gaps in
        # the numbering that look like missing rows.
        needle = self.ed_search.text().strip() if hasattr(self, "ed_search") else ""
        items = [i for i in self.job.items
                 if (not self.chk_pinned_only.isChecked() or i.pinned)
                 and (not getattr(self, "chk_stale_only", None)
                      or not self.chk_stale_only.isChecked() or i.awaits_regen)
                 and i.matches(needle)]
        mode = int(self.cmb_sort.currentIndex())
        if mode == 1:
            items.sort(key=lambda i: (i.sort_rank, i.index))
        elif mode == 3:
            # Clips that exist but were made from an older line.
            items.sort(key=lambda i: (0 if i.awaits_regen else 1, i.index))
        elif mode == 2:
            # Lines whose pronunciation was changed are exactly the ones that
            # need a second look, so they go to the top.
            items.sort(key=lambda i: (i.pron_rank, i.index))
        else:
            items.sort(key=lambda i: i.index)

        # Remember what the user is looking at before the table is rebuilt.
        # Clearing the rows drops the selection to the top, so without this the
        # view jumped back to the first line after every re-sort.
        # By ITEM index, not row number: after a sort the same item sits in a
        # different row, so a row number would restore the wrong line.
        cur = self.table._rows.get(self.table.currentRow() or -1) or {}
        current_index = cur.get("index")
        selected = set(self.table.selected_indices())
        scroll = self.table.verticalScrollBar().value()

        self.table.clear_rows()
        self.table.add_rows([{
            "index": i.index, "text": i.text, "speaker": i.speaker,
            "phonetic": i.phonetic, "spoken": i.spoken,
            "status": i.status, "duration": i.duration,
            "defects": list(i.defects), "file": i.file, "error": i.error,
            "pinned": i.pinned,
        } for i in items])
        self.table.apply_colors(self.ctx.palette_name())

        # Restore scroll first, then the current row: selecting a row scrolls it
        # into view, so doing it the other way round would override the position.
        for row in range(self.table.rowCount()):
            it = self.table._rows.get(row) or {}
            if current_index is not None and it.get("index") == current_index:
                self.table.setCurrentCell(row, 1)
                self.table.selectRow(row)
                break

        for row in range(self.table.rowCount()):
            it = self.table._rows.get(row) or {}
            if it.get("index") in selected and it.get("index") != current_index:
                self.table.selectRow(row)

        self.table.verticalScrollBar().setValue(scroll)

    def _remember_voice(self, _index: int = -1) -> None:
        """Persist the selected voice id, but never overwrite it with an empty one.

        On an offline start the list is empty; writing "" there erased the
        remembered voice, so the next launch had nothing to restore.
        """
        vid = self.cmb_voice.currentData()
        if not vid:
            return
        self.ctx.cfg.set("voice", vid)
        try:
            self.ctx.cfg.save()
        except Exception:
            pass

    def _on_follow_toggled(self, checked: bool) -> None:
        self.ctx.cfg.set("auto_scroll", bool(checked))
        try:
            self.ctx.cfg.save()
        except Exception:
            pass
        if checked:
            self.scroll_to_active()

    def scroll_to_active(self) -> None:
        """Bring the row being worked on into view.

        Follows the ACTIVE row rather than always the last one: with "generate
        selected" the queue may be working in the middle of the list, and jumping
        to the end would hide exactly the row the user is watching. Falls back to
        the last row when nothing is running.
        """
        if not self.chk_follow.isChecked():
            return
        try:
            target = self.job.running_index if self.job else None
        except Exception:
            target = None

        try:
            row = target if target is not None else self.table.rowCount() - 1
            if 0 <= row < self.table.rowCount():
                self.table.scrollToItem(
                    self.table.item(row, 1), QAbstractItemView.PositionAtCenter)
        except Exception:
            # A table that has not laid out yet throws; the next event retries.
            pass

    def _maybe_shutdown(self, done: int) -> None:
        """Power off when the option is ticked and the run produced something.

        Three guards, all deliberate: a cancelled run never shuts down (the user
        is still there); a run with zero successes never shuts down (that is a
        broken server, not a finished job); and the shutdown is scheduled with a
        delay so it can be cancelled.
        """
        from ..core import applog, power

        if not self.chk_shutdown.isChecked():
            return
        tr = self.ctx.tr.t

        if done == 0:
            self.lbl_status.setText(tr("shutdown_skipped"))
            return

        # Save once more before the machine goes down. The worker already saves
        # after every item, but an explicit final write costs nothing and means
        # the state on disk cannot be stale by even one item.
        if self.job:
            self.job.save()

        # A visible countdown, not a silent scheduled shutdown. The user asked
        # for a message and a way to interrupt; `shutdown /t` gives neither.
        if not self._countdown_prompt(done):
            self.lbl_status.setText(tr("shutdown_cancelled"))
            return

        ok, message = power.shutdown_now()
        self.lbl_status.setText(
            f"{tr('shutdown_scheduled')} — {message}" if ok
            else f"{tr('shutdown_failed')}: {message}")

    def _countdown_prompt(self, done: int) -> bool:
        """Modal countdown with a Cancel button. True means "go ahead"."""
        from PySide6.QtCore import QTimer
        from PySide6.QtWidgets import QDialog, QProgressBar, QVBoxLayout

        tr = self.ctx.tr.t
        dlg = QDialog(self)
        dlg.setWindowTitle(tr("shutdown_when_done"))
        dlg.setModal(True)
        remaining = 60

        lay = QVBoxLayout(dlg)
        head = QLabel(tr("shutdown_when_done"))
        head.setObjectName("Title")
        body = QLabel(
            f"{tr('done')}: {done}\n" + tr("shutdown_hint"))
        bar = QProgressBar()
        bar.setRange(0, 60)
        bar.setValue(60)
        count = QLabel(f"{remaining} s")
        row = QHBoxLayout()
        keep = QPushButton(tr("cancel"))
        keep.clicked.connect(dlg.reject)
        row.addStretch(1)
        row.addWidget(keep)

        lay.addWidget(head)
        lay.addWidget(body)
        lay.addWidget(bar)
        lay.addWidget(count)
        lay.addLayout(row)

        timer = QTimer(dlg)
        timer.setInterval(1000)

        def tick() -> None:
            nonlocal remaining
            remaining -= 1
            count.setText(f"{remaining} s")
            bar.setValue(max(0, remaining))
            if remaining <= 0:
                timer.stop()
                dlg.accept()

        timer.timeout.connect(tick)
        timer.start()

        accepted = dlg.exec() == QDialog.Accepted
        timer.stop()
        return accepted

    # ------------------------------------------------------------- row actions
    def _find_item(self, index: int):
        if not self.job:
            return None
        for it in self.job.items:
            if it.index == index:
                return it
        return None

    def _show_playback(self, position: int, duration: int) -> None:
        """Draw playback position on the shared bar.

        One bar serves both the queue and the clip being listened to. Playback wins
        while it lasts, because that is what the user is watching at that moment.
        """
        self._showing_playback = True

        if duration <= 0:
            self.bar.setRange(0, 100)
            self.bar.setValue(0)
            self.lbl_time.setText(self._clock(position))
            return

        # A fine range: clips are seconds long, and 100 steps would jump visibly.
        self.bar.setRange(0, 1000)
        self.bar.setValue(max(0, min(1000, round(1000 * position / duration))))
        self.lbl_time.setText(f"{self._clock(position)} / {self._clock(duration)}")

    @staticmethod
    def _clock(ms: int) -> str:
        total = max(0, ms // 1000)
        h, rest = divmod(total, 3600)
        m, sec = divmod(rest, 60)
        return f"{h}:{m:02d}:{sec:02d}" if h else f"{m}:{sec:02d}"

    def _restore_queue_progress(self) -> None:
        """Hands the bar back to the queue once playback ends."""
        if not getattr(self, "_showing_playback", False):
            return
        self._showing_playback = False
        self.bar.setRange(0, max(1, self._run_total or 1))
        self.bar.setValue(max(0, min(self._run_done, self.bar.maximum())))

    def _play(self, index: int) -> None:
        it = self._find_item(index)
        if not it or not it.file:
            return
        from PySide6.QtMultimedia import QMediaPlayer  # noqa: F401
        try:
            from .widgets import AudioPreview
            AudioPreview._ensure_player()
            player = AudioPreview._player
            from PySide6.QtCore import QUrl
            player.stop()

            # QMediaPlayer reports position natively, so the bar can follow the clip
            # frame by frame instead of being estimated from its length.
            try:
                player.positionChanged.disconnect()
                player.durationChanged.disconnect()
                player.playbackStateChanged.disconnect()
            except Exception:
                pass

            player.positionChanged.connect(self._show_playback)
            player.durationChanged.connect(
                lambda d: self._show_playback(player.position(), d))
            player.playbackStateChanged.connect(
                lambda st: self._restore_queue_progress()
                if st == QMediaPlayer.StoppedState else None)

            player.setSource(QUrl.fromLocalFile(str(Path(it.file).resolve())))
            player.play()
        except Exception:
            pass

    # ------------------------------------------------------------- workspace
    def has_work(self) -> bool:
        """True when a dropped project would replace something.

        Mirrors build_workspace, so the confirm dialog is only shown when there is
        actually something to lose.
        """
        return bool((self.job and self.job.items) or self.text.toPlainText().strip())

    def build_workspace(self):
        """Snapshot of the editing state, for the session slot and Save project."""
        from ..core import workspace

        if self.job is None and not self.text.toPlainText().strip():
            return None

        opts = dict(self.job.options) if self.job else self._current_options()
        return workspace.Project(
            mode=self.mode,
            out_dir=self.ed_out.text().strip(),
            voice=self.cmb_voice.currentData() or "",
            input_text=self.text.toPlainText(),
            options=opts,
            voice_map={s: (c.currentData() or "")
                       for s, c in self._speaker_rows.items()},
            items=[workspace.ProjectItem(
                index=i.index, text=i.text, spoken=i.spoken,
                phonetic=i.phonetic, speaker=i.speaker, pinned=i.pinned,
                source_line=i.source_line,
            ) for i in (self.job.items if self.job else [])],
        )

    def apply_workspace(self, proj) -> bool:
        """Restore a workspace into this panel. False on a mode mismatch."""
        if proj.mode != self.mode:
            return False

        self.text.setPlainText(proj.input_text)
        self.ed_out.setText(proj.out_dir)
        if proj.voice:
            i = self.cmb_voice.findData(proj.voice)
            if i >= 0:
                self.cmb_voice.setCurrentIndex(i)

        opts = proj.options or {}
        self.chk_norm.setChecked(bool(opts.get("normalize", True)))
        self.chk_def.setChecked(bool(opts.get("detect_defects", True)))
        self.chk_ext.setChecked(bool(opts.get("add_extension", True)))
        self.chk_meta.setChecked(bool(opts.get("metadata_spoken", True)))
        self.spn_start.setValue(int(opts.get("index_start") or 0))
        if getattr(self, "ed_marker", None) and opts.get("split_marker"):
            self.ed_marker.setText(str(opts["split_marker"]))

        self.job = Job(
            mode=self.mode, out_dir=proj.out_dir, voice=proj.voice,
            options=opts, voice_map=proj.voice_map or {},
            items=[Item(index=i.index, text=i.text, spoken=i.spoken,
                        phonetic=i.phonetic, speaker=i.speaker, pinned=i.pinned,
                        source_line=i.source_line)
                   for i in proj.items])
        self.table.clear_rows()
        self.table.add_rows([{
            "index": i.index, "text": i.text, "speaker": i.speaker,
            "phonetic": i.phonetic, "spoken": i.spoken,
            "status": "pending", "duration": 0.0, "defects": [], "file": "",
            "pinned": i.pinned,
        } for i in self.job.items])
        # Statuses live in the per-run state file, not in the session: the session
        # carries the definition of the work, the state file carries what happened
        # to it. Without this the list came back all-pending and the user had to
        # press Parse to see which clips existed.
        restored = self.job.restore_progress()

        self.bar.setRange(0, max(1, len(self.job.items)))
        self.bar.setValue(0)
        self.lbl_parsed.setText(
            f"{self.ctx.tr.t('items_found')}: {len(self.job.items)}"
            + (f"   ·   {self.ctx.tr.t('resumed')}: {restored}" if restored else ""))
        self._apply_sort()
        return True

    def _save_project(self) -> None:
        """Save the editing session: mode, text, parsed lines, options, voices.

        Results are not included — a project is the definition of a run, and the
        audio either still exists or is being redone.
        """
        from ..core import workspace

        path, _ = QFileDialog.getSaveFileName(
            self, tr_t(self.ctx, "save_project"),
            f"project.{workspace.EXTENSION}", workspace.FILTER)
        if not path:
            return

        opts = dict(self.job.options) if self.job else self._current_options()
        proj = workspace.Project(
            mode=self.mode,
            out_dir=self.ed_out.text().strip(),
            voice=self.cmb_voice.currentData() or "",
            input_text=self.text.toPlainText(),
            options=opts,
            voice_map={s: (c.currentData() or "")
                       for s, c in self._speaker_rows.items()},
            items=[workspace.ProjectItem(
                index=i.index, text=i.text, spoken=i.spoken,
                phonetic=i.phonetic, speaker=i.speaker, pinned=i.pinned,
                source_line=i.source_line,
            ) for i in (self.job.items if self.job else [])],
        )
        try:
            workspace.save(path, proj)
            self.lbl_status.setText(f"{tr_t(self.ctx, 'saved')}: {Path(path).name}")
        except Exception as exc:  # noqa: BLE001 — surfaced to the user
            QMessageBox.warning(self, tr_t(self.ctx, "app_title"), str(exc))

    def _load_project(self) -> None:
        from ..core import workspace

        path, _ = QFileDialog.getOpenFileName(
            self, tr_t(self.ctx, "load_project"), "", workspace.FILTER)
        if not path:
            return

        try:
            proj = workspace.load(path)
        except ValueError as exc:
            QMessageBox.warning(self, tr_t(self.ctx, "app_title"), str(exc))
            return

        # A project carries its own mode; a panel of the wrong mode would show
        # the list but write it with the wrong filenames.
        if proj.mode != self.mode:
            QMessageBox.information(
                self, tr_t(self.ctx, "app_title"),
                f"{tr_t(self.ctx, 'project_mode_mismatch')}\n{proj.mode}")
            return

        self.text.setPlainText(proj.input_text)
        self.ed_out.setText(proj.out_dir)
        if proj.voice:
            i = self.cmb_voice.findData(proj.voice)
            if i >= 0:
                self.cmb_voice.setCurrentIndex(i)

        opts = proj.options or {}
        self.chk_norm.setChecked(bool(opts.get("normalize", True)))
        self.chk_def.setChecked(bool(opts.get("detect_defects", True)))
        self.chk_ext.setChecked(bool(opts.get("add_extension", True)))
        self.chk_meta.setChecked(bool(opts.get("metadata_spoken", True)))
        self.spn_start.setValue(int(opts.get("index_start") or 0))
        if getattr(self, "ed_marker", None) and opts.get("split_marker"):
            self.ed_marker.setText(str(opts["split_marker"]))

        self.job = Job(
            mode=self.mode, out_dir=proj.out_dir, voice=proj.voice,
            options=opts, voice_map=proj.voice_map or {},
            items=[Item(index=i.index, text=i.text, spoken=i.spoken,
                        phonetic=i.phonetic, speaker=i.speaker, pinned=i.pinned,
                        source_line=i.source_line)
                   for i in proj.items])
        self.table.clear_rows()
        self.table.add_rows([{
            "index": i.index, "text": i.text, "speaker": i.speaker,
            "phonetic": i.phonetic, "spoken": i.spoken,
            "status": "pending", "duration": 0.0, "defects": [], "file": "",
            "pinned": i.pinned,
        } for i in self.job.items])
        self.table.apply_colors(self.ctx.palette_name())
        self.bar.setRange(0, max(1, len(self.job.items)))
        self.bar.setValue(0)
        self.lbl_parsed.setText(f"{tr_t(self.ctx, 'items_found')}: {len(self.job.items)}")
        self.lbl_status.setText(f"{tr_t(self.ctx, 'loaded')}: {Path(path).name}")

    def _clear_project(self) -> None:
        """Discard the work list and its saved state. Written files stay."""
        if self.worker is not None:
            self.lbl_status.setText(tr_t(self.ctx, "queue_running"))
            return

        if self.job is not None and any(i.status == "done" for i in self.job.items):
            answer = QMessageBox.question(
                self, tr_t(self.ctx, "app_title"),
                tr_t(self.ctx, "clear_project"),
                QMessageBox.Yes | QMessageBox.No, QMessageBox.No)
            if answer != QMessageBox.Yes:
                return

        if self.job is not None:
            try:
                state = Path(self.job.out_dir) / ".voxqueue" / f"{self.job.id}.json"
                state.unlink(missing_ok=True)
            except Exception:  # noqa: BLE001 — a locked state file is not fatal
                pass

        self.job = None
        self.table.clear_rows()
        self.text.clear()
        self._speaker_rows.clear()
        self.tbl_map.setRowCount(0)
        self.spn_start.setValue(0)
        self.bar.setValue(0)
        self.lbl_parsed.setText("")
        self.lbl_status.setText(tr_t(self.ctx, "status_ready"))

        # Clearing is the one action that says "do not bring this back".
        from ..core import workspace
        workspace.clear_session()

    def _generate_selected(self) -> None:
        """Render only the selected rows.

        A dataset run is long, so a single wrong clip should not cost a full
        pass. The job stays the same object — only_indices narrows what the
        runner touches, so numbering and filenames stay consistent.
        """
        if self.job is None or not self.job.items:
            return

        chosen = set(self.table.selected_indices())
        if not chosen:
            self.lbl_status.setText(tr_t(self.ctx, "select_rows_first"))
            return

        for it in self.job.items:
            if it.index in chosen:
                it.status = "pending"
                it.defects = []
                it.error = ""

        self._start(only=chosen)

    def _regen_selected(self) -> None:
        idx = self.table.selected_indices()
        if idx:
            self._regen_indices(idx)

    def _regen_indices(self, indices: list[int]) -> None:
        if not self.job:
            return
        n = self.job.mark_for_regen(indices)
        for i in indices:
            self.table.update_row(i, status="pending", defects=[], error="")
        self.table.apply_colors(self.ctx.palette_name())
        self.job.save()
        self.lbl_status.setText(f"{self.ctx.tr.t('regenerate')}: {n}")

    def _retry_failed(self) -> None:
        """Re-queue failed and stale rows. See Job.reset_failed."""
        if not self.job:
            return

        tr = self.ctx.tr.t
        count = self.job.reset_failed()

        if count == 0:
            self.lbl_status.setText(tr("nothing_to_retry"))
            return

        for it in self.job.items:
            if it.status == "pending":
                self.table.update_row(it.index, status="pending", error="")

        self.table.apply_colors(self.ctx.palette_name())
        self.job.save()
        self._apply_sort()
        applog.info(f"retry: {count} row(s) re-queued")
