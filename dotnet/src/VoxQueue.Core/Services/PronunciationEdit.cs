using System;
using System.Collections.Generic;

namespace VoxQueue.Core.Services;

/// <summary>
/// Applies a pronunciation edit to the source text.
///
/// This lives in Core, not in the panel, because the edit was reported as "the
/// spelling does not change" and there is no way to see that from a UI method
/// that returns a bool. Here it is a pure function over strings, so a test can
/// feed it the exact case and assert the exact line it must produce.
///
/// The rewrite matches the CLEANED form of the raw line, not a position: the user
/// may have edited the box since the last parse, and a positional rewrite would
/// then corrupt a different line.
/// </summary>
public static class PronunciationEdit
{
    /// <summary>
    /// Builds the replacement line. Only wraps in a marker when the two forms
    /// actually differ — a marker that says the same thing twice is noise, and it
    /// makes the grid show a "changed pronunciation" that was never changed.
    /// </summary>
    public static string BuildLine(string spoken, string written)
    {
        var s = (spoken ?? "").Trim();
        var w = (written ?? "").Trim();

        if (s.Length == 0) return w;
        if (string.Equals(s, w, StringComparison.Ordinal)) return w;

        return "{" + s + "|" + w + "}";
    }

    /// <summary>
    /// Rewrites the first line of <paramref name="sourceText"/> that matches the
    /// item's raw line. Returns null when nothing matches any more, so the caller
    /// can say so instead of pretending the edit landed.
    /// </summary>
    /// <param name="sourceText">Whole contents of the editor.</param>
    /// <param name="sourceLine">The item's raw line, markers and all. Empty is
    /// tolerated: the written form is used instead, which is weaker (a repeated
    /// line could match the wrong one) but better than doing nothing.</param>
    /// <param name="itemText">Written form, used when <paramref name="sourceLine"/>
    /// is empty.</param>
    /// <param name="replacement">Line to put in its place.</param>
    public static string? Rewrite(string sourceText, string sourceLine, string itemText,
                                  string replacement, out int lineIndex)
    {
        lineIndex = -1;

        var raw = string.IsNullOrEmpty(sourceLine) ? itemText : sourceLine;
        var target = TextSplitter.Clean(Pronunciation.ToWritten(raw));
        if (target.Length == 0) return null;

        // Split on \n only, then rejoin with \n: the editor may hold \r\n and
        // Environment.NewLine is \r\n on Windows, but a file loaded on Linux and
        // edited here must not gain doubled line breaks.
        var lines = (sourceText ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            // Compare the WRITTEN form of the candidate against the written form of
            // the target. Comparing the raw line to a marker-stripped target meant
            // every line that already carried a {spoken|written} marker failed to
            // match — which is why editing a pronunciation appeared to do nothing
            // on exactly the lines that had one.
            var candidate = TextSplitter.Clean(Pronunciation.ToWritten(lines[i]));
            if (!string.Equals(candidate, target, StringComparison.Ordinal))
                continue;

            lines[i] = replacement;
            lineIndex = i;
            return string.Join("\n", lines);
        }

        return null;
    }

    /// <summary>
    /// Counts how many occurrences a replace would hit, without touching the text.
    ///
    /// The dialog shows this before the user commits: "Replace" over a thousand
    /// lines with no idea whether it matches three times or three hundred is a
    /// button nobody dares press.
    /// </summary>
    public static int CountMatches(
        string sourceText, string find, bool wholeWord, bool caseSensitive)
    {
        var (_, count) = ReplaceAll(sourceText, find, "\u0001", "\u0002",
                                    wholeWord, caseSensitive);
        return count;
    }

    /// <summary>
    /// The written side of the FIRST marker whose spoken side matches, or null.
    ///
    /// The dialog used to prefill "Written as" with the search term, so searching the
    /// phonetic form "i-de" showed i-de there even though every match sat inside
    /// {i-de|ID}. Leaving the field alone then meant "record i-de", which contradicted
    /// what the replace actually did. Prefilling with the form found in the text makes
    /// the field say what will really be recorded.
    /// </summary>
    public static string? WrittenFormFor(
        string sourceText, string find, bool wholeWord, bool caseSensitive)
    {
        var text = sourceText ?? "";
        var needle = (find ?? "").Trim();
        if (needle.Length == 0) return null;

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var i = 0;
        while (i < text.Length)
        {
            var open = text.IndexOf('{', i);
            if (open < 0) break;

            var close = text.IndexOf('}', open + 1);
            if (close < 0) break;

            var inner = text.Substring(open + 1, close - open - 1);
            var bar = inner.IndexOf('|');

            if (bar > 0 && bar < inner.Length - 1)
            {
                var spoken = inner[..bar].Trim();
                var written = inner[(bar + 1)..].Trim();

                if (Matches(spoken, needle, wholeWord, comparison) && written.Length > 0)
                    return written;
            }

            i = close + 1;
        }

        return null;
    }

    /// <summary>
    /// Counts occurrences that sit INSIDE an existing {spoken|written} marker.
    ///
    /// Not used to replace — replacing there would nest braces. It exists to answer
    /// the question a user actually asks: "the phrase is right there in the list, why
    /// does it say nothing was found?" Because it is already marked, and a second
    /// marker around it would corrupt the line.
    /// </summary>
    public static int CountInsideMarkers(string sourceText, string find, bool caseSensitive)
    {
        var text = sourceText ?? "";
        var needle = find ?? "";
        if (needle.Length == 0) return 0;

        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var count = 0;
        var i = 0;

        while (i < text.Length)
        {
            var open = text.IndexOf('{', i);
            if (open < 0) break;

            var close = text.IndexOf('}', open + 1);
            if (close < 0) break;

            var inner = text.Substring(open + 1, close - open - 1);
            var at = 0;
            while (at + needle.Length <= inner.Length)
            {
                if (string.Compare(inner, at, needle, 0, needle.Length, comparison) == 0)
                {
                    count++;
                    at += needle.Length;
                }
                else at++;
            }

            i = close + 1;
        }

        return count;
    }

