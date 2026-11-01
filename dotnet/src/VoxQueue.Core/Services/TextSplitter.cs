using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VoxQueue.Core.Models;

namespace VoxQueue.Core.Services;

/// <summary>
/// The three text splitters. Each returns lines in input order; the caller
/// assigns indices.
/// </summary>
public static class TextSplitter
{
    private static readonly Regex Ws = new(@"[^\S\n]+", RegexOptions.Compiled);
    private static readonly Regex MarkerChars = new(@"^[#=\-*~_+]{2,}$", RegexOptions.Compiled);

    // NAME: text | [NAME] text | NAME - text
    //
    // The label classes accept `{`, `}` and `|` so a phonetic marker survives
    // the match: without them "{Szerd|SHEPPARD}: ..." fails every pattern and the
    // whole line falls through to the text, losing the speaker. The engine never
    // says a speaker name — it only becomes a filename and a voice-map key — so
    // the written half is taken and the phonetic half is dropped.
    private static readonly Regex[] SpeakerPatterns =
    {
        new(@"^\s*([\w{][\w \-'.{}|]{0,48})\s*:\s*(.*)$", RegexOptions.Compiled),
        new(@"^\s*\[([^\]]{1,50})\]\s*(.*)$", RegexOptions.Compiled),
        new(@"^\s*([A-Z{][\w \-'.{}|]{0,48})\s+[\u2014\u2013-]\s+(.*)$", RegexOptions.Compiled)
    };

    private static readonly HashSet<string> Abbrev = new(StringComparer.OrdinalIgnoreCase)
    {
        "np", "itp", "itd", "tzn", "tj", "prof", "dr", "mgr", "inz", "ul", "al",
        "pl", "godz", "ok", "por", "nr", "str", "rys", "tab", "ww", "zob", "pkt",
        "ust", "art", "m.in"
    };

