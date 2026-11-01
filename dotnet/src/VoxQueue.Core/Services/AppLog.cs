using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VoxQueue.Core.Services;

/// <summary>
/// The application log: connections, edits, saves, failures.
///
/// Two destinations on purpose. The file is what survives a crash and what a user
/// can send on; the in-memory ring is what the Log tab shows, so opening the tab
/// does not have to parse a file that may be megabytes long.
///
/// The ring is bounded. An unbounded list in a long-running app that logs every
/// item of a ten-thousand-clip job is a slow leak, and the file already holds the
/// full history.
/// </summary>
public static class AppLog
{
    private const int MaxLines = 5000;
    private static readonly object Gate = new();
    private static readonly Queue<string> Ring = new();

    /// <summary>Raised for every line, on the thread that logged it.</summary>
    public static event Action<string>? LineAdded;

    public static string Path => System.IO.Path.Combine(Config.Dir, "voxqueue.log");

    /// <summary>Current contents of the in-memory ring, oldest first.</summary>
    public static IReadOnlyList<string> Lines
    {
        get { lock (Gate) return Ring.ToArray(); }
    }

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception ex) =>
        Write("ERROR", message + " — " + ex.GetType().Name + ": " + ex.Message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";

        lock (Gate)
        {
            Ring.Enqueue(line);
            while (Ring.Count > MaxLines) Ring.Dequeue();
        }

        try
        {
            Directory.CreateDirectory(Config.Dir);

            // Rotate at a size a user can still attach to a message. Ten megabytes
            // of log is not a log, it is a dump.
            var file = Path;
            if (File.Exists(file) && new FileInfo(file).Length > 10 * 1024 * 1024)
                File.Move(file, file + ".1", overwrite: true);

            File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // A log that throws must never take the app with it.
        }

        try { LineAdded?.Invoke(line); } catch { }
    }

    /// <summary>Empties both the file and the ring.</summary>
    public static void Clear()
    {
        lock (Gate) Ring.Clear();

        try
        {
            Directory.CreateDirectory(Config.Dir);
            if (File.Exists(Path)) File.Delete(Path);
            if (File.Exists(Path + ".1")) File.Delete(Path + ".1");
        }
        catch { }
    }

    /// <summary>Loads the tail of the file into the ring, for a fresh window.</summary>
    public static void LoadTail(int maxLines = 2000)
    {
        try
        {
            if (!File.Exists(Path)) return;

            var all = File.ReadAllLines(Path);
            var from = Math.Max(0, all.Length - maxLines);

            lock (Gate)
            {
                Ring.Clear();
                for (var i = from; i < all.Length; i++) Ring.Enqueue(all[i]);
            }
        }
        catch { }
    }
}
