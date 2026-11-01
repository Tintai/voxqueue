using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VoxQueue.Desktop;

/// <summary>
/// Deterministic card layout helpers.
///
/// Why this exists: a Panel with AutoSize = true and a single Dock = Top child
/// is unreliable in WinForms — the preferred-size pass runs before the child has
/// a size and the panel ends up zero pixels tall, which looks exactly like "the
/// settings page is empty". These helpers size the card explicitly from the
/// child's preferred height instead of trusting AutoSize.
/// </summary>
public static class UiKit
{
    /// <summary>
    /// Builds a card around a table of controls. The card's height is computed
    /// from the table's preferred height, so nothing depends on AutoSize chains.
    /// </summary>
    public static Card MakeCard(TableLayoutPanel table, bool alt = false, int padX = 16, int padY = 14)
    {
        var card = new Card { Alt = alt, Padding = new Padding(padX, padY, padX, padY) };

        table.Dock = DockStyle.None;
        table.Location = new Point(padX, padY);
        table.AutoSize = true;
        table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        table.BackColor = alt ? Theme.CardAlt : Theme.Card;

        card.Controls.Add(table);

        // Setting Width raises SizeChanged synchronously, Fit then sets Height,
        // which raises SizeChanged again. The guard stops that from recursing
        // while the sizes settle.
        var busy = false;

        // An AutoSize table collapses to its preferred width, so every field hugged
        // the left edge and the card showed an empty right half — that is what made
        // the settings page look unfinished. AutoSize is used only to MEASURE the
        // height, then switched off and the table is pinned to the card's inner
        // rectangle, which is what lets the percent-wide column stretch.
        void Fit()
        {
            if (busy) return;
            busy = true;
            try
            {
                table.AutoSize = true;
                var inner = Math.Max(0, card.ClientSize.Width - padX * 2);
                if (inner > 0 && table.Width != inner) table.Width = inner;

                var h = Math.Max(48, table.PreferredSize.Height + padY * 2);
                if (card.Height != h) card.Height = h;

                table.AutoSize = false;
                table.Width = Math.Max(1, card.ClientSize.Width - padX * 2);
                table.Height = Math.Max(1, card.ClientSize.Height - padY * 2);
            }
            finally { busy = false; }
        }

        card.SizeChanged += (_, _) => Fit();
        table.SizeChanged += (_, _) => Fit();
        card.HandleCreated += (_, _) => Fit();

        return card;
    }

    /// <summary>Two-column grid: caption on the left, field on the right.</summary>
    public static TableLayoutPanel Grid(int captionWidth = 150)
    {
        var g = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        g.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, captionWidth));
        g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return g;
    }

    /// <summary>A caption + field row. The caption Label is returned so it can be re-translated.</summary>
    public static Label Row(TableLayoutPanel grid, string text, Control field, Font? captionFont = null)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var caption = new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 10, 6),
            Font = captionFont ?? Theme.Body(9f),
            BackColor = Color.Transparent
        };

        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(0, 3, 0, 3);

        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(field, 1, row);
        return caption;
    }

    /// <summary>
    /// A text field with a compact button beside it, both exactly one row tall.
    ///
    /// The row style is the point. Without an explicit RowStyle a TableLayoutPanel
    /// falls back to AutoSize, and an AutoSize row takes the tallest preferred
    /// height among its children — for a FlatButton that is 32 px, whatever the
    /// cell it was given. Inside a 24 px cell the button was therefore drawn taller
    /// than its own container and its lower edge was hidden behind the next row.
    /// Percent 100 pins the row to the cell and the button follows.
    /// </summary>
    public static TableLayoutPanel FieldWithButton(Control field, Control button, int buttonWidth)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, buttonWidth));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        field.Dock = DockStyle.Fill;
        field.Margin = new Padding(0, 1, 4, 1);

        if (button is FlatButton flat) flat.AutoSize = false;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 1, 0, 1);

        row.Controls.Add(field, 0, 0);
        row.Controls.Add(button, 1, 0);
        return row;
    }

    /// <summary>A row whose content spans both columns (buttons, notes).</summary>
    public static void SpanRow(TableLayoutPanel grid, Control field)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        field.Margin = new Padding(0, 3, 0, 3);
        grid.Controls.Add(field, 1, row);
    }


}

