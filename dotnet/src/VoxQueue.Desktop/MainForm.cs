using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using VoxQueue.Core.I18n;
using VoxQueue.Core.Models;
using VoxQueue.Core.Services;

namespace VoxQueue.Desktop;

/// <summary>
/// Host window: custom chrome, header, tab strip, and the wiring between panels.
///
/// Borderless on purpose — the OS title bar brings a light grey frame and
/// caption buttons that cannot be themed, which breaks the look far more than
/// the small amount of code this replaces.
/// </summary>
public sealed class MainForm : Form
{
    private readonly TitleBar _titleBar = new();
    private readonly FlatTabs _tabs = new();
    private readonly WorkPanel _train;
    private readonly WorkPanel _dub;
    private readonly WorkPanel _bulk;
    private readonly SettingsPanel _settings;
    private readonly LogPanel _log;

    private TtsClient? _client;
    private bool _themeApplied;
    private bool _closeConfirmed;

    /// <summary>True while a .vqproj hovers over the window.</summary>
    private bool _dropHot;

    /// <summary>
    /// Drop feedback, as real controls rather than paint on the form.
    ///
    /// The form's OnPaint never showed any of it: the TabControl is docked over
    /// the entire client area, so anything drawn beneath it is invisible. The
    /// frame is a panel whose Region is only the ring, which leaves the middle
    /// free for the tabs; the banner is a small opaque label above it.
    /// </summary>
    private readonly Panel _dropFrame = new();
    private readonly Label _dropBanner = new();

    public MainForm()
    {
        Text = "VoxQueue";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        Font = Theme.Body(9f);
        Padding = new Padding(1);
        BackColor = Theme.WindowA;

        // The icon is an embedded resource, so a single-file build still has one.
        // Without this the window shows the generic .NET executable icon.
        var icon = Program.LoadAppIcon();
        if (icon is not null) Icon = icon;

        var w = Program.Settings.WindowWidth > 600 ? Program.Settings.WindowWidth : 1440;
        var h = Program.Settings.WindowHeight > 400 ? Program.Settings.WindowHeight : 920;
        ClientSize = new Size(w, h);
        MinimumSize = new Size(1180, 760);

        _train = new WorkPanel(Mode.Train);
        _dub = new WorkPanel(Mode.Dub);
        _bulk = new WorkPanel(Mode.Bulk);
        _settings = new SettingsPanel();
        _log = new LogPanel();

        BuildLayout();
        Wire();
        Retranslate();

        _train.LoadSettings();
        _dub.LoadSettings();
        _bulk.LoadSettings();

        if (Program.Settings.LastTab >= 0 && Program.Settings.LastTab < _tabs.TabCount)
            _tabs.SelectedIndex = Program.Settings.LastTab;

        Resize += (_, _) => _titleBar.SyncMaximizeIcon();

        // A project dropped anywhere on the window. Handled here rather than in
        // WorkPanel because a .vqproj carries a MODE, and only MainForm can switch
        // tabs to the one it belongs in.
        AllowDrop = true;
        DragEnter += OnProjectDragEnter;
        DragOver += OnProjectDragEnter;
        DragLeave += (_, _) => SetDropHot(false);
        DragDrop += OnProjectDrop;
    }

