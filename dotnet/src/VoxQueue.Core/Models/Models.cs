using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxQueue.Core.Models;

public enum Mode { Train, Dub, Bulk }

public enum ItemState
{
    Pending,
    Running,
    Done,
    Failed,

    /// <summary>
    /// Rendered once, then the line was edited — the audio on disk no longer
    /// matches the text. Distinct from Pending because the clip still EXISTS:
    /// the file plays, it is simply out of date, and treating it as pending would
    /// hide the fact that something was already produced.
    /// </summary>
    Stale
}

/// <summary>One utterance: what to say and what happened when we said it.</summary>
public sealed class ItemRow : INotifyPropertyChanged
{
    private ItemState _state = ItemState.Pending;
    private double _duration;
    private string _file = "";
    private string _error = "";
    private List<string> _defects = new();

    public int Index { get; set; }

    /// <summary>Written form — what metadata.csv and the .txt transcript record.</summary>
    public string Text { get; set; } = "";

    /// <summary>
    /// Phonetic form — what the engine is asked to say. Equal to <see cref="Text"/>
    /// when the line carries no {spoken|written} markers.
    /// </summary>
    public string Spoken { get; set; } = "";

    /// <summary>
    /// Only the words whose pronunciation was changed, e.g. "Eter · Dżoker".
    /// Stored rather than recomputed: after the split the markers are gone, and
    /// the written form alone cannot say which words were substituted.
    /// </summary>
    public string Phonetic { get; set; } = "";

    /// <summary>
    /// The line exactly as it appears in the source box, markers and all.
    ///
    /// Editing a pronunciation has to rewrite the source text, not just the
    /// parsed item — otherwise the next Parse would silently undo the edit.
    /// Keeping the raw line is what makes that rewrite findable.
    /// </summary>
    public string SourceLine { get; set; } = "";

    public string Speaker { get; set; } = "";

    /// <summary>
    /// Marked for attention by the user. Pinning is how a list of ten thousand
    /// clips stops being a list of ten thousand clips: the marked handful can be
    /// filtered to, regenerated together, and reviewed in one pass.
    /// </summary>
    public bool Pinned { get; set; }