/// <summary>
/// Scrolling container with a scrollbar we draw ourselves.
///
/// The native WinForms scrollbar is painted by the OS theme and stays light even
/// inside a dark window — the one element that refuses to follow the palette.
/// This host scrolls its content manually and paints a thin thumb in the accent
/// colour, so the bar matches the theme instead of fighting it.
/// </summary>
public sealed class ScrollHost : Panel
{
    private const int BarWidth = 10;
    private const int MinThumb = 32;

    private readonly Panel _content = new();
    private int _scroll;
    private bool _dragging;
    private int _dragStartY;
    private int _dragStartScroll;

    public ScrollHost()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Padding = new Padding(0);

        _content.BackColor = Theme.Surface;
        _content.Location = new Point(0, 0);
        Controls.Add(_content);

        _content.ControlAdded += (_, _) => Recalc();
        _content.SizeChanged += (_, _) => Recalc();
    }

    /// <summary>Add controls here, not to the host.</summary>
    public Panel Content => _content;

    private int ViewportHeight => Math.Max(0, ClientSize.Height);
    private int ContentHeight => _content.Height;
    private int MaxScroll => Math.Max(0, ContentHeight - ViewportHeight);

    private Rectangle Track => new(ClientSize.Width - BarWidth, 0, BarWidth, ClientSize.Height);

    private Rectangle Thumb
    {
        get
        {
            var track = Track;
            if (ContentHeight <= 0 || MaxScroll <= 0) return Rectangle.Empty;
            var ratio = ViewportHeight / (double)ContentHeight;
            var h = Math.Max(MinThumb, (int)(track.Height * ratio));
            var travel = track.Height - h;
            var pos = MaxScroll == 0 ? 0 : (int)(travel * (_scroll / (double)MaxScroll));
            return new Rectangle(track.X + 2, track.Y + pos, BarWidth - 4, h);
        }
    }

    public void Recalc()
    {
        // Called from OnLayout, which fires before the control has a size on
        // first show. Bailing out here keeps the arithmetic away from negative
        // widths; the next layout pass with a real size does the work.
        if (IsDisposed || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

        var width = Math.Max(0, ClientSize.Width - BarWidth);
        var total = 0;
        foreach (Control c in _content.Controls)
            total = Math.Max(total, c.Bottom + c.Margin.Bottom);

        _content.Width = width;
        if (total > 0) _content.Height = total;

        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        _content.Location = new Point(0, -_scroll);
        Invalidate();
    }


    private void SetScroll(int value)
    {
        var clamped = Math.Clamp(value, 0, MaxScroll);
        if (clamped == _scroll) return;
        _scroll = clamped;
        _content.Location = new Point(0, -_scroll);
        Invalidate(Track);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Recalc();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Recalc();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        SetScroll(_scroll - e.Delta / 3);
        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var thumb = Thumb;
        if (thumb != Rectangle.Empty && thumb.Contains(e.Location))
        {
            _dragging = true;
            _dragStartY = e.Y;
            _dragStartScroll = _scroll;
        }
        else if (Track.Contains(e.Location))
        {
            // Clicking the track pages, the way a real scrollbar does.
            SetScroll(e.Y < thumb.Y ? _scroll - ViewportHeight : _scroll + ViewportHeight);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var thumb = Thumb;
        var travel = Track.Height - thumb.Height;
        if (travel <= 0) return;

        var delta = e.Y - _dragStartY;
        SetScroll(_dragStartScroll + (int)(delta * (MaxScroll / (double)travel)));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bg = new SolidBrush(Theme.Surface)) g.FillRectangle(bg, ClientRectangle);

        var thumb = Thumb;
        if (thumb == Rectangle.Empty) return;

        using var path = Theme.Rounded(thumb, (BarWidth - 4) / 2);
        using var brush = new SolidBrush(_dragging ? Theme.Accent : Theme.Border);
        g.FillPath(brush, path);
    }
}
