"""Saved projects (``.vqproj``).

A project is everything needed to restore the editing session, without the
results: mode, source text, parsed lines, options and voice map.

Deliberately separate from :class:`~voxqueue.core.state.Job`. A job is one run
and carries per-item progress in ``.voxqueue/``; a project is the definition of
a run and is portable. Rendering output is never referenced — reopening a
project does not assume the audio still exists, because the point of reopening
is usually to redo it.
"""

from __future__ import annotations

import json
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path

EXTENSION = "vqproj"
FILTER = f"VoxQueue project (*.{EXTENSION});;All files (*)"

VERSION = 1


@dataclass
class ProjectItem:
    index: int
    text: str = ""          # written form — what the transcript keeps
    spoken: str = ""        # full phonetic form, may equal text
    phonetic: str = ""      # only the changed words, for the grid
    speaker: str = ""
    pinned: bool = False
    source_line: str = ""


@dataclass
class Project:
    version: int = VERSION
    mode: str = "train"
    out_dir: str = ""
    voice: str = ""
    input_text: str = ""
    options: dict = field(default_factory=dict)
    voice_map: dict = field(default_factory=dict)
    items: list[ProjectItem] = field(default_factory=list)
    saved_at: float = 0.0
    saved_with: str = ""


def session_path() -> Path:
    """The implicit session slot, kept next to the config."""
    from .config import config_dir
    return config_dir() / "session.vqproj"


def save_session(bundle: "dict[str, Project | None] | Project | None",
                 active: int = 0) -> None:
    """Save the session for every tab, or clear the slot when all are empty.

    A single workspace is accepted for backwards compatibility with callers that
    only ever had one; a dict is the normal case now.

    Saving only the active tab lost the other two every time the app was closed
    from a different tab, because the slot is rewritten on every close.
    """
    target = session_path()
    try:
        if isinstance(bundle, Project):
            bundle = {bundle.mode: bundle}

        bundle = bundle or {}
        filled = {k: v for k, v in bundle.items() if v is not None and v.items}

        if not filled:
            if target.exists():
                target.unlink()
            return

        import json
        target.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "version": 2,
            "active_tab": int(active),
            "tabs": {mode: _project_to_dict(p) for mode, p in filled.items()},
        }
        tmp = target.with_suffix(".vqproj.tmp")
        tmp.write_text(json.dumps(payload, ensure_ascii=False, indent=1),
                       encoding="utf-8")
        tmp.replace(target)
    except Exception:
        pass


def clear_session() -> None:
    try:
        p = session_path()
        if p.exists():
            p.unlink()
    except Exception:
        pass


def load_session() -> "tuple[dict[str, Project], int] | None":
    """Reads the session: {mode: project} plus the tab that was open.

    Falls back to the old single-workspace file so an upgrade does not silently
    drop the user's work.
    """
    import json

    p = session_path()
    try:
        if not p.exists():
            return None

        raw = json.loads(p.read_text(encoding="utf-8"))
        if isinstance(raw, dict) and "tabs" in raw:
            out: dict[str, Project] = {}
            for mode, data in (raw.get("tabs") or {}).items():
                out[mode] = _project_from_dict(data)
            return (out, int(raw.get("active_tab") or 0)) if out else None
    except Exception:
        pass

    # Old format: a bare project.
    try:
        single = load(p)
        return ({single.mode: single}, 0) if single is not None else None
    except Exception:
        return None


def _project_to_dict(p: "Project") -> dict:
    from dataclasses import asdict
    return asdict(p)


def _project_from_dict(data: dict) -> "Project":
    items = [ProjectItem(
        index=int(i.get("index", 0)),
        text=str(i.get("text", "")),
        spoken=str(i.get("spoken", "")),
        phonetic=str(i.get("phonetic", "")),
        speaker=str(i.get("speaker", "")),
        pinned=bool(i.get("pinned", False)),
        source_line=str(i.get("source_line", "")),
    ) for i in (data.get("items") or [])]

    return Project(
        mode=str(data.get("mode", "train")),
        out_dir=str(data.get("out_dir", "")),
        voice=str(data.get("voice", "")),
        input_text=str(data.get("input_text", "")),
        options=dict(data.get("options") or {}),
        voice_map=dict(data.get("voice_map") or {}),
        items=items,
    )


def save(path: str | Path, project: Project) -> None:
    """Write atomically. A half-written project is worse than an old one:
    it opens and looks almost right."""
    project.saved_at = time.time()
    # Version lives in the package root, not in core; a missing one is not
    # worth failing a save over.
    try:
        from .. import __version__ as version
        project.saved_with = version
    except Exception:
        project.saved_with = ""

    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)

    payload = asdict(project)
    tmp = target.with_suffix(target.suffix + ".tmp")
    tmp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    tmp.replace(target)


def load(path: str | Path) -> Project:
    """Read a project. Raises ``ValueError`` with a readable message."""
    target = Path(path)
    try:
        raw = json.loads(target.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise ValueError(f"not a valid project file: {exc}") from exc

    if not isinstance(raw, dict):
        raise ValueError("file is empty or not a project")

    version = int(raw.get("version") or 1)
    if version > VERSION:
        raise ValueError(
            f"project was saved by a newer version (format {version})"
        )

    items = [
        ProjectItem(
            index=int(it.get("index", 0)),
            text=it.get("text", ""),
            spoken=it.get("spoken", ""),
            phonetic=it.get("phonetic", ""),
            speaker=it.get("speaker", ""),
            pinned=bool(it.get("pinned", False)),
            source_line=str(it.get("source_line", "")),
        )
        for it in raw.get("items") or []
    ]

    return Project(
        version=version,
        mode=raw.get("mode", "train"),
        out_dir=raw.get("out_dir", ""),
        voice=raw.get("voice", ""),
        input_text=raw.get("input_text", ""),
        options=raw.get("options") or {},
        voice_map=raw.get("voice_map") or {},
        items=items,
        saved_at=float(raw.get("saved_at") or 0.0),
        saved_with=raw.get("saved_with", ""),
    )
