using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VoxQueue.Desktop;

/// <summary>
/// Fully owner-drawn drop-down.
///
/// The native ComboBox with DropDownStyle.DropDownList paints its closed state
/// through the OS theme and ignores BackColor — that is why it stayed black in
/// the light theme while every other control followed the palette. WinForms has
/// no supported way to repaint it completely, so this replaces it: a flat control
/// that draws background, text and chevron itself and opens a borderless form
/// with a themed list.
///
/// Public surface mirrors the parts of ComboBox this app used, so call sites did
/// not have to change shape.
/// </summary>
public sealed class Dropdown : Control
{
    private readonly List<object> _items = new();
    private int _selected = -1;
    private bool _hover;
    private bool _open;
    private ToolStripDropDown? _popup;

    public event EventHandler? SelectedIndexChanged;

    public Dropdown()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                 | ControlStyles.Selectable, true);
        Height = 30;
        Cursor = Cursors.Hand;
        Font = Theme.Body(9f);
        TabStop = true;
    }

    /// <summary>
    /// Mirrors ComboBox.Items for the handful of members this app uses
    /// (Add, AddRange, Clear, Count, indexer, IndexOf) so the call sites did not
    /// have to be rewritten when the control was replaced.
    /// </summary>
    public sealed class ItemCollection
    {
        private readonly Dropdown _owner;
        internal ItemCollection(Dropdown owner) => _owner = owner;

        public int Count => _owner._items.Count;
        public object this[int i] => _owner._items[i];

        public void Add(object item)
        {
            _owner._items.Add(item);
            if (_owner._selected < 0) _owner._selected = 0;
            _owner.Invalidate();
        }

        public void AddRange(object[] items) => AddRange((IEnumerable<object>)items);

        public void AddRange(IEnumerable<object> items)
        {
            foreach (var it in items) _owner._items.Add(it);
            if (_owner._selected < 0 && _owner._items.Count > 0) _owner._selected = 0;
            _owner.Invalidate();
        }

        public void Clear() => _owner.ClearItems();

        public int IndexOf(object item) => _owner._items.IndexOf(item);
    }

    private ItemCollection? _itemsView;

    /// <summary>Items, as strings or VoiceInfo — anything with ToString().</summary>
    public ItemCollection Items => _itemsView ??= new ItemCollection(this);

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var v = value < 0 || value >= _items.Count ? -1 : value;
            if (v == _selected) return;
            _selected = v;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem
    {
        get => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;
        set
        {
            var idx = value is null ? -1 : _items.IndexOf(value);
            SelectedIndex = idx;
        }
    }


    public void ClearItems()
    {
        _items.Clear();
        _selected = -1;
        Invalidate();
    }

    // ------------------------------------------------------------------ paint

    public void ApplyTheme() => Invalidate();

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        ShowPopup();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Down or Keys.Space or Keys.Enter) { ShowPopup(); e.Handled = true; }
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Clear with the parent colour first: a transparent control over a Card
        // would otherwise keep stale pixels from the previous theme.
        using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Surface))
            g.FillRectangle(bg, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.Rounded(r, 8);

        var border = _open || Focused ? Theme.Accent
                   : _hover ? Theme.BorderHover
                   : Theme.Border;

        using (var fill = new SolidBrush(Theme.Input)) g.FillPath(fill, path);
        using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);

        var text = SelectedItem?.ToString() ?? "";
        var textRect = new Rectangle(10, 0, Width - 34, Height);
        TextRenderer.DrawText(g, text, Font, textRect,
            _items.Count == 0 ? Theme.Dim : Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // Chevron, drawn rather than imaged so it always matches the palette.
        var cx = Width - 16;
        var cy = Height / 2;
        using var arrow = new Pen(Theme.Dim, 1.4f);
        g.DrawLines(arrow, new[]
        {
            new Point(cx - 4, cy - 2),
            new Point(cx, cy + 2),
            new Point(cx + 4, cy - 2)
        });
    }

    // ------------------------------------------------------------------ popup

    private void ShowPopup()
    {
        if (_open || _items.Count == 0) return;

        _popup = new ToolStripDropDown
        {
            AutoSize = true,
            Padding = new Padding(0),
            Margin = new Padding(0),
            DropShadowEnabled = true,
            BackColor = Theme.WindowB
        };

        var list = new ListBox
        {
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 26,
            IntegralHeight = false,
            Font = Font,
            Width = Width,
            Height = Math.Min(280, _items.Count * 26 + 6),
            BackColor = Theme.WindowB,
            ForeColor = Theme.Text
        };
        foreach (var it in _items) list.Items.Add(it);
        if (_selected >= 0) list.SelectedIndex = _selected;

        list.DrawItem += (_, de) =>
        {
            if (de.Index < 0) return;
            var sel = (de.State & DrawItemState.Selected) != 0;
            using var b = new SolidBrush(sel ? Theme.Accent : Theme.WindowB);
            de.Graphics.FillRectangle(b, de.Bounds);
            var label = list.Items[de.Index]?.ToString() ?? "";
            TextRenderer.DrawText(de.Graphics, label, Font, de.Bounds,
                sel ? Color.White : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };

        list.Click += (_, _) =>
        {
            var idx = list.SelectedIndex;
            _popup?.Close();
            if (idx >= 0) SelectedIndex = idx;
        };
        list.KeyDown += (_, ke) =>
        {
            if (ke.KeyCode == Keys.Enter)
            {
                var idx = list.SelectedIndex;
                _popup?.Close();
                if (idx >= 0) SelectedIndex = idx;
                ke.Handled = true;
            }
            else if (ke.KeyCode == Keys.Escape)
            {
                _popup?.Close();
                ke.Handled = true;
            }
        };

        var host = new ToolStripControlHost(list)
        {
            AutoSize = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
            Size = new Size(Width, list.Height)
        };
        _popup.Items.Add(host);
        _popup.Closed += (_, _) => { _open = false; Invalidate(); };

        _open = true;
        Invalidate();
        _popup.Show(this, new Point(0, Height + 2));
        list.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _popup?.Dispose();
            _popup = null;
        }
        base.Dispose(disposing);
    }
}
