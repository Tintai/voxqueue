using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

using VoxQueue.Core.I18n;

namespace VoxQueue.Desktop;

/// <summary>
/// Script editor with phonetic markers highlighted.
///
/// A plain TextBox shows `{Eter|Aether}` as one grey string, so the two halves —
/// the one that will be spoken and the one that will be written — look identical.
/// That is the mistake this control exists to prevent: they are different texts
/// serving different purposes, and the eye has to be able to separate them.
///
/// Colour scheme:
///   spoken half      accent  — what the engine says
///   written half     teal    — what lands in the transcript
///   braces and pipe  dim     — syntax, not content
///
/// Built on RichTextBox because TextBox cannot colour a range. Tabs are disabled
/// and the border removed so it looks exactly like the plain box it replaces;
/// exposing RichTextBox directly would leak a different visual style.
/// </summary>
public sealed class ScriptBox : UserControl
{
    private readonly RichTextBox _rtb = new();
    private readonly System.Windows.Forms.Timer _colourTimer = new();
    private bool _dark = true;

    /// <summary>
    /// How much text one colouring pass handles.
    ///
    /// The whole document is coloured — colouring only what is visible left the
    /// rest plain until it scrolled into view, which reads as "colouring does not
    /// work". Doing it in slices keeps the window responsive: each pass is a few
    /// milliseconds, and the timer yields between them, so typing and scrolling
    /// stay smooth while the rest fills in.
    /// </summary>
    /// <summary>
    /// Characters per slice. 4000 keeps one tick to a few milliseconds: a slice
    /// costs roughly one Select per marker, and Select is the expensive call.
    /// </summary>
    private const int ChunkChars = 4000;

    /// <summary>Character where the current pass resumes; -1 when nothing is queued.</summary>

    /// <summary>
    /// Snapshot of the text taken once per pass.
    ///
    /// This was the actual cause of the freeze: the old code called
    /// <c>_rtb.Text</c> inside every slice, and that getter copies the entire
    /// document. At 15 ms intervals on a 10 000-line script it was copying
    /// hundreds of kilobytes several dozen times a second, on the UI thread.
    /// </summary>

    /// <summary>Scroll offset captured at the start of a pass, restored at the end.</summary>

    /// <summary>Guards against re-entry while a slice is being painted.</summary>
    private bool _colourBusy;

    /// <summary>
    /// Set while a programmatic line replacement runs. Suppresses the full pass so
    /// editing one line does not repaint the whole document.
    /// </summary>
    private bool _suppressRecolour;

    /// <summary>
    /// The colouring setting this box currently reflects.
    ///
    /// ApplyColouringSetting is pushed on every tab change, and re-applying it
    /// unconditionally restarted the whole pass — switching tabs made the editor
    /// visibly re-colour from the top each time. Remembering the applied value
    /// turns that into a no-op.
    /// </summary>
    private bool _colouringApplied = true;

    /// <summary>A pass was asked for while the control could not be painted.</summary>
    private bool _needsRecolour;

    /// <summary>How far the pass has got. -1 when nothing is queued.</summary>
    private int _colourCursor = -1;

    /// <summary>Snapshot taken once per pass, so the getter is not called per slice.</summary>
    private string _colourText = "";

    private readonly Panel _badge = new();
    private readonly Label _badgeText = new();
    private readonly FlatProgress _badgeBar = new();

    /// <summary>A pass was requested while a slice was running; see ScheduleRecolour.</summary>


    /// <summary>
    /// Passes shorter than this are not announced: a 200 ms flicker of a spinner
    /// looks like a glitch, not like progress.
    /// </summary>
    private const int BusyThreshold = 40_000;

    /// <summary>True while a colouring pass is running.</summary>
    public bool IsBusy => _colourBusy;

    /// <summary>Raised when a pass starts and when it ends.</summary>

    /// <summary>Caret captured when a pass starts, restored when it ends.</summary>

    public event EventHandler? TextChanged2;

    /// <summary>Raised when a .txt/.csv/.srt/.md file is dropped on the box.</summary>
    public event EventHandler<string>? FileDropped;

    private bool _hot;

