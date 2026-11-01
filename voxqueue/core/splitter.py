"""Text splitting for the three modes.

Every splitter returns a list of `Line` objects. Keeping the original text
next to the cleaned text matters: the dataset's metadata.csv must contain
what was *said*, and what was said is the cleaned form actually sent to the
engine, not the raw paste.
"""

from __future__ import annotations

import re
from dataclasses import dataclass


@dataclass
class Line:
    text: str            # what will be spoken and written to metadata
    speaker: str = ""    # dubbing mode only
    index: int = 0       # assigned when the plan is built
    raw: str = ""        # source line before cleaning, for edits to the box
    source_index: int | None = None   # number from a "NNNN|" prefix, if any


_WS = re.compile(r"[^\S\n]+")

# A leading "0000|" is metadata.csv syntax, not part of the line. Files handed
# back from an export arrive with it, and without stripping it the id ends up
# inside the utterance: spoken aloud, then written into the next CSV twice.
#
# Telling a dataset id from an ordinary pipe is not free: "12|13 to para liczb"
# is a sentence, not an indexed line. The rule is therefore narrow — a prefix
# counts only when it is zero-padded or at least four digits long.
_INDEX_PREFIX = re.compile(r"^\s*(\d{2,6})\s*\|")


def strip_index(line: str | None) -> tuple[str, int | None]:
    """Remove a leading ``NNNN|`` and report the number it carried."""
    if not line:
        return line or "", None

    m = _INDEX_PREFIX.match(line)
    if not m:
        return line, None

    digits = m.group(1)
    if not (digits[0] == "0" or len(digits) >= 4):
        return line, None

    rest = line[m.end():].lstrip(" \t")
    if not rest:
        return line, None

    return rest, int(digits)
_MARKER_CHARS = re.compile(r"^[#=\-*~_+]{2,}$")

# Speaker prefixes are written in many ways: "SHEPPARD:", "Sheppard:", "[SHEPPARD]",
# "SHEPPARD —". Accept all of them, but require the name to look like a name
# (no sentence punctuation) so a normal line ending in a colon is not eaten.
_SPEAKER_PATTERNS = (
    # NAME: text    (text may be empty - the line below carries it)
    re.compile(r"^\s*([\w][\w \-'.]{0,38})\s*:\s*(.*)$"),
    # [NAME] text   — the bracket form has no colon, so it needs its own rule,
    # otherwise a line like "[EDI] Analysis complete." is appended to the
    # previous speaker instead of starting a new one.
    re.compile(r"^\s*\[([^\]]{1,40})\]\s*(.*)$"),
    # NAME - text
    re.compile(r"^\s*([A-Z][\w \-'.]{0,38})\s+[—–-]\s+(.*)$"),
)


def _looks_like_name(label: str) -> bool:
    """Distinguish a character label from prose that happens to have a colon.

    Character labels are written ALL CAPS or Title Case, so every word after
    the first starts with a capital. "He said: hello" therefore fails and is
    kept as ordinary text; "Master Chief: hello" passes.
    """
    if not label or len(label) > 40:
        return False
    if any(ch in label for ch in ".!?,"):
        return False
    words = label.split()
    if len(words) > 4:
        return False
    for w in words[1:]:
        if w[:1].islower():
            return False
    return True


def clean(text: str) -> str:
    """Collapse runs of spaces/tabs, drop stray control characters.

    Newlines inside one utterance are spaces: a line break inside a single
    spoken line would otherwise reach the engine as its own utterance.
    """
    t = (text or "").replace("\r", "")
    t = t.replace("\n", " ")
    t = _WS.sub(" ", t)
    return t.strip()


def split_lines(text: str) -> list[Line]:
    """Training mode: one non-empty line = one utterance."""
    out: list[Line] = []
    for raw in (text or "").splitlines():
        body, idx = strip_index(raw)
        c = clean(body)
        if c:
            out.append(Line(text=c, raw=raw, source_index=idx))
    return out


