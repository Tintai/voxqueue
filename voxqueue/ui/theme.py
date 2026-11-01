"""Theme: palettes and the stylesheet for both modes.

The look is "glass card on a soft gradient": translucent panels, 1px hairline
borders, generous radii, one accent colour. Real backdrop blur is not portably
available in Qt, so depth comes from layered translucency instead — it reads as
frosted glass without a compositor dependency.
"""

from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class Palette:
    name: str
    window_a: str
    window_b: str
    card: str
    card_alt: str
    border: str
    border_hover: str
    text: str
    text_dim: str
    accent: str
    accent_soft: str
    accent_text: str
    # Written half of a {spoken|written} marker. A different hue from the
    # accent, not a lighter tone: the two halves serve opposite purposes and
    # the difference has to read at a glance while editing a long script.
    written: str
    row_fail_bg: str
    row_warn_bg: str
    row_pin_bg: str
    row_stale_bg: str
    stale: str
    ok: str
    warn: str
    fail: str
    input_bg: str
    selection: str


DARK = Palette(
    name="dark",
    window_a="#12111a", window_b="#1a1826",
    card="rgba(255,255,255,0.045)", card_alt="rgba(255,255,255,0.075)",
    border="rgba(255,255,255,0.09)", border_hover="rgba(255,255,255,0.20)",
    text="#eae8f2", text_dim="#9d99b3",
    accent="#8b7cf6", accent_soft="rgba(139,124,246,0.16)", accent_text="#ffffff",
    written="#5fd1b8",
    # Row tints for the work list. Opaque so they can be used as a cell brush.
    row_fail_bg="#3e1f22",
    row_warn_bg="#3b3216",
    row_pin_bg="#262242",
    row_stale_bg="#1b2a3e",
    stale="#7fb0e8",
    ok="#4ade80", warn="#fbbf24", fail="#f87171",
    input_bg="rgba(0,0,0,0.28)", selection="rgba(139,124,246,0.35)",
)

LIGHT = Palette(
    name="light",
    window_a="#f4f3f9", window_b="#e9e7f3",
    card="rgba(255,255,255,0.72)", card_alt="rgba(255,255,255,0.92)",
    border="rgba(20,16,45,0.10)", border_hover="rgba(20,16,45,0.24)",
    text="#1b1830", text_dim="#615c78",
    accent="#6d5ce0", accent_soft="rgba(109,92,224,0.12)", accent_text="#ffffff",
    written="#0e746b",
    row_fail_bg="#fdecec",
    row_warn_bg="#fdf6e0",
    row_pin_bg="#eeecfc",
    row_stale_bg="#e8f2fc",
    stale="#1d4e89",
    ok="#15803d", warn="#b45309", fail="#b91c1c",
    input_bg="rgba(255,255,255,0.85)", selection="rgba(109,92,224,0.22)",
)


def palette(name: str) -> Palette:
    return LIGHT if name == "light" else DARK


