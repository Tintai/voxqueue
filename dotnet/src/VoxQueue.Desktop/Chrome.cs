using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VoxQueue.Desktop;

/// <summary>
/// Custom window chrome.
///
/// The form is borderless (FormBorderStyle.None) so the OS title bar and its
/// light grey frame disappear; everything a title bar does is re-implemented
/// here: dragging, double-click to maximise, minimise / maximise / close, and
/// edge resizing through WM_NCHITTEST. Windows 11 gets rounded corners through
/// DWM, older Windows falls back to square corners rather than a wrong shape.
/// </summary>
public sealed class WindowChrome
{
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    private const int ResizeBorder = 6;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public int dwFlags;
    }

    /// <summary>Rounds the window corners on Windows 11; a no-op elsewhere.</summary>
    public static void EnableRoundedCorners(Form form)
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
            const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            const int DWMWCP_ROUND = 2;
            var pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch { /* cosmetic only */ }
    }

    /// <summary>Handles the messages a borderless form needs to stay usable.</summary>
    public static bool HandleMessage(Form form, ref Message m)
    {
        switch (m.Msg)
        {
            case WM_NCHITTEST:
            {
                if (form.WindowState == FormWindowState.Maximized) return false;

                var p = form.PointToClient(Cursor.Position);
                var w = form.ClientSize.Width;
                var h = form.ClientSize.Height;
                var left = p.X <= ResizeBorder;
                var right = p.X >= w - ResizeBorder;
                var top = p.Y <= ResizeBorder;
                var bottom = p.Y >= h - ResizeBorder;

                var hit = HTCLIENT;
                if (top && left) hit = HTTOPLEFT;
                else if (top && right) hit = HTTOPRIGHT;
                else if (bottom && left) hit = HTBOTTOMLEFT;
                else if (bottom && right) hit = HTBOTTOMRIGHT;
                else if (left) hit = HTLEFT;
                else if (right) hit = HTRIGHT;
                else if (top) hit = HTTOP;
                else if (bottom) hit = HTBOTTOM;

                if (hit == HTCLIENT) return false;
                m.Result = (IntPtr)hit;
                return true;
            }

            case WM_GETMINMAXINFO:
            {
                // A borderless form maximises over the taskbar unless the work
                // area is used, so the maximised bounds are clamped by hand.
                var monitor = MonitorFromWindow(form.Handle, 2 /* MONITOR_DEFAULTTONEAREST */);
                if (monitor == IntPtr.Zero) return false;

                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(monitor, ref info)) return false;

                var mmi = Marshal.PtrToStructure<MINMAXINFO>(m.LParam);
                mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
                mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
                mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
                mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
                Marshal.StructureToPtr(mmi, m.LParam, true);
                m.Result = IntPtr.Zero;
                return true;
            }
        }
        return false;
    }
}

/// <summary>One of the three window buttons. Drawn, not themed — the native
/// caption buttons vanish with the frame.</summary>
public sealed class WindowButton : Control
{
    public enum Kind { Minimize, Maximize, Restore, Close }

    private Kind _kind = Kind.Minimize;
    private bool _hover;

    public WindowButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(44, 30);
        Cursor = Cursors.Hand;
        BackColor = Theme.WindowA;
    }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Kind ButtonKind
    {
        get => _kind;
        set { _kind = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var hot = _kind == Kind.Close
            ? Color.FromArgb(0xC4, 0x2B, 0x2B)
            : Theme.CardAlt;

        using (var bg = new SolidBrush(_hover ? hot : Theme.WindowA))
            g.FillRectangle(bg, ClientRectangle);

        var fg = _hover && _kind == Kind.Close ? Color.White : Theme.Text;
        using var pen = new Pen(fg, 1.4f);

        var cx = Width / 2;
        var cy = Height / 2;
        const int s = 5;

        switch (_kind)
        {
            case Kind.Minimize:
                g.DrawLine(pen, cx - s, cy + 1, cx + s, cy + 1);
                break;

            case Kind.Maximize:
                g.DrawRectangle(pen, cx - s, cy - s, s * 2, s * 2);
                break;

            case Kind.Restore:
                g.DrawRectangle(pen, cx - s, cy - s + 2, s * 2 - 2, s * 2 - 2);
                g.DrawLine(pen, cx - s + 2, cy - s, cx + s, cy - s);
                g.DrawLine(pen, cx + s, cy - s, cx + s, cy + s - 2);
                break;

            case Kind.Close:
                g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s);
                break;
        }
    }
}