    public ScriptBox()
    {
        BackColor = Theme.Input;
        Padding = new Padding(8, 6, 8, 6);

        _rtb.BorderStyle = BorderStyle.None;
        _rtb.Dock = DockStyle.Fill;
        _rtb.Font = Theme.Mono();
        _rtb.AcceptsTab = false;
        // No wrapping and both scrollbars. With WordWrap on, a long line has
        // nowhere to go but off the right edge and no horizontal scrollbar is
        // possible at all — the text simply became unreachable on a narrow window.
        _rtb.WordWrap = false;
        _rtb.ScrollBars = RichTextBoxScrollBars.Both;
        _rtb.DetectUrls = false;
        _rtb.HideSelection = false;
        _rtb.TextChanged += (_, _) =>
        {
            // Debounced: a paste fires TextChanged once per chunk, and a full
            // recolour on each one is the difference between instant and several
            // seconds.
            if (!_suppressRecolour) ScheduleRecolour();
            TextChanged2?.Invoke(this, EventArgs.Empty);
        };

        // A 15 ms slice keeps the UI at roughly 60 fps while a full pass runs.
        _colourTimer.Interval = 15;
        _colourTimer.Tick += (_, _) => ColourNextChunk();

        // The inner box has to be added, and it has to fill the control. This line
        // was deleted together with the busy badge in the previous release, which
        // left a rich text box that existed, held the text, and was never displayed
        // — the editor pane came up empty with the list fully populated.
        _rtb.Dock = DockStyle.Fill;
        Controls.Add(_rtb);

        // Static badge, not a marquee. A marquee ProgressBar runs its own animation
        // timer for as long as it exists, so it kept moving even with the badge
        // hidden. This one is a determinate strip: it only moves when the pass does.
        _badge.Size = new Size(210, 30);
        _badge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _badge.BackColor = Theme.Card;
        _badge.Visible = false;

        _badgeText.AutoSize = false;
        _badgeText.TextAlign = ContentAlignment.MiddleLeft;
        _badgeText.ForeColor = Theme.Accent;
        _badgeText.Font = Theme.Body(8.5f);
        _badgeText.SetBounds(8, 3, 194, 15);

        _badgeBar.Maximum = 100;
        _badgeBar.SetBounds(8, 20, 194, 7);

        _badge.Controls.Add(_badgeText);
        _badge.Controls.Add(_badgeBar);

        Controls.Add(_badge);
        _badge.BringToFront();
        _badge.Location = new Point(Math.Max(0, Width - _badge.Width - 12), 8);
        Resize += (_, _) =>
            _badge.Location = new Point(Math.Max(0, ClientSize.Width - _badge.Width - 12), 8);

        AllowDrop = true;
        _rtb.AllowDrop = true;
        DragEnter += OnDragEnter;
        DragOver += OnDragEnter;
        DragLeave += OnDragLeave;
        DragDrop += OnDragDrop;
        _rtb.DragEnter += OnDragEnter;
        _rtb.DragOver += OnDragEnter;
        _rtb.DragLeave += OnDragLeave;
        _rtb.DragDrop += OnDragDrop;

        // The deferred pass runs here, once the control is actually on screen.
        VisibleChanged += (_, _) =>
        {
            if (Visible && _needsRecolour) ScheduleRecolour();
        };

        ApplyTheme();
    }

    /// <summary>
    /// Replaces one line in place and recolours only that line.
    ///
    /// Assigning TextValue would replace the entire document: the text box resets
    /// its whole buffer, fires TextChanged, and every line is repainted. For an
    /// edit that touches a single line that is enormous wasted work and it looked
    /// like the window blinking on every pronunciation change.
    ///
    /// Returns false when the line no longer exists, so the caller can report it.
    /// </summary>
    public bool ReplaceLine(int lineIndex, string newText)
    {
        if (lineIndex < 0) return false;

        var lines = _rtb.Lines;
        if (lineIndex >= lines.Length) return false;

        var start = _rtb.GetFirstCharIndexFromLine(lineIndex);
        if (start < 0) return false;

        var oldLength = lines[lineIndex].Length;
        var caret = _rtb.SelectionStart;

        // The edit changes line lengths, so the only correct thing is one full pass
        // afterwards. It costs a fraction of a second and cannot leave the document
        // half-coloured.
        var fullRecolour = true;

        _suppressRecolour = true;
        try
        {
            _rtb.Select(start, oldLength);

            Native.StopRedraw(_rtb);
            try
            {
                // SelectedText keeps every other line's formatting untouched and
                // avoids rebuilding the buffer.
                _rtb.SelectedText = newText;
            }
            finally
            {
                Native.ResumeRedraw(_rtb);
            }
        }
        finally
        {
            _suppressRecolour = false;
        }

        // Recolour just this line, and adjust the caret if it sat after the change.
        var newLength = _rtb.Lines[lineIndex].Length;
        var delta = newLength - oldLength;
        var newCaret = caret >= start + oldLength ? caret + delta
                     : caret >= start ? Math.Min(start + newLength, caret)
                     : caret;

        _rtb.Select(Math.Clamp(newCaret, 0, _rtb.TextLength), 0);

        if (fullRecolour) ScheduleRecolour();
        else ColourLine(lineIndex);

        return true;
    }

