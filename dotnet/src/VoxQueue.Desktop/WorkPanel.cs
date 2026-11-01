using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using VoxQueue.Core.I18n;
using VoxQueue.Core.Models;
using VoxQueue.Core.Services;

namespace VoxQueue.Desktop;

/// <summary>
/// The working panel. One class, three modes — they differ only in how the paste
/// is parsed and where the files land, so three near-identical classes would
/// guarantee they drift apart.
/// </summary>
public sealed class WorkPanel : UserControl
{
    private readonly Mode _mode;
    private readonly List<VoiceInfo> _voices = new();
    private readonly BindingList<ItemRow> _rows = new();
    private readonly List<(string Name, Dropdown Combo)> _speakerRows = new();
    private readonly List<(Label Label, string Key)> _settingLabels = new();

    private Job? _job;
    private JobRunner? _runner;

    /// <summary>Set before the first await in StartAsync; see the comment there.</summary>
    private bool _starting;

    /// <summary>
    /// Set when the run itself blew up, as opposed to finishing or being cancelled.
    /// Read by the Finished handler before it considers shutting the machine down.
    /// </summary>
    private bool _runErrored;

    // The bar follows the CURRENT run, not the lifetime totals. Filling it with
    // DoneCount on load made it read 9999/10000 — visually full and motionless,
    // because regenerating one clip moves a bar that is already at the end.
    private int _runTotal;
    private int _runDone;

    /// <summary>Wall clock at the start of this run, for the elapsed/remaining label.</summary>
    private DateTime _runStarted;

    /// <summary>
    /// True while the bar is showing playback position instead of queue progress.
    ///
    /// One bar serves both: the queue's progress while it renders, and the position
    /// of a clip while one is being listened to. Playback takes precedence while it
    /// lasts, because that is the thing the user is watching at that moment.
    /// </summary>
    private bool _showingPlayback;

    /// <summary>
    /// Ticks the clock once a second while a run is in flight.
    ///
    /// Without it the label only changes when an item finishes: on a three-second
    /// clip the time stands still, which looks like a frozen display.
    /// </summary>
    private readonly System.Windows.Forms.Timer _clockTimer = new();

    // left
    private readonly Label _title = new();
    private readonly Label _hint = new();
    private readonly ScriptBox _input = new();
    private readonly FlatButton _btnLoad = new() { Text = "Load" };
    private readonly FlatButton _btnClear = new() { Text = "Clear" };
    private readonly FlatButton _btnParse = new() { Text = "Parse", Primary = true };
    private readonly Label _parsed = new();

    // right
    private readonly Dropdown _voice = new();
    private readonly TextBox _outDir = new();
    private readonly FlatButton _btnBrowse = new() { Text = "..." };
    private readonly CheckBox _chkNormalize = new();
    private readonly CheckBox _chkDefects = new();
    private readonly FlatNumber _indexStart = new();
    private readonly TextBox _marker = new();
    private readonly CheckBox _chkTranscript = new();
    private readonly CheckBox _chkExtension = new();
    private readonly CheckBox _chkMetaSpoken = new();
    private readonly CheckBox _chkShutdown = new();
    private readonly CheckBox _chkAutoScroll = new();
    private readonly Dropdown _cmbSort = new();
    private readonly CheckBox _chkPinnedOnly = new();
    private readonly CheckBox _chkStaleOnly = new();
    private readonly TextBox _search = new();
    private readonly FlatButton _btnBulkPron = new();
    private readonly FlatButton _btnClearProject = new();
    private readonly FlatButton _btnSaveProject = new();
    private readonly FlatButton _btnLoadProject = new();
    private string _projectPath = "";
    private readonly AudioPlayer _player = new();

    private readonly Panel _speakerBody = new();

    private readonly FlatButton _btnStart = new() { Primary = true };
    private readonly FlatButton _btnPause = new();
    private readonly FlatButton _btnCancel = new() { Danger = true };
    private readonly FlatButton _btnRetry = new();
    private readonly FlatButton _btnRegen = new();
    private readonly FlatButton _btnOpen = new();
    private readonly FlatButton _btnOne = new();
    private readonly FlatButton _btnSettingsFolder = new();
    private readonly FlatProgress _bar = new();
    private readonly Label _lblTime = new();
    private readonly Label _status = new();
    private readonly DataGridView _grid = new();

    public WorkPanel(Mode mode)
    {
        _mode = mode;
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        Build();
        Retranslate();
    }

    public Mode Mode => _mode;

    // ------------------------------------------------------------------ build

    private void Build()
    {
        // Nothing size-related goes in the initialiser. A SplitContainer is
        // 150 px wide when it is constructed, and a 520 px minimum on the
        // right pane from that starting width throws before the window exists:
        //   SplitterDistance must be between Panel1MinSize and Width - Panel2MinSize
        // The same trap already bit SplitterDistance once. Both are therefore
        // applied in SetSplitter(), which runs only once the control has a real
        // width, and clamps everything to what actually fits.
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = Theme.Surface,
            SplitterWidth = 10
        };
        split.HandleCreated += (_, _) => SetSplitter(split);
        split.SizeChanged += (_, _) => SetSplitter(split);

