"""Audio I/O, optional loudness normalisation, and defect detection.

The defect pass exists because the training set must be clean: one click at the
end of a clip teaches the model to click. It is heuristic by design — it flags
for human review, it does not delete anything.
"""

from __future__ import annotations

import io
import math
import shutil
import subprocess
import tempfile
import wave
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np

# Defect codes -> i18n keys. Keep in sync with voxqueue.i18n.
DEFECTS = (
    "clipped",        # samples pinned at full scale
    "click_end",      # isolated transient near the end
    "beep",           # steady high-amplitude tone (unwanted beep / tone)
    "discontinuity",  # sample-to-sample jump (pop)
    "short",          # too short to train on
    "silent",         # no usable signal
    "dc_offset",      # skewed baseline
    "loud_tail",      # tail much louder than the body
    "rambling",       # far longer than the text can plausibly need
)


@dataclass
class Analysis:
    duration: float = 0.0
    sample_rate: int = 0
    channels: int = 0
    peak_db: float = -120.0
    rms_db: float = -120.0
    defects: list[str] = field(default_factory=list)
    metrics: dict = field(default_factory=dict)

    @property
    def ok(self) -> bool:
        return not self.defects

    @property
    def severity(self) -> str:
        if not self.defects:
            return "ok"
        if "silent" in self.defects or "short" in self.defects:
            return "fail"
        return "warn"


# ---------------------------------------------------------------- reading


def read_wav(data: bytes | str | Path) -> tuple[np.ndarray, int, int]:
    """-> (float32 mono samples in [-1, 1], sample_rate, original_channels)."""
    if isinstance(data, (bytes, bytearray)):
        src = io.BytesIO(data)
    else:
        src = str(data)
    with wave.open(src, "rb") as w:
        ch = w.getnchannels()
        sw = w.getsampwidth()
        sr = w.getframerate()
        raw = w.readframes(w.getnframes())

    if sw == 1:
        arr = (np.frombuffer(raw, dtype=np.uint8).astype(np.float32) - 128.0) / 128.0
    elif sw == 2:
        arr = np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0
    elif sw == 3:
        b = np.frombuffer(raw, dtype=np.uint8).reshape(-1, 3).astype(np.int32)
        v = b[:, 0] | (b[:, 1] << 8) | (b[:, 2] << 16)
        v = np.where(v & 0x800000, v - 0x1000000, v)
        arr = v.astype(np.float32) / 8388608.0
    elif sw == 4:
        arr = np.frombuffer(raw, dtype=np.int32).astype(np.float32) / 2147483648.0
    else:
        raise ValueError(f"unsupported sample width: {sw}")

    if ch > 1:
        arr = arr.reshape(-1, ch).mean(axis=1)
    return arr.astype(np.float32, copy=False), sr, ch