    private void BuildLayout()
    {
        _titleBar.Dock = DockStyle.Top;
        _titleBar.SetVersion("v" + (typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"));

        _tabs.Dock = DockStyle.Fill;
        _tabs.TabPages.Add(MakePage(_train));
        _tabs.TabPages.Add(MakePage(_dub));
        _tabs.TabPages.Add(MakePage(_bulk));
        _tabs.TabPages.Add(MakePage(_settings));
        _tabs.TabPages.Add(MakePage(_log));
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            // Reading the in-memory ring is cheap; doing it on every tick would
            // still be wasted work, so the tab refreshes only when it is entered.
            if (_tabs.SelectedIndex == 4) _log.Reload();

            Program.Settings.LastTab = _tabs.SelectedIndex;
            Config.Save(Program.Settings);

            // The format may have been changed on the settings tab since this
            // panel was last built.
            _train.RefreshFormatDependentUi();
            _dub.RefreshFormatDependentUi();
            _bulk.RefreshFormatDependentUi();

            // Self-healing: the colouring switch lives on the settings tab, and a
            // panel that was built before it changed would otherwise keep the old
            // behaviour until the app restarts.
            _train.ApplyColouringSetting();
            _dub.ApplyColouringSetting();
            _bulk.ApplyColouringSetting();
        };

        // Fill first, then Top: WinForms docks from the highest child index down,
        // so the Fill control must have the lowest index to receive the remainder.
        Controls.Add(_tabs);
        Controls.Add(_titleBar);

        _dropFrame.Dock = DockStyle.Fill;
        _dropFrame.BackColor = Theme.Accent;
        _dropFrame.Visible = false;

        _dropBanner.AutoSize = true;
        _dropBanner.BackColor = Theme.Card;
        _dropBanner.ForeColor = Theme.Accent;
        _dropBanner.Font = Theme.Body(11f, FontStyle.Bold);
        _dropBanner.Padding = new Padding(20, 12, 20, 12);
        _dropBanner.TextAlign = ContentAlignment.MiddleCenter;
        _dropBanner.Visible = false;

        Controls.Add(_dropFrame);
        Controls.Add(_dropBanner);
        Resize += (_, _) => PositionDropBanner();
    }

    private static TabPage MakePage(Control content)
    {
        var page = new TabPage { BackColor = Theme.Surface, Padding = new Padding(0) };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private void Wire()
    {
        _settings.ColourTextChanged += (_, _) =>
        {
            _train.ApplyColouringSetting();
            _dub.ApplyColouringSetting();
            _bulk.ApplyColouringSetting();
            AppLog.Info("colour text: " + (Program.Settings.ColourText ? "on" : "off"));
        };

        _settings.VoicesRefreshRequested += async (_, r) => await ReloadVoicesAsync(r.ok);
        _settings.ThemeOrLanguageChanged += (_, _) =>
        {
            ApplyTheme();
            Retranslate();
        };
    }

    // ------------------------------------------------------------------ chrome

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowChrome.EnableRoundedCorners(this);
    }

    protected override void WndProc(ref Message m)
    {
        if (WindowChrome.HandleMessage(this, ref m)) return;
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!_themeApplied)
        {
            base.OnPaint(e);
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // A one-pixel outline instead of the OS frame, so the window still has an
        // edge against a same-coloured desktop.
        using var brush = new LinearGradientBrush(ClientRectangle, Theme.WindowA, Theme.WindowB, 45f);
        g.FillRectangle(brush, ClientRectangle);

        using var pen = new Pen(Theme.Border, 1f);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

    }

    // ------------------------------------------------------------------ theme

    private void ApplyTheme()
    {
        Theme.Set(Program.Settings.Theme switch
        {
            "light" => false,
            "dark" => true,
            _ => !SystemIsLight()
        });

        BackColor = Theme.WindowA;
        _titleBar.ApplyTheme();
        _train.ApplyTheme();
        _dub.ApplyTheme();
        _bulk.ApplyTheme();
        _settings.ApplyTheme();
        _log.ApplyTheme();

        // The tab pages themselves are not inside any panel's ApplyTheme, and
        // they were the last dark surface left in light mode.
        foreach (TabPage tp in _tabs.TabPages)
        {
            tp.BackColor = Theme.Surface;
            tp.Invalidate();
        }
        _tabs.BackColor = Theme.WindowA;
        _tabs.Invalidate();

        _themeApplied = true;
        Invalidate(true);

        // Native scrollbars inside TextBox and DataGridView are drawn by the OS.
        // SetColorMode can only be called once, before the first window, so the
        // per-window messages are re-applied here — this is what kept them black
        // after switching to the light theme.
        NativeTheme.Apply(this, Theme.Dark);
    }

    private static bool SystemIsLight()
    {
        try
        {
            var v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return v is int i && i == 1;
        }
        catch { return false; }
    }

    // --------------------------------------------------------------- language

    private void Retranslate()
    {
        _tabs.TabPages[0].Text = L.Tt("tab_train");
        _tabs.TabPages[1].Text = L.Tt("tab_dub");
        _tabs.TabPages[2].Text = L.Tt("tab_bulk");
        _tabs.TabPages[3].Text = L.Tt("tab_settings");
        _tabs.TabPages[4].Text = L.Tt("tab_log");

        _train.Retranslate();
        _dub.Retranslate();
        _bulk.Retranslate();
        _settings.Retranslate();
        _log.Retranslate();

        _train.RefreshFormatDependentUi();
        _dub.RefreshFormatDependentUi();
        _bulk.RefreshFormatDependentUi();
    }

    // ---------------------------------------------------------------- startup

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyTheme();

        // RestoreSession was defined but never called — the previous session was
        // written on close and then ignored on the next start, so the list came
        // back empty. It runs before BootAsync so the restored voice id is in
        // place before the voice list arrives.
        // Maximised state has to be applied after the window exists: setting it
        // in the constructor is ignored because there is no handle yet.
        if (Program.Settings.WindowState == "max")
        {
            WindowState = FormWindowState.Maximized;
            _titleBar.SyncMaximizeIcon();
        }

        RestoreSession();

        await BootAsync();
    }