    /// <summary>Colours a single line, without touching the rest of the document.</summary>
    private void ColourLine(int lineIndex)
    {
        var lines = _rtb.Lines;
        if (lineIndex < 0 || lineIndex >= lines.Length) return;

        var start = _rtb.GetFirstCharIndexFromLine(lineIndex);
        if (start < 0) return;
        var end = start + lines[lineIndex].Length;

        _colourBusy = true;
        try
        {
            Native.StopRedraw(_rtb);
            _suppressRecolour = true;
            try
            {
                var scroll = GetScrollPos();
                _rtb.Select(start, end - start);
                _rtb.SelectionColor = Theme.Text;
                _rtb.SelectionBackColor = Theme.Input;
                HighlightMarkers(_rtb.Text, start, end);
                SetScrollPos(scroll);
            }
            finally
            {
                _suppressRecolour = false;
                Native.ResumeRedraw(_rtb);
            }
        }
        catch { /* the box may be tearing down */ }
        finally
        {
            _colourBusy = false;
        }
    }

    /// <summary>The inner control, for read-only toggling and focus.</summary>
    public RichTextBox Inner => _rtb;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string TextValue
    {
        get => _rtb.Text;
        set
        {
            if (_rtb.Text == value) return;
            _rtb.Text = value;
            // Scheduled, not immediate: the pass is sliced, so assigning a
            // large file returns at once and the colouring arrives over the next
            // few frames instead of freezing the window.
            ScheduleRecolour();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Placeholder { get; set; } = "";

    public void ApplyTheme()
    {
        _dark = Theme.Dark;
        Theme.SafeBackColor(this, Theme.Input);
        Theme.SafeBackColor(_rtb, Theme.Input);
        _rtb.ForeColor = Theme.Text;
        ScheduleRecolour();
        Invalidate();
    }

    // ------------------------------------------------------------- colouring

    /// <summary>
    /// Queues a full pass, restarting from the top.
    ///
    /// Restarting rather than patching is deliberate: an edit can change the
    /// colouring of anything below it (an opening brace makes a distant brace a
    /// closing one), and a sliced pass from the top costs a few frames at most.
    /// </summary>
    /// <summary>Queues a full pass, restarting from the top.</summary>
    private void ScheduleRecolour()
    {
        if (!VoxQueue.Desktop.Program.Settings.ColourText)
        {
            FinishColouring();
            return;
        }

        _colourText = _rtb.Text;
        _colourCursor = 0;
        _needsRecolour = false;

        ShowBadge(_colourText.Length >= BusyThreshold);
        UpdateBadge();

        _colourTimer.Stop();
        _colourTimer.Start();
    }

    /// <summary>
    /// Colours one slice and schedules the next.
    ///
    /// Sliced, not whole: a 10 000-line script with markers costs tens of thousands
    /// of Select/SelectionColor calls, and doing them in one go blocks the UI thread
    /// for seconds. A slice of a few thousand characters is a few milliseconds.
    ///
    /// Two guards make the slicing safe, and both were missing from the earlier
    /// attempt:
    ///
    ///   * _suppressRecolour around the paint. Setting SelectionColor raises
    ///     TextChanged, so without it every slice re-queued a pass and reset the
    ///     cursor to zero — colouring restarted from the top forever.
    ///   * the visibility check. A hidden RichTextBox has no line metrics, so Select
    ///     takes a much slower path; a pass started from Settings while the work tab
    ///     was hidden froze the window. It defers instead.
    /// </summary>
    private void ColourNextChunk()
    {
        _colourTimer.Stop();

        if (_colourBusy) return;
        if (_colourCursor < 0) return;

        if (!VoxQueue.Desktop.Program.Settings.ColourText)
        {
            FinishColouring();
            return;
        }

        if (!Visible || _rtb.ClientSize.Height <= 0)
        {
            _needsRecolour = true;
            return;
        }

        var text = _colourText;
        if (text.Length == 0 || _rtb.TextLength < text.Length)
        {
            FinishColouring();
            return;
        }

        var start = Math.Clamp(_colourCursor, 0, text.Length);
        var end = Math.Min(text.Length, start + ChunkChars);

        // Cut on a line boundary so a marker is never split across two slices.
        if (end < text.Length)
        {
            var line = _rtb.GetLineFromCharIndex(end);
            var next = _rtb.GetFirstCharIndexFromLine(line + 1);
            if (next > end) end = Math.Min(text.Length, next);
        }

        _colourBusy = true;
        _suppressRecolour = true;
        Native.StopRedraw(_rtb);

        try
        {
            var scroll = GetScrollPos();

            _rtb.Select(start, end - start);
            _rtb.SelectionColor = Theme.Text;
            _rtb.SelectionBackColor = Theme.Input;
            HighlightMarkers(text, start, end);

            SetScrollPos(scroll);

            _colourCursor = end;
        }
        catch
        {
            FinishColouring();
            return;
        }
        finally
        {
            Native.ResumeRedraw(_rtb);
            _suppressRecolour = false;
            _colourBusy = false;
        }

        UpdateBadge();

        if (_colourCursor >= text.Length) FinishColouring();
        else _colourTimer.Start();
    }

    private void FinishColouring()
    {
        _colourTimer.Stop();
        _colourCursor = -1;
        ShowBadge(false);
    }

    private void ShowBadge(bool show)
    {
        if (_badge.Visible == show) return;

        _badge.Visible = show;
        if (show)
        {
            _badge.BringToFront();
            UpdateBadge();
        }
    }

    private void UpdateBadge()
    {
        if (!_badge.Visible) return;

        var total = Math.Max(1, _colourText.Length);
        var done = Math.Clamp(_colourCursor, 0, total);
        var percent = (int)Math.Round(100.0 * done / total);

        _badgeText.Text = $"{L.Tt("colouring")}… {percent}%";
        _badgeBar.Value = percent;
    }

    private Point GetScrollPos()
    {
        var native = new Native.POINT();
        Native.SendMessage(_rtb.Handle, Native.EM_GETSCROLLPOS, IntPtr.Zero, ref native);
        return new Point(native.X, native.Y);
    }

    private void SetScrollPos(Point p)
    {
        if (p == Point.Empty) return;
        var native = new Native.POINT { X = p.X, Y = p.Y };
        Native.SendMessage(_rtb.Handle, Native.EM_SETSCROLLPOS, IntPtr.Zero, ref native);
    }


    private void HighlightMarkers(string text) => HighlightMarkers(text, 0, text.Length);

    private void HighlightMarkers(string text, int from, int to)
    {
        var i = Math.Max(0, from);

        while (i < text.Length && i < to)
        {
            var open = text.IndexOf('{', i);
            if (open < 0) break;

            var close = text.IndexOf('}', open + 1);
            if (close < 0)
            {
                // Unclosed brace: leave the rest as plain text but mark the brace
                // so the mistake is visible.
                Colour(open, 1, Theme.Warn);
                break;
            }

            var inner = text.Substring(open + 1, close - open - 1);
            var bar = inner.IndexOf('|');

            var isMarker = bar > 0 && bar < inner.Length - 1 &&
                           inner[..bar].Trim().Length > 0 &&
                           inner[(bar + 1)..].Trim().Length > 0;

            if (!isMarker)
            {
                // Not a marker: colour the braces dim so it reads as literal text
                // rather than silently pretending to be syntax.
                Colour(open, 1, Theme.Dim);
                Colour(close, 1, Theme.Dim);
                i = close + 1;
                continue;
            }

            var pipeAt = open + 1 + bar;

            Colour(open, 1, Theme.Dim);                            // {
            Colour(open + 1, bar, Theme.Accent);                   // spoken — what the engine says
            Colour(pipeAt, 1, Theme.Dim);                          // |
            Colour(pipeAt + 1, close - pipeAt - 1, Theme.Written); // written — what is recorded
            Colour(close, 1, Theme.Dim);                           // }

            i = close + 1;
        }
    }

    private void Colour(int start, int length, Color colour)
    {
        if (length <= 0 || start < 0 || start + length > _rtb.TextLength) return;
        _rtb.Select(start, length);
        _rtb.SelectionColor = colour;
    }

    // ------------------------------------------------------------- drag&drop

    private static bool Accepts(IDataObject? data)
    {
        if (data?.GetDataPresent(DataFormats.FileDrop) != true) return false;
        if (data.GetData(DataFormats.FileDrop) is not string[] files) return false;
        foreach (var f in files)
        {
            var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
            if (ext is ".txt" or ".csv" or ".srt" or ".md") return true;
        }
        return false;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        var ok = Accepts(e.Data);
        e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
        if (ok && !_hot) { _hot = true; Invalidate(); }
    }

    private void OnDragLeave(object? sender, EventArgs e)
    {
        // DragLeave also fires when the pointer crosses onto the inner control,
        // so the highlight clears only when the cursor really left the area.
        if (!ClientRectangle.Contains(PointToClient(Cursor.Position)))
        {
            _hot = false;
            Invalidate();
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        _hot = false;
        Invalidate();
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
        foreach (var f in files)
        {
            var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
            if (ext is ".txt" or ".csv" or ".srt" or ".md") FileDropped?.Invoke(this, f);
        }
    }

    // ---------------------------------------------------------------- layout

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // A hairline frame, drawn here rather than via BorderStyle so it matches
        // the other inputs instead of the OS 3D edge.
        var frame = _hot ? Theme.Accent
                  : _rtb.Focused ? Theme.Accent
                  : Theme.Border;
        // Inset by one pixel. A 2 px pen centred on the edge draws half of itself
        // outside the control, and because the parent clips to the bounds that
        // showed up as the right edge of the field being cut off while dragging.
        var thickness = _hot ? 2f : 1f;
        using var pen = new Pen(frame, thickness);
        e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);

        if (_rtb.TextLength == 0 && !string.IsNullOrEmpty(Placeholder))
        {
            TextRenderer.DrawText(e.Graphics, Placeholder, _rtb.Font,
                new Rectangle(10, 8, Width - 20, Height - 16),
                Theme.Dim,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    /// <summary>
    /// Re-applies the colouring setting: clears the document's colours when it is
    /// switched off, and queues a pass when it is switched on.
    /// </summary>
    public void ApplyColouringSetting()
    {
        var wanted = VoxQueue.Desktop.Program.Settings.ColourText;

        // Nothing to do when the setting has not changed since the last push.
        if (_colouringApplied == wanted) return;

        _colouringApplied = wanted;

        if (!wanted)
        {
            _colourTimer.Stop();

            // Clearing the colours means selecting the whole document first, and
            // SelectAll leaves it selected: the text came back fully highlighted,
            // which looks like the editor had been focus-grabbed on purpose.
            // The caret and the scroll offset are captured and put back immediately.
            var caret = _rtb.SelectionStart;
            var length = _rtb.SelectionLength;
            var scroll = GetScrollPos();

            Native.StopRedraw(_rtb);
            try
            {
                _rtb.SelectAll();
                _rtb.SelectionColor = Theme.Text;
                _rtb.SelectionBackColor = Theme.Input;

                var at = Math.Clamp(caret, 0, _rtb.TextLength);
                _rtb.Select(at, Math.Clamp(length, 0, _rtb.TextLength - at));
                SetScrollPos(scroll);
            }
            finally
            {
                Native.ResumeRedraw(_rtb);
            }
            return;
        }

        ScheduleRecolour();
    }

    /// <summary>True when at least one usable marker is present.</summary>
    public bool HasMarkers => _rtb.Text.Contains('{'); 
}