    /// <summary>Collapse runs of spaces; a newline inside one utterance becomes a space.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = text.Replace("\r", "").Replace("\n", " ");
        t = Ws.Replace(t, " ");
        return t.Trim();
    }

    // A leading "0000|" is metadata.csv syntax, not part of the line. Files
    // handed back from an export — or produced by an assistant told to "make a
    // metadata.csv" — arrive with it, and without this the id ends up inside the
    // utterance, gets spoken, and is written into the next metadata.csv twice.
    private static readonly Regex IndexPrefix = new(@"^\s*(\d{2,6})\s*\|", RegexOptions.Compiled);

    /// <summary>
    /// Removes a leading "NNNN|" and reports the number it carried.
    ///
    /// Telling a dataset id from an ordinary pipe is not free: "12|13 to para
    /// liczb" is a sentence, not an indexed line, and eating the "12|" would
    /// silently destroy it. The rule is therefore narrow — a prefix counts only
    /// when it is zero-padded ("0000", "0017") or at least four digits long,
    /// which is what every dataset export produces and what almost nothing else
    /// looks like.
    /// </summary>
    public static (string text, int? index) StripIndex(string? line)
    {
        if (string.IsNullOrEmpty(line)) return (line ?? "", null);

        var m = IndexPrefix.Match(line);
        if (!m.Success) return (line, null);

        var digits = m.Groups[1].Value;
        var looksLikeId = digits[0] == '0' || digits.Length >= 4;
        if (!looksLikeId) return (line, null);

        var rest = line[m.Length..].TrimStart(' ', '\t');
        if (rest.Length == 0) return (line, null);

        return int.TryParse(digits, out var n) ? (rest, n) : (line, null);
    }

    /// <summary>Training mode: one non-empty line = one utterance.</summary>
    public static List<ParsedLine> SplitLines(string? text)
    {
        var outp = new List<ParsedLine>();
        if (string.IsNullOrEmpty(text)) return outp;
        foreach (var raw in text.Split('\n'))
        {
            var (body, index) = StripIndex(raw);
            var c = Clean(body);
            if (c.Length > 0) outp.Add(new ParsedLine { Text = c, Raw = raw, SourceIndex = index });
        }
        return outp;
    }

    /// <summary>Bulk mode: blocks separated by a marker line or a triple newline.</summary>
    public static List<ParsedLine> SplitBlocks(string? text, string marker = "###")
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return new List<ParsedLine>();

        var m = (marker ?? "").Trim();
        var buf = new List<string>();

        void Flush()
        {
            var chunk = Clean(string.Join(" ", buf));
            if (chunk.Length > 0) result.Add(chunk);
            buf.Clear();
        }

        foreach (var raw in text.Split('\n'))
        {
            // The prefix belongs to the line, not to the block, so it is removed
            // before anything else looks at the text.
            var (body, _) = StripIndex(raw);
            var stripped = body.Trim();
            var isMarker = false;
            if (m.Length > 0 && stripped == m) isMarker = true;
            else if (m.Length > 0 && stripped.Contains(m) && stripped.Length < Math.Max(12, m.Length + 4)) isMarker = true;
            else if (MarkerChars.IsMatch(stripped)) isMarker = true;

            if (isMarker) { Flush(); continue; }
            if (stripped.Length == 0) { buf.Add(""); continue; }
            buf.Add(stripped);
        }
        Flush();

        // A pasted list with no markers and no blank lines still has to split
        // somewhere meaningful: fall back to one line = one block.
        if (result.Count == 1)
        {
            var lines = SplitLines(text);
            if (lines.Count > 1) return lines;
        }
        return result.Select(r => new ParsedLine { Text = r }).ToList();
    }

    /// <summary>
    /// A real speaker label is short, has no sentence punctuation, and every word
    /// after the first starts with a capital. That last rule is what keeps prose
    /// like "He said: hello" from inventing a character called "He said".
    /// </summary>
    private static bool LooksLikeName(string label)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 40) return false;
        if (label.Any(c => c is '.' or '!' or '?' or ',')) return false;
        var words = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 4) return false;
        for (var i = 1; i < words.Length; i++)
            if (words[i].Length > 0 && char.IsLower(words[i][0])) return false;
        return true;
    }

    /// <summary>Dubbing mode: split on speaker prefixes, keep script order.</summary>
    public static List<ParsedLine> SplitDub(string? text, string defaultSpeaker = "")
    {
        var outp = new List<ParsedLine>();
        var buf = new List<string>();
        var current = defaultSpeaker;

        void Flush()
        {
            var chunk = Clean(string.Join(" ", buf));
            if (chunk.Length > 0) outp.Add(new ParsedLine { Text = chunk, Speaker = current });
            buf.Clear();
        }

        if (string.IsNullOrEmpty(text)) return outp;

        foreach (var raw in text.Split('\n'))
        {
            // "0000|SHEPPARD: ..." — the id would otherwise hide the speaker and
            // the whole line would fall through as unattributed text.
            var (noIndex, _) = StripIndex(raw);
            var line = noIndex.Trim();
            if (line.Length == 0) continue;

            var matched = false;
            foreach (var pat in SpeakerPatterns)
            {
                var mo = pat.Match(line);
                if (!mo.Success) continue;
                var rawName = mo.Groups[1].Value.Trim();
                var body = mo.Groups[2].Value.Trim();

                // "{Szerd|SHEPPARD}" -> "SHEPPARD". A marker in the label is
                // tolerated rather than rejected, because the label is the one
                // place a user will instinctively reach for it.
                var name = Pronunciation.ToWritten(rawName);
                if (!LooksLikeName(name)) continue;

                Flush();
                current = name;
                if (body.Length > 0) buf.Add(body);
                matched = true;
                break;
            }
            if (!matched) buf.Add(line);
        }
        Flush();
        return outp;
    }

    /// <summary>Distinct speakers in first-appearance order.</summary>
    public static List<string> Speakers(IEnumerable<ParsedLine> lines)
    {
        var seen = new List<string>();
        foreach (var l in lines)
            if (!string.IsNullOrEmpty(l.Speaker) && !seen.Contains(l.Speaker))
                seen.Add(l.Speaker);
        return seen;
    }

    /// <summary>
    /// Speaker name or filename stem -> something every filesystem accepts.
    ///
    /// Three separate hazards are handled here:
    ///   * characters that are illegal or act as separators, so a name cannot
    ///     escape the output folder;
    ///   * leading/trailing dots and spaces, which Windows silently strips and
    ///     which would make two different names collide;
    ///   * the DOS device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9), which are
    ///     still refused for a file called exactly that, even today.
    /// </summary>
    public static string SafeFilename(string? name, string fallback = "voice")
    {
        if (string.IsNullOrWhiteSpace(name)) return fallback;

        var chars = name.Trim()
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ')
            .ToArray();

        var n = Regex.Replace(new string(chars).Trim(), @"\s+", "_").Trim('.', '_', '-');
        if (n.Length == 0) return fallback;

        // "CON.wav" is as invalid as "CON", so the check is on the stem.
        var stem = n.Split('.')[0];
        if (Reserved.Contains(stem.ToUpperInvariant())) n = "_" + n;

        return n;
    }

    /// <summary>The names Windows reserves for devices. Still enforced.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };
}