def write_wav(path: Path, samples: np.ndarray, sr: int) -> None:
    """16-bit PCM mono, written atomically through a temp file."""
    path.parent.mkdir(parents=True, exist_ok=True)
    pcm = np.clip(samples, -1.0, 1.0)
    pcm = (pcm * 32767.0).astype(np.int16)
    tmp = path.with_suffix(path.suffix + ".tmp")
    with wave.open(str(tmp), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(pcm.tobytes())
    tmp.replace(path)


def wav_duration(data: bytes | Path) -> float:
    try:
        arr, sr, _ = read_wav(data)
        return len(arr) / float(sr) if sr else 0.0
    except Exception:
        return 0.0


# ------------------------------------------------------------ normalising


def _db(x: float) -> float:
    return 20.0 * math.log10(max(float(x), 1e-9))


def normalize(samples: np.ndarray, sr: int, target_peak_db: float = -1.5) -> np.ndarray:
    """Remove DC and bring the clip to a consistent perceived level.

    Pure-numpy fallback: DC block, then peak-normalise to `target_peak_db`.
    Consistent loudness across clips matters because the model learns level
    along with timbre — clips at different levels yield an unstable voice.
    """
    x = samples.astype(np.float32, copy=True)
    if x.size == 0:
        return x
    x -= float(np.mean(x))
    peak = float(np.max(np.abs(x)))
    if peak < 1e-6:
        return x
    target = 10.0 ** (target_peak_db / 20.0)
    x *= target / peak
    return x


def _ffmpeg() -> str | None:
    return shutil.which("ffmpeg")


def normalize_ffmpeg(src: Path, dst: Path, sample_rate: int) -> bool:
    """Preferred path: highpass + EBU R128 loudness, mono, target rate.

    Returns False when ffmpeg is missing or fails, so the caller can fall back
    to the numpy path instead of losing the clip.
    """
    exe = _ffmpeg()
    if not exe:
        return False
    cmd = [exe, "-hide_banner", "-v", "error", "-y", "-i", str(src),
           "-af", "highpass=f=70:p=2,loudnorm=I=-16:TP=-1.5:LRA=11",
           "-ar", str(sample_rate), "-ac", "1", "-c:a", "pcm_s16le", str(dst)]
    try:
        subprocess.run(cmd, check=True, capture_output=True,
                       creationflags=_no_window())
        return dst.exists() and dst.stat().st_size > 44
    except Exception:
        return False


def _no_window() -> int:
    """Keep console windows from flashing on Windows."""
    if hasattr(subprocess, "CREATE_NO_WINDOW"):
        return subprocess.CREATE_NO_WINDOW  # type: ignore[attr-defined]
    return 0


# --------------------------------------------------------------- analysis


def _frame_peaks(x: np.ndarray, frame: int) -> np.ndarray:
    n = len(x) // frame
    if n < 1:
        return np.array([float(np.max(np.abs(x))) if len(x) else 0.0], dtype=np.float32)
    trimmed = x[: n * frame].reshape(n, frame)
    return np.max(np.abs(trimmed), axis=1)


def _tonality(seg: np.ndarray, sr: int) -> float:
    """How much energy sits in one narrow spectral peak (0..1).

    A steady beep is nearly a single frequency, so one bin dominates. Speech
    spreads energy across many bins even in a sustained vowel.
    """
    if len(seg) < 64:
        return 0.0
    win = seg * np.hanning(len(seg))
    spec = np.abs(np.fft.rfft(win))
    total = float(np.sum(spec))
    if total <= 1e-9:
        return 0.0
    # Sum the strongest bin plus its immediate neighbours — real tones leak a
    # little, and a pure single-bin test misses them.
    peak = int(np.argmax(spec))
    lo, hi = max(0, peak - 2), min(len(spec), peak + 3)
    return float(np.sum(spec[lo:hi]) / total)


def analyze(data: bytes | Path, min_duration: float = 0.4) -> Analysis:
    """Measure a clip and list every defect found. Never raises on bad audio."""
    a = Analysis()
    try:
        x, sr, ch = read_wav(data)
    except Exception:
        a.defects.append("silent")
        return a

    a.sample_rate, a.channels = sr, ch
    if x.size == 0 or sr <= 0:
        a.defects.append("silent")
        return a

    a.duration = len(x) / float(sr)
    peak = float(np.max(np.abs(x)))
    rms = float(np.sqrt(np.mean(x ** 2)))
    a.peak_db, a.rms_db = _db(peak), _db(rms)

    if peak < 0.02:
        a.defects.append("silent")
        return a
    if a.duration < min_duration:
        a.defects.append("short")

    # --- clipping
    clipped = float(np.mean(np.abs(x) >= 0.999))
    a.metrics["clipped_frac"] = clipped
    if clipped > 0.0005:
        a.defects.append("clipped")

    # --- DC offset
    dc = abs(float(np.mean(x)))
    a.metrics["dc"] = dc
    if dc > 0.05:
        a.defects.append("dc_offset")

    # --- discontinuity (pop): an isolated sample-to-sample jump
    if len(x) > 2:
        d = np.abs(np.diff(x))
        jump = float(np.max(d))
        a.metrics["max_jump"] = jump
        # A pop is a jump that is large in absolute terms AND stands far above
        # the typical jump of this clip.
        typical = float(np.percentile(d, 99.0)) or 1e-6
        if jump > 0.6 and jump > 8.0 * typical:
            a.defects.append("discontinuity")

    # --- tail analysis: click / beep / loud tail at the end
    frame = max(1, int(sr * 0.010))          # 10 ms
    peaks = _frame_peaks(x, frame)
    if len(peaks) >= 4:
        body = peaks[: max(1, int(len(peaks) * 0.6))]
        body_med = float(np.median(body)) or 1e-6
        body_max = float(np.max(body)) or 1e-6

        tail_start = max(0, int(len(x) - min(len(x), int(sr * 0.6))))
        tail = x[tail_start:]
        if len(tail) > frame * 3:
            # A click: one tail frame far above the body, with quieter frames
            # around it. Without the neighbour test, normal emphasis trips it.
            tpeaks = _frame_peaks(tail, frame)
            if len(tpeaks) >= 3:
                idx = int(np.argmax(tpeaks))
                loc = float(tpeaks[idx])
                left = float(tpeaks[idx - 1]) if idx > 0 else 0.0
                right = float(tpeaks[idx + 1]) if idx < len(tpeaks) - 1 else 0.0
                neighbours = max(left, right)
                a.metrics["tail_peak"] = loc
                if loc > max(0.12, 4.0 * body_med) and neighbours < 0.6 * loc:
                    a.defects.append("click_end")

            # A beep: tonal, sustained, and louder than the body.
            ton = _tonality(tail, sr)
            a.metrics["tail_tonality"] = ton
            tail_rms = float(np.sqrt(np.mean(tail ** 2)))
            if ton > 0.55 and tail_rms > max(0.02, 1.6 * rms):
                a.defects.append("beep")

            # A loud tail: the ending is much hotter than the average body.
            if tail_rms > max(0.05, 3.0 * rms) and "beep" not in a.defects:
                a.defects.append("loud_tail")

    return a


# ----------------------------------------------------------------- export


def transcode(src: Path, dst: Path, fmt: str) -> bool:
    """Convert a finished WAV to mp3/ogg when the user asked for one."""
    if fmt == "wav":
        try:
            shutil.copy2(src, dst)
            return True
        except Exception:
            return False
    exe = _ffmpeg()
    if not exe:
        return False
    codec = {"mp3": ["-c:a", "libmp3lame", "-q:a", "2"],
             "ogg": ["-c:a", "libvorbis", "-q:a", "5"],
             "flac": ["-c:a", "flac"]}.get(fmt)
    if not codec:
        return False
    cmd = [exe, "-hide_banner", "-v", "error", "-y", "-i", str(src),
           "-ac", "1", *codec, str(dst)]
    try:
        subprocess.run(cmd, check=True, capture_output=True, creationflags=_no_window())
        return dst.exists()
    except Exception:
        return False


def temp_wav(data: bytes) -> Path:
    fd, name = tempfile.mkstemp(suffix=".wav")
    with io.open(fd, "wb") as f:
        f.write(data)
    return Path(name)
