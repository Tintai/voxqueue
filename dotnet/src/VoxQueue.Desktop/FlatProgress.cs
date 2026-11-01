using System;
using System.Drawing;
using System.Windows.Forms;

namespace VoxQueue.Desktop;

/// <summary>
/// A progress strip that draws exactly what it is told and nothing else.
///
/// This replaces a native <see cref="ProgressBar"/>. That control, once ForeColor
/// and BackColor are assigned, drops the themed renderer and falls back to the
/// classic one, which paints the fill as sliding segments — so the bar visibly
/// moves with no value change at all, and never settles. Three attempts at
/// suppressing that animation failed because the animation is not ours to control.
///
/// This draws a track and a filled rectangle from <see cref="Value"/> over
/// <see cref="Maximum"/>. No animation, no timer, no theming to fight: an idle bar
/// is an empty track and stays that way.
/// </summary>
public sealed class FlatProgress : Control
{
    private int _value;
    private int _maximum = 100;

    public FlatProgress()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        Height = 8;
    }

    /// <summary>Upper bound. Never zero, so a division cannot be produced anywhere.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            var v = Math.Max(1, value);
            if (v == _maximum) return;

            _maximum = v;
            if (_value > _maximum) _value = _maximum;
            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, 0, _maximum);
            if (v == _value) return;

            _value = v;
            Invalidate();
        }
    }

    /// <summary>Fraction done, 0..1. Exposed so the self-test can assert it.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Fraction => _maximum <= 0 ? 0 : (double)_value / _maximum;

    /// <summary>
    /// Width in pixels of the filled part, for the current control width.
    ///
    /// A proportional fill alone is unusable at this scale: with 10 000 items one
    /// finished clip is 0.01% of the width — less than a pixel — so the bar looked
    /// frozen at zero while work was happening. Any non-zero progress now shows at
    /// least three pixels, which is enough to see movement without distorting the
    /// proportions later.
    /// </summary>
    public int FillWidth => FillWidthFor(Math.Max(0, Width - 1));

    /// <summary>Same computation, for a given usable width. Testable without a screen.</summary>
    public int FillWidthFor(int usableWidth)
    {
        if (_value <= 0 || usableWidth <= 0) return 0;

        var proportional = (int)Math.Round(usableWidth * Fraction);

        // Never wider than the bar, never thinner than a visible sliver.
        return Math.Clamp(proportional, Math.Min(3, usableWidth), usableWidth);
    }

    public void ApplyTheme() => Invalidate();

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        var track = new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height));

        // The track uses the border colour, not the input colour. Filling it with
        // the input colour made an empty bar indistinguishable from the card behind
        // it, so a bar at zero looked like no bar at all — which reads as "the bar
        // was removed".
        using (var back = new SolidBrush(Theme.Border))
            g.FillRectangle(back, track);

        var filled = FillWidthFor(track.Width - 1);
        if (filled > 0)
        {
            using var fill = new SolidBrush(Theme.Accent);
            g.FillRectangle(fill, new Rectangle(0, 0, filled, track.Height));
        }

        // Always outlined, so the control is visible at every value including zero.
        using var edge = new Pen(Theme.Dim);
        g.DrawRectangle(edge, 0, 0, track.Width - 1, track.Height - 1);
    }
}
