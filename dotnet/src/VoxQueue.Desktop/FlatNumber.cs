using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VoxQueue.Desktop;

/// <summary>
/// A number field with increment arrows that the application draws itself.
///
/// This replaces <see cref="NumericUpDown"/>. That control contains a native
/// <c>UpDownButtons</c> child, and the OS paints it in its own colours: in light
/// mode the two arrows stayed black against a white field, because a native child
/// control ignores the palette entirely. There is no supported way to recolour it,
/// which is the same reason the drop-down and the scrollbars are hand-drawn here.
///
/// The behaviour kept from NumericUpDown is the one that matters in this app: the
/// wheel never changes the value unless the control has focus, so scrolling a panel
/// cannot silently alter a setting.
/// </summary>
public sealed class FlatNumber : Control
{
    private decimal _value;
    private decimal _minimum;
    private decimal _maximum = 100;
    private decimal _step = 1;
    private bool _hover;

    /// <summary>Width of the arrow column. Wide enough for a fingertip, narrow enough to stay compact.</summary>
    private const int ArrowWidth = 20;

    /// <summary>Height of each arrow half; the two share the field's full height.</summary>
    private int HalfHeight => Math.Max(1, Height / 2);

    public FlatNumber()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw
                 | ControlStyles.Selectable, true);

        Height = 24;
        Width = 82;
        Font = Theme.Body(9f);
        Cursor = Cursors.IBeam;
        TabStop = true;
        MinimumSize = new Size(56, 22);
    }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public decimal Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, _minimum, _maximum);
            if (v == _value) return;

            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public decimal Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_maximum < _minimum) _maximum = _minimum;
            Value = Math.Clamp(_value, _minimum, _maximum);
            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public decimal Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(value, _minimum);
            Value = Math.Clamp(_value, _minimum, _maximum);
            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public decimal Increment
    {
        get => _step;
        set => _step = value <= 0 ? 1 : value;
    }

    public event EventHandler? ValueChanged;

    /// <summary>Rounded up, or down, by one step. Clamped by the Value setter.</summary>
    public void Step(int direction) => Value = _value + _step * direction;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Surface))
            g.FillRectangle(bg, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.Rounded(r, 6);

        using (var fill = new SolidBrush(Enabled ? Theme.Input : Theme.Card))
            g.FillPath(fill, path);
        using (var pen = new Pen(_hover && Enabled ? Theme.Accent : Theme.Border, 1f))
            g.DrawPath(pen, path);

        // The number, inset by the arrow column.
        var textRect = new Rectangle(8, 0, Math.Max(0, Width - ArrowWidth - 12), Height);
        TextRenderer.DrawText(g, _value.ToString(), Font, textRect,
            Enabled ? Theme.Text : Theme.Dim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        DrawArrows(g);
    }

    /// <summary>
    /// Two stacked triangles. Drawn as filled paths rather than glyphs so they keep
    /// the same weight at every DPI and cannot be replaced by a fallback font.
    /// </summary>
    private void DrawArrows(Graphics g)
    {
        var top = new Rectangle(Width - ArrowWidth - 4, 3, ArrowWidth - 4, HalfHeight - 4);
        var bottom = new Rectangle(top.X, HalfHeight + 1, top.Width, HalfHeight - 4);

        var colour = Enabled ? Theme.Dim : Theme.Border;

        using (var brush = new SolidBrush(colour))
        {
            using var up = Triangle(top, true);
            g.FillPath(brush, up);

            using var down = Triangle(bottom, false);
            g.FillPath(brush, down);
        }

        // A hairline between the halves separates the two hit areas.
        using var sep = new Pen(Theme.Border, 1f);
        g.DrawLine(sep, top.X - 2, HalfHeight, Width - 2, HalfHeight);
    }

    private static GraphicsPath Triangle(Rectangle r, bool up)
    {
        var path = new GraphicsPath();
        var cx = r.X + r.Width / 2;

        if (up)
        {
            path.AddLine(cx - r.Width / 2, r.Bottom, cx + r.Width / 2, r.Bottom);
            path.AddLine(cx + r.Width / 2, r.Bottom, cx, r.Top);
        }
        else
        {
            path.AddLine(cx - r.Width / 2, r.Top, cx + r.Width / 2, r.Top);
            path.AddLine(cx + r.Width / 2, r.Top, cx, r.Bottom);
        }

        path.CloseFigure();
        return path;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var inside = e.X >= Width - ArrowWidth - 4;
        Cursor = inside ? Cursors.Hand : Cursors.IBeam;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled) return;

        Focus();

        // Only the arrow column changes the value. Clicking the number itself does
        // not, so a misplaced click cannot cost a setting silently.
        if (e.X < Width - ArrowWidth - 4) return;

        Step(e.Y <= HalfHeight ? 1 : -1);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown => true,
        _ => base.IsInputKey(keyData)
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Entered here, the arrows are the only way to change the value: there is no
        // inline text editing, which keeps the control small and its state unambiguous.
        switch (e.KeyCode)
        {
            case Keys.Up: Step(1); e.Handled = true; break;
            case Keys.Down: Step(-1); e.Handled = true; break;
            case Keys.PageUp: Step(10); e.Handled = true; break;
            case Keys.PageDown: Step(-10); e.Handled = true; break;
        }
    }

    /// <summary>
    /// The wheel is ignored unless the control has focus. This is the whole reason
    /// the previous control existed: scrolling a panel used to change values the
    /// cursor merely passed over.
    /// </summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!Focused) return;      // deliberately swallow, do not bubble
        Step(e.Delta > 0 ? 1 : -1);
    }

    public void ApplyTheme() => Invalidate();
}
