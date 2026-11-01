"""Resumable job state.

One JSON file per job, rewritten atomically after every finished item. A crash
or a power cut therefore costs at most the item that was in flight — never the
whole run. This is the whole reason the files are separate from the WAVs: the
audio can be deleted and regenerated, but the mapping text <-> index cannot be
rebuilt once the paste is gone.
"""

from __future__ import annotations

import json
import time
import uuid
from dataclasses import asdict, dataclass, field
from pathlib import Path

JOBS_DIRNAME = ".voxqueue"


@dataclass
class Item:
    index: int
    text: str                    # written form — what metadata.csv keeps
    spoken: str = ""             # phonetic form — what the engine says
    phonetic: str = ""           # only the changed words, for the grid
    source_line: str = ""        # raw line from the box, markers and all
    pinned: bool = False         # marked for attention by the user

    # Waiting to be rendered again, and stays flagged until it succeeds. Separate
    # from status because status moves during the run (stale -> running -> done),
    # and a filter on status made the row vanish the instant it started — the one
    # moment the user is watching it.
    needs_regen: bool = False
    speaker: str = ""
    status: str = "pending"      # pending | running | done | failed | skipped
    file: str = ""               # relative path of the written audio
    duration: float = 0.0
    defects: list[str] = field(default_factory=list)
    error: str = ""
    attempts: int = 0
    updated: float = 0.0

    @property
    def spoken_or_text(self) -> str:
        """What the engine receives. Falls back to the written form."""
        return self.spoken or self.text

    @property
    def has_phonetic(self) -> bool:
        return bool(self.spoken) and self.spoken != self.text

    @property
    def phonetic_display(self) -> str:
        """Empty when the pronunciation matches the transcription."""
        return self.phonetic or (self.spoken if self.has_phonetic else "")

    @property
    def pron_rank(self) -> int:
        """Bucket for "changed pronunciation first": 0 = has markers, 1 = rest."""
        return 0 if self.has_phonetic else 1

    def matches(self, needle: str) -> bool:
        """True when the query appears in the text, the pronunciation or the id.

        Searching only the text would hide exactly the row a user is hunting for:
        they remember the word they fixed, not the sentence around it.
        """
        n = (needle or "").strip().lower()
        if not n:
            return True
        return (n in (self.text or "").lower()
                or n in (self.spoken or "").lower()
                or n in (self.phonetic or "").lower()
                or n in (self.speaker or "").lower()
                or n in str(self.index))

    @property
    def sort_rank(self) -> int:
        """Bucket for "errors first": 0 failed, 1 stale, 2 flagged, 3 rest.

        A thousand finished clips bury the dozen that need attention, so the list
        offers to lift them to the top.
        """
        if self.status == "failed":
            return 0
        if self.status == "stale":
            return 1
        if self.status == "done" and self.defects:
            return 2
        return 3

    @property
    def awaits_regen(self) -> bool:
        """In the "needs regeneration" list: flagged, or legacy stale row."""
        return self.needs_regen or self.status == "stale"

    @property
    def is_stale(self) -> bool:
        """Rendered once, then the line was edited: the clip is out of date."""
        return self.status == "stale"

    @property
    def ok(self) -> bool:
        return self.status == "done" and not self.defects

    @property
    def needs_review(self) -> bool:
        return self.status == "done" and bool(self.defects)


