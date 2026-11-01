using System;
using System.Drawing;
using System.Windows.Forms;
using VoxQueue.Core.I18n;
using VoxQueue.Core.Services;

namespace VoxQueue.Desktop;

/// <summary>
/// Replaces every occurrence of a phrase with a pronunciation marker.
///
/// The three fields answer three separate questions, and the labels say so:
/// what to look for, how the engine should say it, and what the transcript should
/// keep. The previous captions ("Replace", "Engine says this", "Transcript keeps
/// this") described the mechanism rather than the task, which is why they read as
/// nonsense out of context.
///
/// "Count matches" exists because replacing across a thousand lines with no idea
/// whether the phrase appears three times or three hundred is a button nobody
/// dares press — so the count comes first and Replace stays disabled until it is
/// known to do something.
/// </summary>
public sealed class BulkPronunciationDialog : Form
{
    private readonly TextBox _find = new();
    private readonly TextBox _spoken = new();
    private readonly TextBox _written = new();
    private readonly CheckBox _wholeWord = new();
    private readonly CheckBox _caseSensitive = new();
    private readonly Label _result = new();
    private readonly FlatButton _btnReplace = new();
    private readonly string _sourceText;

    /// <summary>
    /// Recounts shortly after typing stops.
    ///
    /// The Count button answers the question explicitly, but requiring a click
    /// before Replace unlocks makes the dialog feel broken: you type a phrase and
    /// the action stays greyed out with no hint that another button is needed.
    /// Typing now recounts on its own and the button is still there to force it.
    /// </summary>
    private readonly System.Windows.Forms.Timer _recount = new();

    public string Find => _find.Text.Trim();
    public string Spoken => _spoken.Text.Trim();
    public string Written => _written.Text.Trim();
    public bool WholeWord => _wholeWord.Checked;
    public bool CaseSensitive => _caseSensitive.Checked;

    public BulkPronunciationDialog(string sourceText, string selection)
    {
        _sourceText = sourceText ?? "";

        Text = L.Tt("bulk_pron");
        AutoScaleMode = AutoScaleMode.None;

        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(640, 470);
        BackColor = Theme.Card;
        Font = Theme.Body(9f);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(20, 18, 20, 6),
            BackColor = Theme.Card
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        Label Caption(string key) => new()
        {
            Text = L.Tt(key),
            ForeColor = Theme.Dim,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        // One line of explanation under each field. Without them the three captions
        // are a riddle: "Written as" gives no clue whether it renames the clip, edits
        // the transcript, or does nothing at all when left alone.
        Label Note(string key) => new()
        {
            Text = L.Hint(key),
            ForeColor = Theme.Dim,
            Font = Theme.Body(8.5f),
            AutoSize = false,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Height = 30,
            Margin = new Padding(0, 0, 0, 8)
        };

        _find.Text = selection ?? "";
        foreach (var box in new[] { _find, _spoken, _written })
        {
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Theme.Input;
            box.ForeColor = Theme.Text;
            box.Font = Theme.Mono(10f);
            box.Dock = DockStyle.Fill;
        }

        // Spoken and written follow the search term until the user edits them, so
        // the common case is two keystrokes, not three fields.
        //
        // The `syncing` guard is essential: assigning .Text raises TextChanged just
        // like typing does, so without it the first mirrored character marked the
        // field as "user edited" and the mirroring stopped after one letter — typing
        // "i-de" left "i" in the other two boxes.
        var spokenTouched = false;
        var writtenTouched = false;
        var syncing = false;

        _spoken.TextChanged += (_, _) =>
        {
            if (!syncing) spokenTouched = true;
            InvalidateCount();
        };
        _written.TextChanged += (_, _) =>
        {
            if (!syncing) writtenTouched = true;
            InvalidateCount();
        };
        // "Written as" follows what the text actually records, not the search term.
        //
        // Searching the phonetic form "i-de" while every match sits inside
        // {i-de|ID} used to prefill i-de, so leaving the field alone appeared to say
        // "record i-de" — which is not what happened. Prefilling with ID makes the
        // field honest: what you see is what the transcript will hold.
        void SyncFromFind()
        {
            syncing = true;
            try
            {
                if (!spokenTouched) _spoken.Text = _find.Text;

                if (!writtenTouched)
                {
                    var recorded = PronunciationEdit.WrittenFormFor(
                        _sourceText, _find.Text, _wholeWord.Checked, _caseSensitive.Checked);

                    _written.Text = recorded ?? _find.Text;
                }
            }
            finally { syncing = false; }
        }

        _find.TextChanged += (_, _) =>
        {
            SyncFromFind();
            InvalidateCount();
        };

        _wholeWord.CheckedChanged += (_, _) =>
        {
            if (!writtenTouched) SyncFromFind();
        };
        _caseSensitive.CheckedChanged += (_, _) =>
        {
            if (!writtenTouched) SyncFromFind();
        };

        SyncFromFind();

        _wholeWord.Text = L.Tt("whole_word");
        _wholeWord.AutoSize = true;
        _wholeWord.Checked = true;
        _wholeWord.ForeColor = Theme.Text;
        _wholeWord.CheckedChanged += (_, _) => InvalidateCount();

        _caseSensitive.Text = L.Tt("case_sensitive");
        _caseSensitive.AutoSize = true;
        _caseSensitive.ForeColor = Theme.Text;
        _caseSensitive.CheckedChanged += (_, _) => InvalidateCount();

        var summary = new Label
        {
            Text = L.Hint("bulk_pron_hint"),
            ForeColor = Theme.Dim,
            Font = Theme.Body(8.5f),
            AutoSize = false,
            Height = 34,
            Dock = DockStyle.Fill
        };

        // Count and result live together: the number is the answer to the button
        // that produces it.
        var countRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Card,
            Margin = new Padding(0)
        };
        var btnCount = new FlatButton { Text = L.Tt("bulk_count") };
        btnCount.Margin = new Padding(0, 0, 12, 0);
        btnCount.Click += (_, _) => CountMatches();

        _result.AutoSize = true;
        _result.ForeColor = Theme.Dim;
        _result.Margin = new Padding(0, 9, 0, 0);

        countRow.Controls.Add(btnCount);
        countRow.Controls.Add(_result);

        grid.Controls.Add(Caption("bulk_find"), 0, 0);
        grid.Controls.Add(_find, 1, 0);
        grid.Controls.Add(Note("bulk_find_note"), 1, 1);

        grid.Controls.Add(Caption("bulk_spoken"), 0, 2);
        grid.Controls.Add(_spoken, 1, 2);
        grid.Controls.Add(Note("bulk_spoken_note"), 1, 3);

        grid.Controls.Add(Caption("bulk_written"), 0, 4);
        grid.Controls.Add(_written, 1, 4);
        grid.Controls.Add(Note("bulk_written_note"), 1, 5);

        grid.Controls.Add(_wholeWord, 1, 6);
        grid.Controls.Add(_caseSensitive, 1, 7);

        grid.Controls.Add(summary, 0, 8);
        grid.SetColumnSpan(summary, 2);

        grid.Controls.Add(countRow, 0, 9);
        grid.SetColumnSpan(countRow, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 12, 20, 12),
            BackColor = Theme.Card
        };

