using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VoxQueue.Desktop;

/// <summary>
/// Dark/light styling for native Win32 chrome: scrollbars inside TextBox and
/// DataGridView, combo drop-downs, the context menus.
///
/// Why not Application.SetColorMode: that API must be called before the first
/// window exists, so it can set the colour mode once at start-up and never
/// change it. Switching the theme at runtime therefore left those scrollbars
/// black in light mode. These are per-window messages, so they can be re-applied
/// on every theme change.
/// </summary>
public static class NativeTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subApp, string? subIdList);

    /// <summary>Walks the tree and switches every realised window to the theme.</summary>
    public static void Apply(Control root, bool dark)
    {
        try
        {
            if (root.IsHandleCreated) ApplyOne(root.Handle, dark);
            foreach (Control child in root.Controls) Apply(child, dark);
        }
        catch { /* cosmetic only — never let theming break the app */ }
    }

    private static void ApplyOne(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                var on = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
            }
        }
        catch { }

        try
        {
            // "DarkMode_Explorer" is what repaints the scrollbar of an EDIT or
            // DATAGRID control; "Explorer" restores the light one.
            SetWindowTheme(hwnd, dark ? "DarkMode_Explorer" : "Explorer", null);
        }
        catch { }
    }
}
