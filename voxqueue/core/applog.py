"""The application log: connections, edits, saves, failures.

Two destinations on purpose. The file is what survives a crash and what a user
can send on; the in-memory ring is what the Log tab shows, so opening the tab
does not have to parse a file that may be megabytes long.

The ring is bounded. An unbounded list in a long-running app that logs every item
of a ten-thousand-clip job is a slow leak, and the file already holds the full
history.
"""

from __future__ import annotations

from collections import deque
from datetime import datetime
from pathlib import Path

from .config import config_dir

_MAX_LINES = 5000
_MAX_BYTES = 10 * 1024 * 1024

_ring: deque[str] = deque(maxlen=_MAX_LINES)
_subscribers: list = []


def log_path() -> Path:
    return config_dir() / "voxqueue.log"


def subscribe(callback) -> None:
    """Register a callback invoked with every new line."""
    if callback not in _subscribers:
        _subscribers.append(callback)


def unsubscribe(callback) -> None:
    try:
        _subscribers.remove(callback)
    except ValueError:
        pass


def lines() -> list[str]:
    return list(_ring)


def clear() -> None:
    """Empties both the file and the ring."""
    _ring.clear()
    try:
        p = log_path()
        if p.exists():
            p.unlink()
        rotated = p.with_suffix(".log.1")
        if rotated.exists():
            rotated.unlink()
    except Exception:
        pass


def load_tail(max_lines: int = 2000) -> None:
    """Loads the tail of the file into the ring, for a fresh window."""
    try:
        p = log_path()
        if not p.exists():
            return

        all_lines = p.read_text(encoding="utf-8", errors="replace").splitlines()
        _ring.clear()
        for line in all_lines[-max_lines:]:
            _ring.append(line)
    except Exception:
        pass


def info(message: str) -> None:
    _write("INFO ", message)


def warn(message: str) -> None:
    _write("WARN ", message)


def error(message: str, exc: BaseException | None = None) -> None:
    if exc is not None:
        message = f"{message} — {type(exc).__name__}: {exc}"
    _write("ERROR", message)


def _write(level: str, message: str) -> None:
    line = f"{datetime.now():%Y-%m-%d %H:%M:%S} {level} {message}"
    _ring.append(line)

    try:
        directory = config_dir()
        directory.mkdir(parents=True, exist_ok=True)

        target = log_path()

        # Rotate at a size a user can still attach to a message. Ten megabytes of
        # log is not a log, it is a dump.
        if target.exists() and target.stat().st_size > _MAX_BYTES:
            target.replace(target.with_suffix(".log.1"))

        with target.open("a", encoding="utf-8") as fh:
            fh.write(line + "\n")
    except Exception:
        # A log that throws must never take the app with it.
        pass

    for cb in list(_subscribers):
        try:
            cb(line)
        except Exception:
            pass
