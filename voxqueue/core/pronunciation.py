"""Splitting spelling from pronunciation.

A TTS model reads letters, not spelling. "Aether" comes out as "A-e-ther"
instead of "Eter", "2024" is read digit by digit or skipped, and acronyms get
mangled. The fix is to give the engine a phonetic spelling while keeping the
correct spelling in metadata.csv and in the .txt sidecar.

Syntax — one marker, spoken form first::

    {Eter|Aether}      says "Eter", writes "Aether"
    {dwa tysiące dwudziestego czwartego|2024}
    {pi em|PM}

Braces were chosen over square brackets because this app already uses
``[NAME]`` for speaker labels.

Anything that is not a well-formed marker is passed through untouched: a stray
``{``, an empty side, or a missing ``|`` stays literal rather than silently
deleting part of the line.
"""

from __future__ import annotations


def split(text: str | None) -> tuple[str, str]:
    """Return ``(spoken, written)``.

    Without markers both are the input, trimmed — so callers never have to
    branch on "are there markers".
    """
    src = text or ""
    if not src or "{" not in src:
        stripped = src.strip()
        return stripped, stripped

    spoken: list[str] = []
    written: list[str] = []
    i = 0

    while i < len(src):
        open_at = src.find("{", i)

        if open_at < 0:
            spoken.append(src[i:])
            written.append(src[i:])
            break

        # Text before the marker is literally both forms.
        spoken.append(src[i:open_at])
        written.append(src[i:open_at])

        close_at = src.find("}", open_at + 1)
        if close_at < 0:
            # Unclosed brace: keep the remainder as written.
            spoken.append(src[open_at:])
            written.append(src[open_at:])
            break

        inner = src[open_at + 1:close_at]
        bar = inner.find("|")

        spoken_part = inner[:bar].strip() if bar > 0 else ""
        written_part = inner[bar + 1:].strip() if 0 <= bar < len(inner) - 1 else ""

        if spoken_part and written_part:
            spoken.append(spoken_part)
            written.append(written_part)
        else:
            # Not a usable marker (no pipe, or one side empty): leave it be
            # rather than dropping content the user typed.
            literal = src[open_at:close_at + 1]
            spoken.append(literal)
            written.append(literal)

        i = close_at + 1

    return "".join(spoken).strip(), "".join(written).strip()


def to_written(text: str | None) -> str:
    """Drop the markers, keeping only the written form. For display."""
    return split(text)[1]


def spoken_fragments(text: str | None) -> list[str]:
    """The spoken halves of every usable marker, in order.

    The grid shows these instead of the whole spoken sentence: only the words
    that were actually changed matter for review, and a full sentence repeated
    in a second column is noise. The written form is already in the Text
    column, so listing both here would just duplicate it.
    """
    out: list[str] = []
    src = text or ""
    if not src or "{" not in src:
        return out

    i = 0
    while i < len(src):
        open_at = src.find("{", i)
        if open_at < 0:
            break
        close_at = src.find("}", open_at + 1)
        if close_at < 0:
            break

        inner = src[open_at + 1:close_at]
        bar = inner.find("|")
        if 0 < bar < len(inner) - 1:
            spoken = inner[:bar].strip()
            written = inner[bar + 1:].strip()
            if spoken and written and spoken != written:
                # Only the words that actually differ. A marker can wrap a whole
                # sentence — the pronunciation dialog produces exactly that — and
                # repeating the sentence in the second column is noise, not
                # information. The written form already sits in the Text column.
                for word in changed_words(spoken, written):
                    if word not in out:
                        out.append(word)

        i = close_at + 1

    return out


_EDGE = " \t\n.,!?;:\"'()"


def changed_words(spoken: str | None, written: str | None) -> list[str]:
    """Words of the spoken form that differ from the written form.

    Positional comparison, which is enough for the case this exists for: a
    pronunciation marker replaces a word with a phonetic spelling of the same
    word. When the word counts differ the whole spoken form is returned — a real
    alignment would be guesswork, and a wrong guess in a review column is worse
    than a long one.
    """
    sp = (spoken or "").split()
    wr = (written or "").split()

    if len(sp) != len(wr):
        return [" ".join(sp)] if sp else []

    out: list[str] = []
    for a, b in zip(sp, wr):
        if a.strip(_EDGE).lower() != b.strip(_EDGE).lower():
            out.append(a)
    return out


def summary(text: str | None) -> str:
    """Spoken fragments as one line for the grid. Empty when nothing changed."""
    return " · ".join(spoken_fragments(text))


