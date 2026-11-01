using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VoxQueue.Desktop;

/// <summary>
/// Palette and helpers for the dark/light look.
///
/// EVERY colour here is opaque, deliberately. WinForms throws
/// "Control does not support transparent background colors" the moment a control
/// that lacks SupportsTransparentBackColor is given an ARGB value with alpha
/// below 255 — ComboBox and TextBox are two of them and both sit in this UI.
/// The depth of the "glass" look therefore comes from layered opaque tones plus
/// a hairline border, not from real translucency.
///
/// Rule: Color.FromArgb with FOUR arguments is only ever passed to a GDI+ brush
/// inside OnPaint. Anything that reaches Control.BackColor uses three arguments.
/// </summary>
public static class Theme
{
    public static bool Dark { get; private set; } = true;

    // window background (painted, never assigned to a control that could refuse it)
    public static Color WindowA => Dark ? Color.FromArgb(0x12, 0x11, 0x1A) : Color.FromArgb(0xF4, 0xF3, 0xF9);
    public static Color WindowB => Dark ? Color.FromArgb(0x1A, 0x18, 0x26) : Color.FromArgb(0xE9, 0xE7, 0xF3);

    /// <summary>Opaque base for panels and tab pages.</summary>
    public static Color Surface => Dark ? Color.FromArgb(0x18, 0x17, 0x22) : Color.FromArgb(0xEF, 0xEE, 0xF6);

    public static Color Text => Dark ? Color.FromArgb(0xEA, 0xE8, 0xF2) : Color.FromArgb(0x1B, 0x18, 0x30);
    public static Color Dim => Dark ? Color.FromArgb(0x9D, 0x99, 0xB3) : Color.FromArgb(0x61, 0x5C, 0x78);

    /// <summary>Card fill. Opaque — panels do accept transparency, but keeping one
    /// rule for every surface removes a whole class of runtime surprises.</summary>
    public static Color Card => Dark ? Color.FromArgb(0x21, 0x20, 0x2D) : Color.FromArgb(0xFF, 0xFF, 0xFF);
    public static Color CardAlt => Dark ? Color.FromArgb(0x29, 0x27, 0x37) : Color.FromArgb(0xF8, 0xF7, 0xFD);
    public static Color Border => Dark ? Color.FromArgb(0x35, 0x33, 0x44) : Color.FromArgb(0xD5, 0xD2, 0xE4);
    public static Color BorderHover => Dark ? Color.FromArgb(0x50, 0x4C, 0x66) : Color.FromArgb(0xB6, 0xB2, 0xCC);

    /// <summary>Input fill — must stay opaque: ComboBox and TextBox reject alpha.</summary>
    public static Color Input => Dark ? Color.FromArgb(0x14, 0x13, 0x1C) : Color.FromArgb(0xFF, 0xFF, 0xFF);

    public static Color Accent => Color.FromArgb(0x8B, 0x7C, 0xF6);

    /// <summary>
    /// Colour for the written half of a {spoken|written} marker.
    ///
    /// Deliberately a different hue from Text, not just a lighter tone: the two
    /// halves serve opposite purposes — one is spoken, the other is recorded —
    /// and the difference has to read at a glance while editing a long script.
    /// Teal sits far enough from the violet accent that the pair is unambiguous
    /// in both themes.
    /// </summary>
    public static Color Written => Dark ? Color.FromArgb(0x5F, 0xD1, 0xB8) : Color.FromArgb(0x0E, 0x74, 0x6B);
    public static Color AccentHover => Color.FromArgb(0x9D, 0x8F, 0xF8);
    /// <summary>Tinted surface for hover/drag states. Opaque: it is used as a
    /// BackColor in places where alpha would throw.</summary>
    public static Color AccentSoft => Dark ? Color.FromArgb(0x2A, 0x24, 0x46) : Color.FromArgb(0xEE, 0xEC, 0xFC);
    public static Color Ok => Dark ? Color.FromArgb(0x4A, 0xDE, 0x80) : Color.FromArgb(0x15, 0x80, 0x3D);
    public static Color Warn => Dark ? Color.FromArgb(0xFB, 0xBF, 0x24) : Color.FromArgb(0xB4, 0x53, 0x09);
    public static Color Fail => Dark ? Color.FromArgb(0xF8, 0x71, 0x71) : Color.FromArgb(0xB9, 0x1C, 0x1C);

