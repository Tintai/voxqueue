"""Reusable widgets: cards, drop-enabled text box, audio preview, item table."""

from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import QPoint, QRect, QSize, Qt, QUrl, Signal
from PySide6.QtGui import QTextCursor, QColor, QCursor, QFont, QSyntaxHighlighter
from PySide6.QtWidgets import (QAbstractItemView, QCheckBox, QFrame, QHBoxLayout, QLayout,
                               QHeaderView, QLabel, QPlainTextEdit, QPushButton,
                               QSizePolicy, QTableWidget, QTableWidgetItem,
                               QVBoxLayout, QWidget)

try:
    from PySide6.QtMultimedia import QAudioOutput, QMediaPlayer
    _HAS_AUDIO = True
except Exception:  # QtMultimedia missing on a stripped build
    _HAS_AUDIO = False


class Card(QFrame):
    def __init__(self, parent=None, alt: bool = False) -> None:
        super().__init__(parent)
        self.setObjectName("CardAlt" if alt else "Card")
        self._lay = QVBoxLayout(self)
        self._lay.setContentsMargins(16, 14, 16, 14)
        self._lay.setSpacing(10)

    def body(self) -> QVBoxLayout:
        return self._lay

    def add(self, w: QWidget, stretch: int = 0) -> QWidget:
        self._lay.addWidget(w, stretch)
        return w

    def add_row(self, *widgets: QWidget, spacing: int = 8) -> QHBoxLayout:
        row = QHBoxLayout()
        row.setSpacing(spacing)
        for w in widgets:
            row.addWidget(w)
        self._lay.addLayout(row)
        return row


def section(text: str) -> QLabel:
    lb = QLabel(text.upper())
    lb.setObjectName("SectionTitle")
    return lb


def hint(text: str) -> QLabel:
    lb = QLabel(text)
    lb.setObjectName("Hint")
    lb.setWordWrap(True)
    return lb


class Texts:
    """Widgets whose text comes from a translation key.

    Every label, section header and hint is created with a key and registered
    here, and `apply()` re-reads them all in one call.

    The alternative — writing each label's setText by hand inside retranslate —
    is what left half of the settings page in the previous language. A label made
    inline with QLabel(tr("base_url")) is not reachable afterwards, so nobody can
    update it, and nothing complains: it simply keeps the old wording. Doing it
    through a registry makes "create a label, forget to translate it" impossible,
    because creating it is what registers it.
    """

    def __init__(self) -> None:
        self._items: list[tuple[QLabel, str, bool]] = []

    def label(self, tr, key: str) -> QLabel:
        lb = QLabel(tr(key))
        self._items.append((lb, key, False))
        return lb

    def section(self, tr, key: str) -> QLabel:
        lb = section(tr(key))
        self._items.append((lb, key, True))
        return lb

    def hint(self, tr, key: str) -> QLabel:
        lb = hint(tr(key))
        self._items.append((lb, key, False))
        return lb

    def apply(self, tr) -> None:
        """Re-reads every registered key in the active language."""
        for widget, key, upper in self._items:
            text = tr(key)
            widget.setText(text.upper() if upper else text)

    def __len__(self) -> int:
        return len(self._items)


class DropTextEdit(QPlainTextEdit):
    """Text box that also accepts a dragged .txt/.csv/.srt file.

    Dropping *appends* rather than replaces — people drop a second file onto
    text they already prepared, and silently losing the paste would be rude.
    """

    file_dropped = Signal(str)

    def __init__(self, placeholder: str = "", parent=None) -> None:
        super().__init__(parent)
        self.setPlaceholderText(placeholder)
        self.setAcceptDrops(True)
        self.setTabChangesFocus(True)

    def _accepts(self, event) -> bool:
        if not event.mimeData().hasUrls():
            return False
        for u in event.mimeData().urls():
            if u.isLocalFile() and Path(u.toLocalFile()).suffix.lower() in (
                    ".txt", ".csv", ".srt", ".md"):
                return True
        return False

    def dragEnterEvent(self, event) -> None:  # noqa: N802
        if self._accepts(event):
            event.acceptProposedAction()
        else:
            super().dragEnterEvent(event)

    def dragMoveEvent(self, event) -> None:  # noqa: N802
        if self._accepts(event):
            event.acceptProposedAction()
        else:
            super().dragMoveEvent(event)

    def dropEvent(self, event) -> None:  # noqa: N802
        if not self._accepts(event):
            super().dropEvent(event)
            return
        for u in event.mimeData().urls():
            if u.isLocalFile():
                path = u.toLocalFile()
                if Path(path).suffix.lower() in (".txt", ".csv", ".srt", ".md"):
                    self.file_dropped.emit(path)
        event.acceptProposedAction()