    /// <summary>
    /// Replaces every occurrence of <paramref name="find"/> outside existing
    /// markers.
    ///
    /// Markers are skipped on purpose: replacing inside <c>{spoken|written}</c>
    /// would nest braces and corrupt a line that was already correct.
    ///
    /// Returns the new text and how many replacements were made, so the caller can
    /// report "0 replaced" instead of closing silently.
    /// </summary>
    public static (string text, int count) ReplaceAll(
        string sourceText, string find, string spoken, string written,
        bool wholeWord, bool caseSensitive)
    {
        var count = 0;
        var text = sourceText ?? "";
        var needle = find ?? "";

        if (needle.Length == 0) return (text, 0);

        // An empty "Written as" means "keep whatever the text already records":
        // the marker's own written side for a marker, the found text for free text.
        // A non-empty value applies everywhere, so the field means what it says.
        var explicitWritten = (written ?? "").Trim();
        var newSpoken = (spoken ?? "").Trim();

        // An empty field means "record whatever is already there". For a marker that
        // is the marker's own written side; for free text it is the text that was
        // found, so the line still becomes a marker and keeps its spelling.
        var replacement = explicitWritten.Length > 0
            ? BuildLine(newSpoken, explicitWritten)
            : BuildLine(newSpoken, needle);
        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var outp = new System.Text.StringBuilder(text.Length);
        var i = 0;

        while (i < text.Length)
        {
            // A marker is not copied blindly. The whole point of this dialog is
            // often to fix the PRONUNCIATION of words that are already marked:
            // searching "i-de" and asking for "ajdi" must turn {i-de|ID} into
            // {ajdi|ID}. Copying markers verbatim made that impossible and the
            // dialog answered "already inside a marker, nothing to replace" — for
            // the one operation the user actually wanted.
            if (text[i] == '{')
            {
                var close = text.IndexOf('}', i + 1);
                if (close > i)
                {
                    var inner = text.Substring(i + 1, close - i - 1);
                    var bar = inner.IndexOf('|');

                    if (bar > 0 && bar < inner.Length - 1)
                    {
                        var oldSpoken = inner[..bar];
                        var oldWritten = inner[(bar + 1)..];

                        if (Matches(oldSpoken.Trim(), needle, wholeWord, comparison))
                        {
                            var keep = explicitWritten.Length > 0
                                ? explicitWritten
                                : oldWritten.Trim();

                            outp.Append('{').Append(newSpoken).Append('|')
                                .Append(keep).Append('}');
                            count++;
                            i = close + 1;
                            continue;
                        }
                    }

                    // Not a match, or not a usable marker: keep it exactly as it is.
                    outp.Append(text, i, close - i + 1);
                    i = close + 1;
                    continue;
                }
            }

            if (i + needle.Length <= text.Length &&
                string.Compare(text, i, needle, 0, needle.Length, comparison) == 0 &&
                (!wholeWord || IsWholeWord(text, i, needle.Length)))
            {
                outp.Append(replacement);
                i += needle.Length;
                count++;
                continue;
            }

            outp.Append(text[i]);
            i++;
        }

        return (outp.ToString(), count);
    }

    /// <summary>
    /// Whole-string match with the same word and case rules the free-text scan uses.
    ///
    /// "Whole word" inside a marker means the spoken side IS the phrase, not that it
    /// merely contains it: replacing a fragment of a pronunciation would leave
    /// nonsense like "ajdi-x" behind.
    /// </summary>
    private static bool Matches(string candidate, string needle,
                                bool wholeWord, StringComparison comparison)
    {
        if (candidate.Length == 0) return false;

        if (string.Equals(candidate, needle, comparison)) return true;

        // Without the whole-word rule, a marker may contain the phrase in a longer
        // pronunciation, so a plain substring test applies.
        return !wholeWord && candidate.IndexOf(needle, comparison) >= 0;
    }

    /// <summary>True when the match at [start,start+len) is not inside a word.</summary>
    private static bool IsWholeWord(string text, int start, int len)
    {
        var before = start == 0 || !char.IsLetterOrDigit(text[start - 1]);
        var afterIdx = start + len;
        var after = afterIdx >= text.Length || !char.IsLetterOrDigit(text[afterIdx]);
        return before && after;
    }

    /// <summary>
    /// Convenience wrapper for the whole operation: build the line, rewrite the
    /// text, and report what the item should now show.
    /// </summary>
    public static (string text, string spoken, string phonetic, int lineIndex, string line)? Apply(
        string sourceText, string sourceLine, string itemText,
        string spoken, string written)
    {
        var replacement = BuildLine(spoken, written);
        var updated = Rewrite(sourceText, sourceLine, itemText, replacement, out var lineIndex);
        if (updated is null) return null;

        var spokenForm = (spoken ?? "").Trim();
        var w = (written ?? "").Trim();

        // The phonetic column must show the WORDS that changed, not the whole
        // sentence. Summary does a word-level diff, so wrapping a full sentence in
        // a marker — which is exactly what the dialog produces — no longer repeats
        // the sentence back at the user.
        var phonetic = Pronunciation.Summary(replacement);

        return (w, spokenForm, phonetic, lineIndex, replacement);
    }
}
