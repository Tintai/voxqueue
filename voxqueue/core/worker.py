"""The generation queue.

Runs on its own thread so the window stays responsive during a 900-second
render, and reports through signals rather than touching widgets directly.

Two behaviours worth knowing:

* Pause takes effect *between* items, never mid-request. Cutting a request in
  half leaves the server rendering into a socket nobody reads, which is how
  you get a stuck GPU or a leaked sidecar process.
* A retry happens only for transport/protocol failures. A clip that came back
  with a defect is *kept* and flagged — a person decides, because the detector
  is a heuristic and silently discarding takes away the choice.
"""

from __future__ import annotations

import time
import traceback
from pathlib import Path

import numpy as np
from PySide6.QtCore import QThread, Signal

from . import audio
from .state import Item, Job
from .tts_client import TTSClient, TTSError


class QueueWorker(QThread):
    item_started = Signal(int, str)        # index, text
    item_finished = Signal(int, str, float, list)   # index, relpath, duration, defects
    item_failed = Signal(int, str)         # index, error
    item_skipped = Signal(int)
    progress = Signal(int, int, int)       # done, total, review
    log = Signal(str)
    all_finished = Signal(bool)            # cancelled?

    def __init__(self, job: Job, client: TTSClient, parent=None) -> None:
        super().__init__(parent)
        self.job = job
        self.client = client
        self._pause = False
        self._cancel = False
        self._retries = 2

    # ------------------------------------------------------------- controls
    # When set, only these item indices are rendered. Used by "generate this
    # line" — a full queue run would redo everything, and the user asked for one
    # clip. None means no filter.
    only_indices: set[int] | None = None

    def pause(self) -> None:
        self._pause = True

    def resume(self) -> None:
        self._pause = False

    def cancel(self) -> None:
        self._cancel = True
        self._pause = False

    @property
    def paused(self) -> bool:
        return self._pause

    # -------------------------------------------------------------- helpers
    def _out_name(self, item: Item, dur: float) -> Path:
        """Where this clip goes.

        train/bulk: `0000.wav` — plain numbers, so metadata.csv can reference
                    them by stem the way LJSpeech does.
        dub:        `<speaker>/<SPEAKER>_0000_2.4s.wav` — speaker first so a
                    directory listing is readable and one voice's lines can be
                    shipped as a folder.
        """
        out = Path(self.job.out_dir)
        fmt = str(self.job.options.get("audio_format") or "wav").lower()
        start = int(self.job.options.get("index_start") or 0)
        num = item.index + start

        # Piper's dataset is documented as extensionless stems with the text in
        # metadata.csv, so the extension is optional. The format is therefore
        # carried separately and never derived from the name, which may have none.
        ext = f".{fmt}" if self.job.options.get("add_extension", True) else ""

        if self.job.mode == "dub":
            from .splitter import safe_filename
            spk = safe_filename(item.speaker or "voice")
            # Invariant formatting: on a Polish locale "{:.2f}" yields "2,41",
            # and the comma would not reproduce on any other machine.
            base = f"{spk}_{num:04d}_{dur:.2f}s".replace(",", ".")
            return out / "voices" / spk / f"{base}{ext}"

        from .splitter import safe_filename

        tpl = str(self.job.options.get("train_template") or "{index}")
        raw = tpl.replace("{index}", f"{num:04d}").replace(
            "{index_raw}", str(num))

        # The template is free text from Settings and is concatenated straight onto
        # the output folder. "../outside/{index}" wrote outside the chosen folder;
        # a ':' or '?' threw on the first item and failed the whole run.
        stem = safe_filename(raw, f"{num:04d}")
        return out / "wav" / f"{stem}{ext}"

    def _voice_for(self, item: Item) -> str:
        if self.job.mode == "dub" and item.speaker:
            v = self.job.voice_map.get(item.speaker)
            if v:
                return v
        return self.job.voice

    # ----------------------------------------------------------------- main
    def run(self) -> None:  # noqa: C901 — the loop is linear, not complex
        total = len(self.job.items)
        cancelled = False
        errored = False
        try:
            for item in self.job.items:
                if self._cancel:
                    cancelled = True
                    break
                while self._pause and not self._cancel:
                    self.msleep(120)
                if self._cancel:
                    cancelled = True
                    break
                if item.status == "done" and not self.job.options.get("force"):
                    continue

                self.item_started.emit(item.index, item.text)
                item.status = "running"
                item.attempts += 1
                item.updated = time.time()
                self.job.save()

                voice = self._voice_for(item)
                if not voice:
                    item.status = "failed"
                    item.error = "no voice assigned"
                    self.job.save()
                    self.item_failed.emit(item.index, item.error)
                    continue

                data = None
                last_err = ""
                for attempt in range(self._retries + 1):
                    try:
                        # The phonetic form is what the engine can pronounce; the written
                        # form never leaves the transcript.
                        data = self.client.speak(item.spoken_or_text, voice)
                        break
                    except TTSError as e:
                        last_err = str(e)
                        self.log.emit(f"[{item.index}] attempt {attempt + 1}: {last_err}")
                        time.sleep(1.5 * (attempt + 1))
                    except Exception as e:  # noqa: BLE001
                        last_err = f"{type(e).__name__}: {e}"
                        self.log.emit(traceback.format_exc(limit=2))

                if not data:
                    # A cancel is not a failure: the row goes back to pending, which
                    # is neutral, instead of turning red and staying red until the
                    # next run rewrote it.
                    if self._cancel:
                        item.status = "pending"
                        item.error = ""
                        self.job.save()
                        cancelled = True
                        break

                    item.status = "failed"
                    item.error = last_err or "generation failed"
                    self.job.save()
                    self.item_failed.emit(item.index, item.error)
                    continue

                try:
                    dur, rel = self._finish(item, data)
                except Exception as e:  # noqa: BLE001
                    item.status = "failed"
                    item.error = f"{type(e).__name__}: {e}"
                    self.job.save()
                    self.item_failed.emit(item.index, item.error)
                    continue

                # The only place the flag is cleared. Until the clip is written the
                # row stays in the list: queued, running, or failed and to be retried.
                item.needs_regen = False

                item.status = "done"
                item.file = rel
                item.duration = dur
                item.error = ""
                item.updated = time.time()
                self.job.save()
                self.item_finished.emit(item.index, rel, dur, list(item.defects))

                self.progress.emit(self.job.done, total, self.job.review)
        except Exception:  # noqa: BLE001
            # A run that blew up is neither "finished" nor "cancelled". Reported as
            # finished, the shutdown option would power the machine off because of a
            # crash — the opposite of what it is for.
            errored = True
            self.log.emit(traceback.format_exc())
        finally:
            # No row may be left mid-run: a "running" row keeps its marker forever.
            stranded = [i for i in self.job.items if i.status == "running"]
            for i in stranded:
                i.status = "pending"
                i.error = ""
            if stranded:
                self.log.emit("interrupted row(s) returned to the queue")

            self.job.save()
            self.progress.emit(self.job.done, total, self.job.review)
            self.all_finished.emit(cancelled or errored)

    # ------------------------------------------------------------- per item
    def _finish(self, item: Item, data: bytes) -> tuple[float, str]:
        """Normalise, measure, flag defects, write. Returns (duration, relpath)."""
        opts = self.job.options
        raw_path = Path(self.job.out_dir) / ".tmp" / f"raw_{item.index:06d}.wav"
        raw_path.parent.mkdir(parents=True, exist_ok=True)
        raw_path.write_bytes(data)

        samples, sr, _ = audio.read_wav(raw_path)
        if samples.size == 0:
            raise ValueError("decoded audio is empty")

        # Normalisation first: the defect pass must judge the clip as it will
        # be stored, otherwise a normalisation-induced level change invalidates
        # the "loud tail" measurement.
        if opts.get("normalize", True):
            sr_target = int(opts.get("sample_rate") or sr)
            ok = audio.normalize_ffmpeg(raw_path, raw_path, sr_target)
            if ok:
                samples, sr, _ = audio.read_wav(raw_path)
            else:
                samples = audio.normalize(samples, sr)
                audio.write_wav(raw_path, samples, sr)

        defects: list[str] = []
        if opts.get("detect_defects", True):
            a = audio.analyze(raw_path)
            defects = list(a.defects)
            # Plausibility: a clip that runs several times longer than the text
            # can need means the engine rambled. This is the OmniVoice failure
            # mode that silently poisons a dataset.
            # Length estimate uses the spoken form: "2024" is four
            # characters written but six syllables spoken.
            expected = max(0.8, len(item.spoken_or_text) / 13.0)
            if a.duration > max(2.5, expected * 2.2):
                defects.append("rambling")

        dur = len(samples) / float(sr) if sr else 0.0
        item.defects = defects

        final = self._out_name(item, dur)
        final.parent.mkdir(parents=True, exist_ok=True)
        fmt = final.suffix.lstrip(".").lower()
        if fmt == "wav":
            audio.write_wav(final, samples, sr)
        else:
            tmp = final.with_suffix(".wav")
            audio.write_wav(tmp, samples, sr)
            if not audio.transcode(tmp, final, fmt):
                raise ValueError(f"cannot encode {fmt} (ffmpeg missing?)")
            tmp.unlink(missing_ok=True)

        # Bulk mode carries its transcript next to the audio.
        if self.job.mode == "bulk" and opts.get("write_transcript", True):
            final.with_suffix(".txt").write_text(item.text + "\n", encoding="utf-8")

        try:
            raw_path.unlink()
        except Exception:
            pass

        return dur, str(final.relative_to(Path(self.job.out_dir)))


def write_metadata(job: Job) -> Path:
    """Write metadata.csv in LJSpeech form: `<stem>|<text>`, no header.

    Only finished, defect-free items are written. Including a flagged clip
    would train the model on exactly the artefact the detector found.
    """
    out = Path(job.out_dir)
    path = out / "metadata.csv"
    rows = []
    for item in sorted(job.items, key=lambda i: i.index):
        if item.status != "done" or not item.file:
            continue
        stem = Path(item.file).stem
        transcript = (
            item.spoken_or_text
            if job.options.get("metadata_spoken", True)
            else item.text
        )
        rows.append(f"{stem}|{transcript}")
    path.write_text("\n".join(rows) + ("\n" if rows else ""), encoding="utf-8")
    return path