def stylesheet(p: Palette) -> str:
    return f"""
* {{
    font-family: "Inter", "Segoe UI", "SF Pro Text", "Noto Sans", "DejaVu Sans", sans-serif;
    font-size: 13px;
    color: {p.text};
}}

QWidget#Root {{
    background: qlineargradient(x1:0, y1:0, x2:1, y2:1,
        stop:0 {p.window_a}, stop:1 {p.window_b});
}}

/* ---------- cards ---------- */
QFrame#Card {{
    background: {p.card};
    border: 1px solid {p.border};
    border-radius: 16px;
}}
QFrame#CardAlt {{
    background: {p.card_alt};
    border: 1px solid {p.border};
    border-radius: 14px;
}}

/* ---------- text ---------- */
QLabel#Title {{ font-size: 19px; font-weight: 700; letter-spacing: 0.2px; }}
QLabel#Subtitle {{ color: {p.text_dim}; font-size: 11px; }}
QLabel#SectionTitle {{ font-size: 12px; font-weight: 600; color: {p.text_dim};
    text-transform: uppercase; letter-spacing: 0.8px; }}
QLabel#Hint {{ color: {p.text_dim}; font-size: 11px; }}
QLabel#Dim {{ color: {p.text_dim}; }}

/* ---------- tab bar ---------- */
QTabWidget::pane {{ border: none; background: transparent; }}
QTabBar::tab {{
    background: transparent;
    color: {p.text_dim};
    padding: 9px 20px;
    margin-right: 4px;
    border: 1px solid transparent;
    border-radius: 10px;
    font-weight: 600;
}}
QTabBar::tab:hover {{ color: {p.text}; background: {p.card}; }}
QTabBar::tab:selected {{
    color: {p.text};
    background: {p.accent_soft};
    border: 1px solid {p.border_hover};
}}

/* ---------- buttons ---------- */
QPushButton {{
    background: {p.card_alt};
    border: 1px solid {p.border};
    border-radius: 10px;
    padding: 8px 16px;
    font-weight: 600;
}}
QPushButton:hover {{ border-color: {p.border_hover}; }}
QPushButton:pressed {{ background: {p.accent_soft}; }}
QPushButton:disabled {{ color: {p.text_dim}; border-color: {p.border}; }}

QPushButton#Primary {{
    background: {p.accent};
    color: {p.accent_text};
    border: 1px solid transparent;
    padding: 9px 22px;
}}
QPushButton#Primary:hover {{ background: {p.accent}; border-color: {p.border_hover}; }}
QPushButton#Primary:disabled {{ background: {p.card_alt}; color: {p.text_dim}; }}

QPushButton#Ghost {{
    background: transparent;
    border: 1px solid transparent;
    color: {p.text_dim};
    padding: 6px 10px;
}}
QPushButton#Ghost:hover {{ color: {p.text}; background: {p.card}; }}

QPushButton#Danger {{ color: {p.fail}; border-color: {p.border}; }}
QPushButton#Danger:hover {{ border-color: {p.fail}; }}

QPushButton#Play {{
    background: transparent; border: 1px solid {p.border};
    border-radius: 9px; padding: 2px 8px; font-size: 12px;
}}
QPushButton#Play:hover {{ border-color: {p.accent}; color: {p.accent}; }}

/* ---------- inputs ---------- */
QLineEdit, QComboBox, QSpinBox {{
    background: {p.input_bg};
    border: 1px solid {p.border};
    border-radius: 9px;
    padding: 7px 11px;
    selection-background-color: {p.selection};
}}
QLineEdit:focus, QComboBox:focus, QSpinBox:focus {{ border-color: {p.accent}; }}

QComboBox {{ min-height: 22px; padding-right: 26px; }}
QComboBox::drop-down {{
    subcontrol-origin: padding;
    subcontrol-position: center right;
    border: none;
    width: 22px;
}}
QComboBox::down-arrow {{
    image: none;
    width: 0; height: 0;
    border-left: 4px solid transparent;
    border-right: 4px solid transparent;
    border-top: 5px solid {p.text_dim};
    margin-right: 8px;
}}
QComboBox QAbstractItemView {{
    background: {p.window_b};
    border: 1px solid {p.border};
    border-radius: 8px;
    selection-background-color: {p.accent_soft};
    padding: 4px;
}}

QPlainTextEdit, QTextEdit {{
    background: {p.input_bg};
    border: 1px solid {p.border};
    border-radius: 12px;
    padding: 10px;
    selection-background-color: {p.selection};
    font-family: "JetBrains Mono", "Cascadia Code", "Consolas", monospace;
    font-size: 12.5px;
}}
QPlainTextEdit:focus, QTextEdit:focus {{ border-color: {p.accent}; }}

/* ---------- lists and tables ---------- */
QListWidget, QTableWidget, QTreeWidget {{
    background: transparent;
    border: 1px solid {p.border};
    border-radius: 12px;
    outline: none;
}}
QListWidget::item {{ padding: 6px 8px; border-radius: 8px; }}
QListWidget::item:selected {{ background: {p.accent_soft}; color: {p.text}; }}
QListWidget::item:hover {{ background: {p.card}; }}

QTableWidget {{ gridline-color: {p.border}; background: transparent; }}
QTableWidget::item {{ padding: 4px 6px; }}
QTableWidget QTableCornerButton::section {{
    background: transparent; border: none;
}}
/* Without this the header paints the platform default (white) in dark mode. */
QTableWidget QHeaderView {{ background: transparent; }}
QTableWidget QHeaderView::section {{
    background: transparent; color: {p.text_dim};
    border: none; border-bottom: 1px solid {p.border};
    padding: 6px; font-weight: 600; font-size: 11px;
}}
QTableWidget::item:selected {{ background: {p.accent_soft}; color: {p.text}; }}
QHeaderView::section {{
    background: transparent; color: {p.text_dim};
    border: none; border-bottom: 1px solid {p.border};
    padding: 6px; font-weight: 600; font-size: 11px;
}}

/* ---------- misc ---------- */
QProgressBar {{
    background: {p.input_bg};
    border: 1px solid {p.border};
    border-radius: 8px;
    height: 8px;
    text-align: center;
    color: transparent;
}}
QProgressBar::chunk {{ background: {p.accent}; border-radius: 7px; }}

QCheckBox {{ spacing: 8px; }}
QCheckBox::indicator {{
    width: 16px; height: 16px;
    border: 1px solid {p.border_hover};
    border-radius: 5px;
    background: {p.input_bg};
}}
QCheckBox::indicator:checked {{ background: {p.accent}; border-color: {p.accent}; }}

QScrollArea, QScrollArea > QWidget > QWidget {{ background: transparent; border: none; }}
QScrollBar:vertical {{ background: transparent; width: 10px; margin: 2px; }}
QScrollBar::handle:vertical {{
    background: {p.border_hover}; border-radius: 5px; min-height: 28px;
}}
QScrollBar::handle:vertical:hover {{ background: {p.accent}; }}
QScrollBar::add-line, QScrollBar::sub-line {{ height: 0; width: 0; }}
QScrollBar:horizontal {{ background: transparent; height: 10px; margin: 2px; }}
QScrollBar::handle:horizontal {{
    background: {p.border_hover}; border-radius: 5px; min-width: 28px;
}}

QToolTip {{
    background: {p.window_b};
    color: {p.text};
    border: 1px solid {p.border_hover};
    border-radius: 8px;
    padding: 6px 9px;
}}

QSplitter::handle {{ background: transparent; }}
QMenu {{
    background: {p.window_b};
    border: 1px solid {p.border};
    border-radius: 10px;
    padding: 6px;
}}
QMenu::item {{ padding: 6px 14px; border-radius: 7px; }}
QMenu::item:selected {{ background: {p.accent_soft}; }}
"""


def badge_style(p: Palette, kind: str) -> str:
    """Small status pill: ok / warn / fail / idle."""
    colour = {"ok": p.ok, "warn": p.warn, "fail": p.fail}.get(kind, p.text_dim)
    bg = {"ok": "rgba(74,222,128,0.14)", "warn": "rgba(251,191,36,0.16)",
          "fail": "rgba(248,113,113,0.16)"}.get(kind, p.card_alt)
    return (f"color:{colour}; background:{bg}; border:1px solid {colour};"
            f"border-radius:8px; padding:2px 9px; font-size:11px; font-weight:600;")
