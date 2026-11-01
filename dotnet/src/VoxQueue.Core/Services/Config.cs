using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxQueue.Core.Services;

public sealed class Settings
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:3900";
    public string ApiKey { get; set; } = "";
    public string OutputDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "VoxQueue");
    public string Language { get; set; } = "system";
    public string Theme { get; set; } = "dark";
    public bool Normalize { get; set; } = true;
    public bool DetectDefects { get; set; } = true;
    public string AudioFormat { get; set; } = "wav";
    public int SampleRate { get; set; } = 22050;
    /// <summary>
    /// Deliberately NOT persisted: numbering always starts from a clean 0000 on
    /// the next launch. Persisting it meant a number nudged once — the mouse
    /// wheel over the spinner is enough — silently carried into the next dataset.
    /// </summary>
    [JsonIgnore]
    public int IndexStart { get; set; }

    public string TrainTemplate { get; set; } = "{index}";
    public string SplitMarker { get; set; } = "###";
    public bool AddExtension { get; set; } = true;

    /// <summary>Follow the queue in the work list. Persisted: it is a viewing
    /// preference, not a one-off action.</summary>
    public bool AutoScroll { get; set; } = true;

    /// <summary>
    /// Work-list order: 0 = source order, 1 = errors first, 2 = changed
    /// pronunciation first. Kept as an int rather than a flag so a third mode
    /// does not need a second checkbox that interacts with the first.
    /// </summary>
    public int SortMode { get; set; }

    /// <summary>
    /// Colour the script editor. Off means no syntax highlighting at all, which
    /// makes pasting and editing a very large file noticeably cheaper — the
    /// colouring pass is the most expensive thing the editor does.
    /// </summary>
    public bool ColourText { get; set; } = true;

    /// <summary>Restore the previous session on start-up.</summary>
    public bool RestoreSession { get; set; } = true;
    public string? LastVoice { get; set; }
    public int LastTab { get; set; }
    /// <summary>"normal" or "max". Stored separately from the size because a
    /// maximised window has no meaningful restore size of its own.</summary>
    public string WindowState { get; set; } = "normal";

    public int WindowWidth { get; set; } = 1440;
    public int WindowHeight { get; set; } = 920;
}

/// <summary>
/// Settings live in the per-user config directory, not beside the executable: a
/// single-file build gets copied around, and shipping the settings with it would
/// mean every copy starts with someone else's configuration.
/// </summary>
public static class Config
{
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxQueue");

    public static string FilePath => Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Opt) ?? new Settings();
        }
        catch
        {
            // A corrupt file must not reset everything silently — keep the bad one
            // for inspection and start clean.
            try { File.Move(FilePath, FilePath + ".broken", overwrite: true); } catch { }
        }
        return new Settings();
    }

    public static void Save(Settings s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, Opt));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception e)
        {
            // Silent failure here means the user changes a setting, sees it applied,
            // and finds it gone on the next start — with nothing to explain why. It
            // must not interrupt the UI (this runs on every checkbox toggle), so the
            // log is the right destination: invisible until someone looks, but a
            // record exists.
            AppLog.Warn($"settings not saved: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>Delete this app's own data. Explicit user action only.</summary>
    public static void Wipe()
    {
        try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { }
    }
}