def replace_all(text: str, find: str, spoken: str, written: str,
                whole_word: bool = True, case_sensitive: bool = True) -> tuple[str, int]:
    """Replace every occurrence of `find`, including inside existing markers.

    Two different edits live under one button, and both are needed:

      * free text  "Kup ID"  -> "Kup {aj-di|ID}"
      * inside a marker  {i-de|ID} -> {ajdi|ID}

    The second is what someone actually wants when they search "i-de" and type
    "ajdi": they are fixing a pronunciation that is already marked. Skipping
    markers made that impossible and the dialog answered "already inside a marker,
    nothing to replace" — for the one operation the user asked for.

    Returns the new text and how many replacements were made.
    """
    if not find:
        return text or "", 0

    # An empty "Written as" means "record whatever is already there": the marker's
    # own written side for a marker, the found text for free text. A non-empty value
    # applies everywhere, so the field means what it says.
    explicit_written = (written or "").strip()
    new_spoken = (spoken or "").strip()
    replacement = (_build_line(new_spoken, explicit_written) if explicit_written
                   else _build_line(new_spoken, find))
    haystack = text or ""
    needle = find if case_sensitive else find.lower()
    out: list[str] = []
    count = 0
    i = 0
    n = len(haystack)

    while i < n:
        if haystack[i] == "{":
            close = haystack.find("}", i + 1)
            if close > i:
                inner = haystack[i + 1:close]
                bar = inner.find("|")

                if 0 < bar < len(inner) - 1:
                    old_spoken = inner[:bar].strip()
                    old_written = inner[bar + 1:]

                    if _speaks_as(old_spoken, needle, whole_word, case_sensitive):
                        keep = explicit_written or old_written.strip()
                        out.append("{" + new_spoken + "|" + keep + "}")
                        count += 1
                        i = close + 1
                        continue

                out.append(haystack[i:close + 1])
                i = close + 1
                continue

        window = haystack[i:i + len(find)]
        candidate = window if case_sensitive else window.lower()
        if candidate == needle and (
                not whole_word or _is_whole_word(haystack, i, len(find))):
            out.append(replacement)
            i += len(find)
            count += 1
            continue

        out.append(haystack[i])
        i += 1

    return "".join(out), count


def _speaks_as(candidate: str, needle: str, whole_word: bool,
               case_sensitive: bool) -> bool:
    """Does this marker's spoken side match, under the same word and case rules?"""
    if not candidate:
        return False

    a = candidate if case_sensitive else candidate.lower()
    if a == needle:
        return True

    # Without the whole-word rule a longer pronunciation may contain the phrase.
    return not whole_word and needle in a


def written_form_for(text: str, find: str, whole_word: bool = True,
                     case_sensitive: bool = True) -> str | None:
    """The written side of the FIRST marker whose spoken side matches, or None.

    The dialog prefilled "Written as" with the search term, so searching the
    phonetic form "i-de" showed i-de even though every match sat inside
    {i-de|ID}. Prefilling with what the text actually records makes the field say
    what will really end up in the transcript.
    """
    needle = (find or "").strip()
    if not needle:
        return None

    haystack = text or ""
    comparison = needle if case_sensitive else needle.lower()
    i = 0

    while i < len(haystack):
        open_at = haystack.find("{", i)
        if open_at < 0:
            break
        close_at = haystack.find("}", open_at + 1)
        if close_at < 0:
            break

        inner = haystack[open_at + 1:close_at]
        bar = inner.find("|")

        if 0 < bar < len(inner) - 1:
            spoken = inner[:bar].strip()
            written = inner[bar + 1:].strip()
            probe = spoken if case_sensitive else spoken.lower()

            if written and _speaks_as(spoken, comparison, whole_word, case_sensitive):
                return written

        i = close_at + 1

    return None


def count_inside_markers(text: str, find: str, case_sensitive: bool = True) -> int:
    """Count occurrences that sit INSIDE an existing {spoken|written} marker.

    Not used to replace — replacing there would nest braces. It answers the question
    a user actually asks: "the phrase is right there in the list, why does it say
    nothing was found?" Because it is already marked.
    """
    if not find:
        return 0

    haystack = text or ""
    needle = find if case_sensitive else find.lower()
    count = 0
    i = 0

    while i < len(haystack):
        open_at = haystack.find("{", i)
        if open_at < 0:
            break
        close_at = haystack.find("}", open_at + 1)
        if close_at < 0:
            break

        inner = haystack[open_at + 1:close_at]
        probe = inner if case_sensitive else inner.lower()
        at = 0
        while at + len(needle) <= len(probe):
            if probe[at:at + len(needle)] == needle:
                count += 1
                at += len(needle)
            else:
                at += 1

        i = close_at + 1

    return count


def count_matches(text: str, find: str, whole_word: bool = True,
                  case_sensitive: bool = True) -> int:
    """How many occurrences a replace would hit, without touching the text.

    The dialog shows this before the user commits: replacing across a thousand
    lines with no idea whether it matches three times or three hundred is a
    button nobody dares press.
    """
    return replace_all(text, find, "\x01", "\x02", whole_word, case_sensitive)[1]


def _build_line(spoken: str, written: str) -> str:
    """Only wrap in a marker when the two forms differ."""
    s = (spoken or "").strip()
    w = (written or "").strip()
    if not s:
        return w
    if s == w:
        return w
    return "{" + s + "|" + w + "}"


def _is_whole_word(text: str, start: int, length: int) -> bool:
    before = start == 0 or not text[start - 1].isalnum()
    end = start + length
    after = end >= len(text) or not text[end].isalnum()
    return before and after


def has_markers(text: str | None) -> bool:
    spoken, written = split(text)
    return spoken != written
