using System;
using System.Drawing;
using System.Windows.Forms;

namespace VoxQueue.Desktop;

/// <summary>
/// Countdown prompt shown before the machine shuts down.
///
/// The point is that the shutdown is visible and cancellable. A silent
/// `shutdown /t 120` works, but nobody sees the clock, and a run that finished
/// while the user was still at the desk would power the machine off under their
/// hands with no warning at all.
///
/// The dialog counts down in the title bar and on a progress bar; cancelling
/// returns <see cref="DialogResult.Cancel"/> and nothing happens.
/// </summary>
public sealed class ShutdownPrompt : Form
{
    private readonly System.Windows.Forms.Timer _timer = new();
    private int _remaining;

    public ShutdownPrompt(int seconds, string headline, string detail)
    {
        _remaining = Math.Max(5, seconds);

        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        ClientSize = new Size(420, 150);
        Text = headline;
        BackColor = Theme.Card;
        Font = Theme.Body(9f);

        var icon = new Label
        {
            Text = "⏻",
            Font = new Font("Segoe UI Symbol", 28f),
            ForeColor = Theme.Accent,
            AutoSize = true,
            Location = new Point(18, 22)
        };

        var title = new Label
        {
            Text = headline,
            Font = Theme.Body(11f, FontStyle.Bold),
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(80, 22)
        };

        var body = new Label
        {
            Text = detail,
            ForeColor = Theme.Text,
            AutoSize = false,
            Location = new Point(80, 50),
            Size = new Size(320, 44)
        };

        var countdown = new Label
        {
            Name = "Countdown",
            Text = $"{_remaining} s",
            Font = Theme.Body(10f, FontStyle.Bold),
            ForeColor = Theme.Warn,
            AutoSize = true,
            Location = new Point(80, 96)
        };

        var keep = new FlatButton
        {
            Text = "Cancel",
            Width = 120,
            Location = new Point(ClientSize.Width - 130, 100)
        };
        keep.Click += (_, _) =>
        {
            _timer.Stop();
            DialogResult = DialogResult.Cancel;
            Close();
        };

        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = _remaining,
            Value = _remaining,
            Style = ProgressBarStyle.Continuous,
            Location = new Point(80, 118),
            Size = new Size(ClientSize.Width - 210, 6)
        };

        Controls.AddRange(new Control[] { icon, title, body, countdown, bar, keep });

        _timer.Interval = 1000;
        _timer.Tick += (_, _) =>
        {
            _remaining--;
            countdown.Text = $"{_remaining} s";
            bar.Value = Math.Max(0, _remaining);
            if (_remaining > 0) return;

            _timer.Stop();
            DialogResult = DialogResult.OK;
            Close();
        };

        Shown += (_, _) => _timer.Start();
        FormClosing += (_, _) => _timer.Stop();
    }
}