def split_blocks(text: str, marker: str = "###") -> list[Line]:
    """Bulk mode: blocks separated by a marker line or a triple newline.

    A block may span several lines; they are joined, because one block is one
    utterance. Blank markers (`====`, `----`, `***`) also count, which is what
    people paste from scripts without meaning to.
    """
    if not text:
        return []
    m = (marker or "").strip()
    parts: list[str] = []
    buf: list[str] = []

    def flush() -> None:
        chunk = clean(" ".join(buf))
        if chunk:
            parts.append(chunk)
        buf.clear()

    for raw in text.splitlines():
        # The prefix belongs to the line, not to the block, so it is removed
        # before anything else looks at the text.
        body, _ = strip_index(raw)
        stripped = body.strip()
        is_marker = False
        if m and stripped == m:
            is_marker = True
        elif m and m in stripped and len(stripped) < max(12, len(m) + 4):
            is_marker = True
        elif _MARKER_CHARS.match(stripped):
            is_marker = True
        if is_marker:
            flush()
            continue
        if not stripped:
            buf.append("")
            continue
        buf.append(stripped)
    flush()

    # A pasted list with no explicit markers and no blank lines still has to
    # split somewhere meaningful: fall back to one line = one block.
    if len(parts) == 1:
        lines = split_lines(text)
        if len(lines) > 1:
            return lines
    return [Line(text=p) for p in parts]


def split_dub(text: str, default_speaker: str = "") -> list[Line]:
    """Dubbing mode: split on speaker prefixes.

    Returns lines in script order. Text before the first recognised speaker is
    kept under `default_speaker` rather than dropped — losing a line silently
    is worse than attributing it to a fallback voice.
    """
    out: list[Line] = []
    current = default_speaker
    buf: list[str] = []

    def flush() -> None:
        chunk = clean(" ".join(buf))
        if chunk:
            out.append(Line(text=chunk, speaker=current))
        buf.clear()

    for raw in (text or "").splitlines():
        # "0000|SHEPPARD: ..." — the id would otherwise hide the speaker and the
        # whole line would fall through as unattributed text.
        body, _ = strip_index(raw)
        line = body.strip()
        if not line:
            continue
        matched = False
        for pat in _SPEAKER_PATTERNS:
            mo = pat.match(line)
            if not mo:
                continue
            name = mo.group(1).strip()
            body = mo.group(2).strip()
            if not _looks_like_name(name):
                continue
            flush()
            current = name
            if body:
                buf.append(body)
            matched = True
            break
        if not matched:
            buf.append(line)
    flush()
    return out


def speakers(lines: list[Line]) -> list[str]:
    """Distinct speakers in first-appearance order."""
    seen: list[str] = []
    for ln in lines:
        if ln.speaker and ln.speaker not in seen:
            seen.append(ln.speaker)
    return seen


# The names Windows reserves for devices. A folder called CON is refused even
# today, so a speaker named "Con" would fail the whole dubbing run.
_RESERVED = {
    "CON", "PRN", "AUX", "NUL",
    *(f"COM{i}" for i in range(1, 10)),
    *(f"LPT{i}" for i in range(1, 10)),
}


def safe_filename(name: str, fallback: str = "voice") -> str:
    """Turn a speaker name or filename stem into something every filesystem accepts.

    Three separate hazards are handled:
      * characters that are illegal or act as separators, so a name cannot escape
        the output folder;
      * leading/trailing dots and spaces, which Windows silently strips and which
        would make two different names collide;
      * the DOS device names, which are still refused for a file called exactly
        that.
    """
    n = re.sub(r"[^\w\-. ]+", "", (name or "").strip(), flags=re.UNICODE)
    n = re.sub(r"\s+", "_", n).strip("._-")
    if not n:
        return fallback

    # "CON.wav" is as invalid as "CON", so the check is on the stem.
    if n.split(".")[0].upper() in _RESERVED:
        n = "_" + n

    return n
