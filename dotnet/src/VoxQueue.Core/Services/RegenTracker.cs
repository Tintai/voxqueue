using System;
using System.Collections.Generic;
using System.Linq;
using VoxQueue.Core.Models;

namespace VoxQueue.Core.Services;

/// <summary>One row's result, captured before a re-parse rebuilds the list.</summary>
public sealed class ItemSnapshot
{
    public int Index { get; init; }
    public string Text { get; init; } = "";
    public string Spoken { get; init; } = "";
    public ItemState State { get; init; }

    /// <summary>Kept so an untouched row also keeps its "needs regeneration" flag.</summary>
    public bool NeedsRegen { get; init; }
    public string File { get; init; } = "";
    public double Duration { get; init; }
    public List<string> Defects { get; init; } = new();
    public string Error { get; init; } = "";

    /// <summary>True when this row had audio on disk that is now out of date.</summary>
    public bool WasRendered => State is ItemState.Done or ItemState.Failed or ItemState.Stale;
}

/// <summary>
/// Carries render results across a re-parse.
///
/// A re-parse rebuilds every row from the text box, which is correct — the text is
/// the definition of the work — but it throws away what happened to the previous
/// rows. Editing ten lines out of ten thousand must not cost the other nine
/// thousand their rendered clips.
///
/// Deciding which rows are now out of date is the part that was wrong twice, so it
/// lives here as a pure function over data and has a test that drives the real path
/// end to end.
///
/// The comparison is on BOTH the written and the spoken form. Comparing only the
/// written form is the specific bug that hid for several releases: changing a
/// pronunciation leaves the written side untouched, so every changed row looked
/// unchanged and was restored as 'OK' — the list claimed its audio matched the text
/// when the audio had been made from the previous pronunciation.
/// </summary>
public static class RegenTracker
{
    public static List<ItemSnapshot> Snapshot(Job? job)
    {
        if (job is null) return new List<ItemSnapshot>();

        return job.Items.Select(i => new ItemSnapshot
        {
            Index = i.Index,
            Text = i.Text ?? "",
            Spoken = i.SpokenOrText ?? "",
            State = i.State,
            NeedsRegen = i.NeedsRegen,
            File = i.File ?? "",
            Duration = i.Duration,
            Defects = new List<string>(i.Defects ?? new List<string>()),
            Error = i.Error ?? ""
        }).ToList();
    }

    /// <summary>
    /// Restores unchanged rows exactly as they were and marks changed ones stale.
    ///
    /// Returns how many rows were marked, which is what the status line reports —
    /// "nothing marked" and "ten marked" must be distinguishable, or the user cannot
    /// tell whether the replace did anything.
    /// </summary>
    public static int Restore(Job? job, List<ItemSnapshot>? before)
    {
        if (job is null || before is null || before.Count == 0) return 0;

        var byIndex = before.ToDictionary(s => s.Index);
        var marked = 0;

        foreach (var item in job.Items)
        {
            if (!byIndex.TryGetValue(item.Index, out var old)) continue;

            var textSame = string.Equals(old.Text, item.Text ?? "", StringComparison.Ordinal);
            var spokenSame = string.Equals(old.Spoken, item.SpokenOrText ?? "", StringComparison.Ordinal);

            if (textSame && spokenSame)
            {
                // Untouched: the clip on disk still says what this row says.
                item.State = old.State;
                item.NeedsRegen = old.NeedsRegen;
                item.File = old.File;
                item.Duration = old.Duration;
                item.Defects = new List<string>(old.Defects);
                item.Error = old.Error;
                continue;
            }

            // The audio was rendered from the old text or the old pronunciation.
            if (old.WasRendered)
            {
                item.State = ItemState.Stale;
                item.NeedsRegen = true;
                marked++;
            }
        }

        return marked;
    }
}