    /// <summary>Row selection tint, opaque blend so DataGridView does not choke.</summary>
    public static Color Selection => Dark ? Color.FromArgb(0x44, 0x3E, 0x78) : Color.FromArgb(0xDC, 0xD8, 0xF8);

    // Row tints. Opaque: DataGridView rejects an alpha background and the row
    // would fall back to the default instead of showing the state.
    public static Color RowFailBg => Dark ? Color.FromArgb(0x3E, 0x1F, 0x22) : Color.FromArgb(0xFD, 0xEC, 0xEC);
    public static Color RowWarnBg => Dark ? Color.FromArgb(0x3B, 0x32, 0x16) : Color.FromArgb(0xFD, 0xF6, 0xE0);
    public static Color RowPinBg => Dark ? Color.FromArgb(0x26, 0x22, 0x42) : Color.FromArgb(0xEE, 0xEC, 0xFC);

    /// <summary>A clip rendered before its line was edited: out of date, not broken.</summary>
    public static Color Stale => Dark ? Color.FromArgb(0x7F, 0xB0, 0xE8) : Color.FromArgb(0x1D, 0x4E, 0x89);
    public static Color RowStaleBg => Dark ? Color.FromArgb(0x1B, 0x2A, 0x3E) : Color.FromArgb(0xE8, 0xF2, 0xFC);

    public static void Set(bool dark) => Dark = dark;

    /// <summary>
    /// Recolours an entire control tree for the current palette.
    ///
    /// Why one function instead of per-panel switches: every control here sets
    /// its BackColor once in its constructor, and a theme switch only repainted
    /// what each panel happened to remember to touch. Panels, splits and cards
    /// were therefore still dark in light mode. Walking the tree and deciding by
    /// type means a control added later is covered without extra wiring.
    /// </summary>
    public static void Restyle(Control root)
    {
        root.BackColor = root is Form f && f.FormBorderStyle == FormBorderStyle.None
            ? WindowA
            : Surface;

        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case Card card:
                    card.BackColor = card.Alt ? CardAlt : Card;
                    card.Invalidate();
                    break;

                case FlatTabs tabs:
                    tabs.BackColor = WindowA;
                    tabs.Invalidate();
                    break;

                case FlatButton btn:
                    btn.BackColor = Surface;
                    btn.Invalidate();
                    break;

                case Dropdown combo:
                    combo.ApplyTheme();
                    break;

                case TabPage page:
                    page.BackColor = Surface;
                    break;

                case TabControl tc:
                    tc.BackColor = Surface;
                    break;

                case TextBox tb:
                    SafeBackColor(tb, Input);
                    tb.ForeColor = Text;
                    break;

                // RichTextBox derives from TextBoxBase, not from TextBox, so it
                // matched no case at all: the script editor and the log kept the
                // dark background after switching to the light theme.
                case RichTextBox rtb:
                    SafeBackColor(rtb, Input);
                    rtb.ForeColor = Text;
                    break;

                case ComboBox cb:
                    SafeBackColor(cb, Input);
                    cb.ForeColor = Text;
                    break;

                case CheckBox chk:
                    chk.ForeColor = Text;
                    SafeBackColor(chk, Surface);
                    break;

                case ProgressBar bar:
                    // The native progress bar is drawn by the OS theme and stays
                    // light-on-light; a flat strip in the accent colour replaces it.
                    bar.ForeColor = Accent;
                    bar.BackColor = Input;
                    break;

                case DataGridView grid:
                    RestyleGrid(grid);
                    break;

                // Hand-drawn controls read the palette at paint time; they only need
                // to be told to repaint.
                case FlatNumber or FlatProgress or Dropdown or FlatButton:
                    c.Invalidate();
                    break;

                case Label lb:
                    lb.ForeColor = lb.Tag as string == "dim" ? Dim : Text;
                    SafeBackColor(lb, Color.Transparent);
                    break;

                case Panel or UserControl or SplitContainer or SplitterPanel
                     or FlowLayoutPanel or TableLayoutPanel:
                    SafeBackColor(c, Surface);
                    break;
            }

