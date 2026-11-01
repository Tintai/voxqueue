using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using VoxQueue.Core.I18n;
using VoxQueue.Core.Services;

namespace VoxQueue.Desktop;

/// <summary>Connection, appearance, defaults and data removal.</summary>
public sealed class SettingsPanel : UserControl
{
    private Label _lblUrl = new();
    private Label _lblKey = new();
    private Label _lblTheme = new();
    private Label _lblLang = new();
    private Label _lblOut = new();
    private Label _lblFormat = new();
    private Label _lblRate = new();
    private Label _lblTemplate = new();
    private readonly Label _hConn = new();
    private readonly Label _hLook = new();
    private readonly Label _hDefs = new();
    private readonly CheckBox _chkExtension = new();

    private readonly TextBox _url = new();
    private readonly TextBox _key = new() { UseSystemPasswordChar = true };
    private readonly TextBox _out = new();
    private readonly TextBox _template = new();
    private readonly Dropdown _theme = new();
    private readonly Dropdown _lang = new();
    private readonly CheckBox _chkColour = new();
    private readonly Dropdown _format = new();
    private readonly Dropdown _rate = new();

    private readonly FlatButton _btnTest = new();
    private readonly FlatButton _btnRefresh = new();
    private readonly FlatButton _btnBrowse = new() { Text = "..." };
    private readonly FlatButton _btnReveal = new() { Text = "o", Width = 34 };
    private readonly FlatButton _btnSave = new() { Primary = true };
    private readonly FlatButton _btnWipe = new() { Danger = true };

    private readonly Label _connStatus = new();
    private readonly Label _metaNote = new();
    private readonly Label _resumeNote = new();

    private readonly ScrollHost _scroll = new();
    private readonly List<Card> _cards = new();

    public event EventHandler<(bool ok, string message)>? VoicesRefreshRequested;
    public event EventHandler? ThemeOrLanguageChanged;

    /// <summary>Raised when the colouring switch changes, so the editors can react.</summary>
    public event EventHandler? ColourTextChanged;

    private bool _loading;

    public SettingsPanel()
    {
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        Build();
        Retranslate();
        LoadFromSettings();
    }

    // ------------------------------------------------------------------ build

    private void Build()
    {
        _scroll.Dock = DockStyle.Fill;
        Controls.Add(_scroll);

        // Section headers: small, spaced, dim. They were set with no explicit font
        // and no spacing, so they read as another field caption rather than as a
        // heading — the page had no visual structure at all.
        foreach (var h in new[] { _hConn, _hLook, _hDefs })
        {
            h.AutoSize = true;
            h.Font = Theme.Body(8.5f, FontStyle.Bold);
            h.ForeColor = Theme.Accent;
            h.Margin = new Padding(0, 0, 0, 10);
            h.BackColor = Color.Transparent;
        }

        // Fields share one font and one background so the form stops looking like
        // a collection of whatever the OS defaults to.
        foreach (var t in new[] { _url, _key, _out, _template })
        {
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = Theme.Input;
            t.ForeColor = Theme.Text;
            t.Font = Theme.Body(9.5f);
        }

        var conn = BuildConnectionCard();
        var look = BuildAppearanceCard();
        var defs = BuildDefaultsCard();
        var actions = BuildActionsRow();

        // Explicit stacking, positions computed here — the FlowLayoutPanel +
        // AutoSize combination silently collapsed these cards to zero height.
        var y = 8;
        foreach (var c in new Control[] { conn, look, defs, actions })
        {
            c.Location = new Point(0, y);
            _scroll.Content.Controls.Add(c);
            y += c.Height + 10;
        }

        _cards.AddRange(new[] { conn, look, defs });

        _scroll.SizeChanged += (_, _) => Relayout(8);
        Relayout(8);
    }