/// <summary>
/// The title bar: app name, version, the three window buttons, dragging and
/// double-click to maximise.
/// </summary>
public sealed class TitleBar : Panel
{
    private readonly Label _title = new();
    private readonly Label _version = new();
    private readonly WindowButton _min = new() { ButtonKind = WindowButton.Kind.Minimize };
    private readonly WindowButton _max = new() { ButtonKind = WindowButton.Kind.Maximize };
    private readonly WindowButton _close = new() { ButtonKind = WindowButton.Kind.Close };

    private bool _dragging;
    private Point _dragStart;

    public TitleBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 38;
        BackColor = Theme.WindowA;

        _title.Text = "VoxQueue";
        _title.AutoSize = true;
        _title.Font = Theme.Body(9.5f, FontStyle.Bold);
        _title.ForeColor = Theme.Text;
        _title.Location = new Point(14, 11);
        _title.BackColor = Color.Transparent;
        _title.Tag = "keepbg";

        _version.AutoSize = true;
        _version.Font = Theme.Body(8f);
        _version.ForeColor = Theme.Dim;
        _version.BackColor = Color.Transparent;
        _version.Tag = "keepbg";

        Controls.Add(_title);
        Controls.Add(_version);
        Controls.Add(_min);
        Controls.Add(_max);
        Controls.Add(_close);

        _min.Click += (_, _) => FindForm()!.WindowState = FormWindowState.Minimized;
        _max.Click += (_, _) => ToggleMaximize();
        _close.Click += (_, _) => FindForm()!.Close();

        // Dragging: the delta is taken from the absolute cursor position, not
        // accumulated, so the window cannot drift away from the pointer.
        MouseDown += OnDown;
        MouseMove += OnMove;
        MouseUp += (_, _) => _dragging = false;
        DoubleClick += (_, _) => ToggleMaximize();
    }

    public void SetVersion(string text)
    {
        _version.Text = text;
        LayoutChildren();
    }

    public void ApplyTheme()
    {
        BackColor = Theme.WindowA;
        _title.ForeColor = Theme.Text;
        _version.ForeColor = Theme.Dim;
        foreach (var b in new[] { _min, _max, _close }) { b.BackColor = Theme.WindowA; b.Invalidate(); }
        Invalidate();
    }

    public void SyncMaximizeIcon()
    {
        var form = FindForm();
        _max.ButtonKind = form?.WindowState == FormWindowState.Maximized
            ? WindowButton.Kind.Restore
            : WindowButton.Kind.Maximize;
    }

    private void ToggleMaximize()
    {
        var form = FindForm();
        if (form is null) return;
        form.WindowState = form.WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
        SyncMaximizeIcon();
    }

    private void OnDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragStart = Cursor.Position;
    }

    private void OnMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging || e.Button != MouseButtons.Left) return;
        var form = FindForm();
        if (form is null) return;

        var now = Cursor.Position;
        var dx = now.X - _dragStart.X;
        var dy = now.Y - _dragStart.Y;

        if (form.WindowState == FormWindowState.Maximized)
        {
            // Pulling a maximised window restores it under the pointer.
            form.WindowState = FormWindowState.Normal;
            SyncMaximizeIcon();
        }

        form.Location = new Point(form.Location.X + dx, form.Location.Y + dy);
        _dragStart = now;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutChildren();
    }

    private void LayoutChildren()
    {
        var right = Width - 4;
        _close.Location = new Point(right - _close.Width, 4);
        _max.Location = new Point(_close.Left - _max.Width, 4);
        _min.Location = new Point(_max.Left - _min.Width, 4);
        _version.Location = new Point(_title.Right + 10, 13);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, Theme.WindowA, Theme.WindowB, 45f);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}