            // DataGridView spawns native scroll buttons and an inner edit box that
            // reject colouring; recursing into it only re-enters this method to no
            // effect. Everything else is walked, including RichTextBox, which used
            // to be skipped by accident and stayed dark in light mode.
            if (c is not DataGridView)
                Restyle(c);
        }
    }

    private static void RestyleGrid(DataGridView grid)
    {
        var back = Dark ? Color.FromArgb(0x16, 0x14, 0x1E) : Color.White;
        grid.BackgroundColor = back;
        grid.GridColor = Border;

        grid.DefaultCellStyle ??= new DataGridViewCellStyle();
        grid.DefaultCellStyle.BackColor = back;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Selection;
        grid.DefaultCellStyle.SelectionForeColor = Dark ? Color.White : Color.FromArgb(0x1B, 0x18, 0x30);

        grid.ColumnHeadersDefaultCellStyle.BackColor = back;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Dim;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = back;

        grid.RowHeadersDefaultCellStyle.BackColor = back;
        grid.RowHeadersDefaultCellStyle.ForeColor = Dim;

        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.Invalidate();
    }

    /// <summary>Semantic colour for a RowKind value from the model.</summary>
    public static Color ForKind(string kind) => kind switch
    {
        "ok" => Ok,
        "warn" => Warn,
        "fail" => Fail,
        "stale" => Stale,
        "accent" => Accent,
        _ => Dim
    };

    public static Font Body(float size = 9f, FontStyle style = FontStyle.Regular)
        => new("Segoe UI", size, style);

    public static Font Mono(float size = 9.5f)
    {
        try { return new Font("Cascadia Mono", size); }
        catch { return new Font("Consolas", size); }
    }

    /// <summary>Rounded rectangle path, used by the card and button painting.</summary>
    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        if (d <= 0 || d > Math.Min(r.Width, r.Height))
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void PaintGradient(Control c, Graphics g)
    {
        using var brush = new LinearGradientBrush(c.ClientRectangle, WindowA, WindowB, 45f);
        g.FillRectangle(brush, c.ClientRectangle);
    }

    /// <summary>
    /// Assigns a background colour only when the control can actually take it.
    /// A guard rather than a comment, because getting this wrong crashes the app
    /// at construction time, before any window appears.
    /// </summary>
    public static void SafeBackColor(Control c, Color color)
    {
        if (color.A < 255) color = Color.FromArgb(255, color.R, color.G, color.B);
        try { c.BackColor = color; }
        catch (ArgumentException) { /* control refuses custom backgrounds; leave it */ }
    }
}

/// <summary>A rounded card. Painted entirely by us, so the control's own
/// background is only ever an opaque fallback.</summary>
public sealed class Card : Panel
{
    // WFO1000: .NET 10's WinForms analyser wants an explicit serialisation
    // decision on every property of a Control. These are runtime flags, never
    // design-time state, so they are hidden from the designer.
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Alt { get; set; }

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        Padding = new Padding(16, 14, 16, 14);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Opaque fill first, so no stale pixels survive a resize.
        using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Surface))
            g.FillRectangle(bg, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.Rounded(r, 14);
        using var fill = new SolidBrush(Alt ? Theme.CardAlt : Theme.Card);
        using var pen = new Pen(Theme.Border, 1f);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }
}