    private void Relayout(int top)
    {
        if (_scroll.ClientSize.Width <= 0) return;
        var width = Math.Max(320, _scroll.ClientSize.Width - 24);
        var y = top;
        foreach (Control c in _scroll.Content.Controls)
        {
            c.Width = width;
            c.Location = new Point(0, y);
            y += c.Height + 10;
        }
        _scroll.Recalc();
    }

    private Card BuildConnectionCard()
    {
        var g = UiKit.Grid(150);

        UiKit.SpanRow(g, _hConn);
        _lblUrl = UiKit.Row(g, L.Tt("base_url"), _url);

        var keyRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent
        };
        keyRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // An absolute column, not AutoSize.
        //
        // With AutoSize the column width comes from the control's preferred size, and
        // for a one-glyph button that measure disagreed with what was drawn: the
        // column collapsed towards the margin and the label had no room. An absolute
        // width taken from the measured caption cannot collapse.
        var revealSize = _btnReveal.CompactSize();
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, revealSize.Width + 6));

        _key.Dock = DockStyle.Fill;
        _btnReveal.AutoSize = false;
        _btnReveal.Size = revealSize;
        _btnReveal.Dock = DockStyle.Fill;
        _btnReveal.Margin = new Padding(6, 0, 0, 0);
        _btnReveal.Click += (_, _) =>
        {
            _key.UseSystemPasswordChar = !_key.UseSystemPasswordChar;
            _btnReveal.Primary = !_key.UseSystemPasswordChar;
            _btnReveal.Invalidate();
        };
        keyRow.Controls.Add(_key, 0, 0);
        keyRow.Controls.Add(_btnReveal, 1, 0);
        _lblKey = UiKit.Row(g, L.Tt("api_key"), keyRow);

        var btnRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        _btnTest.Width = 160;
        _btnRefresh.Width = 120;
        _btnTest.Margin = new Padding(0, 0, 8, 0);
        _btnTest.Click += async (_, _) => await TestAsync();
        _btnRefresh.Click += async (_, _) => await RefreshAsync();
        btnRow.Controls.Add(_btnTest);
        btnRow.Controls.Add(_btnRefresh);
        UiKit.SpanRow(g, btnRow);

        _connStatus.Font = Theme.Body(8.5f);
        _connStatus.AutoSize = true;
        _connStatus.ForeColor = Theme.Dim;
        UiKit.SpanRow(g, _connStatus);

        return UiKit.MakeCard(g);
    }

    private Card BuildAppearanceCard()
    {
        var g = UiKit.Grid(150);
        UiKit.SpanRow(g, _hLook);

        _theme.Width = 220;
        _lang.Width = 220;
        _theme.SelectedIndexChanged += (_, _) => OnThemeChanged();
        _lang.SelectedIndexChanged += (_, _) => OnLanguageChanged();

        _lblTheme = UiKit.Row(g, L.Tt("theme"), _theme);
        _lblLang = UiKit.Row(g, L.Tt("language"), _lang);

        // Turning colouring off removes the most expensive thing the editor does.
        // It is a real trade: no highlighting, but large scripts paste and scroll
        // noticeably faster.
        _chkColour.Text = L.Tt("colour_text");
        _chkColour.AutoSize = true;
        _chkColour.ForeColor = Theme.Text;
        _chkColour.Margin = new Padding(0, 10, 0, 0);
        _chkColour.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            AppSettings.ColourText = _chkColour.Checked;
            Config.Save(AppSettings);
            ColourTextChanged?.Invoke(this, EventArgs.Empty);
        };
        UiKit.SpanRow(g, _chkColour);

        return UiKit.MakeCard(g, alt: true);
    }

    private Card BuildDefaultsCard()
    {
        var g = UiKit.Grid(150);
        UiKit.SpanRow(g, _hDefs);

        var outRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent
        };
        outRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Absolute, never AutoSize: an AutoSize column next to a fixed-size control
        // can collapse towards the margin, and then the caption has no room left.
        var browseSize = _btnBrowse.CompactSize();
        outRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, browseSize.Width + 6));
        _out.Dock = DockStyle.Fill;
        // AutoSize is off here, and the button stretches with the row.
        //
        // A FlatButton measures itself at 32 px tall. Sitting beside a single-line
        // text box in an auto-sized row, that 32 px was taller than the row the text
        // box defined, so the bottom of the button was clipped away. Disabling
        // AutoSize and anchoring to both edges makes it take exactly the row height,
        // whatever the font or DPI turns out to be.
        _btnBrowse.AutoSize = false;
        _btnBrowse.Size = browseSize;
        _btnBrowse.Dock = DockStyle.Fill;
        _btnBrowse.Margin = new Padding(6, 0, 0, 0);
        _btnBrowse.Click += (_, _) => BrowseOut();
        outRow.Controls.Add(_out, 0, 0);
        outRow.Controls.Add(_btnBrowse, 1, 0);
        _lblOut = UiKit.Row(g, L.Tt("output_dir"), outRow);

        var fmtRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        _format.Width = 100;
        _rate.Width = 130;
        _rate.Margin = new Padding(10, 0, 0, 0);
        _format.Items.AddRange(new object[] { "wav", "mp3", "m4a", "ogg", "flac" });
        _rate.Items.AddRange(new object[] { 22050, 24000, 44100, 48000 });
        fmtRow.Controls.Add(_format);
        fmtRow.Controls.Add(_rate);
        _lblFormat = UiKit.Row(g, L.Tt("audio_format"), fmtRow);

        _template.Width = 170;
        _lblTemplate = UiKit.Row(g, L.Hint("filename_template"), _template);

        _chkExtension.AutoSize = true;
        _chkExtension.Margin = new Padding(0, 4, 0, 0);
        UiKit.SpanRow(g, _chkExtension);

        _metaNote.Font = Theme.Body(8.5f);
        _metaNote.AutoSize = true;
        _metaNote.MaximumSize = new Size(520, 0);
        _metaNote.ForeColor = Theme.Dim;
        UiKit.SpanRow(g, _metaNote);

        _resumeNote.Font = Theme.Body(8.5f);
        _resumeNote.AutoSize = true;
        _resumeNote.MaximumSize = new Size(520, 0);
        _resumeNote.ForeColor = Theme.Dim;
        UiKit.SpanRow(g, _resumeNote);

        return UiKit.MakeCard(g, alt: true);
    }

    /// <summary>
    /// Save and wipe, in a card of their own.
    ///
    /// They used to be two bare buttons floating under the last card with a fixed
    /// Height and hard-coded X positions, which read as leftovers rather than as
    /// actions. A docked row inside a card lines up with everything above it and
    /// cannot be clipped when the panel is narrow.
    /// </summary>
    private Control BuildActionsRow()
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Card,
            Margin = new Padding(0),
            WrapContents = true
        };

        _btnSave.Click += (_, _) => Save();
        _btnSave.Margin = new Padding(0, 0, 10, 0);

        _btnWipe.Click += (_, _) => Wipe();
        _btnWipe.Margin = new Padding(0);

        row.Controls.Add(_btnSave);
        row.Controls.Add(_btnWipe);

        var g = UiKit.Grid(150);
        UiKit.SpanRow(g, row);
        return UiKit.MakeCard(g, padX: 16, padY: 12);
    }

    // --------------------------------------------------------------- language

    public void Retranslate()
    {
        _hConn.Text = L.Tt("section_connection").ToUpperInvariant();
        _hLook.Text = L.Tt("section_appearance").ToUpperInvariant();
        _hDefs.Text = L.Tt("section_output").ToUpperInvariant();

        _lblUrl.Text = L.Tt("base_url");
        _lblKey.Text = L.Tt("api_key");
        _lblTheme.Text = L.Tt("theme");
        _lblLang.Text = L.Tt("language");
        _lblOut.Text = L.Tt("output_dir");
        _lblFormat.Text = L.Tt("audio_format");
        _lblRate.Text = L.Hint("sample_rate");
        _lblTemplate.Text = L.Hint("filename_template");

        _btnTest.Text = L.Tt("test_connection");
        _btnRefresh.Text = L.Tt("refresh_voices");
        _btnSave.Text = L.Tt("save_settings");
        _btnWipe.Text = L.Tt("delete_data");

        _chkExtension.Text = L.Tt("add_extension");
        _chkColour.Text = L.Tt("colour_text");
        _chkColour.AccessibleName = L.Tt("colour_text");
        _btnReveal.Text = L.Hint("show_key");
        _metaNote.Text = L.Hint("metadata_note");
        _resumeNote.Text = L.Hint("resume_note");

        _loading = true;
        var themeCode = ItemCode(_theme.SelectedItem) is { Length: > 0 } t ? t : AppSettings.Theme;
        _theme.Items.Clear();
        _theme.Items.Add(new Choice("dark", L.Tt("theme_dark")));
        _theme.Items.Add(new Choice("light", L.Tt("theme_light")));
        _theme.Items.Add(new Choice("system", L.Tt("theme_system")));
        SelectTheme(themeCode);

        var langCode = ItemCode(_lang.SelectedItem) is { Length: > 0 } l ? l : AppSettings.Language;
        _lang.Items.Clear();
        // "System language" is first and is the default: most people never pick
        // a language, and following the OS is the right answer for them.
        _lang.Items.Add(new Choice("system", L.W("language_system")));
        foreach (var (code, name) in L.Languages)
            _lang.Items.Add(new Choice(code, name));
        SelectLang(string.IsNullOrEmpty(langCode) ? "system" : langCode);
        _loading = false;

        ApplyTheme();
    }

    private void SelectTheme(string code)
    {
        for (var i = 0; i < _theme.Items.Count; i++)
            if (_theme.Items[i] is Choice c && c.Code == code) { _theme.SelectedIndex = i; return; }
        if (_theme.Items.Count > 0) _theme.SelectedIndex = 0;
    }

    private void SelectLang(string code)
    {
        for (var i = 0; i < _lang.Items.Count; i++)
            if (_lang.Items[i] is Choice c && c.Code == code) { _lang.SelectedIndex = i; return; }
        if (_lang.Items.Count > 0) _lang.SelectedIndex = 0;
    }

    /// <summary>
    /// The stored code behind a combo entry.
    ///
    /// Entries are Choice now, so the label and the value cannot drift apart. The
    /// string branch is kept for the audio-format and sample-rate combos, which
    /// hold plain values.
    /// </summary>
    private static string ItemCode(object? item) => item switch
    {
        Choice c => c.Code,
        string s => s,
        null => "",
        _ => item.ToString() ?? ""
    };

    // ------------------------------------------------------------------ theme

    public void ApplyTheme()
    {
        BackColor = Theme.Surface;

        Theme.Restyle(this);

        _hConn.ForeColor = _hLook.ForeColor = _hDefs.ForeColor = Theme.Dim;
        _metaNote.ForeColor = _resumeNote.ForeColor = Theme.Dim;
        _connStatus.ForeColor = Theme.Dim;

        foreach (var card in _cards) card.Invalidate();
        _scroll.Invalidate(true);
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

    // ------------------------------------------------------------------ state

    private static Settings AppSettings => VoxQueue.Desktop.Program.Settings;

    public void LoadFromSettings()
    {
        _loading = true;
        _url.Text = AppSettings.BaseUrl;
        _url.PlaceholderText = "http://127.0.0.1:3900";
        _key.Text = AppSettings.ApiKey;
        _out.Text = AppSettings.OutputDir;
        _template.Text = AppSettings.TrainTemplate;
        SelectTheme(AppSettings.Theme);
        SelectLang(AppSettings.Language);

        var fi = _format.Items.IndexOf(AppSettings.AudioFormat);
        _format.SelectedIndex = fi >= 0 ? fi : 0;
        var ri = _rate.Items.IndexOf(AppSettings.SampleRate);
        _rate.SelectedIndex = ri >= 0 ? ri : 0;
        _chkExtension.Checked = AppSettings.AddExtension;
        _chkColour.Checked = AppSettings.ColourText;

        _format.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            // The work panel disables "add extension" for non-WAV, so a format
            // change has to reach it — same event the language switch uses.
            AppSettings.AudioFormat = _format.SelectedItem as string ?? "wav";
            ThemeOrLanguageChanged?.Invoke(this, EventArgs.Empty);
        };
        _loading = false;
    }

    public void Save()
    {
        var s = AppSettings;
        s.BaseUrl = _url.Text.Trim();
        s.ApiKey = _key.Text.Trim();
        s.OutputDir = string.IsNullOrWhiteSpace(_out.Text) ? s.OutputDir : _out.Text.Trim();
        s.TrainTemplate = string.IsNullOrWhiteSpace(_template.Text) ? "{index}" : _template.Text.Trim();
        s.AudioFormat = _format.SelectedItem as string ?? "wav";
        s.SampleRate = _rate.SelectedItem is int r ? r : 22050;
        s.AddExtension = _chkExtension.Checked;
        Config.Save(s);
        _connStatus.Text = L.Tt("saved");
        _connStatus.ForeColor = Theme.Dim;
    }

    private void OnThemeChanged()
    {
        if (_loading) return;
        var code = ItemCode(_theme.SelectedItem);
        AppSettings.Theme = code;
        Config.Save(AppSettings);
        Theme.Set(code switch
        {
            "light" => false,
            "dark" => true,
            _ => !SystemIsLight()
        });
        ThemeOrLanguageChanged?.Invoke(this, EventArgs.Empty);
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

    private void OnLanguageChanged()
    {
        if (_loading) return;
        var code = ItemCode(_lang.SelectedItem);
        if (code.Length == 0) return;
        AppSettings.Language = code;
        Config.Save(AppSettings);
        L.Set(code);
        Retranslate();
        Relayout(8);
        ThemeOrLanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    // --------------------------------------------------------------- network

    private async Task TestAsync()
    {
        Save();
        using var client = new TtsClient(AppSettings.BaseUrl, AppSettings.ApiKey, 25);
        var (ok, msg) = await client.PingAsync();
        Report(ok, msg);
    }

    private async Task RefreshAsync()
    {
        Save();
        using var client = new TtsClient(AppSettings.BaseUrl, AppSettings.ApiKey, 60);
        try
        {
            var voices = await client.VoicesAsync();
            if (voices.Count == 0)
            {
                Report(false, "server returned an empty voice list");
                return;
            }
            Report(true, $"{voices.Count} voices");
            VoicesRefreshRequested?.Invoke(this, (true, $"{voices.Count} voices"));
        }
        catch (Exception e)
        {
            Report(false, e.Message);
            VoicesRefreshRequested?.Invoke(this, (false, e.Message));
        }
    }

    private void Report(bool ok, string msg)
    {
        _connStatus.Text = (ok ? "OK   " : "ERR   ") + msg;
        _connStatus.ForeColor = ok ? Theme.Ok : Theme.Fail;
    }

    private void BrowseOut()
    {
        using var dlg = new FolderBrowserDialog { SelectedPath = _out.Text };
        if (dlg.ShowDialog(this) == DialogResult.OK) _out.Text = dlg.SelectedPath;
    }

    private void Wipe()
    {
        var answer = MessageBox.Show(this,
            L.Hint("delete_warn"), L.Tt("app_title"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        Config.Wipe();
        _connStatus.Text = L.Tt("saved");
        _connStatus.ForeColor = Theme.Dim;
    }
}