    /// <summary>
    /// This row is waiting to be rendered again, and stays flagged until it succeeds.
    ///
    /// A separate flag from State, because State moves during the run: a stale row
    /// becomes Running the moment it starts, and a filter that looked at State made
    /// the row vanish the instant it began — the one moment the user wants to watch
    /// it. The flag is cleared only when the render finishes OK.
    /// </summary>
    public bool NeedsRegen { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ItemState State
    {
        get => _state;
        set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(RowKind)); }
    }

    public double Duration
    {
        get => _duration;
        set { _duration = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationText)); }
    }

    public List<string> Defects
    {
        get => _defects;
        set
        {
            _defects = value ?? new List<string>();
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Notes));
            OnPropertyChanged(nameof(RowKind));
        }
    }

    public string File
    {
        get => _file;
        set { _file = value; OnPropertyChanged(); }
    }

    public string Error
    {
        get => _error;
        set { _error = value; OnPropertyChanged(); OnPropertyChanged(nameof(Notes)); OnPropertyChanged(nameof(RowKind)); }
    }

    [JsonIgnore] public string DurationText => _duration > 0 ? $"{_duration:0.00}s" : "—";

    [JsonIgnore]
    public string StatusText => _state switch
    {
        ItemState.Done when _defects.Count > 0 => "!",
        ItemState.Done => "OK",
        ItemState.Stale => "~",
        ItemState.Running => "…",
        ItemState.Failed => "X",
        _ => "·"
    };

    /// <summary>
    /// Human-readable notes: the error message, or the defect names.
    ///
    /// The defect list holds stable codes ("click_end"), which are what lands in
    /// the state file and what the detector returns. Showing them raw put English
    /// identifiers in a Polish UI, and left the whole defect translation table
    /// dead — L.Defect() existed but nothing called it. Translation happens here,
    /// on the display property, so the stored data stays language-neutral.
    /// </summary>
    [JsonIgnore]
    public string Notes => !string.IsNullOrEmpty(_error)
        ? _error
        : string.Join(", ", _defects.Select(d => I18n.L.Defect(d)));

    [JsonIgnore]
    public string Label => string.IsNullOrEmpty(Speaker) ? Text : $"[{Speaker}] {Text}";

    /// <summary>
    /// Sort bucket for "errors first": 0 = failed, 1 = done but flagged,
    /// 2 = everything else. A thousand finished clips bury the dozen that need
    /// attention, so the list offers to lift them to the top.
    /// </summary>
    /// <summary>Bucket for "changed pronunciation first": 0 = has markers, 1 = rest.</summary>
    [JsonIgnore]
    public int PronRank => HasPhonetic ? 0 : 1;

    [JsonIgnore]
    public int SortRank => _state switch
    {
        ItemState.Failed => 0,
        ItemState.Stale => 1,
        ItemState.Done when _defects.Count > 0 => 2,
        _ => 3
    };

    /// <summary>True when the phonetic form differs — the grid marks those rows.</summary>
    [JsonIgnore]
    public bool HasPhonetic => !string.IsNullOrEmpty(Spoken) &&
                               !string.Equals(Spoken, Text, StringComparison.Ordinal);

    /// <summary>What the engine receives. Falls back to the written form.</summary>
    [JsonIgnore]
    public string SpokenOrText => string.IsNullOrEmpty(Spoken) ? Text : Spoken;

    /// <summary>
    /// Phonetic form for the grid — empty when it matches the transcription, so
    /// the column only draws attention where pronunciation was actually changed.
    /// </summary>
    [JsonIgnore]
    public string SpokenDisplay =>
        !string.IsNullOrEmpty(Phonetic) ? Phonetic
        : HasPhonetic ? Spoken
        : "";

    /// <summary>Semantic colour name the UI maps onto a brush, so the model stays UI-free.</summary>
    [JsonIgnore]
    public string RowKind => _state switch
    {
        ItemState.Done when _defects.Count > 0 => "warn",
        ItemState.Done => "ok",
        ItemState.Stale => "stale",
        ItemState.Failed => "fail",
        ItemState.Running => "accent",
        _ => "dim"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Resumable job. Saved after every item, so a crash costs one clip.</summary>
public sealed class Job
{
    public string Id { get; set; } = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
    public Mode Mode { get; set; } = Mode.Train;
    public string OutDir { get; set; } = "";
    public string Voice { get; set; } = "";
    public Dictionary<string, string> VoiceMap { get; set; } = new();
    public JobOptions Options { get; set; } = new();
    public List<ItemRow> Items { get; set; } = new();

    [JsonIgnore] public int DoneCount => Items.Count(i => i.State == ItemState.Done);
    [JsonIgnore] public int FailedCount => Items.Count(i => i.State == ItemState.Failed);
    [JsonIgnore] public int ReviewCount => Items.Count(i => i.State == ItemState.Done && i.Defects.Count > 0);

    public static string StateDir(string outDir) => Path.Combine(outDir, ".voxqueue");

    [JsonIgnore] public string StatePath => Path.Combine(StateDir(OutDir), Id + ".json");

    private static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Puts every row that needs re-rendering back in the queue.
    ///
    /// Two states need it and they are equally "not up to date":
    ///
    ///   Failed — generation did not succeed;
    ///   Stale  — generation succeeded, but the line was edited afterwards, so the
    ///            audio on disk was made from the previous text or pronunciation.
    ///
    /// The retry button used to reset only Failed, so the rows the user had just
    /// marked as needing regeneration were the one thing it skipped.
    ///
    /// The existing file is deliberately kept: if re-rendering fails, the clip that
    /// is on disk can still be played and compared against.
    /// </summary>
    public int ResetRetryable()
    {
        var n = 0;

        foreach (var item in Items)
        {
            if (item.State is not (ItemState.Failed or ItemState.Stale)) continue;
            var state = item.State;

            item.State = ItemState.Pending;
            item.Error = "";

            // The flag survives the reset: the row still needs rendering, it has only
            // moved from "out of date" to "queued". Without this it vanished from the
            // filtered list at the moment Retry was pressed.
            if (state == ItemState.Stale) item.NeedsRegen = true;

            // Defects were judged on the old audio and no longer describe anything.
            item.Defects = new List<string>();

            n++;
        }

        return n;
    }

    public void Save()
    {
        // A failed state write must never kill the run, but it must not vanish
        // either. Resume is built entirely on these files: if they are not being
        // written, the next run silently regenerates everything and the only
        // symptom is wasted time. The error goes to the log instead of a dialog,
        // because this runs once per item and a dialog per item is unusable.
        try
        {
            // An empty OutDir made StateDir relative, so the state landed in
            // whatever the process working directory happened to be — usually the
            // Desktop, where nobody would look for it.
            if (string.IsNullOrWhiteSpace(OutDir))
            {
                Services.AppLog.Warn("state not saved: output folder is not set");
                return;
            }

            Directory.CreateDirectory(StateDir(OutDir));
            var tmp = StatePath + ".tmp";
            System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opt));
            System.IO.File.Move(tmp, StatePath, overwrite: true);
        }
        catch (Exception e)
        {
            Services.AppLog.Warn($"state not saved for {StatePath}: {e.GetType().Name}: {e.Message}");
        }
    }

    public static Job? Load(string path)
    {
        try { return JsonSerializer.Deserialize<Job>(System.IO.File.ReadAllText(path), Opt); }
        catch { return null; }
    }

    /// <summary>
    /// Copies per-item results from an earlier job in the same output folder.
    ///
    /// Without this, every restart looks like a fresh start: the audio is on
    /// disk but the list says "pending", so a night run that was interrupted
    /// gets regenerated from scratch — paying twice for the same clips.
    ///
    /// Matching is by TEXT, not by index, because index_start may differ between
    /// runs. A result is copied only when the file it points at still exists;
    /// otherwise the item stays pending, so the list never claims to have
    /// something it cannot play.
    ///
    /// Returns how many items were restored.
    /// </summary>
    public static int RestoreProgress(Job job)
    {
        try
        {
            var dir = StateDir(job.OutDir);
            if (!Directory.Exists(dir)) return 0;

            // Newest state file wins: an older one describes a run that a later
            // one already superseded.
            var files = Directory.GetFiles(dir, "*.json")
                .OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f))
                .ToList();

            foreach (var file in files)
            {
                var previous = Load(file);
                if (previous is null) continue;
                if (previous.Id == job.Id) continue;
                if (previous.Mode != job.Mode) continue;
                if (previous.Items.Count == 0) continue;

                // Map written text -> final state. Only finished work counts;
                // a "failed" item should be retried, not inherited as failed.
                var done = new Dictionary<string, ItemRow>(StringComparer.Ordinal);
                foreach (var old in previous.Items)
                {
                    if (old.State != ItemState.Done) continue;
                    if (string.IsNullOrEmpty(old.File)) continue;
                    if (!done.ContainsKey(old.Text)) done[old.Text] = old;
                }
                if (done.Count == 0) continue;

                var restored = 0;
                foreach (var item in job.Items)
                {
                    if (item.State == ItemState.Done) continue;
                    if (!done.TryGetValue(item.Text, out var old)) continue;

                    // The file must still be there. A restored row that points at
                    // a deleted clip is worse than a pending row.
                    if (!System.IO.File.Exists(old.File)) continue;

                    item.State = ItemState.Done;
                    item.File = old.File;
                    item.Duration = old.Duration;
                    item.Defects = new List<string>(old.Defects);
                    item.Error = "";
                    restored++;
                }

                if (restored > 0) return restored;
            }
        }
        catch { /* a broken state file must not stop the parse */ }

        return 0;
    }
}