    /// <summary>Try the saved server once. An offline start is normal, not an error.</summary>
    private async Task BootAsync()
    {
        _client = new TtsClient(Program.Settings.BaseUrl, Program.Settings.ApiKey, 15);
        try
        {
            var (ok, _) = await _client.PingAsync();
            if (!ok) return;
            var voices = await _client.VoicesAsync();
            PushVoices(voices);
        }
        catch { /* nothing to report yet */ }
    }

    private async Task ReloadVoicesAsync(bool previousOk)
    {
        if (!previousOk) return;
        _client?.Dispose();
        _client = new TtsClient(Program.Settings.BaseUrl, Program.Settings.ApiKey, 60);
        try
        {
            var voices = await _client.VoicesAsync();
            PushVoices(voices);
        }
        catch { }
    }

    private const string ProjectExtension = ".vqproj";

    private static string? DroppedProject(DragEventArgs e)
    {
        // e.Data can be null for a drag that lost its source; the drag handler
        // used to dereference it and would have thrown on a dropped shortcut.
        if (e.Data is null) return null;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return null;

        foreach (var f in files)
            if (string.Equals(Path.GetExtension(f), ProjectExtension,
                              StringComparison.OrdinalIgnoreCase))
                return f;

        return null;
    }

    private void OnProjectDragEnter(object? sender, DragEventArgs e)
    {
        var accepts = DroppedProject(e) is not null;
        e.Effect = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropHot(accepts);
    }

    private void SetDropHot(bool hot)
    {
        if (_dropHot == hot) return;
        _dropHot = hot;

        _dropBanner.Text = L.Hint("drop_project_hint");

        _dropFrame.Visible = hot;
        _dropBanner.Visible = hot;

        if (!hot) return;

        // The ring is the panel's Region, so only the border area is painted and
        // the rest of the window stays visible and usable underneath.
        var ring = new System.Drawing.Drawing2D.GraphicsPath();
        var bounds = new Rectangle(0, 0, Math.Max(1, _dropFrame.Width), Math.Max(1, _dropFrame.Height));
        var outer = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var inner = Rectangle.Inflate(bounds, -4, -4);
        ring.AddRectangle(outer);
        if (inner.Width > 0 && inner.Height > 0) ring.AddRectangle(inner);
        _dropFrame.Region = new Region(ring);

        _dropFrame.BringToFront();
        _dropBanner.BringToFront();
        PositionDropBanner();
    }

    private void PositionDropBanner()
    {
        if (!_dropBanner.Visible) return;
        _dropBanner.Location = new Point(
            Math.Max(0, (ClientSize.Width - _dropBanner.Width) / 2),
            Math.Max(0, (ClientSize.Height - _dropBanner.Height) / 2));
    }

