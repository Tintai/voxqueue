using System;
using System.Collections.Generic;
using System.Text;

namespace VoxQueue.Core.Services;

/// <summary>
/// Splits a line into what the engine should *say* and what the transcript
/// should *record*.
///
/// A TTS model reads letters, not spelling. "Aether" comes out as "A-e-ther"
/// rather than "Eter", "2024" is read digit by digit or skipped, and any
/// acronym gets mangled. The fix is to give the engine a phonetic spelling while
/// keeping the correct spelling in metadata.csv and in the .txt sidecar.
///
/// Syntax — one marker, spoken form first:
///
///     {Eter|Aether}      says "Eter", writes "Aether"
///     {pięćset|500}      says "pięćset", writes "500"
///     {pe-em|PM}         says "pe-em", writes "PM"
///
/// Braces were chosen over square brackets because this app already uses
/// `[NAME]` for speaker labels, and over a prefix syntax because the two forms
/// belong side by side where the eye can compare them.
///
/// Everything that is not a well-formed marker is passed through untouched:
/// a stray `{`, an empty side, or a missing `|` stays literal rather than
/// silently deleting part of the line.
/// </summary>
public static class Pronunciation
{
    /// <summary>
    /// Returns the spoken and written forms. Without markers both are the input,
    /// trimmed — so callers never have to branch on "are there markers".
    /// </summary>
    public static (string spoken, string written) Split(string? text)
    {
        var input = text ?? "";
        if (input.Length == 0 || input.IndexOf('{') < 0)
            return (input.Trim(), input.Trim());

        var spoken = new StringBuilder(input.Length);
        var written = new StringBuilder(input.Length);
        var i = 0;

        while (i < input.Length)
        {
            var open = input.IndexOf('{', i);

            if (open < 0)
            {
                AppendBoth(spoken, written, input, i, input.Length - i);
                break;
            }

            // Text before the marker is literally both forms.
            AppendBoth(spoken, written, input, i, open - i);

            var close = input.IndexOf('}', open + 1);
            if (close < 0)
            {
                // Unclosed brace: keep the remainder as written.
                AppendBoth(spoken, written, input, open, input.Length - open);
                break;
            }

            var inner = input.Substring(open + 1, close - open - 1);
            var bar = inner.IndexOf('|');

            var spokenPart = bar > 0 ? inner[..bar].Trim() : "";
            var writtenPart = bar >= 0 && bar < inner.Length - 1 ? inner[(bar + 1)..].Trim() : "";

            if (spokenPart.Length > 0 && writtenPart.Length > 0)
            {
                spoken.Append(spokenPart);
                written.Append(writtenPart);
            }
            else
            {
                // Not a usable marker (no pipe, or one side empty): leave it be
                // rather than dropping content the user typed.
                AppendBoth(spoken, written, input, open, close - open + 1);
            }

            i = close + 1;
        }

        return (spoken.ToString().Trim(), written.ToString().Trim());
    }

    private static void AppendBoth(StringBuilder spoken, StringBuilder written,
                                   string source, int start, int length)
    {
        if (length <= 0) return;
        spoken.Append(source, start, length);
        written.Append(source, start, length);
    }

    /// <summary>True when the line contains at least one usable marker.</summary>
    public static bool HasMarkers(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var (spoken, written) = Split(text);
        return !string.Equals(spoken, written, StringComparison.Ordinal);
    }

    /// <summary>Removes markers, keeping only the written form. For display.</summary>
    public static string ToWritten(string? text) => Split(text).written;

    /// <summary>
    /// The spoken halves of every usable marker, in order.
    ///
    /// The grid shows these instead of the whole spoken sentence: only the words
    /// that were actually changed matter for review, and a full sentence repeated
    /// in a second column is noise. The written form is already in the Text
    /// column, so listing both here would just duplicate it.
    /// </summary>
    public static List<string> SpokenFragments(string? text)
    {
        var outp = new List<string>();
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return outp;

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
                if (spoken.Length > 0 && written.Length > 0 &&
                    !string.Equals(spoken, written, StringComparison.Ordinal))
                {
                    // Only the words that actually differ. A marker can wrap a whole
                    // sentence — the pronunciation dialog produces exactly that — and
                    // repeating the sentence in the second column is noise, not
                    // information. The written form already sits in the Text column.
                    foreach (var word in ChangedWords(spoken, written))
                        if (!outp.Contains(word)) outp.Add(word);
                }
            }
            i = close + 1;
        }
        return outp;
    }

    /// <summary>
    /// Words of <paramref name="spoken"/> that differ from <paramref name="written"/>.
    ///
    /// Positional comparison, which is enough for the case this exists for: a
    /// pronunciation marker replaces a word with a phonetic spelling of the same
    /// word. When the word counts differ the whole spoken form is returned — a
    /// realignment would be guesswork, and a wrong guess in a review column is
    /// worse than a long one.
    /// </summary>
    public static IEnumerable<string> ChangedWords(string? spoken, string? written)
    {
        var sp = (spoken ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wr = (written ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (sp.Length != wr.Length)
        {
            if (sp.Length > 0) yield return string.Join(" ", sp);
            yield break;
        }

        for (var i = 0; i < sp.Length; i++)
        {
            var a = sp[i].Trim(',', '.', '!', '?', ';', ':', '"', '\'', '(', ')');
            var b = wr[i].Trim(',', '.', '!', '?', ';', ':', '"', '\'', '(', ')');
            if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                yield return sp[i];
        }
    }

    /// <summary>Spoken fragments as one line for the grid. Empty when nothing changed.</summary>
    public static string Summary(string? text)
    {
        var parts = SpokenFragments(text);
        return parts.Count == 0 ? "" : string.Join(" · ", parts);
    }
}
