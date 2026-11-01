using System;
using System.Drawing;
using System.Windows.Forms;
using VoxQueue.Core.I18n;

namespace VoxQueue.Desktop;

/// <summary>
/// Edits the pronunciation of one line.
///
/// Two fields, because the whole point of the feature is that they differ: the
/// spoken form is what the engine says, the written form is what the transcript
/// keeps. The written field is read-only until the checkbox is ticked, and the
/// checkbox now changes the field visibly — background and border, not only the
/// invisible ReadOnly flag, which looked like it did nothing.
///
/// Layout is a bottom-docked button bar rather than absolute positions, so a
/// longer translation cannot push the buttons outside the dialog.
/// </summary>
public sealed class PronunciationDialog : Form
{
    private readonly TextBox _spoken = new();
    private readonly TextBox _written = new();
    private readonly CheckBox _editWritten = new();

    public string Spoken => _spoken.Text.Trim();
    public string Written => _written.Text.Trim();

    public PronunciationDialog(string written, string spoken, string lineNumber)
    {
        Text = L.Tt("edit_pronunciation");

        // Scaling off: every coordinate below is set by hand, and font-based
        // auto-scaling moved the buttons off the dialog at 125 % DPI.
        AutoScaleMode = AutoScaleMode.None;

        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(540, 268);
        BackColor = Theme.Card;
        Font = Theme.Body(9f);

        var heading = new Label
        {
            Text = $"{L.Tt("dialog_line")} {lineNumber}",
            Font = Theme.Body(10f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(18, 16)
        };

        var spokenLabel = new Label
        {
            Text = L.Tt("dialog_engine_says"),
            ForeColor = Theme.Dim,
            AutoSize = true,
            Location = new Point(18, 46)
        };

        _spoken.Text = spoken;
        _spoken.Location = new Point(18, 66);
        _spoken.Width = ClientSize.Width - 36;
        _spoken.Font = Theme.Mono(10f);
        _spoken.BackColor = Theme.Input;
        _spoken.ForeColor = Theme.Text;
        _spoken.BorderStyle = BorderStyle.FixedSingle;

        _editWritten.Text = L.Tt("dialog_change_spelling");
        _editWritten.AutoSize = true;
        _editWritten.ForeColor = Theme.Text;
        _editWritten.BackColor = Theme.Card;
        _editWritten.Location = new Point(18, 104);
        _editWritten.CheckedChanged += (_, _) => SyncWrittenState();

        var writtenLabel = new Label
        {
            Name = "WrittenLabel",
            Text = L.Tt("dialog_transcript_keeps"),
            ForeColor = Theme.Dim,
            AutoSize = true,
            Location = new Point(18, 136)
        };

        _written.Text = written;
        _written.Location = new Point(18, 156);
        _written.Width = ClientSize.Width - 36;
        _written.Font = Theme.Mono(10f);
        _written.BorderStyle = BorderStyle.FixedSingle;

        // The hint explains why the field is locked: without it the field looks
        // broken rather than intentionally read-only.
        var hint = new Label
        {
            Name = "WrittenHint",
            Text = L.Tt("dialog_change_spelling"),
            ForeColor = Theme.Dim,
            AutoSize = true,
            Location = new Point(18, 184)
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 18, 10),
            BackColor = Theme.Card
        };

        var ok = new FlatButton
        {
            Text = L.Tt("ok"),
            Primary = true,
            DialogResult = DialogResult.OK
        };

        var cancel = new FlatButton
        {
            Text = L.Tt("cancel"),
            DialogResult = DialogResult.Cancel
        };

        // DialogResult on the button, plus AcceptButton/CancelButton, is the path
        // Windows actually honours. Setting DialogResult in a Click handler and
        // calling Close() looked equivalent but depended on the handler running —
        // when it did not, the dialog simply sat there.
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancel;

        ok.Click += (_, _) =>
        {
            if (Spoken.Length > 0) return;

            // Refuse to close on an empty spoken form rather than opening a
            // message box, which used to swallow the click and look like a freeze.
            DialogResult = DialogResult.None;
            _spoken.BackColor = Theme.RowFailBg;
            _spoken.Focus();
            hint.Text = L.Tt("dialog_spoken_empty");
            hint.ForeColor = Theme.Fail;
        };

        _spoken.TextChanged += (_, _) =>
        {
            if (_spoken.BackColor != Theme.Input)
            {
                _spoken.BackColor = Theme.Input;
                hint.Text = L.Tt("dialog_change_spelling");
                hint.ForeColor = Theme.Dim;
            }
        };

        Controls.AddRange(new Control[]
        {
            heading, spokenLabel, _spoken, _editWritten, writtenLabel, _written, hint, buttons
        });

        SyncWrittenState();
        ActiveControl = _spoken;
        _spoken.SelectAll();
    }

    /// <summary>
    /// Makes the checkbox's effect visible.
    ///
    /// ReadOnly alone changes nothing on screen, so ticking the box looked like a
    /// dead control. The field now gets an active background and a readable
    /// caption, and the hint stops telling the user it is locked.
    /// </summary>
    private void SyncWrittenState()
    {
        var editable = _editWritten.Checked;

        _written.ReadOnly = !editable;
        _written.BackColor = editable ? Theme.Input : Theme.Card;
        _written.ForeColor = editable ? Theme.Text : Theme.Dim;

        var hint = Controls["WrittenHint"] as Label;
        if (hint is null) return;

        hint.Text = editable
            ? L.Tt("dialog_transcript_keeps")
            : L.Tt("dialog_change_spelling");
        hint.ForeColor = Theme.Dim;

        if (editable) _written.Focus();
    }
}