@dataclass
class Job:
    id: str = ""
    mode: str = "train"          # train | dub | bulk
    created: float = 0.0
    out_dir: str = ""
    voice: str = ""
    voice_map: dict = field(default_factory=dict)   # speaker -> voice id
    options: dict = field(default_factory=dict)
    items: list[Item] = field(default_factory=list)

    def __post_init__(self) -> None:
        if not self.id:
            self.id = time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6]
        if not self.created:
            self.created = time.time()

    # ------------------------------------------------------------- counters
    def count(self, status: str) -> int:
        return sum(1 for i in self.items if i.status == status)

    @property
    def done(self) -> int:
        return self.count("done")

    @property
    def failed(self) -> int:
        return self.count("failed")

    @property
    @property
    def running_index(self) -> int | None:
        """Index of the item currently in flight, or None."""
        for item in self.items:
            if item.status == "running":
                return item.index
        return None

    @property
    def review(self) -> int:
        return sum(1 for i in self.items if i.needs_review)

    def next_pending(self) -> Item | None:
        for i in self.items:
            if i.status in ("pending", "running"):
                return i
        return None

    # ---------------------------------------------------------------- paths
    @staticmethod
    def state_dir(out_dir: Path) -> Path:
        d = Path(out_dir) / JOBS_DIRNAME
        d.mkdir(parents=True, exist_ok=True)
        return d

    def state_path(self) -> Path:
        return self.state_dir(Path(self.out_dir)) / f"{self.id}.json"

    # ------------------------------------------------------------ persistence
    def save(self) -> None:
        path = self.state_path()
        tmp = path.with_suffix(".json.tmp")
        payload = {
            "id": self.id, "mode": self.mode, "created": self.created,
            "out_dir": self.out_dir, "voice": self.voice,
            "voice_map": self.voice_map, "options": self.options,
            "items": [asdict(i) for i in self.items],
        }
        try:
            tmp.write_text(json.dumps(payload, ensure_ascii=False, indent=1),
                           encoding="utf-8")
            tmp.replace(path)
        except Exception:
            pass

    @classmethod
    def load(cls, path: Path) -> "Job | None":
        try:
            raw = json.loads(Path(path).read_text(encoding="utf-8"))
        except Exception:
            return None
        job = cls(id=raw.get("id", ""), mode=raw.get("mode", "train"),
                  created=raw.get("created", 0.0), out_dir=raw.get("out_dir", ""),
                  voice=raw.get("voice", ""), voice_map=raw.get("voice_map") or {},
                  options=raw.get("options") or {})
        job.items = [Item(**{k: v for k, v in it.items()
                             if k in Item.__dataclass_fields__})
                     for it in raw.get("items") or []]
        return job

    @classmethod
    def list_jobs(cls, out_dir: Path) -> list[Path]:
        d = Path(out_dir) / JOBS_DIRNAME
        if not d.is_dir():
            return []
        return sorted(d.glob("*.json"), key=lambda p: p.stat().st_mtime, reverse=True)

    def restore_progress(self) -> int:
        """Copy per-item results from an earlier job in the same output folder.

        Without this, every restart looks like a fresh start: the audio is on
        disk but the list says "pending", so a night run that was interrupted
        gets regenerated from scratch — paying twice for the same clips.

        Matching is by TEXT, not by index, because index_start may differ
        between runs. A result is copied only when the file it points at still
        exists; otherwise the item stays pending, so the list never claims to
        have something it cannot play.

        Returns how many items were restored.
        """
        try:
            out = Path(self.out_dir)
            files = self.list_jobs(out)
        except Exception:
            return 0

        for path in files:
            previous = Job.load(path)
            if previous is None:
                continue
            if previous.id == self.id:
                continue
            if previous.mode != self.mode:
                continue
            if not previous.items:
                continue

            # Map written text -> final state. Only finished work counts: a
            # "failed" item should be retried, not inherited as failed.
            done: dict[str, Item] = {}
            for old_item in previous.items:
                if old_item.status != "done":
                    continue
                if not old_item.file:
                    continue
                done.setdefault(old_item.text, old_item)
            if not done:
                continue

            restored = 0
            for item in self.items:
                if item.status == "done":
                    continue
                old_item = done.get(item.text)
                if old_item is None:
                    continue

                # The file must still be there. A restored row pointing at a
                # deleted clip is worse than a pending row.
                target = Path(old_item.file)
                if not target.is_absolute():
                    target = out / old_item.file
                if not target.exists():
                    continue

                item.status = "done"
                item.file = old_item.file
                item.duration = old_item.duration
                item.defects = list(old_item.defects)
                item.error = ""
                restored += 1

            if restored:
                return restored

        return 0

    def reset_failed(self) -> int:
        """Re-queue everything that is not up to date. Returns how many.

        Two states need it and they are equally out of date:

          failed - generation did not succeed;
          stale  - generation succeeded, then the line was edited, so the audio on
                   disk was made from the previous text or pronunciation.

        Resetting only "failed" skipped exactly the rows a pronunciation edit had
        just marked, which is what the button was being asked to pick up.
        """
        n = 0
        for i in self.items:
            if i.status in ("failed", "stale"):
                if i.status == "stale":
                    # Survives the reset: still needs rendering, only moved from
                    # "out of date" to "queued".
                    i.needs_regen = True
                i.status = "pending"
                i.error = ""
                i.defects = []
                n += 1
        return n

    def mark_for_regen(self, indices: list[int]) -> int:
        n = 0
        ids = set(indices)
        for i in self.items:
            if i.index in ids:
                i.status = "pending"
                i.error = ""
                i.defects = []
                n += 1
        return n
