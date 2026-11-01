using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace VoxQueue.Core.Services;

/// <summary>
/// File writes that survive a Windows machine doing something else at the same
/// time.
///
/// Writing a freshly rendered clip and immediately replacing the target file is
/// the exact moment an on-access antivirus scanner opens it. The replacement then
/// fails with UnauthorizedAccessException or IOException — not because anything
/// is wrong with the code or the path, but because the handle is momentarily
/// held elsewhere. It shows up on a single item, seemingly at random, which is
/// what makes it look like a content problem.
///
/// Every write therefore retries a few times with a short backoff. Only after
/// the retries are exhausted does the error surface, and it surfaces with the
/// path and the inner exception attached, so the next report says where.
/// </summary>
public static class SafeFile
{
    private const int Attempts = 6;
    private const int BaseDelayMs = 60;

    /// <summary>Numbers destined for a filename must not follow the machine locale.</summary>
    public static string Invariant(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Replaces an existing file atomically, retrying while it is locked.</summary>
    public static void Replace(string tempPath, string targetPath)
    {
        Retry(() =>
        {
            // A read-only target cannot be replaced; clear the flag rather than
            // failing, because a read-only output file is not user intent.
            if (File.Exists(targetPath))
            {
                var attrs = File.GetAttributes(targetPath);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(targetPath, attrs & ~FileAttributes.ReadOnly);
            }
            File.Move(tempPath, targetPath, overwrite: true);
        }, $"replace {targetPath}");
    }

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        var tmp = TempFor(path);
        Retry(() => File.WriteAllBytes(tmp, bytes), $"write {path}");
        Replace(tmp, path);
    }

    public static void WriteAllText(string path, string text)
    {
        var tmp = TempFor(path);
        Retry(() => File.WriteAllText(tmp, text), $"write {path}");
        Replace(tmp, path);
    }

    /// <summary>Deletes a file if it is there; a locked temp file is not an error.</summary>
    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* leftovers in .tmp are cleaned on the next run */ }
    }

    /// <summary>A sibling temp name, so the move stays on one volume.</summary>
    private static string TempFor(string path) =>
        path + "." + Guid.NewGuid().ToString("N")[..6] + ".tmp";

    private static void Retry(Action action, string what)
    {
        Exception? last = null;

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                last = e;
                // Backoff grows: 60, 120, 180, 240, 300 ms. Beyond that the file
                // is genuinely held, not briefly scanned.
                Thread.Sleep(BaseDelayMs * attempt);
            }
        }

        throw new IOException(
            $"could not {what} after {Attempts} attempts (file may be open in another program)", last);
    }
}
