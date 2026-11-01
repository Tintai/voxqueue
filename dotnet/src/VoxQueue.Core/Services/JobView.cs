using System;
using System.Collections.Generic;
using System.Linq;
using VoxQueue.Core.Models;

namespace VoxQueue.Core.Services;

/// <summary>
/// Filtering and ordering for the work list.
///
/// This lives here rather than in the panel because it is the part that was
/// wrong: the panel filtered its own displayed collection and then refilled that
/// collection from the filtered result, so every filtered-out row disappeared
/// from the only place the next pass read from. Unticking the filter restored
/// nothing. Keeping the logic in a pure function over the job makes that
/// mistake impossible to hide — the source is the job, always, and a test says so.
/// </summary>
public static class JobView
{
    /// <summary>
    /// Rows to display: filtered by pin and by search, then ordered.
    ///
    /// <paramref name="search"/> matches anywhere in the written text OR in the
    /// pronunciation. Searching only the text would hide exactly the rows a user
    /// is hunting for — they remember the word they fixed, not the sentence.
    /// </summary>
    public static List<ItemRow> Build(Job? job, bool pinnedOnly, int sortMode,
                                      string? search = null, bool staleOnly = false)
    {
        if (job is null || job.Items.Count == 0) return new List<ItemRow>();

        // Filter first, then order: ordering first and filtering after would leave
        // gaps that look like missing rows.
        IEnumerable<ItemRow> source = pinnedOnly
            ? job.Items.Where(i => i.Pinned)
            : job.Items;

        // All three filters live here rather than in the panel. They combine, and a
        // combination spread across two files is one the test suite cannot reach —
        // which is exactly how the stale filter ended up applied after the sort in
        // one build and inside it in the other.
        // NeedsRegen, not State. State moves during the run (Stale -> Running -> Done),
        // so filtering on it made a row disappear the instant it started rendering.
        // The State check is kept as a fallback for rows written before the flag
        // existed.
        if (staleOnly)
            source = source.Where(i => i.NeedsRegen || i.State == ItemState.Stale);

        var needle = (search ?? "").Trim();
        if (needle.Length > 0)
            source = source.Where(i => Matches(i, needle));

        return sortMode switch
        {
            1 => source.OrderBy(i => i.SortRank).ThenBy(i => i.Index).ToList(),
            2 => source.OrderBy(i => i.PronRank).ThenBy(i => i.Index).ToList(),

            // "Needs regeneration": the clips that exist but were made from an
            // older line. Everything else keeps its source order after them.
            3 => source.OrderBy(i => i.State == ItemState.Stale ? 0 : 1)
                        .ThenBy(i => i.Index).ToList(),

            _ => source.OrderBy(i => i.Index).ToList()
        };
    }

    /// <summary>
    /// True when the query appears in the text, the spoken form or the phonetic
    /// summary. Case-insensitive and accent-sensitive: a user typing "eter" wants
    /// the row whose pronunciation says "Eter", and one typing "Aether" wants the
    /// text.
    /// </summary>
    public static bool Matches(ItemRow item, string needle)
    {
        if (needle.Length == 0) return true;

        return Contains(item.Text, needle)
            || Contains(item.Spoken, needle)
            || Contains(item.Phonetic, needle)
            || Contains(item.Speaker, needle)
            || Contains(item.Index.ToString(), needle);
    }

    private static bool Contains(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) &&
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>How many items carry the pin. Used for the filter label.</summary>
    public static int PinnedCount(Job? job) => job?.Items.Count(i => i.Pinned) ?? 0;
}
