using System;
using System.Diagnostics;

namespace VoxQueue.Core.Services;

/// <summary>
/// Shutting the machine down after a long unattended run.
///
/// A render that finishes at 03:00 should not leave the machine awake until
/// morning, so the queue offers to power off when it is done. Two rules make
/// this safe rather than alarming:
///
///   * The shutdown is scheduled with a delay, never immediate. Windows accepts
///     `shutdown /a` during that window, so a wrong guess is reversible.
///   * The caller decides whether to call this at all — it is not wired into the
///     runner, because "finished" is not the same as "succeeded".
/// </summary>
public static class PowerAction
{
    /// <summary>
    /// Powers the machine off now that the countdown has run out.
    ///
    /// A short delay is still passed to the OS (5 s on Windows) rather than 0:
    /// it gives the window time to repaint one last time, so the user sees the
    /// dialog close instead of the screen cutting out mid-frame.
    /// </summary>
    public static (bool ok, string message) ShutdownNow() => ScheduleShutdown(5);

    /// <summary>
    /// Schedules a shutdown. Returns whether it took, and a message for the UI.
    /// The message is deliberately specific: a silent failure here would leave
    /// the machine on all night with no indication why.
    /// </summary>
    public static (bool ok, string message) ScheduleShutdown(int delaySeconds = 120)
    {
        delaySeconds = Math.Max(30, delaySeconds);

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var psi = new ProcessStartInfo("shutdown")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var a in new[]
                         {
                             "/s", "/t", delaySeconds.ToString(),
                             "/c", "VoxQueue finished. Run 'shutdown /a' to cancel."
                         })
                    psi.ArgumentList.Add(a);

                using var p = Process.Start(psi);
                if (p is null) return (false, "could not start shutdown.exe");
                p.WaitForExit(5000);
                return p.ExitCode == 0
                    ? (true, $"shutting down in {delaySeconds / 60} min — run 'shutdown /a' to cancel")
                    : (false, $"shutdown.exe returned {p.ExitCode}");
            }

            // Linux and macOS have no permission-free guaranteed path. systemctl
            // works when a polkit rule allows it; shutdown(8) is the fallback.
            var minutes = Math.Max(1, delaySeconds / 60);
            foreach (var (cmd, args) in new[]
                     {
                         ("systemctl", new[] { "poweroff" }),
                         ("shutdown", new[] { "-h", $"+{minutes}" })
                     })
            {
                try
                {
                    var psi = new ProcessStartInfo(cmd)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    };
                    foreach (var a in args) psi.ArgumentList.Add(a);

                    using var p = Process.Start(psi);
                    if (p is null) continue;
                    p.WaitForExit(5000);
                    if (p.ExitCode == 0) return (true, $"shutting down in {minutes} min");
                }
                catch { /* try the next command */ }
            }

            return (false, "cannot shut down: no permission (systemctl poweroff refused)");
        }
        catch (Exception e)
        {
            return (false, $"{e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>Cancels a scheduled shutdown. No-op off Windows.</summary>
    public static void CancelShutdown()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return;
            var psi = new ProcessStartInfo("shutdown")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("/a");
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch { /* nothing scheduled, or no rights — both fine */ }
    }
}