        _btnReplace.Text = L.Tt("bulk_replace");
        _btnReplace.Primary = true;
        _btnReplace.DialogResult = DialogResult.OK;

        var cancel = new FlatButton { Text = L.Tt("cancel"), DialogResult = DialogResult.Cancel };
        cancel.Margin = new Padding(8, 0, 0, 0);

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_btnReplace);

        AcceptButton = _btnReplace;
        CancelButton = cancel;

        // Nothing to replace yet, so the button says so by being unavailable.
        _btnReplace.Enabled = false;

        Controls.Add(grid);
        Controls.Add(buttons);

        _recount.Interval = 350;
        _recount.Tick += (_, _) =>
        {
            _recount.Stop();
            CountMatches();
        };

        // Count once up front so the dialog opens with an answer, not a question.
        Shown += (_, _) =>
        {
            CountMatches();
            _find.SelectAll();
            _find.Focus();
        };

        FormClosed += (_, _) => _recount.Dispose();
    }

    /// <summary>Counts without replacing, and enables Replace only when it will do something.</summary>
    private void CountMatches()
    {
        if (Find.Length == 0)
        {
            _result.Text = "";
            _btnReplace.Enabled = false;
            return;
        }

        var count = PronunciationEdit.CountMatches(_sourceText, Find, WholeWord, CaseSensitive);

        if (count == 0)
        {
            // Distinguish "this phrase is not here" from "it is here, but already
            // inside a marker". Both mean nothing to replace, but only the second
            // explains why the list visibly shows the phrase — which read as a bug.
            var inside = PronunciationEdit.CountInsideMarkers(
                _sourceText, Find, CaseSensitive);

            _result.Text = inside > 0
                ? string.Format(L.Tt("bulk_all_marked"), inside)
                : L.Tt("bulk_none_found");

            _result.ForeColor = inside > 0 ? Theme.Dim : Theme.Warn;
            _btnReplace.Enabled = false;
            return;
        }

        _result.Text = $"{count} {L.Tt("bulk_found_n")}";
        _result.ForeColor = Theme.Ok;
        _btnReplace.Enabled = true;
    }

    /// <summary>
    /// Clears a stale count when any input changes. Leaving the old number on
    /// screen next to new search terms is worse than showing nothing.
    /// </summary>
    private void InvalidateCount()
    {
        _result.Text = "";
        _result.ForeColor = Theme.Dim;
        _btnReplace.Enabled = false;

        _recount.Stop();
        _recount.Start();
    }
}