/// <summary>Flat button with a hover state, since the default WinForms chrome
/// ignores the palette entirely.</summary>
public sealed class FlatButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Danger { get; set; }

    private bool _hover;

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = Theme.Body(9f, FontStyle.Bold);
        Height = 32;
        Padding = new Padding(14, 0, 14, 0);
        // Width comes from the caption, not from a guess. A fixed 75 px default
        // with "Ponów zaznaczone" inside is what produced the clipped labels.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    /// <summary>
    /// Preferred size measured from the caption. TextRenderer is used rather
    /// than Graphics.MeasureString so the result matches what TextRenderer draws
    /// in OnPaint — mixing the two leaves a few pixels short and clips again.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        // A control whose AutoSize is off must report the size it was given.
        //
        // Otherwise the reported preferred height (32) wins over Dock = Fill and the
        // button is drawn taller than the row containing it, so its bottom edge is
        // cut away. That is exactly how the "..." button lost its lower half.
        if (!AutoSize) return Size;

        var text = string.IsNullOrEmpty(Text) ? " " : Text;
        var size = TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, Height),
                                            TextFormatFlags.SingleLine);
        var w = size.Width + Padding.Horizontal + 10;
        return new Size(Math.Max(44, w), Height);
    }

    /// <summary>
    /// Smallest size that still shows the caption, border included.
    ///
    /// Used to size compact buttons (the ones that sit beside a text box) so the
    /// glyph is never clipped by the shape drawn around it.
    /// </summary>
    public Size CompactSize()
    {
        var text = string.IsNullOrEmpty(Text) ? " " : Text;
        var size = TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, Height),
                                            TextFormatFlags.SingleLine);
        return new Size(Math.Max(32, size.Width + 16), Math.Max(22, size.Height + 8));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (!AutoSize) return;
        // A longer caption must widen the button immediately, not on the next
        // layout pass — a language switch changes every label at once.
        var pref = GetPreferredSize(Size.Empty);
        if (Width != pref.Width) Width = pref.Width;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Surface))
            g.FillRectangle(bg, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.Rounded(r, 9);

        Color fill, text, border;
        if (!Enabled)
        {
            fill = Theme.Card;
            text = Theme.Dim;
            border = Theme.Border;
        }
        else if (Primary)
        {
            fill = _hover ? Theme.AccentHover : Theme.Accent;
            text = Color.White;
            border = fill;
        }
        else
        {
            fill = _hover ? Theme.CardAlt : Theme.Card;
            text = Danger ? Theme.Fail : Theme.Text;
            border = _hover ? Theme.Accent : Theme.Border;
        }

        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        using (var p = new Pen(border, 1f)) g.DrawPath(p, path);

        // NoPadding and no EndEllipsis. With EndEllipsis a caption that measured one
        // pixel too wide for the box was replaced by "…" — a short label like "o"
        // turned into a character that reads as missing text. NoPadding keeps the
        // measured width and the drawn width the same, so that misfit cannot happen.
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding);

        if (Focused)
            using (var p = new Pen(Theme.Accent, 1f) { DashStyle = DashStyle.Dot })
                g.DrawPath(p, path);
    }
}

/// <summary>Flat tab strip. The default TabControl paints an OS-themed header that
/// clashes with a dark window, so the tabs are owner-drawn.</summary>
public sealed class FlatTabs : TabControl
{
    public FlatTabs()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(120, 34);
        Padding = new Point(0, 0);
        Font = Theme.Body(9.5f, FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.PaintGradient(this, g);

        for (var i = 0; i < TabCount; i++)
        {
            var rect = GetTabRect(i);
            var active = SelectedIndex == i;

            var r = new Rectangle(rect.X + 2, rect.Y + 3, rect.Width - 6, rect.Height - 4);
            using var path = Theme.Rounded(r, 9);
            if (active)
            {
                using var fill = new SolidBrush(Theme.CardAlt);
                g.FillPath(fill, path);
                using var pen = new Pen(Theme.Accent, 1f);
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, TabPages[i].Text, Font, rect,
                active ? Theme.Text : Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnDrawItem(DrawItemEventArgs e) { /* painted above */ }
}

