using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VoxQueue.Desktop;

/// <summary>
/// The few Win32 calls WinForms does not expose.
///
/// Two things are needed here that matter for a responsive editor:
///
/// * WM_SETREDRAW — switches painting off around a batch of colour changes.
///   SuspendLayout/ResumeLayout is the managed alternative but it forces a full
///   relayout on resume, and doing that dozens of times a second is what made the
///   whole window stutter while colouring.
///
/// * EM_GETSCROLLPOS / EM_SETSCROLLPOS — reading and restoring the scroll offset
///   of a RichTextBox. Setting SelectionStart scrolls the control to the caret,
///   so without this the view jumped back to the top after every pass.
/// </summary>
internal static class Native
{
    public const int WM_SETREDRAW = 0x000B;
    public const int EM_GETSCROLLPOS = 0x04DD;
    public const int EM_SETSCROLLPOS = 0x04DE;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref POINT lParam);

    /// <summary>Freezes painting. Always paired with <see cref="ResumeRedraw"/>.</summary>
    public static void StopRedraw(Control control)
    {
        try { SendMessage(control.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero); }
        catch { /* handle not created yet */ }
    }

    /// <summary>
    /// Re-enables painting and forces one repaint.
    ///
    /// The explicit Invalidate matters: a control that was never told to repaint
    /// keeps showing whatever was on screen when redraw was switched off.
    /// </summary>
    public static void ResumeRedraw(Control control)
    {
        try
        {
            SendMessage(control.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            control.Invalidate();
        }
        catch { /* tearing down */ }
    }
}