class MarkerHighlighter(QSyntaxHighlighter):
    """Colours `{spoken|written}` so the two halves are visually separate.

    A plain editor shows the marker as one grey string, so what will be spoken
    and what will be written look identical — which is precisely the mistake
    this feature exists to prevent. Colours:

        spoken half      accent    what the engine says
        written half     teal      what the transcript records
        braces and pipe  dim       syntax, not content

    Malformed markers (no pipe, empty side) keep dim braces but uncoloured
    content, so a mistake reads as literal text instead of pretending to be
    syntax.
    """

    def __init__(self, document, palette) -> None:
        super().__init__(document)
        self._p = palette

    def set_palette(self, palette) -> None:
        self._p = palette
        self.rehighlight()

    def highlightBlock(self, text: str) -> None:  # noqa: N802
        from PySide6.QtGui import QColor, QTextCharFormat

        p = self._p
        dim = QTextCharFormat()
        dim.setForeground(QColor(p.text_dim))

        spoken_fmt = QTextCharFormat()
        spoken_fmt.setForeground(QColor(p.accent))

        written_fmt = QTextCharFormat()
        written_fmt.setForeground(QColor(p.written))

        i = 0
        while i < len(text):
            open_at = text.find("{", i)
            if open_at < 0:
                break

            close_at = text.find("}", open_at + 1)
            if close_at < 0:
                # Unclosed brace: mark it so the mistake is visible.
                self.setFormat(open_at, 1, dim)
                break

            inner = text[open_at + 1:close_at]
            bar = inner.find("|")
            usable = (
                bar > 0
                and bar < len(inner) - 1
                and inner[:bar].strip()
                and inner[bar + 1:].strip()
            )

            if not usable:
                self.setFormat(open_at, 1, dim)
                self.setFormat(close_at, 1, dim)
                i = close_at + 1
                continue

            pipe_at = open_at + 1 + bar
            self.setFormat(open_at, 1, dim)
            self.setFormat(open_at + 1, bar, spoken_fmt)
            self.setFormat(pipe_at, 1, dim)
            self.setFormat(pipe_at + 1, close_at - pipe_at - 1, written_fmt)
            self.setFormat(close_at, 1, dim)

            i = close_at + 1


class ScriptEdit(DropTextEdit):

    def _colouring_enabled(self) -> bool:
        try:
            return bool(self.ctx.cfg.get("colour_text", True))
        except Exception:
            return True

    def set_colouring(self, enabled: bool) -> None:
        """Switch syntax highlighting on or off.

        Colouring is the most expensive thing the editor does, so a user working
        with very large scripts can trade the highlighting for speed. Detaching
        the highlighter removes the work entirely rather than hiding it.
        """
        try:
            if enabled:
                if self._hl is None:
                    self._hl = MarkerHighlighter(self.document(), self._palette)
            else:
                if self._hl is not None:
                    self._hl.setDocument(None)
                    self._hl = None
        except Exception:
            pass

    def replace_line(self, line_index: int, new_text: str) -> bool:
        """Replace one line in place, without rebuilding the document.

        Assigning the whole text re-runs the syntax highlighter over every line,
        which is why a single pronunciation edit made the editor flash. Touching
        only this block keeps the work proportional to the change.

        Returns False when the line no longer exists.
        """
        if line_index < 0:
            return False

        try:
            doc = self.document()
            block = doc.findBlockByNumber(line_index)
            if not block.isValid():
                return False

            cursor = QTextCursor(block)
            cursor.select(QTextCursor.BlockUnderCursor)
            cursor.insertText(new_text)
            return True
        except Exception:
            return False
    """Drop-capable editor with phonetic markers highlighted."""

    def __init__(self, placeholder: str = "", parent=None) -> None:
        super().__init__(placeholder, parent)
        from .theme import palette
        self._palette = palette("dark")
        self._hl = MarkerHighlighter(self.document(), self._palette)

    def apply_theme(self, palette_name: str) -> None:
        from .theme import palette
        self._palette = palette(palette_name)
        self._hl.set_palette(self._palette)