    /// <summary>
    /// Border and caption while a project hovers.
    ///
    /// Painted rather than added as a control: an overlay panel would take part in
    /// layout and shift the editor underneath, which is exactly the class of bug
    /// this window has already had twice.
    /// </summary>
    private void OnProjectDrop(object? sender, DragEventArgs e)
    {
        SetDropHot(false);
        var path = DroppedProject(e);
        if (path is null) return;

        var ws = WorkspaceFile.Load(path, out var error);
        if (ws is null)
        {
            MessageBox.Show(this,
                $"{L.Tt("load_project")}: {error}", L.Tt("app_title"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var tab = SessionFile.TabOf(ws.Mode);
        var panel = PanelFor(tab);
        if (panel is null) return;

        var hasWork = panel.HasWork;
        if (hasWork)
        {
            var answer = MessageBox.Show(this,
                L.Hint("drop_replace_warn"), L.Tt("app_title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes) return;
        }

        if (!panel.ApplyWorkspace(ws)) return;

        if (_tabs.SelectedIndex != tab) _tabs.SelectedIndex = tab;
        Text = $"VoxQueue — {Path.GetFileName(path)}";
    }

    private WorkPanel? PanelFor(int tab) => tab switch
    {
        0 => _train,
        1 => _dub,
        2 => _bulk,
        _ => null
    };

    /// <summary>
    /// Brings back the previous session.
    ///
    /// The slot is left alone when it cannot be read, so a corrupt file costs one
    /// empty start rather than losing the text; the next close overwrites it.
    /// A mode mismatch switches to the right tab instead of refusing silently.
    /// </summary>
    private void RestoreSession()
    {
        if (!Program.Settings.RestoreSession) return;

        var bundle = SessionFile.Load();
        if (bundle is null) return;

        // Every tab is restored, in its own order. A panel that refuses (a mode
        // mismatch, which cannot happen with this format but is cheap to guard)
        // is simply left empty.
        if (bundle.Train is not null) _train.ApplyWorkspace(bundle.Train);
        if (bundle.Dub is not null) _dub.ApplyWorkspace(bundle.Dub);
        if (bundle.Bulk is not null) _bulk.ApplyWorkspace(bundle.Bulk);

        var tab = Math.Clamp(bundle.ActiveTab, 0, 2);
        if (_tabs.SelectedIndex != tab) _tabs.SelectedIndex = tab;
    }

    private void PushVoices(List<VoiceInfo> voices)
    {
        var preferred = Program.Settings.LastVoice;
        _train.SetVoices(voices, preferred);
        _dub.SetVoices(voices, preferred);
        _bulk.SetVoices(voices, preferred);
    }

    // ------------------------------------------------------------------ close

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing mid-run silently discards a half-rendered dataset — the state
        // file survives, but the user has no idea they just stopped a job.
        if (!_closeConfirmed && (_train.IsBusy || _dub.IsBusy || _bulk.IsBusy))
        {
            var answer = MessageBox.Show(this,
                Core.I18n.L.Hint("close_running_warn"), Core.I18n.L.Tt("app_title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _closeConfirmed = true;
        }

        var s = Program.Settings;
        s.LastTab = _tabs.SelectedIndex;

        // Only remember the restore size. A maximised window reports the screen
        // size in ClientSize, and saving that made the next start open "large but
        // not maximised" — the geometry of a maximised window is not a size the
        // user chose.
        s.WindowState = WindowState == FormWindowState.Maximized ? "max" : "normal";
        if (WindowState == FormWindowState.Normal)
        {
            s.WindowWidth = ClientSize.Width;
            s.WindowHeight = ClientSize.Height;
        }
        _train.PersistSettings();

        // Save ALL THREE tabs, not just the one in front. The slot is written on
        // every close, so saving only the active tab discarded the other two the
        // first time the app was closed from a different tab.
        try
        {
            var bundle = new SessionBundle
            {
                ActiveTab = _tabs.SelectedIndex,
                Train = _train.BuildWorkspace(),
                Dub = _dub.BuildWorkspace(),
                Bulk = _bulk.BuildWorkspace()
            };
            SessionFile.SaveOrClear(bundle);
            s.LastTab = _tabs.SelectedIndex;
            AppLog.Info("session saved");
        }
        catch { /* never let a session save break closing */ }

        Config.Save(s);

        _train.Shutdown();
        _dub.Shutdown();
        _bulk.Shutdown();
        _client?.Dispose();
        base.OnFormClosing(e);
    }
}