        split.Panel1.Controls.Add(BuildLeft());
        split.Panel2.Controls.Add(BuildRight());
        Controls.Add(split);
    }


    /// <summary>
    /// Places the splitter, in an order where every single assignment is legal
    /// at the moment it happens.
    ///
    /// Three separate traps live in this control, and all three have now fired:
    ///   1. SplitContainer is 150 px wide when constructed, so any pane minimum
    ///      larger than that throws in the constructor.
    ///   2. Each minimum setter re-validates the current SplitterDistance, so
    ///      raising the minimums before positioning throws.
    ///   3. Math.Clamp throws when min > max, which is easy to hit while the
    ///      control is still being laid out.
    ///
    /// The order below is therefore: lower both minimums, position, then raise
    /// the minimums to fit the space that now exists. Each step is skipped on
    /// failure instead of aborting the whole call, because the next resize
    /// brings a better width and repairs whatever was left undone.
    /// </summary>
    private static void SetSplitter(SplitContainer split)
    {
        if (split.IsDisposed || !split.IsHandleCreated) return;

        var width = split.Width;
        if (width <= 0) return;

        // A floor for both panes; below this there is nothing sensible to split.
        // 80 px left the text field unusable — the editor is the main input and
        // must stay readable. 300 is the width at which the hint text and the
        // caret are still workable.
        const int narrowest = 300;
        const int minSpan = narrowest * 2 + 10;   // + splitter
        var usable = width - split.SplitterWidth;
        if (usable < minSpan) return;

        var left = Math.Clamp((int)(width * 0.36), narrowest, usable - narrowest);

        const int relax = 25;                     // WinForms default minimum

        Attempt(() => split.Panel1MinSize = relax);
        Attempt(() => split.Panel2MinSize = relax);
        Attempt(() => split.SplitterDistance = left);

        // Only now is there a known distance to clamp the minimums against.
        var distance = split.SplitterDistance;
        Attempt(() => split.Panel1MinSize = Math.Min(340, Math.Max(relax, distance)));
        Attempt(() => split.Panel2MinSize = Math.Min(520, Math.Max(relax, usable - distance)));
    }

    /// <summary>Runs one property assignment, ignoring a refused value.</summary>
    private static void Attempt(Action assignment)
    {
        try { assignment(); }
        catch (ArgumentException) { }              // range and min/max failures
        catch (InvalidOperationException) { }      // not laid out yet
    }

    private Control BuildLeft()
    {
        var card = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Theme.Surface
        };

        // Without an explicit column style the single column sizes itself to the
        // widest child, so the script box took its preferred width from the text
        // inside it and ran past the panel — the field appeared to slide under the
        // right-hand column and its vertical scrollbar was clipped away. The
        // column must be a percentage of the panel, not a measurement of the text.
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _title.Font = Theme.Body(13f, FontStyle.Bold);
        _title.AutoSize = true;
        _hint.Font = Theme.Body(8.5f);
        _hint.AutoSize = true;
        _hint.MaximumSize = new Size(560, 0);

        var titleBox = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 6)
        };
        titleBox.Controls.Add(_title);
        titleBox.Controls.Add(_hint);
        table.Controls.Add(titleBox, 0, 0);

        _input.Dock = DockStyle.Fill;
        _input.FileDropped += (_, path) => AppendFile(path);
        table.Controls.Add(_input, 0, 2);

        var btnRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        _btnLoad.Click += async (_, _) => await LoadFileAsync();
        _btnClear.Click += (_, _) => _input.TextValue = "";
        _btnParse.Click += async (_, _) => await ParseAsync();

        _btnSaveProject.Click += (_, _) => SaveProject();
        _btnLoadProject.Click += (_, _) => LoadProject();

        btnRow.Controls.Add(_btnLoad);
        btnRow.Controls.Add(_btnClear);
        btnRow.Controls.Add(new Panel { Width = 16, Height = 1 });
        btnRow.Controls.Add(_btnParse);
        btnRow.Controls.Add(new Panel { Width = 16, Height = 1 });
        btnRow.Controls.Add(_btnSaveProject);
        btnRow.Controls.Add(_btnLoadProject);
        table.Controls.Add(btnRow, 0, 3);

        _parsed.Font = Theme.Body(8.5f);
        _parsed.AutoSize = true;
        _parsed.Margin = new Padding(0, 6, 0, 0);
        table.Controls.Add(_parsed, 0, 4);

        card.Controls.Add(table);
        return card;
    }

    /// <summary>
    /// Asks before discarding a work list that still has results.
    ///
    /// True when there is nothing to lose, when the user confirms, or when the
    /// text box no longer matches the list and re-parsing is therefore the right
    /// thing to do anyway. The "no changes" case is reported without a dialog:
    /// the list is already up to date and re-parsing would only reset it.
    /// </summary>
    private async Task<bool> ConfirmReparseAsync(string raw)
    {
        if (_job is null || _job.Items.Count == 0) return true;

        var hasWork = _job.Items.Any(i => i.State is ItemState.Done
                                                or ItemState.Failed
                                                or ItemState.Stale);
        if (!hasWork) return true;

        // Which items would survive a re-parse unchanged? Compare the text of the
        // current rows against what the box holds now.
        var lines = _mode switch
        {
            Mode.Train => TextSplitter.SplitLines(raw),
            Mode.Bulk => TextSplitter.SplitBlocks(raw, _marker.Text),
            _ => TextSplitter.SplitDub(raw)
        };

        var before = _job.Items.Select(i => i.Text).ToList();
        var after = lines.Select(l => Pronunciation.Split(l.Text).written).ToList();

        var sameContent = before.Count == after.Count &&
                          before.SequenceEqual(after, StringComparer.Ordinal);

        if (sameContent)
        {
            _status.Text = L.Tt("list_up_to_date");
            return false;
        }

        var rendered = _job.Items.Count(i => i.State is ItemState.Done
                                                  or ItemState.Failed
                                                  or ItemState.Stale);

        var answer = MessageBox.Show(this,
            L.Hint("reparse_warn") + Environment.NewLine + Environment.NewLine
            + string.Format(L.Tt("reparse_detail"), rendered),
            L.Tt("app_title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes) return false;

        AppLog.Info($"re-parse confirmed: {rendered} rendered row(s) reset");
        return true;
    }

    private Control BuildRight()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Theme.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 178));   // settings
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // speakers (dub only)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // buttons (wraps)
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));    // progress + clock
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));    // status + sort + filters
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // work list

        // ---- settings, laid out directly on the surface ----
        // No card, no AutoSize chain, no scroll host. That chain is what kept
        // collapsing this block: every layer reported a different preferred
        // height and the smallest one won. Fixed rows and a two-column grid are
        // boring and they work.
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, 0, 6),
            Padding = new Padding(0)
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Row 2 carries up to five checkboxes. At 24 px they wrap onto a second
        // line and the overflow is clipped without a word — which is exactly how
        // the shutdown checkbox disappeared. It gets two lines of room.
        for (var r = 0; r < 5; r++)
            top.RowStyles.Add(new RowStyle(SizeType.Absolute, r == 2 ? 50 : 24));

        _voice.Dock = DockStyle.Fill;
        _voice.Margin = new Padding(0, 1, 0, 1);
        _voice.SelectedIndexChanged += (_, _) => RememberVoice();
        _settingLabels.Add((AddCaption(top, 0, L.Tt("voice")), "voice"));
        top.Controls.Add(_voice, 1, 0);

        var outRow = UiKit.FieldWithButton(_outDir, _btnBrowse, 44);
        _btnBrowse.Font = Theme.Body(9f, FontStyle.Bold);
        _btnBrowse.Click += async (_, _) => await BrowseOutAsync();

        _settingLabels.Add((AddCaption(top, 1, L.Tt("output_dir")), "output_dir"));
        top.Controls.Add(outRow, 1, 1);

        // Row 2 holds only checkboxes, so it carries no caption. It used to be
        // labelled "index_start" while containing normalize/defects — a label
        // that described nothing.
        _chkNormalize.AutoSize = true;
        _chkNormalize.Margin = new Padding(0, 3, 22, 0);
        _chkDefects.AutoSize = true;
        _chkDefects.Margin = new Padding(0, 3, 22, 0);

        // "Add extension" lives here, not next to the numbering: FlowLayoutPanel
        // with WrapContents=false CLIPS whatever does not fit and says nothing,
        // so in Bulk mode (marker + transcript also visible) the checkbox simply
        // vanished. A dedicated row cannot overflow.
        _chkExtension.AutoSize = true;
        _chkExtension.Margin = new Padding(0, 3, 0, 0);
        _chkExtension.CheckedChanged += (_, _) => OnExtensionToggled();

        _chkMetaSpoken.AutoSize = true;
        _chkMetaSpoken.Margin = new Padding(0, 3, 18, 0);
        _chkMetaSpoken.Checked = true;

        // Shutdown is opt-in and never remembered: a setting that silently
        // powers the machine off after the next run is a trap.
        _chkShutdown.AutoSize = true;
        _chkShutdown.Margin = new Padding(0, 3, 0, 0);

        var checks = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        checks.Controls.Add(_chkNormalize);
        checks.Controls.Add(_chkDefects);
        checks.Controls.Add(_chkExtension);
        checks.Controls.Add(_chkMetaSpoken);
        checks.Controls.Add(_chkShutdown);
        top.Controls.Add(checks, 1, 2);

        var numRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        _indexStart.Width = 82;
        _indexStart.Margin = new Padding(0, 1, 16, 1);
        _marker.Width = 58;
        _marker.Margin = new Padding(0, 1, 8, 1);
        _chkTranscript.AutoSize = true;
        _chkTranscript.Margin = new Padding(0, 3, 0, 0);
        var markerLbl = new Label
        {
            Text = L.Tt("split_marker"),
            AutoSize = true,
            ForeColor = Theme.Dim,
            Margin = new Padding(0, 5, 6, 0)
        };
        _settingLabels.Add((markerLbl, "split_marker"));

        numRow.Controls.Add(_indexStart);
        numRow.Controls.Add(markerLbl);
        numRow.Controls.Add(_marker);
        numRow.Controls.Add(_chkTranscript);
        _settingLabels.Add((AddCaption(top, 3, L.Tt("index_start")), "index_start"));
        top.Controls.Add(numRow, 1, 3);

        layout.Controls.Add(top, 0, 0);

        // ---- speaker map (dubbing only) ----
        // AutoSize, not a fixed 150 px row: with no speakers parsed yet the
        // row would still occupy its height and read as a gap between the
        // settings and the buttons — which is exactly how it looked in Dubbing
        // while Training and Bulk were tight.
        _speakerBody.Dock = DockStyle.Top;
        _speakerBody.AutoSize = true;
        _speakerBody.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _speakerBody.BackColor = Theme.Surface;
        _speakerBody.Margin = new Padding(0, 0, 0, 6);
        _speakerBody.Visible = false;
        layout.Controls.Add(_speakerBody, 0, 1);

        // ---- buttons ----
        //
        // WrapContents = false clipped the trailing buttons on a narrow window
        // with no indication — "Clear project" simply was not there. Wrapping is
        // on, and the row height is recomputed from the buttons' real preferred
        // size instead of being fixed at 40 px, so every button stays reachable
        // at any window width.
        // AutoSize + Dock=Top, inside an AutoSize row: the table measures the row
        // from the panel's preferred height, so a wrapped second or third line of
        // buttons is accounted for automatically. The previous version pinned the
        // row to 40 px and then tried to correct it by hand, which is how the last
        // buttons were clipped away as soon as the window got narrower.
        var ctrl = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            AutoScroll = false,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, 0, 4)
        };
        foreach (var b in new[] { _btnStart, _btnPause, _btnCancel, _btnRetry, _btnRegen, _btnOpen })
            b.Margin = new Padding(0, 0, 6, 0);

        _btnStart.Click += async (_, _) => await StartAsync();
        _btnPause.Click += (_, _) => TogglePause();
        _btnCancel.Click += (_, _) => _runner?.Cancel();
        _btnRetry.Click += async (_, _) => await RetryFailedAsync();
        _btnRegen.Click += async (_, _) => await RegenSelectedAsync();
        _btnOpen.Click += (_, _) => OpenFolder();
        _btnOne.Click += async (_, _) => await GenerateSelectedAsync();
        _btnBulkPron.Click += async (_, _) => await BulkPronunciationAsync();
        _btnBulkPron.Margin = new Padding(0, 0, 6, 0);

        _btnClearProject.Danger = true;
        _btnClearProject.Margin = new Padding(12, 0, 0, 0);
        _btnClearProject.Click += (_, _) => ClearProject();
        ctrl.Controls.AddRange(new Control[]
        {
            _btnStart, _btnPause, _btnCancel, _btnOne, _btnRetry, _btnRegen, _btnOpen,
            _btnBulkPron, _btnClearProject
        });
        layout.Controls.Add(ctrl, 0, 2);

        // The row is sized from the buttons themselves: at one column they need
        // ~40 px, at three columns ~120 px, and only the panel knows which applies
        // at the current width.
        //
        // The measurement is deferred to the layout lifecycle. Doing it from the
        // constructor is what crashed start-up: the control has no window handle
        // yet, and BeginInvoke throws outright ("Invoke or BeginInvoke cannot be
        // called on a control until the window handle has been created").

        SetRunningUi(false);

        // ---- progress: bar plus the clock ----
        //
        // The bar answers "how far", the label answers "how long". A bare percentage
        // over ten thousand clips says nothing about whether to wait or come back
        // tomorrow, so elapsed and estimated total sit next to it.
        //
        // A strip we draw ourselves: the native ProgressBar fell back to its classic
        // renderer once ForeColor/BackColor were set, and that renderer slides its
        // fill segments on its own timer - an idle bar that never stops moving.
        _bar.Dock = DockStyle.Fill;
        _bar.Height = 8;
        _bar.Maximum = 100;
        _bar.Margin = new Padding(0, 5, 8, 0);

        _lblTime.AutoSize = true;
        _lblTime.Font = Theme.Body(8.5f);
        _lblTime.ForeColor = Theme.Dim;
        _lblTime.TextAlign = ContentAlignment.MiddleRight;
        _lblTime.Margin = new Padding(0, 2, 0, 0);
        _lblTime.Text = "";

        var barRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        barRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        barRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        barRow.Controls.Add(_bar, 0, 0);
        barRow.Controls.Add(_lblTime, 1, 0);
        layout.Controls.Add(barRow, 0, 3);

        _clockTimer.Interval = 1000;
        _clockTimer.Tick += (_, _) => UpdateClock();

        _player.Progress += (_, t) => Ui(() => ShowPlayback(t.elapsed, t.total));
        _player.Finished += (_, _) => Ui(RestoreQueueProgress);

        // ---- status line, search and filters ----
        //
        // A table, not hand-placed controls. The previous version positioned each
        // control from the right edge on every resize and put the search box at
        // Left = 0, so a narrow window made the search overlap the status text and
        // the pinned-only checkbox slide under the filter row. Columns cannot
        // overlap by construction.
        _status.Font = Theme.Body(8.5f);
        _status.AutoSize = false;
        _status.AutoEllipsis = true;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Margin = new Padding(0, 6, 8, 0);

        _search.PlaceholderText = L.Tt("search");
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.BackColor = Theme.Input;
        _search.ForeColor = Theme.Text;
        _search.Font = Theme.Body(9f);
        _search.Width = 190;
        _search.Margin = new Padding(0, 4, 8, 0);
        _search.TextChanged += (_, _) => ApplySort();

        _chkPinnedOnly.Text = L.Tt("pinned_only");
        _chkPinnedOnly.AutoSize = true;
        _chkPinnedOnly.Margin = new Padding(0, 9, 12, 0);
        _chkPinnedOnly.CheckedChanged += (_, _) => ApplySort();

        // Sorting is a three-way choice, so it is a dropdown rather than two
        // checkboxes whose combination would need its own rules.
        _cmbSort.Width = 150;
        _cmbSort.Margin = new Padding(0, 4, 12, 0);
        _cmbSort.Items.Add(L.Tt("sort_source"));
        _cmbSort.Items.Add(L.Tt("sort_errors"));
        _cmbSort.Items.Add(L.Tt("sort_pron"));
        _cmbSort.Items.Add(L.Tt("sort_stale"));
        _cmbSort.SelectedIndexChanged += (_, _) =>
        {
            VoxQueue.Desktop.Program.Settings.SortMode = _cmbSort.SelectedIndex;
            Config.Save(VoxQueue.Desktop.Program.Settings);
            ApplySort();
        };

        // Filter to the clips that exist but were made from an older line.
        _chkStaleOnly.Text = L.Tt("stale_only");
        _chkStaleOnly.AutoSize = true;
        _chkStaleOnly.Margin = new Padding(0, 9, 12, 0);
        _chkStaleOnly.CheckedChanged += (_, _) => ApplySort();

        _chkAutoScroll.Text = L.Tt("auto_scroll");
        _chkAutoScroll.AutoSize = true;
        _chkAutoScroll.Checked = true;
        _chkAutoScroll.Margin = new Padding(0, 9, 0, 0);
        _chkAutoScroll.CheckedChanged += (_, _) =>
        {
            VoxQueue.Desktop.Program.Settings.AutoScroll = _chkAutoScroll.Checked;
            Config.Save(VoxQueue.Desktop.Program.Settings);
            if (_chkAutoScroll.Checked) ScrollToActive();
        };

        var statusRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            // Six controls are added, so six columns. With ColumnCount = 5 the last
            // one - the follow-the-queue checkbox - was placed outside the table and
            // never drawn, which reads as "the checkbox was removed".
            ColumnCount = 6,
            RowCount = 1,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // status
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // search
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // pinned only
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // stale only
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // sort
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // follow

        statusRow.Controls.Add(_status, 0, 0);
        statusRow.Controls.Add(_search, 1, 0);
        statusRow.Controls.Add(_chkPinnedOnly, 2, 0);
        statusRow.Controls.Add(_chkStaleOnly, 3, 0);
        statusRow.Controls.Add(_cmbSort, 4, 0);
        statusRow.Controls.Add(_chkAutoScroll, 5, 0);
        layout.Controls.Add(statusRow, 0, 4);

        // ---- work list ----
        _grid.Dock = DockStyle.Fill;
        _grid.Margin = new Padding(0);
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeColumns = false;   // column width is set by the app
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.AutoGenerateColumns = false;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.EnableHeadersVisualStyles = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.DataSource = _rows;
        _grid.CellDoubleClick += (_, _) => PlaySelected();
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            _grid.ClearSelection();
            _grid.Rows[e.RowIndex].Selected = true;

            // e.Location is relative to the CELL, not to the grid, so passing it
            // straight to menu.Show() put the menu one cell to the right and one
            // row down. The cursor position converted to grid coordinates is the
            // only value that means what it looks like it means.
            ShowRowMenu(_grid.PointToClient(Cursor.Position));
        };
        BuildColumns();

        layout.Controls.Add(_grid, 0, 5);
        return layout;
    }

    /// <summary>Caption in column 0, returned so it can be re-translated.</summary>
    private static Label AddCaption(TableLayoutPanel grid, int row, string text)
    {
        var lb = new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Dim,
            Font = Theme.Body(9f),
            Margin = new Padding(0, 0, 8, 0)
        };
        grid.Controls.Add(lb, 0, row);
        return lb;
    }

    private static readonly string[] ColumnKeys =
    {
        "col_index", "col_text", "col_phonetic", "col_duration", "col_status", "col_notes"
    };

    /// <summary>Re-labels the grid columns for the current language.</summary>
    private void ApplyColumnHeaders()
    {
        for (var i = 0; i < ColumnKeys.Length && i < _grid.Columns.Count; i++)
            _grid.Columns[i].HeaderText = L.W(ColumnKeys[i]);
    }

    private void BuildColumns()
    {
        // Headers come from ApplyColumnHeaders() so a language switch only
        // re-labels them; MinimumWidth stops a narrow window squeezing the
        // full "Duration" caption away.
        var idx = new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ItemRow.Index),
            FillWeight = 7, MinimumWidth = 40,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        var txt = new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ItemRow.Label),
            FillWeight = 34, MinimumWidth = 160,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        // The phonetic form gets its own column rather than sharing a cell with
        // the transcription: DataGridView paints one foreground colour per cell,
        // and two colours in one cell would need a fully custom CellPainting path
        // for a benefit a separate column already gives — the two texts sit side
        // by side and are compared at a glance.
        var phon = new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ItemRow.SpokenDisplay),
            FillWeight = 30, MinimumWidth = 140,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        var dur = new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ItemRow.DurationText),
            FillWeight = 12, MinimumWidth = 90,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        var st = new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ItemRow.StatusText),
            FillWeight = 10, MinimumWidth = 70,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        var notes = new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ItemRow.Notes),
            FillWeight = 30, MinimumWidth = 140,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _grid.Columns.Clear();
        _grid.Columns.AddRange(idx, txt, phon, dur, st, notes);
        ApplyColumnHeaders();

        // Colour is applied in code: DataGridView does not inherit a palette.
        //
        // The row background carries the state, so a list of thousands can be
        // scanned without reading the status column. Priority is deliberate:
        // a failed row must never be masked by the fact that it is also pinned,
        // and a flagged row outranks a pinned one for the same reason.
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
            var item = _rows[e.RowIndex];
            var kind = item.RowKind;

            // Precedence: broken beats outdated, outdated beats pinned, pinned
            // beats plain. A stale row must be visible even when it is pinned.
            e.CellStyle!.BackColor = kind switch
            {
                "fail" => Theme.RowFailBg,
                "warn" => Theme.RowWarnBg,
                "stale" => Theme.RowStaleBg,
                _ => item.Pinned ? Theme.RowPinBg : _grid.BackgroundColor
            };

            if (e.ColumnIndex == 4)          // status
            {
                e.CellStyle.ForeColor = Theme.ForKind(kind);
                e.CellStyle.Font = Theme.Body(9f, FontStyle.Bold);
            }
            else if (e.ColumnIndex == 2)     // phonetic
            {
                // Accent: this is what the engine will actually say, and it only
                // appears when it differs from the transcription.
                e.CellStyle.ForeColor = Theme.Accent;
            }
            else if (e.ColumnIndex == 0)     // id, doubles as the pin marker
            {
                e.CellStyle.ForeColor = item.Pinned ? Theme.Accent : Theme.Dim;
                e.CellStyle.Font = Theme.Body(9f, item.Pinned ? FontStyle.Bold : FontStyle.Regular);
            }
            else if (kind is "warn" or "fail")
            {
                e.CellStyle.ForeColor = Theme.ForKind(kind);
            }
            else
            {
                e.CellStyle.ForeColor = Theme.Text;
            }
        };
    }

    private void AddRow(TableLayoutPanel grid, string label, Control field)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lb = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 7, 10, 6)
        };
        lb.Font = Theme.Body(9f);
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(0, 3, 0, 3);

        grid.Controls.Add(lb, 0, row);
        grid.Controls.Add(field, 1, row);
        _settingLabels.Add((lb, label));
    }

    // ------------------------------------------------------------------ theme

    public void ApplyTheme()
    {
        BackColor = Theme.Surface;

        // One tree walk handles every control type; the per-case switch that used
        // to live here missed panels, splits and cards, which is why parts of the
        // window stayed dark in light mode.
        Theme.Restyle(this);

        // The editor has its own palette pass: Restyle fixes the box, but the marker
        // spans keep the previous theme's colours until the pass re-runs.
        _input.ApplyTheme();

        _title.ForeColor = Theme.Text;
        _hint.ForeColor = Theme.Dim;
        _status.ForeColor = Theme.Dim;
        _parsed.ForeColor = Theme.Dim;

        _grid.Invalidate();
        ApplyExtensionAvailability();
        Invalidate(true);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    // --------------------------------------------------------------- language

    public void Retranslate()
    {
        var keys = _mode switch
        {
            Mode.Train => ("tab_train", "train_hint"),
            Mode.Dub => ("tab_dub", "dub_hint"),
            _ => ("tab_bulk", "bulk_hint")
        };
        _title.Text = L.Tt(keys.Item1);
        _hint.Text = L.Hint(keys.Item2);
        _input.Placeholder = L.Hint("paste_hint");

        _btnLoad.Text = L.Tt("load_file");
        _btnParse.Text = L.Tt("build_plan");
        _btnClear.Text = L.Tt("clear");
        _btnStart.Text = L.Tt("start");
        _btnPause.Text = _runner?.IsPaused == true ? L.Tt("resume") : L.Tt("pause");
        _btnCancel.Text = L.Tt("cancel");
        _btnRetry.Text = L.Tt("regenerate_failed");
        _btnRegen.Text = L.Tt("regenerate_selected");
        _btnOpen.Text = L.Tt("open_folder");
        _btnOne.Text = L.Tt("generate_one");
        _btnSettingsFolder.Text = L.Tt("open_settings_folder");

        _chkNormalize.Text = L.Tt("normalize");
        _chkDefects.Text = L.Tt("detect_defects");
        _chkTranscript.Text = L.Tt("write_transcript");
        _chkExtension.Text = L.Tt("add_extension");
        _chkMetaSpoken.Text = L.Tt("metadata_spoken");
        _chkShutdown.Text = L.Tt("shutdown_when_done");
        _chkAutoScroll.Text = L.Tt("auto_scroll");
        _chkPinnedOnly.Text = L.Tt("pinned_only");

        // Every other checkbox was relabelled here except this one, so after a
        // language switch it kept whatever language the session started in —
        // a single Polish caption in the middle of an English window.
        _chkStaleOnly.Text = L.Tt("stale_only");

        _search.PlaceholderText = L.Tt("search");
        var sortIdx = _cmbSort.SelectedIndex;
        _cmbSort.Items.Clear();
        _cmbSort.Items.Add(L.Tt("sort_source"));
        _cmbSort.Items.Add(L.Tt("sort_errors"));
        _cmbSort.Items.Add(L.Tt("sort_pron"));
        _cmbSort.Items.Add(L.Tt("sort_stale"));
        _cmbSort.SelectedIndex = Math.Clamp(sortIdx, 0, 2);
        _btnClearProject.Text = L.Tt("clear_project");
        _btnBulkPron.Text = L.Tt("bulk_pron");
        _btnSaveProject.Text = L.Tt("save_project");
        _btnLoadProject.Text = L.Tt("load_project");

        ApplyColumnHeaders();

        if (string.IsNullOrEmpty(_status.Text)) _status.Text = L.Tt("status_ready");

        // Re-label the settings grid: the row captions were built from the old language.
        RelabelSettings();

        // Defect names in the Notes column are translated on read, so the rows
        // have to be re-bound for a language switch to reach them.
        _rows.ResetBindings();

        ApplyTheme();
    }

    private void RelabelSettings()
    {
        foreach (var (label, key) in _settingLabels)
            label.Text = key.Length == 0 ? "" : L.Tt(key);
    }

    // ------------------------------------------------------------------ voices

    public void SetVoices(List<VoiceInfo> voices, string? preferred)
    {
        _voices.Clear();
        _voices.AddRange(voices);

        var keep = preferred
                   ?? (_voice.SelectedItem as VoiceInfo)?.Id
                   ?? VoxQueue.Desktop.Program.Settings.LastVoice;

        _voice.Items.Clear();
        _voice.Items.AddRange(_voices.Cast<object>());
        if (_voice.Items.Count > 0)
        {
            var match = _voices.FirstOrDefault(v => v.Id == keep);
            _voice.SelectedItem = match ?? _voice.Items[0];
        }

        foreach (var (name, combo) in _speakerRows)
        {
            var prev = (combo.SelectedItem as VoiceInfo)?.Id ?? keep;
            combo.Items.Clear();
            combo.Items.AddRange(_voices.Cast<object>());
            if (combo.Items.Count > 0)
                combo.SelectedItem = _voices.FirstOrDefault(v => v.Id == prev) ?? combo.Items[0];
        }
    }

    private string SelectedVoiceId => (_voice.SelectedItem as VoiceInfo)?.Id ?? "";

    private void RememberVoice()
    {
        var id = SelectedVoiceId;
        if (!string.IsNullOrEmpty(id))
        {
            VoxQueue.Desktop.Program.Settings.LastVoice = id;
            Config.Save(VoxQueue.Desktop.Program.Settings);
        }
    }

    // ------------------------------------------------------------------- files

    private async Task LoadFileAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = L.Tt("load_file"),
            Filter = "Text (*.txt;*.csv;*.srt;*.md)|*.txt;*.csv;*.srt;*.md|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        await Task.Yield();
        AppendFile(dlg.FileName);
    }

    /// <summary>Dropping appends rather than replaces: people drop a second file
    /// onto text they already prepared, and losing the paste would be rude.</summary>
    private void AppendFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var data = File.ReadAllText(path);
            var existing = _input.TextValue;
            _input.TextValue = existing.Trim().Length > 0
                ? existing + Environment.NewLine + data
                : data;
        }
        catch (Exception e)
        {
            _status.Text = e.Message;
        }
    }

    private async Task BrowseOutAsync()
    {
        using var dlg = new FolderBrowserDialog { SelectedPath = _outDir.Text };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _outDir.Text = dlg.SelectedPath;
            await Task.Yield();
        }
    }

    /// <summary>Opens %APPDATA%\VoxQueue — where config.json and the logs live.</summary>
    private void OpenSettingsFolder()
    {
        try
        {
            Directory.CreateDirectory(Program.LogDir);
            Process.Start(new ProcessStartInfo { FileName = Program.LogDir, UseShellExecute = true });
        }
        catch (Exception e) { _status.Text = e.Message; }
    }

    /// <summary>
    /// Turning the extension off only makes sense for WAV. For anything else the
    /// resulting file would be a nameless blob no player can identify, so the
    /// checkbox is disabled and forced back on.
    /// </summary>
    private void OnExtensionToggled()
    {
        var fmt = VoxQueue.Desktop.Program.Settings.AudioFormat ?? "wav";
        if (!string.Equals(fmt, "wav", StringComparison.OrdinalIgnoreCase) &&
            _chkExtension.Checked == false)
        {
            _chkExtension.Checked = true;
        }
        ApplyExtensionAvailability();
    }

    private void ApplyExtensionAvailability()
    {
        var fmt = VoxQueue.Desktop.Program.Settings.AudioFormat ?? "wav";
        var wavOnly = string.Equals(fmt, "wav", StringComparison.OrdinalIgnoreCase);
        _chkExtension.Enabled = wavOnly;
        if (!wavOnly) _chkExtension.Checked = true;
    }

    /// <summary>
    /// Discards the current work list and its saved state, and clears the input.
    /// Files already written stay on disk — this resets the run, it does not
    /// delete the user's render.
    /// </summary>
    private void ClearProject()
    {
        string tr(string k) => L.Tt(k);
        if (_runner is not null)
        {
            _status.Text = tr("queue_running");
            return;
        }

        if (_job is not null && _job.Items.Any(i => i.State == ItemState.Done))
        {
            var answer = MessageBox.Show(this,
                L.Hint("clear_project_warn"), L.Tt("app_title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
        }

        _player.Stop();

        try
        {
            var statePath = _job?.StatePath;
            if (!string.IsNullOrEmpty(statePath) && File.Exists(statePath))
                File.Delete(statePath);
        }
        catch { /* a locked state file is not worth failing the reset over */ }

        _job = null;
        _rows.RaiseListChangedEvents = false;
        _rows.Clear();
        _rows.RaiseListChangedEvents = true;
        _rows.ResetBindings();

        _input.TextValue = "";
        _speakerRows.Clear();
        _speakerBody.Controls.Clear();
        _speakerBody.Visible = false;

        _indexStart.Value = 0;
        _bar.Value = 0;
        _parsed.Text = "";
        _status.Text = L.Tt("status_ready");

        // Clearing is the one action that says "do not bring this back".
        SessionFile.Clear();
    }

    /// <summary>
    /// Saves the editing session: mode, text, parsed lines, options and voices.
    /// Results are not included — a project is the definition of a run, and the
    /// audio either still exists or is being redone.
    /// </summary>
    /// <summary>
    /// Snapshot of the current editing state, for the session slot and for
    /// "Save project". One builder, so the two cannot drift.
    /// </summary>
    /// <summary>
    /// True when the panel holds something a dropped project would replace.
    /// Mirrors the condition BuildWorkspace uses to decide it has anything worth
    /// saving, so "replace?" is only asked when there is something to lose.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool HasWork =>
        _job is not null && _job.Items.Count > 0 ||
        !string.IsNullOrWhiteSpace(_input.TextValue);

    /// <summary>Re-applies the colouring setting to the editor.</summary>
    public void ApplyColouringSetting() => _input.ApplyColouringSetting();

    public Workspace? BuildWorkspace()
    {
        if (_job is null && string.IsNullOrWhiteSpace(_input.TextValue)) return null;

        return new Workspace
        {
            Mode = _mode.ToString(),
            OutDir = _outDir.Text ?? "",
            Voice = SelectedVoiceId,
            InputText = _input.TextValue ?? "",
            Options = new JobOptions
            {
                Normalize = _chkNormalize.Checked,
                DetectDefects = _chkDefects.Checked,
                IndexStart = (int)_indexStart.Value,
                AudioFormat = VoxQueue.Desktop.Program.Settings.AudioFormat,
                SampleRate = VoxQueue.Desktop.Program.Settings.SampleRate,
                TrainTemplate = VoxQueue.Desktop.Program.Settings.TrainTemplate,
                SplitMarker = _marker.Text ?? "###",
                WriteTranscript = _chkTranscript.Checked,
                AddExtension = _chkExtension.Checked,
                MetadataWithSpoken = _chkMetaSpoken.Checked
            },
            VoiceMap = _speakerRows
                .Where(r => !string.IsNullOrEmpty(r.Name))
                .ToDictionary(r => r.Name, r => (r.Combo.SelectedItem as VoiceInfo)?.Id ?? ""),
            Items = _job?.Items.Select(i => new WorkspaceItem
            {
                Text = i.Text,
                Spoken = i.Spoken,
                Phonetic = i.Phonetic,
                Speaker = i.Speaker,
                Index = i.Index,
                Pinned = i.Pinned,
                SourceLine = i.SourceLine
            }).ToList() ?? new List<WorkspaceItem>()
        };
    }

    /// <summary>Restores a workspace into this panel. Returns false on a mode mismatch.</summary>
    public bool ApplyWorkspace(Workspace ws)
    {
        if (!string.Equals(ws.Mode, _mode.ToString(), StringComparison.OrdinalIgnoreCase))
            return false;

        _input.TextValue = ws.InputText;
        _outDir.Text = ws.OutDir;
        if (!string.IsNullOrEmpty(ws.Voice)) SelectVoice(ws.Voice);

        var o = ws.Options ?? new JobOptions();
        _chkNormalize.Checked = o.Normalize;
        _chkDefects.Checked = o.DetectDefects;
        _chkTranscript.Checked = o.WriteTranscript;
        _chkExtension.Checked = o.AddExtension;
        _chkMetaSpoken.Checked = o.MetadataWithSpoken;
        _indexStart.Value = Math.Clamp(o.IndexStart, 0, (int)_indexStart.Maximum);
        if (!string.IsNullOrWhiteSpace(o.SplitMarker)) _marker.Text = o.SplitMarker;
        ApplyExtensionAvailability();

        var job = new Job
        {
            Mode = _mode,
            OutDir = ws.OutDir,
            Voice = ws.Voice,
            Options = o,
            VoiceMap = ws.VoiceMap ?? new Dictionary<string, string>(),
            Items = ws.Items.Select(i => new ItemRow
            {
                Index = i.Index,
                Text = i.Text,
                Spoken = i.Spoken,
                Phonetic = i.Phonetic,
                Speaker = i.Speaker,
                Pinned = i.Pinned,
                SourceLine = i.SourceLine
            }).ToList()
        };

        _job = job;
        _rows.RaiseListChangedEvents = false;
        _rows.Clear();
        foreach (var it in job.Items) _rows.Add(it);
        _rows.RaiseListChangedEvents = true;
        _rows.ResetBindings();

        if (_mode == Mode.Dub) RebuildSpeakerRowsFrom(job);

        // Statuses live in the per-run state file, not in the session — the
        // session carries the definition of the work, the state file carries
        // what happened to it. Without this the list came back with every row
        // pending, and the user had to press Parse to see which clips existed.
        var restored = Job.RestoreProgress(job);

        _bar.Maximum = Math.Max(1, job.Items.Count);
        _bar.Value = 0;
        _parsed.Text = $"{L.Tt("items_found")}: {job.Items.Count}"
                       + (restored > 0 ? $"   ·   {L.Tt("resumed")}: {restored}" : "");
        ApplySort();
        return true;
    }

    private void SaveProject()
    {
        using var dlg = new SaveFileDialog
        {
            Title = L.Tt("save_project"),
            Filter = WorkspaceFile.Filter,
            DefaultExt = WorkspaceFile.Extension,
            AddExtension = true,
            FileName = string.IsNullOrEmpty(_projectPath)
                ? "project." + WorkspaceFile.Extension
                : Path.GetFileName(_projectPath),
            InitialDirectory = string.IsNullOrEmpty(_projectPath)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Path.GetDirectoryName(_projectPath) ?? ""
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var ws = new Workspace
        {
            Mode = _mode.ToString(),
            OutDir = _outDir.Text ?? "",
            Voice = SelectedVoiceId,
            InputText = _input.TextValue ?? "",
            Options = new JobOptions
            {
                Normalize = _chkNormalize.Checked,
                DetectDefects = _chkDefects.Checked,
                IndexStart = (int)_indexStart.Value,
                AudioFormat = Program.Settings.AudioFormat,
                SampleRate = Program.Settings.SampleRate,
                TrainTemplate = Program.Settings.TrainTemplate,
                SplitMarker = _marker.Text ?? "###",
                WriteTranscript = _chkTranscript.Checked,
                AddExtension = _chkExtension.Checked,
                MetadataWithSpoken = _chkMetaSpoken.Checked
            },
            VoiceMap = _speakerRows
                .Where(r => !string.IsNullOrEmpty(r.Name))
                .ToDictionary(r => r.Name, r => (r.Combo.SelectedItem as VoiceInfo)?.Id ?? ""),
            Items = _job?.Items.Select(i => new WorkspaceItem
            {
                Text = i.Text,
                Spoken = i.Spoken,
                Phonetic = i.Phonetic,
                Speaker = i.Speaker,
                Index = i.Index,
                Pinned = i.Pinned,
                SourceLine = i.SourceLine
            }).ToList() ?? new List<WorkspaceItem>()
        };

        try
        {
            WorkspaceFile.Save(dlg.FileName, ws);
            _projectPath = dlg.FileName;
            _status.Text = L.Tt("saved") + ": " + Path.GetFileName(dlg.FileName);
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, L.Tt("app_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadProject()
    {
        using var dlg = new OpenFileDialog
        {
            Title = L.Tt("load_project"),
            Filter = WorkspaceFile.Filter,
            DefaultExt = WorkspaceFile.Extension,
            CheckFileExists = true
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var ws = WorkspaceFile.Load(dlg.FileName, out var error);
        if (ws is null)
        {
            MessageBox.Show(this, error, L.Tt("app_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // A project carries its own mode; a panel of the wrong mode would show
        // the list but write it with the wrong filenames.
        if (!string.Equals(ws.Mode, _mode.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            // The text lives in Hints, not the key table — asking Tt returned the
            // key name itself on screen.
            _status.Text = L.Hint("project_mode_mismatch") + " (" + ws.Mode + ")";
            MessageBox.Show(this, L.Hint("project_mode_mismatch") + Environment.NewLine + ws.Mode,
                L.Tt("app_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _input.TextValue = ws.InputText;
        _outDir.Text = ws.OutDir;
        if (!string.IsNullOrEmpty(ws.Voice)) SelectVoice(ws.Voice);

        _chkNormalize.Checked = ws.Options.Normalize;
        _chkDefects.Checked = ws.Options.DetectDefects;
        _chkTranscript.Checked = ws.Options.WriteTranscript;
        _chkExtension.Checked = ws.Options.AddExtension;
        _indexStart.Value = Math.Clamp(ws.Options.IndexStart, 0, (int)_indexStart.Maximum);
        if (!string.IsNullOrWhiteSpace(ws.Options.SplitMarker)) _marker.Text = ws.Options.SplitMarker;
        ApplyExtensionAvailability();

        var job = new Job
        {
            Mode = _mode,
            OutDir = ws.OutDir,
            Voice = ws.Voice,
            Options = ws.Options,
            VoiceMap = ws.VoiceMap ?? new Dictionary<string, string>(),
            Items = ws.Items.Select(i => new ItemRow
            {
                Index = i.Index,
                Text = i.Text,
                Spoken = i.Spoken,
                Phonetic = i.Phonetic,
                Speaker = i.Speaker,
                Pinned = i.Pinned,
                SourceLine = i.SourceLine
            }).ToList()
        };

        _job = job;

        // Pick up results from an earlier run into the same folder, so an
        // interrupted night job is resumed instead of regenerated from zero.
        var restored = Job.RestoreProgress(job);

        _rows.RaiseListChangedEvents = false;
        _rows.Clear();
        foreach (var it in job.Items) _rows.Add(it);
        _rows.RaiseListChangedEvents = true;
        _rows.ResetBindings();

        if (_mode == Mode.Dub) RebuildSpeakerRowsFrom(job);
        else
        {
            _speakerRows.Clear();
            _speakerBody.Controls.Clear();
            _speakerBody.Visible = false;
        }

        _bar.Maximum = Math.Max(1, job.Items.Count);
        _bar.Value = 0;
        _parsed.Text = $"{L.Tt("items_found")}: {job.Items.Count}"
                       + (restored > 0 ? $"   ·   {L.Tt("resumed")}: {restored}" : "");

        _projectPath = dlg.FileName;
        _status.Text = L.Tt("loaded") + ": " + Path.GetFileName(dlg.FileName);
        ApplyTheme();
    }

    private void SelectVoice(string id)
    {
        for (var i = 0; i < _voice.Items.Count; i++)
        {
            if (_voice.Items[i] is VoiceInfo v && v.Id == id)
            {
                _voice.SelectedIndex = i;
                return;
            }
        }
    }

    /// <summary>Rebuilds the speaker pickers from a loaded project, keeping its voice map.</summary>
    private void RebuildSpeakerRowsFrom(Job job)
    {
        _speakerRows.Clear();
        _speakerBody.Controls.Clear();

        var names = job.Items.Select(i => i.Speaker)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .ToList();
        if (names.Count == 0) { _speakerBody.Visible = false; return; }

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };

        foreach (var name in names)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                BackColor = Theme.Surface,
                Margin = new Padding(0, 2, 0, 2)
            };
            var lb = new Label
            {
                Text = name,
                Width = 150,
                AutoSize = false,
                ForeColor = Theme.Text,
                Margin = new Padding(0, 8, 8, 0)
            };
            lb.Font = Theme.Body(9f);

            var combo = new Dropdown { Width = 240 };
            combo.Items.AddRange(_voices.Cast<object>());
            var want = job.VoiceMap.TryGetValue(name, out var v) ? v : job.Voice;
            combo.SelectedItem = _voices.FirstOrDefault(x => x.Id == want)
                              ?? (combo.Items.Count > 0 ? combo.Items[0] : null);

            row.Controls.Add(lb);
            row.Controls.Add(combo);
            flow.Controls.Add(row);
            _speakerRows.Add((name, combo));
        }

        _speakerBody.Controls.Add(flow);
        _speakerBody.Visible = true;
    }

    private void OpenFolder()
    {
        var dir = _outDir.Text;
        if (string.IsNullOrWhiteSpace(dir)) return;
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception e) { _status.Text = e.Message; }
    }

    // ------------------------------------------------------------------- parse

    /// <summary>
    /// Splits the text and builds the work list.
    ///
    /// The split itself runs on a worker thread: a ten-thousand-line script took
    /// seconds during which the window was frozen — no repaint, no scroll, and
    /// the OS drew a white rectangle over it. The grid is still populated on the
    /// UI thread, because WinForms controls are not thread-safe, but that part is
    /// bounded by the item count rather than by parsing.
    /// </summary>
    public async Task<bool> ParseAsync(bool askFirst = true)
    {
        var raw = _input.TextValue ?? "";
        if (raw.Trim().Length == 0)
        {
            _status.Text = L.Tt("no_text");
            return false;
        }

        // Re-parsing throws away per-row state: what was rendered, what was
        // pinned, what was edited. That is fine when the text changed — it is what
        // the user asked for — but it is silent data loss when the list and the
        // text still describe the same lines, which happens on a second click.
        if (askFirst && _job is not null && _job.Items.Count > 0
            && !await ConfirmReparseAsync(raw))
            return false;

        var marker = _marker.Text;
        var mode = _mode;

        _btnParse.Enabled = false;
        _status.Text = L.Tt("parsing");

        List<ParsedLine> lines;
        try
        {
            lines = await Task.Run(() => mode switch
            {
                Mode.Train => TextSplitter.SplitLines(raw),
                Mode.Bulk => TextSplitter.SplitBlocks(raw, marker),
                _ => TextSplitter.SplitDub(raw)
            });
        }
        catch (Exception e)
        {
            _status.Text = $"{e.GetType().Name}: {e.Message}";
            _btnParse.Enabled = true;
            return false;
        }
        finally
        {
            _btnParse.Enabled = true;
        }

        if (lines.Count == 0)
        {
            _status.Text = L.Hint("no_items");
            return false;
        }

        for (var i = 0; i < lines.Count; i++) lines[i].Index = i;

        var s = VoxQueue.Desktop.Program.Settings;
        var job = new Job
        {
            Mode = _mode,
            OutDir = string.IsNullOrWhiteSpace(_outDir.Text) ? s.OutputDir : _outDir.Text,
            Voice = SelectedVoiceId,
            Options = new JobOptions
            {
                Normalize = _chkNormalize.Checked,
                DetectDefects = _chkDefects.Checked,
                IndexStart = (int)_indexStart.Value,
                AudioFormat = s.AudioFormat,
                SampleRate = s.SampleRate,
                TrainTemplate = s.TrainTemplate,
                SplitMarker = _marker.Text,
                WriteTranscript = _chkTranscript.Checked,
                AddExtension = _chkExtension.Checked,
                MetadataWithSpoken = _chkMetaSpoken.Checked
            },
            Items = lines.Select(l =>
            {
                // {spoken|written} is resolved once, here: the grid shows the
                // written form, the engine gets the phonetic one, and neither
                // has to look at a marker again.
                var (spoken, written) = Pronunciation.Split(l.Text);
                return new ItemRow
                {
                    Index = l.Index,
                    Text = written,
                    Spoken = spoken,
                    // Only the substituted words, for the Pronunciation column —
                    // the full spoken sentence duplicates the Text column.
                    Phonetic = Pronunciation.Summary(l.Text),
                    SourceLine = l.Raw,
                    Speaker = l.Speaker
                };
            }).ToList()
        };

        if (_mode == Mode.Dub) BuildSpeakerMap(lines, job.Voice);

        _job = job;

        // Pick up results from an earlier run into the same folder, so an
        // interrupted night job resumes instead of regenerating from zero.
        var restored = Job.RestoreProgress(job);

        _rows.RaiseListChangedEvents = false;
        _rows.Clear();
        foreach (var it in job.Items) _rows.Add(it);
        _rows.RaiseListChangedEvents = true;
        _rows.ResetBindings();

        ApplySort();

        _parsed.Text = $"{L.Tt("items_found")}: {lines.Count}"
                       + (restored > 0 ? $"   ·   {L.Tt("resumed")}: {restored}" : "");
        _bar.Maximum = Math.Max(1, lines.Count);
        _bar.Value = 0;
        _status.Text = L.Tt("status_ready");
        job.Save();

        AppLog.Info($"parsed {job.Items.Count} lines ({_mode})"
                    + (restored > 0 ? $", resumed {restored}" : ""));
        return true;
    }

    private void BuildSpeakerMap(List<ParsedLine> lines, string defaultVoice)
    {
        _speakerRows.Clear();
        _speakerBody.Controls.Clear();

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Dock = DockStyle.Top,
            BackColor = Theme.Surface
        };

        var names = TextSplitter.Speakers(lines);
        foreach (var name in names)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                BackColor = Theme.Surface,
                Margin = new Padding(0, 2, 0, 2)
            };
            var lb = new Label
            {
                Text = name,
                Width = 160,
                AutoSize = false,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 8, 8, 0)
            };
            lb.Font = Theme.Body(9f);
            var combo = new Dropdown { Width = 260 };
            combo.Items.AddRange(_voices.Cast<object>());
            if (combo.Items.Count > 0)
                combo.SelectedItem = _voices.FirstOrDefault(v => v.Id == defaultVoice) ?? combo.Items[0];

            row.Controls.Add(lb);
            row.Controls.Add(combo);
            flow.Controls.Add(row);
            _speakerRows.Add((name, combo));
        }

        var autoBtn = new FlatButton
        {
            Text = L.Tt("voice_map"),
            Margin = new Padding(0, 6, 0, 0)
        };
        autoBtn.Click += (_, _) => AutoAssign();
        flow.Controls.Add(autoBtn);

        _speakerBody.Controls.Add(flow);
    }


    private void AutoAssign()
    {
        if (_voice.SelectedItem is not VoiceInfo voice) return;
        foreach (var (_, combo) in _speakerRows) combo.SelectedItem = voice;
    }

    private void ToggleSpeakerBody()
    {
        _speakerBody.Visible = !_speakerBody.Visible;
    }

    // ------------------------------------------------------------------- queue

    private async Task StartAsync(HashSet<int>? only = null)
    {
        // A synchronous guard, not `_runner is not null`.
        //
        // _runner is only assigned after `await ParseAsync()` further down, so two
        // quick clicks both passed the old check and started two runners against
        // the same files: the second overwrote the field, the first kept writing,
        // and both appended to metadata.csv. A bool set before the first await
        // cannot be raced.
        if (_starting) return;
        _starting = true;
        _runErrored = false;

        try
        {
            await StartCoreAsync(only);
        }
        finally
        {
            _starting = false;
        }
    }

    private async Task StartCoreAsync(HashSet<int>? only)
    {
        // Stop playback first. The player holds the clip open while it plays —
        // and on Windows even a loaded SoundPlayer can keep a handle — so
        // regenerating the item that is currently playing failed with
        // UnauthorizedAccessException until some other clip was played.
        _player.Stop();

        if (_runner is not null) return;
        if (_job is null || _job.Items.Count == 0)
            if (!await ParseAsync()) return;

        if (_mode != Mode.Dub && string.IsNullOrEmpty(SelectedVoiceId))
        {
            _status.Text = L.Tt("no_voice");
            return;
        }

        var job = _job!;
        if (!string.IsNullOrWhiteSpace(_outDir.Text)) job.OutDir = _outDir.Text;
        job.Voice = SelectedVoiceId;
        job.Options.Normalize = _chkNormalize.Checked;
        job.Options.DetectDefects = _chkDefects.Checked;
        job.Options.IndexStart = (int)_indexStart.Value;
        job.Options.AddExtension = _chkExtension.Checked;
        job.Options.MetadataWithSpoken = _chkMetaSpoken.Checked;

        if (_mode == Mode.Dub)
        {
            job.VoiceMap = new Dictionary<string, string>();
            foreach (var (name, combo) in _speakerRows)
            {
                var id = (combo.SelectedItem as VoiceInfo)?.Id ?? "";
                job.VoiceMap[name] = string.IsNullOrEmpty(id) ? job.Voice : id;
            }
        }

        // Validated before anything is created: an empty output folder made every
        // path relative to the working directory, and an invalid one threw from
        // CreateDirectory with a message that named neither the field nor the fix.
        if (string.IsNullOrWhiteSpace(job.OutDir))
        {
            _status.Text = L.Tt("no_output_dir");
            return;
        }

        try
        {
            Directory.CreateDirectory(job.OutDir);
        }
        catch (Exception e)
        {
            _status.Text = L.Tt("bad_output_dir") + ": " + e.Message;
            AppLog.Error("cannot create output folder: " + job.OutDir, e);
            return;
        }

        job.Save();

        var s = VoxQueue.Desktop.Program.Settings;
        var client = new TtsClient(s.BaseUrl, s.ApiKey);
        _runner = new JobRunner(client, job) { OnlyIndices = only };

        _runner.ItemStarted += (_, item) => Ui(() =>
        {
            item.State = ItemState.Running;
            _status.Text = $"{L.Tt("status_running")}: {item.Index}";
            ScrollToActive();
        });

        _runner.ItemFinished += (_, _) => Ui(() =>
        {
            _runDone++;
            _bar.Value = _runDone;
            UpdateClock();
            ScrollToActive();
        });
        _runner.ItemFailed += (_, _) => Ui(() =>
        {
            _runDone++;
            _bar.Value = _runDone;
            UpdateClock();
            ScrollToActive();
        });

        _runner.ProgressChanged += (_, p) => Ui(() =>
        {
            // Both the bar and the numbers describe THIS run. p.Done/p.Total are
            // lifetime figures (9999 of 10000 on a restored session), which read as
            // "nothing is happening" while a handful of clips render.
            _status.Text = $"{L.Tt("progress")}: {_runDone}/{Math.Max(1, _runTotal)}"
                           + $"   ·   {L.Tt("done")}: {p.Done}"
                           + (p.Review > 0 ? $"   ·   {L.Tt("review")}: {p.Review}" : "");
        });

        _runner.Finished += (_, cancelled) => Ui(() =>
        {
            var meta = _runner?.WriteMetadata();
            _clockTimer.Stop();
            SetRunningUi(false);
            _btnPause.Text = L.Tt("pause");

            if (cancelled)
            {
                _status.Text = L.Tt("cancel");
            }
            else
            {
                _status.Text = $"{L.Tt("done")}: {job.DoneCount}   ·   {L.Tt("failed")}: {job.FailedCount}"
                               + (job.ReviewCount > 0 ? $"   ·   {L.Tt("defects_found")}: {job.ReviewCount}" : "")
                               + (meta is not null ? $"   ·   {Path.GetFileName(meta)}" : "");
            }
            _runner = null;
            client.Dispose();

            // Re-sort once the run is over: sorting after every item would make
            // rows jump under the cursor mid-run.
            ApplySort();

            // Shutdown is decided here, after the run is over and the state file
            // is written - never from inside the runner, which cannot tell
            // "finished" from "succeeded". An error counts as "not succeeded".
            if (!cancelled && !_runErrored) MaybeShutdown(job);
        });

        // Scope the bar to what this run will actually do.
        _runTotal = job.Items.Count(i =>
            (only is null || only.Contains(i.Index)) && i.State != ItemState.Done);
        _runDone = 0;
        _runStarted = DateTime.Now;
        _bar.Maximum = Math.Max(1, _runTotal);
        _bar.Value = 0;
        UpdateClock();
        _clockTimer.Start();

        SetRunningUi(true);
        _status.Text = L.Tt("status_running");

        try
        {
            await Task.Run(() => _runner.RunAsync());
        }
        catch (Exception e)
        {
            // RunAsync has its own try/finally so Finished always fires, but a
            // failure before the loop (a bad output path, a missing folder) throws
            // before that. Without this the exception surfaced as an unhandled
            // error dialog with the runner still marked busy and the buttons still
            // disabled — the app looked frozen.
            AppLog.Error("run failed", e);
            _status.Text = $"{e.GetType().Name}: {e.Message}";

            // An exception here means the run did not end normally. Shutting the
            // machine down because a run crashed is the opposite of what the option
            // is for, so the Finished handler is told not to.
            _runErrored = true;

            _runner = null;
            SetRunningUi(false);
            _btnPause.Text = L.Tt("pause");

            // The Finished handler normally disposes this; when it never ran, the
            // HttpClient behind it leaked for the life of the process.
            client.Dispose();
        }
    }

    /// <summary>Runs UI work on the thread that owns the controls.</summary>
    private void Ui(Action action)
    {
        // Both checks matter. IsDisposed alone is not enough: a control can be
        // alive but have no handle yet (or have just lost it during teardown), and
        // BeginInvoke throws InvalidOperationException in that state — the same
        // exception that crashed start-up when it was called from the constructor.
        if (IsDisposed || Disposing) return;

        try
        {
            if (!IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }
        // ObjectDisposedException derives from InvalidOperationException, so one
        // clause covers both the "no handle" and "already disposed" cases.
        catch (InvalidOperationException) { /* the handle went away mid-call */ }
    }

    /// <summary>
    /// Powers the machine off when the option is ticked and the run actually
    /// produced something.
    ///
    /// Three guards, all deliberate:
    ///   * cancelled runs never shut down — the user is still there;
    ///   * a run with zero successes never shuts down — that is a broken server,
    ///     not a finished job, and the machine should stay awake for the fix;
    ///   * the shutdown is scheduled with a delay, so it can be cancelled.
    /// </summary>
    private void MaybeShutdown(Job job)
    {
        if (_chkShutdown.Checked == false) return;

        if (job.DoneCount == 0)
        {
            _status.Text = L.Tt("shutdown_skipped");
            return;
        }

        // Save once more before the machine goes down. The runner already saves
        // after every item, but an explicit final write costs nothing and means
        // the state on disk cannot be stale by even one item.
        job.Save();

        // A visible countdown, not a silent `shutdown /t`. The user asked for a
        // message and a chance to interrupt; a scheduled shutdown gives neither.
        using var prompt = new ShutdownPrompt(
            60,
            L.Tt("shutdown_when_done"),
            $"{L.Tt("done")}: {job.DoneCount}   ·   {L.Tt("failed")}: {job.FailedCount}\n"
            + L.Hint("shutdown_hint"));

        if (prompt.ShowDialog(this) != DialogResult.OK)
        {
            // Cancelled: nothing was scheduled, so there is nothing to cancel.
            _status.Text = L.Tt("shutdown_cancelled");
            return;
        }

        var (ok, message) = PowerAction.ShutdownNow();
        _status.Text = ok
            ? L.Tt("shutdown_scheduled") + " — " + message
            : L.Tt("shutdown_failed") + ": " + message;
    }

    /// <summary>
    /// Brings the row being worked on into view.
    ///
    /// Follows the ACTIVE row rather than always the last one: with
    /// "generate selected" the queue may be working in the middle of the list,
    /// and jumping to the end would hide exactly the row the user is watching.
    /// Falls back to the last row when nothing is running.
    /// </summary>
    /// <summary>
    /// Reorders the work list so the rows that need attention come first.
    ///
    /// A thousand finished clips bury the dozen that failed, which is the whole
    /// reason this option exists. Sorting is stable within each bucket by the
    /// stored index, so the original order is recoverable by unticking.
    ///
    /// The selection is preserved: reordering while a row is selected would
    /// otherwise silently move the selection to a different clip, and the next
    /// "regenerate selected" would hit the wrong item.
    /// </summary>
    private void ApplySort()
    {
        // Capture what the user is looking at BEFORE the collection is rebuilt.
        // Restoring only the selection was not enough: CurrentRow goes back to
        // the first row on ResetBindings, so the view scrolled to the top and the
        // user lost their place after every re-sort.
        var currentItem = _grid.CurrentRow?.DataBoundItem as ItemRow;
        var topItem = _grid.FirstDisplayedScrollingRowIndex >= 0 &&
                      _grid.FirstDisplayedScrollingRowIndex < _grid.Rows.Count
            ? _grid.Rows[_grid.FirstDisplayedScrollingRowIndex].DataBoundItem as ItemRow
            : null;

        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as ItemRow)
            .Where(i => i is not null)
            .ToHashSet();

        // JobView reads the job, never the displayed rows. That distinction is the
        // whole bug this replaced: filtering _rows and refilling _rows from the
        // filtered result deleted the hidden rows from the source, so unticking
        // the filter brought nothing back.
        var ordered = JobView.Build(_job, _chkPinnedOnly.Checked,
                                    Math.Clamp(_cmbSort.SelectedIndex, 0, 3),
                                    _search.Text, _chkStaleOnly.Checked);

        _rows.RaiseListChangedEvents = false;
        _rows.Clear();
        foreach (var it in ordered) _rows.Add(it);
        _rows.RaiseListChangedEvents = true;
        _rows.ResetBindings();

        try
        {
            // Scroll position first: setting it afterwards would be overridden by
            // moving the current cell into view.
            var topRow = RowOf(topItem);
            if (topRow >= 0) _grid.FirstDisplayedScrollingRowIndex = topRow;

            if (selected.Count > 0)
                foreach (DataGridViewRow row in _grid.Rows)
                    if (row.DataBoundItem is ItemRow item && selected.Contains(item))
                        row.Selected = true;

            var curRow = RowOf(currentItem);
            if (curRow >= 0)
            {
                _grid.CurrentCell = _grid.Rows[curRow].Cells[1];
                _grid.Rows[curRow].Selected = true;
            }
        }
        catch (Exception) { /* grid not laid out yet */ }
    }

    /// <summary>Visible row holding this item, or -1 when it was filtered out.</summary>
    private int RowOf(ItemRow? item)
    {
        if (item is null) return -1;
        for (var i = 0; i < _grid.Rows.Count; i++)
            if (ReferenceEquals(_grid.Rows[i].DataBoundItem, item)) return i;
        return -1;
    }

    /// <summary>Shows the pinned count on the filter, so an empty list is explained.</summary>
    private void UpdatePinnedFilterLabel()
    {
        var pinned = JobView.PinnedCount(_job);
        _chkPinnedOnly.Text = pinned > 0
            ? $"{L.Tt("pinned_only")} ({pinned})"
            : L.Tt("pinned_only");
    }

    /// <summary>
    /// Draws playback position on the shared bar: filled up to where the clip has
    /// got, and the clock reading "0:01 / 0:04".
    /// </summary>
    private void ShowPlayback(TimeSpan elapsed, TimeSpan total)
    {
        _showingPlayback = true;

        if (total <= TimeSpan.Zero)
        {
            // Length unknown: show the position without pretending to know the end.
            _bar.Maximum = 100;
            _bar.Value = 0;
            _lblTime.Text = Clock(elapsed);
            return;
        }

        // A fine range: clips are seconds long, and 100 steps would jump a whole
        // step every few frames.
        _bar.Maximum = 1000;
        _bar.Value = Math.Clamp(
            (int)Math.Round(1000 * elapsed.TotalSeconds / total.TotalSeconds), 0, 1000);

        _lblTime.Text = $"{Clock(elapsed)} / {Clock(total)}";
    }

    /// <summary>Hands the bar back to the queue once playback ends.</summary>
    private void RestoreQueueProgress()
    {
        if (!_showingPlayback) return;
        _showingPlayback = false;

        _bar.Maximum = Math.Max(1, _runTotal > 0 ? _runTotal : 1);
        _bar.Value = Math.Clamp(_runDone, 0, _bar.Maximum);
        UpdateClock();
    }

    /// <summary>
    /// Shows elapsed time for this run and an estimate of the whole.
    ///
    /// The estimate is extrapolated from what has actually happened — average wall
    /// time per finished item times the items still to do — not from an assumed
    /// speed. Before the first item completes there is no measurement, so only the
    /// elapsed time is shown rather than a guess.
    /// </summary>
    private void UpdateClock()
    {
        // While a clip plays, the clock belongs to the clip.
        if (_showingPlayback) return;

        if (_runDone <= 0 && _runStarted == default)
        {
            _lblTime.Text = "";
            return;
        }

        var elapsed = DateTime.Now - _runStarted;

        if (_runDone <= 0)
        {
            _lblTime.Text = Clock(elapsed);
            return;
        }

        var remaining = _runTotal - _runDone;
        var perItem = elapsed.TotalSeconds / _runDone;
        var total = TimeSpan.FromSeconds(perItem * _runTotal);

        _lblTime.Text = remaining > 0
            ? $"{Clock(elapsed)} / ~{Clock(total)}"
            : $"{Clock(elapsed)}   ·   {L.Tt("done").ToLowerInvariant()}";
    }

    /// <summary>mm:ss below an hour, h:mm:ss above it. Never days: a queue is hours.</summary>
    private static string Clock(TimeSpan t)
    {
        if (t.TotalSeconds < 0) t = TimeSpan.Zero;

        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    private void ScrollToActive()
    {
        if (_chkAutoScroll.Checked == false) return;
        if (_rows.Count == 0) return;

        try
        {
            var target = _rows
                .Select((item, i) => (item, i))
                .FirstOrDefault(t => t.item.State == ItemState.Running);

            var idx = target.item is not null ? target.i : _rows.Count - 1;

            if (idx >= 0 && idx < _grid.Rows.Count)
                _grid.FirstDisplayedScrollingRowIndex = idx;
        }
        catch (Exception)
        {
            // A grid that has not laid out yet throws on this property; the next
            // item event retries. Not worth surfacing.
        }
    }

    private void TogglePause()
    {
        if (_runner is null) return;
        if (_runner.IsPaused)
        {
            _runner.Resume();
            _btnPause.Text = L.Tt("pause");
            _status.Text = L.Tt("status_running");
        }
        else
        {
            _runner.Pause();
            _btnPause.Text = L.Tt("resume");
            _status.Text = L.Tt("status_ready");
        }
    }

    /// <summary>
    /// Renders only the selected rows.
    ///
    /// The whole point of a dataset run is that it is long, so a single clip that
    /// came out wrong should not cost a full pass. The job stays the same object
    /// — OnlyIndices narrows what the runner touches, so numbering, filenames and
    /// state stay consistent with a normal run.
    /// </summary>
    private async Task GenerateSelectedAsync()
    {
        if (_job is null || _job.Items.Count == 0) return;

        var indices = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as ItemRow)
            .Where(i => i is not null)
            .Select(i => i!.Index)
            .ToHashSet();

        if (indices.Count == 0)
        {
            _status.Text = L.Tt("select_rows_first");
            return;
        }

        foreach (var it in _job.Items.Where(i => indices.Contains(i.Index)))
        {
            it.State = ItemState.Pending;
            it.Defects = new List<string>();
            it.Error = "";
        }

        await StartAsync(indices);
    }

    /// <summary>
    /// Re-queues everything that is not up to date — failed rows and rows whose line
    /// was edited after it was rendered.
    ///
    /// Both live in the same button because from the user's side they are one thing:
    /// "this clip needs making again". Resetting only Failed skipped exactly the rows
    /// that had just been marked stale by a pronunciation edit.
    /// </summary>
    private async Task RetryFailedAsync()
    {
        if (_job is null) return;

        var count = _job.ResetRetryable();

        if (count == 0)
        {
            _status.Text = L.Tt("nothing_to_retry");
            return;
        }

        _job.Save();
        _rows.ResetBindings();
        ApplySort();
        AppLog.Info($"retry: {count} row(s) re-queued");

        await StartAsync();
    }

    private async Task RegenSelectedAsync()
    {
        if (_job is null) return;
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as ItemRow)
            .Where(i => i is not null)
            .Cast<ItemRow>()
            .ToList();
        if (selected.Count == 0) return;

        foreach (var it in selected)
        {
            it.State = ItemState.Pending;
            it.Defects = new List<string>();
            it.Error = "";
        }
        _job.Save();
        _rows.ResetBindings();
        await StartAsync();
    }

    private void ShowRowMenu(Point at)
    {
        var item = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as ItemRow)
            .FirstOrDefault(i => i is not null);
        if (item is null) return;

        var menu = new ContextMenuStrip { ShowImageMargin = false, Font = Theme.Body(9f) };

        // Every selected row pins, not just the one under the cursor: a user who
        // selected twenty clips expects all twenty marked.
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as ItemRow)
            .Where(i => i is not null)
            .Cast<ItemRow>()
            .ToList();
        if (selected.Count == 0) selected.Add(item);

        var allPinned = selected.All(i => i.Pinned);
        menu.Items.Add(allPinned ? L.Tt("unpin") : L.Tt("pin"), null, (_, _) =>
        {
            foreach (var it in selected) it.Pinned = !allPinned;
            _rows.ResetBindings();
            ApplySort();
            UpdatePinnedFilterLabel();
        });

        menu.Items.Add(L.Tt("play"), null, (_, _) => PlaySelected());
        menu.Items.Add(L.Tt("edit_pronunciation"), null, (_, _) => EditPronunciation(item));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.Tt("regenerate"), null, async (_, _) =>
        {
            foreach (var it in selected)
            {
                it.State = ItemState.Pending;
                it.Defects = new List<string>();
                it.Error = "";
            }
            await StartAsync(selected.Select(i => i.Index).ToHashSet());
        });

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.Tt("jump_to_index"), null, (_, _) => JumpToIndex());
        menu.Items.Add(L.Tt("copy_index"), null, (_, _) =>
        {
            try { Clipboard.SetText(string.Join(",", selected.Select(i => i.Index))); }
            catch { /* clipboard busy */ }
        });

        menu.Show(_grid, at);
    }

    /// <summary>
    /// Jumps to a row by its number.
    ///
    /// Numbers are matched against the item's own Index, not the row position:
    /// with a sort applied the two differ, and the user types the number they
    /// saw in metadata.csv or in the grid.
    /// </summary>
    private void JumpToIndex()
    {
        var input = IndexPrompt.Ask(this, L.Tt("jump_to_index"));
        if (input is null) return;

        var match = _rows.Select((it, row) => (it, row))
            .FirstOrDefault(t => t.it.Index == input.Value);
        if (match.it is null)
        {
            _status.Text = L.Tt("index_not_found") + ": " + input.Value;
            return;
        }

        try
        {
            _grid.ClearSelection();
            _grid.FirstDisplayedScrollingRowIndex = match.row;
            _grid.Rows[match.row].Selected = true;
            _grid.CurrentCell = _grid.Rows[match.row].Cells[1];
        }
        catch { /* grid not laid out */ }
        _status.Text = L.Tt("index_found") + ": " + input.Value;
    }

    /// <summary>
    /// Edits the pronunciation of one line and rewrites the source text.
    ///
    /// Writing the change into the source box is the whole point: the parsed
    /// item is rebuilt from that box on the next Parse, so an edit that only
    /// touched the item would silently vanish. The line is located by its raw
    /// text — the same string that was parsed — and only the first still-matching
    /// occurrence is replaced, so a line that appears twice is not overwritten
    /// in one go.
    /// </summary>
    /// <summary>
    /// Replaces a phrase everywhere and marks the affected clips as out of date.
    ///
    /// The list is rebuilt by re-parsing, because a replacement changes how lines
    /// split as well as what they say. Rows that were already rendered and whose
    /// text changed are then marked Stale — the clip on disk was made from the old
    /// line, and leaving "OK" would claim the audio matches the text.
    /// </summary>
    private async Task BulkPronunciationAsync()
    {
        var source = _input.TextValue ?? "";
        if (source.Trim().Length == 0)
        {
            _status.Text = L.Tt("no_text");
            return;
        }

        // The dialog counts matches against the live text, so it needs the text.
        using var dlg = new BulkPronunciationDialog(source, "");
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        // Snapshot what was rendered and from which line, before the re-parse.
        // Full snapshot, not just the text: a re-parse rebuilds every row from
        // scratch, so without restoring the unchanged ones a replace of ten lines
        // would silently wipe the render status of the other ten thousand.
        var before = RegenTracker.Snapshot(_job);

        // TextValue is declared non-null but returns the box's text, which is
        // never null in practice — the compiler cannot see that, so pin it once.
        var text = _input.TextValue ?? "";

        var (replaced, count) = PronunciationEdit.ReplaceAll(
            text, dlg.Find, dlg.Spoken, dlg.Written,
            dlg.WholeWord, dlg.CaseSensitive);

        if (count == 0)
        {
            _status.Text = L.Tt("bulk_none") + ": " + dlg.Find;
            return;
        }

        _input.TextValue = replaced;

        // askFirst: false — the user asked for a replacement, not for a re-parse,
        // and a confirmation dialog quoting ten thousand reset clips is both
        // alarming and beside the point.
        await ParseAsync(askFirst: false);

        var marked = RegenTracker.Restore(_job, before);
        ApplySort();

        _status.Text = L.Tt("bulk_done") + ": " + count
                       + (marked > 0 ? $"   ·   {L.Tt("needs_regen")}: {marked}" : "");
        AppLog.Info($"bulk pronunciation: {count} replacement(s) of '{dlg.Find}'");
    }

    private void EditPronunciation(ItemRow item)
    {
        if (item is null) return;

        using var dlg = new PronunciationDialog(
            item.Text, item.SpokenOrText, (item.Index + 1).ToString());
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var text = _input.TextValue ?? "";
        var applied = PronunciationEdit.Apply(
            text, item.SourceLine, item.Text, dlg.Spoken, dlg.Written);

        if (applied is null)
        {
            _status.Text = L.Tt("source_line_missing");
            return;
        }

        // Replace ONE line, not the whole document. Setting TextValue rebuilt the
        // entire buffer and repainted every line, which is why the box appeared to
        // re-colour itself on every pronunciation edit.
        if (!_input.ReplaceLine(applied.Value.lineIndex, applied.Value.line))
        {
            _status.Text = L.Tt("source_line_missing");
            return;
        }

        item.SourceLine = applied.Value.line;

        // Reflect the edit in the row immediately. The grid is rebuilt from the
        // text on the next parse, but the user should see the change now.
        var wasRendered = item.State == ItemState.Done
                          || item.State == ItemState.Failed
                          || item.State == ItemState.Stale;

        item.Text = applied.Value.text;
        item.Spoken = applied.Value.spoken;
        item.Phonetic = applied.Value.phonetic;

        // The clip on disk was made from the old line, so it is out of date.
        // Marking it beats leaving "OK": the status would claim the audio matches
        // the text when it does not.
        if (wasRendered) item.State = ItemState.Stale;

        _rows.ResetBindings();
        ApplySort();
        _status.Text = wasRendered
            ? L.Tt("pronunciation_saved") + " · " + L.Tt("needs_regen")
            : L.Tt("pronunciation_saved");

        AppLog.Info($"pronunciation edited: line {item.Index}"
                    + (wasRendered ? " (marked stale)" : ""));
    }

    private void PlaySelected()
    {
        var item = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as ItemRow)
            .FirstOrDefault(i => i is not null);
        if (item?.File is null || !File.Exists(item.File)) return;

        // Built-in player, not the shell: a dataset file may have no extension
        // at all, and handing that to the OS handler gets "how do you want to
        // open this file?" instead of sound.
        _player.Play(item.File, _job?.Options.AudioFormat ?? "wav");
        _status.Text = L.Tt("playing") + ": " + Path.GetFileName(item.File);
    }

    public void Shutdown()
    {
        _runner?.Cancel();
        _player.Dispose();
    }

    /// <summary>True while items are still being generated — the window asks
    /// before closing so a half-rendered dataset is not thrown away by accident.</summary>
    public bool IsBusy => _runner is not null;

    /// <summary>
    /// While a run is in progress only Pause and Cancel stay live. Everything
    /// that could change the list, the options or the output folder is disabled:
    /// an edit mid-run either applies to nothing (the job already captured its
    /// options) or corrupts the numbering the runner is working from.
    /// </summary>
    private void SetRunningUi(bool running)
    {
        _btnStart.Enabled = !running;
        _btnPause.Enabled = running;
        _btnCancel.Enabled = running;

        _btnRetry.Enabled = !running;
        _btnRegen.Enabled = !running;
        _btnOne.Enabled = !running;
        _btnOpen.Enabled = !running;
        _btnClearProject.Enabled = !running;

        _btnLoadProject.Enabled = !running;
        _btnSaveProject.Enabled = !running;

        // Input that the running job has already read from.
        _input.Inner.ReadOnly = running;
        _voice.Enabled = !running;
        _outDir.Enabled = !running;
        _chkNormalize.Enabled = !running;
        _chkDefects.Enabled = !running;
        _indexStart.Enabled = !running;
        _marker.Enabled = !running;
        _chkTranscript.Enabled = !running;
        _chkMetaSpoken.Enabled = !running;
        _chkShutdown.Enabled = !running;
        _btnBrowse.Enabled = !running;
        _btnLoad.Enabled = !running;
        _btnClear.Enabled = !running;
        _btnParse.Enabled = !running;

        // The extension checkbox also depends on the format; only restore it when
        // the run ends, otherwise this would re-enable it mid-run.
        if (running) _chkExtension.Enabled = false;
        else ApplyExtensionAvailability();
    }

    /// <summary>
    /// Re-reads settings that another tab can change — the format decides
    /// whether "add extension" is even offered.
    /// </summary>
    public void RefreshFormatDependentUi() => ApplyExtensionAvailability();

    public void PersistSettings()
    {
        var s = VoxQueue.Desktop.Program.Settings;
        if (!string.IsNullOrWhiteSpace(_outDir.Text)) s.OutputDir = _outDir.Text;
        if (!string.IsNullOrWhiteSpace(_marker.Text)) s.SplitMarker = _marker.Text;
        s.Normalize = _chkNormalize.Checked;
        s.DetectDefects = _chkDefects.Checked;
        s.AutoScroll = _chkAutoScroll.Checked;
        s.SortMode = Math.Clamp(_cmbSort.SelectedIndex, 0, 2);

        // Only overwrite when a voice is actually selected. On an offline start
        // the list is empty, and writing the empty string here erased the
        // remembered voice — the next launch had nothing to restore.
        var voice = SelectedVoiceId;
        if (!string.IsNullOrEmpty(voice)) s.LastVoice = voice;

        Config.Save(s);
    }

    public void LoadSettings()
    {
        var s = VoxQueue.Desktop.Program.Settings;
        _outDir.Text = s.OutputDir;

        // Numbering always begins at 0000. IndexStart is not persisted on
        // purpose: a value nudged once by the wheel used to leak into the next
        // dataset, so a fresh text began at 0017 instead of 0000.
        _indexStart.Value = 0;
        _marker.Text = s.SplitMarker;
        _chkNormalize.Checked = s.Normalize;
        _chkDefects.Checked = s.DetectDefects;
        _chkTranscript.Checked = true;
        _chkExtension.Checked = s.AddExtension;
        _chkMetaSpoken.Checked = true;
        _chkAutoScroll.Checked = s.AutoScroll;
        _cmbSort.SelectedIndex = Math.Clamp(s.SortMode, 0, 2);
        ApplyExtensionAvailability();

        var showMarker = _mode == Mode.Bulk;
        _marker.Visible = showMarker;
        _chkTranscript.Visible = showMarker;
    }
}