class FlowLayout(QLayout):
    """Left-to-right layout that wraps to the next row when it runs out of width.

    Qt has QHBoxLayout, which does not wrap: on a narrow window the trailing
    buttons are squeezed until the text is unreadable, and QVBoxLayout wastes the
    whole width. Every widget stays reachable here, which is the point — "Clear
    project" disappearing off the right edge is not an acceptable layout.

    Based on the standard Qt FlowLayout example, including the height-for-width
    logic: without it setHeightForWidth the parent gives the layout one row of
    height and the wrapped rows are clipped.
    """

    def __init__(self, parent=None, margin: int = 0, h_spacing: int = 6,
                 v_spacing: int = 4) -> None:
        super().__init__(parent)
        self._items: list = []
        self._h_space = h_spacing
        self._v_space = v_spacing
        self.setContentsMargins(margin, margin, margin, margin)

    # ---------------------------------------------------------- QLayout API
    def addItem(self, item) -> None:  # noqa: N802
        self._items.append(item)

    def count(self) -> int:
        return len(self._items)

    def itemAt(self, index: int):  # noqa: N802
        return self._items[index] if 0 <= index < len(self._items) else None

    def takeAt(self, index: int):  # noqa: N802
        if 0 <= index < len(self._items):
            return self._items.pop(index)
        return None

    def expandingDirections(self):  # noqa: N802
        return Qt.Orientations(Qt.Orientation(0))

    def hasHeightForWidth(self) -> bool:  # noqa: N802
        return True

    def heightForWidth(self, width: int) -> int:  # noqa: N802
        return self._do_layout(QRect(0, 0, width, 0), test_only=True)

    def setGeometry(self, rect) -> None:  # noqa: N802
        super().setGeometry(rect)
        self._do_layout(rect, test_only=False)

    def sizeHint(self):  # noqa: N802
        return self.minimumSize()

    def minimumSize(self):  # noqa: N802
        size = QSize()
        for item in self._items:
            size = size.expandedTo(item.minimumSize())
        m = self.contentsMargins()
        return size + QSize(m.left() + m.right(), m.top() + m.bottom())

    # ------------------------------------------------------------- internals
    def _do_layout(self, rect, test_only: bool) -> int:
        m = self.contentsMargins()
        effective = rect.adjusted(m.left(), m.top(), -m.right(), -m.bottom())
        x = effective.x()
        y = effective.y()
        line_height = 0

        for item in self._items:
            widget = item.widget()
            if widget is not None and not widget.isVisible():
                continue

            hint = item.sizeHint()
            next_x = x + hint.width() + self._h_space

            if next_x - self._h_space > effective.right() and line_height > 0:
                x = effective.x()
                y = y + line_height + self._v_space
                next_x = x + hint.width() + self._h_space
                line_height = 0

            if not test_only:
                item.setGeometry(QRect(QPoint(x, y), hint))

            x = next_x
            line_height = max(line_height, hint.height())

        return y + line_height - rect.y() + m.bottom()


