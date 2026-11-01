"""Persistent application settings (JSON in the user's config directory)."""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path
from typing import Any


def config_dir() -> Path:
    """Per-user, per-OS directory for settings and job state."""
    if sys.platform == "win32":
        base = Path(os.environ.get("APPDATA") or Path.home() / "AppData/Roaming")
    elif sys.platform == "darwin":
        base = Path.home() / "Library" / "Application Support"
    else:
        base = Path(os.environ.get("XDG_CONFIG_HOME") or Path.home() / ".config")
    d = base / "VoxQueue"
    d.mkdir(parents=True, exist_ok=True)
    return d


CONFIG_PATH = config_dir() / "config.json"

DEFAULTS: dict[str, Any] = {
    "base_url": "http://127.0.0.1:3900",
    "api_key": "",
    "voice": "",
    "language": "system",
    "theme": "dark",
    "output_dir": str(Path.home() / "VoxQueue"),
    "normalize": True,
    "detect_defects": True,
    "audio_format": "wav",
    "gap_ms": 0,
    "split_marker": "###",
    "train_template": "{index}",
    "add_extension": True,
    "auto_scroll": True,
    "errors_first": False,
    "sort_mode": 0,
    "restore_session": True,
    "colour_text": True,
    "stale_only": False,
    "metadata_spoken": True,
    "last_tab": 0,
    "window": None,
}


def _merge(base: dict, over: dict) -> dict:
    out = dict(base)
    for k, v in (over or {}).items():
        if k in out and isinstance(out[k], dict) and isinstance(v, dict):
            out[k] = _merge(out[k], v)
        else:
            out[k] = v
    return out


class Config:
    """Settings that survive restarts. Writes atomically — a crash mid-save
    must never leave a truncated file, because that would silently reset every
    preference on the next launch."""

    def __init__(self) -> None:
        self._data = dict(DEFAULTS)
        self.load()

    def load(self) -> None:
        try:
            raw = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
            if isinstance(raw, dict):
                self._data = _merge(DEFAULTS, raw)
        except FileNotFoundError:
            pass
        except Exception:
            # Corrupt file: keep defaults, but preserve the bad file for
            # inspection instead of overwriting evidence.
            try:
                CONFIG_PATH.replace(CONFIG_PATH.with_suffix(".json.broken"))
            except Exception:
                pass

    def save(self) -> None:
        tmp = CONFIG_PATH.with_suffix(".json.tmp")
        try:
            tmp.write_text(json.dumps(self._data, indent=2, ensure_ascii=False),
                           encoding="utf-8")
            tmp.replace(CONFIG_PATH)
        except Exception:
            pass

    def get(self, key: str, default: Any = None) -> Any:
        return self._data.get(key, DEFAULTS.get(key, default))

    def set(self, key: str, value: Any) -> None:
        self._data[key] = value

    def update(self, **kw: Any) -> None:
        self._data.update(kw)

    def wipe(self) -> None:
        """Delete every trace of this app's own data (settings and job state).

        Explicit user action only — never called from a normal code path.
        """
        import shutil
        shutil.rmtree(config_dir(), ignore_errors=True)


CONFIG = Config()
