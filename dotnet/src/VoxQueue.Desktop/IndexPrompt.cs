using System;
using System.Drawing;
using System.Windows.Forms;

namespace VoxQueue.Desktop;

/// <summary>
/// Asks for a number. Used by "jump to index".
///
/// A single-purpose dialog rather than an InputBox clone: the value must be an
/// integer, and a dialog that accepts text and fails afterwards would make the
/// user type the number twice.
/// </summary>
public static class IndexPrompt
{
    public static int? Ask(Control owner, string title)
    {
        using var dlg = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(300, 110),
            BackColor = Theme.Card,
            Font = Theme.Body(9f)
        };

        var label = new Label
        {
            Text = title,
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(16, 14)
        };

        var box = new TextBox
        {
            Location = new Point(16, 40),
            Width = 268,
            Font = Theme.Mono(10f)
        };

        var ok = new FlatButton
        {
            Text = "OK",
            Primary = true,
            Width = 96,
            Location = new Point(300 - 208, 74)
        };
        var cancel = new FlatButton
        {
            Text = "Cancel",
            Width = 96,
            Location = new Point(300 - 104, 74)
        };

        int? result = null;

        ok.Click += (_, _) =>
        {
            if (int.TryParse(box.Text.Trim(), out var n) && n >= 0)
            {
                result = n;
                dlg.DialogResult = DialogResult.OK;
                dlg.Close();
            }
            else
            {
                // Keep the dialog open and say what is wrong, rather than closing
                // and leaving the caller to guess.
                box.BackColor = Color.FromArgb(255, Theme.Fail.R, Theme.Fail.G, Theme.Fail.B);
                box.SelectAll();
            }
        };
        cancel.Click += (_, _) => { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };

        box.TextChanged += (_, _) => Theme.SafeBackColor(box, Theme.Input);

        dlg.Controls.AddRange(new Control[] { label, box, ok, cancel });
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        dlg.ActiveControl = box;

        return dlg.ShowDialog(owner) == DialogResult.OK ? result : null;
    }
}