public sealed class JobOptions
{
    public bool Normalize { get; set; } = true;
    public bool DetectDefects { get; set; } = true;
    public int IndexStart { get; set; }
    public string AudioFormat { get; set; } = "wav";
    public int SampleRate { get; set; } = 22050;
    public string TrainTemplate { get; set; } = "{index}";
    public string SplitMarker { get; set; } = "###";
    public bool WriteTranscript { get; set; } = true;

    /// <summary>
    /// Append the file extension to the output name.
    ///
    /// Piper's own training docs describe the dataset as extensionless stems
    /// with the text in metadata.csv, so the option has to exist. Turning it off
    /// only makes sense for WAV — an extensionless MP3 cannot be opened by
    /// anything without guessing the format.
    /// </summary>
    public bool AddExtension { get; set; } = true;

    /// <summary>
    /// Write the phonetic form into metadata.csv instead of the written one.
    ///
    /// This is not cosmetic. `piper_train` phonemizes whatever metadata.csv
    /// contains, so the transcript must match the audio it was rendered from.
    /// If the clip says "dwa tysiące dwudziestego czwartego" while metadata says
    /// "2024", espeak-ng expands the digits to the NOMINATIVE — "dwa tysiące
    /// dwudziestego czwarty" — and the model trains on a phoneme sequence that
    /// does not match its audio. Verified against espeak-ng 1.52: 2024 becomes
    /// "dwa tysiące dwadzieścia czwarty", and "1 maja" becomes "jeden maja".
    ///
    /// So: on for a training dataset, off when the CSV is meant for reading.
    /// </summary>
    public bool MetadataWithSpoken { get; set; } = true;
}

/// <summary>A line of input after parsing, before it becomes a job item.</summary>
public sealed class ParsedLine
{
    /// <summary>Cleaned text, markers intact.</summary>
    public string Text { get; init; } = "";

    /// <summary>Source line before cleaning, for edits that must reach the box.</summary>
    public string Raw { get; init; } = "";
    public string Speaker { get; init; } = "";
    public int Index { get; set; }

    /// <summary>Number from a "NNNN|" prefix in the source, when one was present.</summary>
    public int? SourceIndex { get; init; }
}