class Badge(QLabel):
    """Status pill. The palette is set explicitly by the owner, never guessed
    from the widget's own colours — that guess broke in light mode."""

    def __init__(self, text: str = "", kind: str = "idle", parent=None,
                 palette_name: str = "dark") -> None:
        super().__init__(text, parent)
        self._palette_name = palette_name
        self.set_kind(kind)

    def set_palette_name(self, name: str) -> None:
        self._palette_name = name
        self.set_kind(self._kind)

    def set_kind(self, kind: str, text: str | None = None) -> None:
        from .theme import badge_style, palette
        self._kind = kind
        self.setStyleSheet(badge_style(palette(self._palette_name), kind))
        if text is not None:
            self.setText(text)


class AudioPreview(QWidget):
    """Small play button. One shared player, so starting a clip stops the last.

    Qt 6 requires the audio output to outlive the player; both are held here so
    they are not collected while playback is still running.
    """

    _player = None
    _output = None

    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self._path = ""
        lay = QHBoxLayout(self)
        lay.setContentsMargins(0, 0, 0, 0)
        self.btn = QPushButton("▶")
        self.btn.setObjectName("Play")
        self.btn.setFixedSize(30, 24)
        self.btn.setCursor(QCursor(Qt.PointingHandCursor))
        self.btn.clicked.connect(self.play)
        lay.addWidget(self.btn)
        self.setEnabled(_HAS_AUDIO)

    @classmethod
    def stop(cls) -> None:
        """Stop playback and drop the source, releasing the file handle.

        QMediaPlayer keeps the open file for as long as it is the source, so
        regenerating the clip that is currently loaded fails while it plays.
        Clearing the source is what actually frees the handle — stop() alone
        leaves the media attached.
        """
        if cls._player is None:
            return
        try:
            cls._player.stop()
            from PySide6.QtCore import QUrl
            cls._player.setSource(QUrl())
        except Exception:
            pass

    @classmethod
    def _ensure_player(cls) -> None:
        if cls._player is None:
            cls._output = QAudioOutput()
            cls._output.setVolume(0.9)
            cls._player = QMediaPlayer()
            cls._player.setAudioOutput(cls._output)

    def set_path(self, path: str) -> None:
        self._path = path or ""
        self.setVisible(bool(self._path))
        self.setEnabled(bool(self._path) and _HAS_AUDIO)

    def play(self) -> None:
        if not self._path or not _HAS_AUDIO:
            return
        p = Path(self._path)
        if not p.exists():
            return
        try:
            self._ensure_player()
            type(self)._player.stop()
            type(self)._player.setSource(QUrl.fromLocalFile(str(p.resolve())))
            type(self)._player.play()
            self.btn.setText("■")
            type(self)._player.playbackStateChanged.connect(self._on_state)
        except Exception:
            pass

    def _on_state(self, state) -> None:
        if state == QMediaPlayer.StoppedState:
            self.btn.setText("▶")


