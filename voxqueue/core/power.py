"""Shutting the machine down after a long unattended run.

A render that finishes at 03:00 should not leave the machine awake until
morning, so the queue offers to power off when it is done. Two rules make this
safe rather than alarming:

  * The shutdown is scheduled with a delay, never immediate. On Windows
    ``shutdown /a`` cancels during that window, so a wrong guess is reversible.
  * The caller decides whether to call this at all — "finished" is not the same
    as "succeeded", and this module does not guess.
"""

from __future__ import annotations

import subprocess
import sys


def shutdown_now() -> tuple[bool, str]:
    """Power off once the countdown has run out.

    A short delay is still passed to the OS (5 s on Windows) rather than 0: it
    gives the window time to repaint one last time, so the user sees the dialog
    close instead of the screen cutting out mid-frame.
    """
    return schedule_shutdown(5)


def schedule_shutdown(delay_seconds: int = 120) -> tuple[bool, str]:
    """Schedule a shutdown.

    Returns ``(ok, message)``. The message is deliberately specific: a silent
    failure here would leave the machine on all night with no indication why.
    """
    delay_seconds = max(30, int(delay_seconds))

    if sys.platform == "win32":
        cmd = [
            "shutdown", "/s", "/t", str(delay_seconds),
            "/c", "VoxQueue finished. Run 'shutdown /a' to cancel.",
        ]
        try:
            p = subprocess.run(cmd, capture_output=True, timeout=10)
            if p.returncode == 0:
                return True, (
                    f"shutting down in {delay_seconds // 60} min — "
                    "run 'shutdown /a' to cancel"
                )
            return False, f"shutdown.exe returned {p.returncode}"
        except Exception as exc:  # noqa: BLE001 — surfaced to the UI
            return False, f"{type(exc).__name__}: {exc}"

    # Linux has no permission-free guaranteed path. systemctl works when a polkit
    # rule allows it; shutdown(8) is the fallback.
    minutes = max(1, delay_seconds // 60)
    for cmd in (["systemctl", "poweroff"], ["shutdown", "-h", f"+{minutes}"]):
        try:
            p = subprocess.run(cmd, capture_output=True, timeout=10)
            if p.returncode == 0:
                return True, f"shutting down in {minutes} min"
        except Exception:  # noqa: BLE001 — try the next command
            continue

    return False, "cannot shut down: no permission (systemctl poweroff refused)"


def cancel_shutdown() -> None:
    """Cancel a scheduled shutdown. No-op off Windows."""
    if sys.platform != "win32":
        return
    try:
        subprocess.run(["shutdown", "/a"], capture_output=True, timeout=10)
    except Exception:  # noqa: BLE001 — nothing scheduled, or no rights
        pass
