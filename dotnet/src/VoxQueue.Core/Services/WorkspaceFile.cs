using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoxQueue.Core.Models;

namespace VoxQueue.Core.Services;

/// <summary>
/// A saved project: everything needed to restore the editing session, without
/// the results.
///
/// Deliberately separate from <see cref="Job"/>: a job is one run and carries
/// per-item progress in `.voxqueue/`, a workspace is the definition — the text,
/// the mode, the options — and is portable. Sharing a file between the two would
/// mean either copying megabytes of state into a project or losing the ability
/// to reopen a project after its state file was cleaned.
///
/// Rendering output is never referenced: reopening a project does not assume the
/// audio still exists, because the point of reopening is usually to redo it.
/// </summary>
public sealed class Workspace
{
    /// <summary>Bumped only when an older file would be read wrongly.</summary>
    public int Version { get; set; } = 1;

    public string Mode { get; set; } = "train";
    public string OutDir { get; set; } = "";
    public string Voice { get; set; } = "";
    public string InputText { get; set; } = "";
    public Dictionary<string, string> VoiceMap { get; set; } = new();
    public JobOptions Options { get; set; } = new();

    /// <summary>
    /// Parsed lines. Kept so a project reopens with its list intact even when the
    /// parser has changed since — the text alone would re-split differently.
    /// </summary>
    public List<WorkspaceItem> Items { get; set; } = new();

    public string SavedWith { get; set; } = "";
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class WorkspaceItem
{
    public string Text { get; set; } = "";        // written form
    public string Spoken { get; set; } = "";      // full phonetic form, may equal Text
    public string Phonetic { get; set; } = "";    // only the changed words, for display
    public string Speaker { get; set; } = "";
    public int Index { get; set; }

    /// <summary>
    /// The raw source line, markers and all.
    ///
    /// Without it a restored session could not rewrite the text box: the edit
    /// matches by the raw line, and an empty value made every attempt fail
    /// silently — the dialog closed and nothing changed.
    /// </summary>
    public string SourceLine { get; set; } = "";

    /// <summary>Pin flag survives a project or session save.</summary>
    public bool Pinned { get; set; }
}

/// <summary>
/// The session that comes back on the next start.
///
/// A workspace is something the user names and keeps; a session is the one
/// implicit slot the app maintains itself. Same format on purpose — the reader
/// is already written and tested, and a separate schema would drift.
///
/// It lives in the config directory rather than in the output folder: it
/// describes what was on screen, not what was rendered, and tying it to a
/// dataset folder would resurrect a job the user had moved away from.
/// </summary>
/// <summary>
/// The session: one workspace per tab, plus which tab was open.
///
/// Saving only the active tab meant that closing the app while the dubbing tab
/// was in front discarded the training tab's work — the slot is written on every
/// close, so the first close from another tab overwrote it. A bundle fixes that
/// without a second file to keep in sync.
///
/// It lives in the config directory, not in an output folder: it describes what
/// was on screen, not what was rendered.
/// </summary>
public sealed class SessionBundle
{
    /// <summary>Format marker, so an old single-workspace file is still readable.</summary>
    public int Version { get; set; } = 2;

    public int ActiveTab { get; set; }

    public Workspace? Train { get; set; }
    public Workspace? Dub { get; set; }
    public Workspace? Bulk { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty =>
        (Train is null || Train.Items.Count == 0) &&
        (Dub is null || Dub.Items.Count == 0) &&
        (Bulk is null || Bulk.Items.Count == 0);

    public Workspace? ForTab(int tab) => tab switch
    {
        0 => Train,
        1 => Dub,
        2 => Bulk,
        _ => null
    };
}

public static class SessionFile
{
    public static string Path => System.IO.Path.Combine(Config.Dir, "session.vqproj");

    public static bool Exists => File.Exists(Path);

    /// <summary>Saves, or deletes the slot when no tab holds anything worth restoring.</summary>
    public static void SaveOrClear(SessionBundle? bundle)
    {
        try
        {
            if (bundle is null || bundle.IsEmpty)
            {
                Clear();
                return;
            }

            // The config directory may not exist on a first run: settings are
            // written lazily, so a session save before any settings existed threw
            // and was swallowed by the catch below — the session never appeared.
            Directory.CreateDirectory(Config.Dir);

            var json = System.Text.Json.JsonSerializer.Serialize(
                bundle, WorkspaceFile.JsonOptions);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, Path, overwrite: true);
        }
        catch { /* a session that cannot be saved is not worth failing a close over */ }
    }

    public static void Clear()
    {
        try { if (File.Exists(Path)) File.Delete(Path); } catch { }
    }

    /// <summary>
    /// Reads the slot, tolerating the old single-workspace format.
    ///
    /// A session written by a previous build deserialises into a bundle with all
    /// three slots null; the version check below turns that into a bundle holding
    /// one workspace, so an upgrade does not silently drop the user's work.
    /// </summary>
    public static SessionBundle? Load()
    {
        try
        {
            if (!File.Exists(Path)) return null;

            var json = File.ReadAllText(Path);

            try
            {
                var bundle = System.Text.Json.JsonSerializer.Deserialize<SessionBundle>(
                    json, WorkspaceFile.JsonOptions);
                if (bundle is not null && !bundle.IsEmpty) return bundle;
            }
            catch { /* fall through to the old format */ }

            var single = WorkspaceFile.Load(Path, out _);
            if (single is null) return null;

            var restored = new SessionBundle { ActiveTab = TabOf(single.Mode) };
            switch (restored.ActiveTab)
            {
                case 1: restored.Dub = single; break;
                case 2: restored.Bulk = single; break;
                default: restored.Train = single; break;
            }
            return restored;
        }
        catch { return null; }
    }

    public static int TabOf(string? mode) => (mode ?? "").ToLowerInvariant() switch
    {
        "dub" => 1,
        "bulk" => 2,
        _ => 0
    };
}


public static class WorkspaceFile
{
    /// <summary>Extension used by the save dialog and by drag-onto-window later.</summary>
    public const string Extension = "vqproj";

    public const string Filter = "VoxQueue project (*.vqproj)|*.vqproj|All files (*.*)|*.*";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Writes atomically through a temp file. A half-written project is worse
    /// than an old one: it opens and looks almost right.
    /// </summary>
    public static void Save(string path, Workspace workspace)
    {
        workspace.SavedAt = DateTimeOffset.Now;
        workspace.SavedWith = typeof(WorkspaceFile).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        SafeFile.WriteAllText(path, JsonSerializer.Serialize(workspace, JsonOptions));
    }

    public static Workspace? Load(string path, out string error)
    {
        error = "";
        try
        {
            var text = File.ReadAllText(path);
            var ws = JsonSerializer.Deserialize<Workspace>(text, JsonOptions);
            if (ws is null)
            {
                error = "file is empty or not a project";
                return null;
            }
            if (ws.Version > 1)
            {
                error = $"project was saved by a newer version (format {ws.Version})";
                return null;
            }
            return ws;
        }
        catch (JsonException e)
        {
            error = "not a valid project file: " + e.Message;
            return null;
        }
        catch (Exception e)
        {
            error = e.Message;
            return null;
        }
    }
}