class ItemTable(QTableWidget):
    """The work list. Rows are the source of truth for what happened to a clip."""

    play_requested = Signal(int)
    regen_requested = Signal(list)
    edit_requested = Signal(int)
    pin_requested = Signal(list, bool)
    jump_requested = Signal()

    # Column keys, translated on every language switch — a hardcoded header
    # cannot follow the locale, which is how "Dur" survived into a Polish UI.
    COLS = ("#", "Text", "Pronunciation", "Duration", "Status", "Notes")

    # The same columns as translation keys. WorkTab calls set_headers on every
    # language switch, and that method did not exist: switching language raised
    # AttributeError and took the whole work tab with it.
    COL_KEYS = ("col_index", "col_text", "col_phonetic",
                "col_duration", "col_status", "col_notes")

    def __init__(self, parent=None) -> None:
        super().__init__(0, len(self.COLS), parent)
        self.setHorizontalHeaderLabels(self.COLS)
        self.verticalHeader().setVisible(False)
        self.setSelectionBehavior(QAbstractItemView.SelectRows)
        self.setSelectionMode(QAbstractItemView.ExtendedSelection)
        self.setEditTriggers(QAbstractItemView.NoEditTriggers)
        self.setAlternatingRowColors(False)
        self.setShowGrid(False)
        self.setWordWrap(False)
        hh = self.horizontalHeader()
        hh.setSectionResizeMode(0, QHeaderView.ResizeToContents)
        hh.setSectionResizeMode(1, QHeaderView.Interactive)
        hh.setSectionResizeMode(2, QHeaderView.Interactive)
        hh.setSectionResizeMode(3, QHeaderView.Fixed)
        hh.setSectionResizeMode(4, QHeaderView.Fixed)
        hh.setSectionResizeMode(5, QHeaderView.Interactive)
        self.setColumnWidth(1, 320)
        self.setColumnWidth(2, 190)
        self.setColumnWidth(3, 78)
        self.setColumnWidth(4, 62)
        self.setColumnWidth(5, 210)
        self.setContextMenuPolicy(Qt.CustomContextMenu)
        self.customContextMenuRequested.connect(self._menu)
        self._rows: dict[int, dict] = {}

    # ------------------------------------------------------------- population
    def clear_rows(self) -> None:
        self._rows.clear()
        self.setRowCount(0)

    def add_rows(self, items: list[dict]) -> None:
        """items: {index, text, speaker, status, duration, defects, file}"""
        self.setRowCount(len(items))
        for r, it in enumerate(items):
            self._rows[r] = it
            self._fill_row(r, it)

    def set_headers(self, tr) -> None:
        """Re-translates the column headers for the active language."""
        try:
            labels = [tr.t(k) for k in self.COL_KEYS]
        except Exception:
            return
        self.setHorizontalHeaderLabels(labels)

    def set_pinned(self, indices: list[int], pinned: bool) -> int:
        """Mark rows as pinned. Returns how many changed."""
        n = 0
        for it in self._rows.values():
            if it.get("index") in indices:
                it["pinned"] = pinned
                n += 1
        if n:
            for r, it in self._rows.items():
                self._fill_row(r, it)
        return n

    def update_row(self, index: int, **fields) -> None:
        for r, it in self._rows.items():
            if it.get("index") == index:
                it.update(fields)
                self._fill_row(r, it)
                return

    def _status_text(self, it: dict) -> tuple[str, str]:
        """-> (text, kind) for the status pill."""
        st = it.get("status", "pending")
        defects = it.get("defects") or []
        if st == "done" and defects:
            return f"⚠ {len(defects)}", "warn"
        if st == "done":
            return "✓", "ok"
        if st == "running":
            return "…", "idle"
        if st == "failed":
            return "✕", "fail"
        return "·", "idle"

    def _fill_row(self, r: int, it: dict) -> None:
        num = QTableWidgetItem(str(it.get("index", r)))
        num.setTextAlignment(Qt.AlignCenter)
        self.setItem(r, 0, num)

        label = it.get("text", "")
        if it.get("speaker"):
            label = f"[{it['speaker']}] {label}"
        txt = QTableWidgetItem(label)
        txt.setToolTip(label)
        self.setItem(r, 1, txt)

        # Only the substituted words, not a repeated sentence: the written form
        # is already in the Text column, so a full phonetic copy would duplicate it.
        phon_text = it.get("phonetic") or ""
        phon = QTableWidgetItem(phon_text)
        phon.setToolTip(phon_text or it.get("spoken") or "")
        self.setItem(r, 2, phon)

        dur = it.get("duration") or 0.0
        d = QTableWidgetItem(f"{dur:.2f}s" if dur else "—")
        d.setTextAlignment(Qt.AlignRight | Qt.AlignVCenter)
        self.setItem(r, 3, d)

        stext, kind = self._status_text(it)
        st = QTableWidgetItem(stext)
        st.setTextAlignment(Qt.AlignCenter)
        st.setData(Qt.UserRole, kind)
        self.setItem(r, 4, st)

        # Defect codes are translated for display; the stored value stays a code.
        from ..i18n import defect_name
        lang = self.property("lang") or "en"
        notes = ", ".join(defect_name(d, lang) for d in (it.get("defects") or []))
        if it.get("error"):
            notes = it["error"][:60]
        n = QTableWidgetItem(notes)
        n.setToolTip(notes)
        self.setItem(r, 5, n)

    @staticmethod
    def _base_bg(p) -> str:
        """Default cell background for the current palette."""
        return "#16141e" if p.text.startswith("#e") else "#ffffff"

    def apply_colors(self, palette_name: str) -> None:
        """Recolour every row for the current theme.

        Done in one place because QTableWidget items do not inherit stylesheet
        colours — setTextColor has to be applied per cell, and doing it here
        keeps the theme switch honest.
        """
        from .theme import palette
        p = palette(palette_name)
        tint = {
            "ok": QColor(p.ok),
            "warn": QColor(p.warn),
            "fail": QColor(p.fail),
            "stale": QColor(p.stale),
            "idle": QColor(p.text_dim),
        }
        default = QColor(p.text)
        accent = QColor(p.accent)

        # Row background carries the state so a long list can be scanned without
        # reading the status column. Priority is deliberate: a failed row must
        # never be masked by the fact that it is also pinned.
        bg_fail = QColor(p.row_fail_bg)
        bg_warn = QColor(p.row_warn_bg)
        bg_pin = QColor(p.row_pin_bg)
        bg_stale = QColor(p.row_stale_bg)
        base_bg = QColor(self._base_bg(p))

        for r, it in self._rows.items():
            cell_st = self.item(r, 4)
            kind = cell_st.data(Qt.UserRole) if cell_st is not None else "idle"
            flagged = bool(it.get("defects"))
            pinned = bool(it.get("pinned"))

            # Broken beats outdated, outdated beats pinned, pinned beats plain.
            row_bg = base_bg
            if kind == "fail":
                row_bg = bg_fail
            elif kind == "stale":
                row_bg = bg_stale
            elif flagged:
                row_bg = bg_warn
            elif pinned:
                row_bg = bg_pin

            for c in range(self.columnCount()):
                cell = self.item(r, c)
                if cell is None:
                    continue
                cell.setBackground(row_bg)
                if c == 4:
                    cell.setForeground(tint.get(kind, default))
                elif c == 2:
                    # Accent: this is what the engine will actually say.
                    cell.setForeground(accent)
                elif c == 0:
                    cell.setForeground(QColor(p.text_dim))
                elif c in (1, 5) and flagged:
                    cell.setForeground(QColor(p.warn))
                elif c in (1, 5) and it.get("status") == "failed":
                    cell.setForeground(QColor(p.fail))
                else:
                    cell.setForeground(default)

    # ---------------------------------------------------------------- context
    def _menu(self, pos) -> None:
        from PySide6.QtWidgets import QMenu
        idx = sorted({self._rows[r]["index"] for r in self.selected_rows()})
        if not idx:
            return
        m = QMenu(self)
        act_play = m.addAction("▶  Play")
        act_pron = m.addAction("✎  Pronunciation")
        act_regen = m.addAction("↻  Regenerate")
        # pos comes from customContextMenuRequested and is already in viewport
        # coordinates, so no translation is needed here — but map it explicitly
        # so a change of source cannot silently reintroduce an offset.
        chosen = m.exec(self.viewport().mapToGlobal(pos))
        if chosen == act_play:
            self.play_requested.emit(idx[0])
        elif chosen == act_pron:
            self.edit_requested.emit(idx[0])
        elif chosen == act_regen:
            self.regen_requested.emit(idx)

    def selected_rows(self) -> list[int]:
        return sorted({i.row() for i in self.selectedIndexes()})

    def selected_indices(self) -> list[int]:
        return [self._rows[r]["index"] for r in self.selected_rows()
                if r in self._rows]

    def resizeEvent(self, event) -> None:  # noqa: N802
        super().resizeEvent(event)
