using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using VoxQueue.Core.I18n;
using VoxQueue.Core.Services;

namespace VoxQueue.Desktop;

/// <summary>
/// The Log tab: everything the app recorded, with the actions a user actually
/// needs — read it, open the file, copy it, clear it.
///
/// It reads the in-memory ring rather than the file. The file is the durable copy;
/// parsing a ten-megabyte log to fill a text box would stall the UI for seconds
/// every time the tab is opened.
/// </summary>
public sealed class LogPanel : UserControl
{
    private readonly TextBox _text = new();
    private readonly FlatButton _btnRefresh = new();
    private readonly FlatButton _btnOpen = new();
    private readonly FlatButton _btnCopy = new();
    private readonly FlatButton _btnClear = new();
    private readonly Label _count = new();

    /// <summary>
    /// Repaints the panel for the current palette.
    ///
    /// The log tab had no handler at all: the shell restyled the three work panels
    /// and the settings card, and the log was simply left dark. It is a plain
    /// TextBox, not a RichTextBox, so the status read "the log stayed black like the
    /// script editor" — two different causes behind one symptom.
    /// </summary>
    public void ApplyTheme()
    {
        BackColor = Theme.Surface;
        Theme.Restyle(this);

        _text.BackColor = Theme.Input;
        _text.ForeColor = Theme.Text;
        _count.ForeColor = Theme.Dim;

        Invalidate(true);
    }

    public LogPanel()
    {
        BackColor = Theme.Surface;
        Padding = new Padding(0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Surface
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // buttons
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // text
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // count

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, 0, 6)
        };
        foreach (var b in new[] { _btnRefresh, _btnOpen, _btnCopy, _btnClear })
        {
            b.Margin = new Padding(0, 0, 6, 0);
            buttons.Controls.Add(b);
        }

        _btnRefresh.Click += (_, _) => Reload();
        _btnOpen.Click += (_, _) => OpenFile();
        _btnCopy.Click += (_, _) => CopyAll();
        _btnClear.Click += (_, _) => ClearLog();

        _text.Multiline = true;
        _text.ReadOnly = true;
        _text.ScrollBars = ScrollBars.Both;
        _text.WordWrap = false;
        _text.Dock = DockStyle.Fill;
        _text.BorderStyle = BorderStyle.FixedSingle;
        _text.BackColor = Theme.Input;
        _text.ForeColor = Theme.Text;
        _text.Font = Theme.Mono(9f);

        _count.ForeColor = Theme.Dim;
        _count.AutoSize = true;
        _count.Margin = new Padding(0, 6, 0, 0);

        root.Controls.Add(buttons, 0, 0);
        root.Controls.Add(_text, 0, 1);
        root.Controls.Add(_count, 0, 2);
        Controls.Add(root);

        Retranslate();
        Reload();
    }

    public void Retranslate()
    {
        _btnRefresh.Text = L.Tt("log_refresh");
        _btnOpen.Text = L.Tt("log_open");
        _btnCopy.Text = L.Tt("log_copy");
        _btnClear.Text = L.Tt("log_clear");
        UpdateCount();
    }

    /// <summary>Re-reads the ring and scrolls to the end — the newest line matters.</summary>
    public void Reload()
    {
        var lines = AppLog.Lines;
        _text.Text = string.Join(Environment.NewLine, lines);
        UpdateCount();

        try
        {
            _text.SelectionStart = _text.TextLength;
            _text.ScrollToCaret();
        }
        catch { /* not laid out yet */ }
    }

    private void UpdateCount()
    {
        _count.Text = $"{L.Tt("log_lines")}: {AppLog.Lines.Count}   ·   {AppLog.Path}";
    }

    private void OpenFile()
    {
        try
        {
            Directory.CreateDirectory(Config.Dir);

            // The file may not exist yet on a fresh install: create it so the
            // button opens an empty file rather than an error dialog.
            if (!File.Exists(AppLog.Path)) File.WriteAllText(AppLog.Path, "");

            Process.Start(new ProcessStartInfo(AppLog.Path) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            AppLog.Error("log: cannot open file", e);
            MessageBox.Show(this, e.Message, L.Tt("app_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CopyAll()
    {
        try { Clipboard.SetText(_text.Text); }
        catch (Exception e) { AppLog.Warn("log: clipboard busy — " + e.Message); }
    }

    private void ClearLog()
    {
        var answer = MessageBox.Show(this, L.Hint("log_clear_warn"), L.Tt("app_title"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes) return;

        AppLog.Clear();
        AppLog.Info("log cleared by user");
        Reload();
    }
}
