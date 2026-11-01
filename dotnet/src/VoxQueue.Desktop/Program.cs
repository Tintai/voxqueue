using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using VoxQueue.Core.I18n;
using VoxQueue.Core.Services;

namespace VoxQueue.Desktop;

internal static class Program
{
    /// <summary>Loaded once at start-up and saved on exit.</summary>
    public static Settings Settings { get; internal set; } = new();

    /// <summary>Where diagnostics go. A WinExe has no console, so anything we
    /// want a user to see has to land in a file.</summary>
    public static string LogDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxQueue");

    public static string LogPath => Path.Combine(LogDir, "startup.log");

    [STAThread]
    private static void Main(string[] args)
    {
        // Everything is wrapped: a WinExe that throws before the message loop
        // exits silently, which looks exactly like "the app does not start".
        try
        {
            InstallCrashHandlers();

            if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
            {
                Environment.ExitCode = UiSelfTest.Run();
                return;
            }

            if (args.Any(a => a.Equals("--diag", StringComparison.OrdinalIgnoreCase)))
            {
                WriteDiagnostics();
                return;
            }

            Log("main: start");

            // Theme.Dark has to be known before the native colour mode is picked,
            // and settings are read before ApplicationConfiguration.Initialize so
            // the very first window already has the right palette.
            Settings = Config.Load();
            Theme.Set(Settings.Theme switch
            {
                "light" => false,
                "dark" => true,
                _ => !SystemLight()
            });

            // ApplicationConfiguration.Initialize() is source-generated from the
            // csproj and already calls SetHighDpiMode, EnableVisualStyles and
            // SetCompatibleTextRenderingDefault. Calling them again throws:
            // SetCompatibleTextRenderingDefault is single-shot and throws
            // InvalidOperationException on the second call. That was the silent
            // death on start-up, so it is called exactly once now.
            ApplicationConfiguration.Initialize();

            // Dark native chrome: scrollbars, context menus and dialogs are drawn
            // by the OS and stay light otherwise, which is what made them stand
            // out inside the dark window. Must run before the first window exists.
            try
            {
                Application.SetColorMode(Theme.Dark ? SystemColorMode.Dark : SystemColorMode.Classic);
            }
            catch (Exception ex)
            {
                Log($"main: SetColorMode unavailable ({ex.GetType().Name}) — native chrome stays light");
            }

            Log("main: application initialized");

            L.Set(Settings.Language);
            Log($"main: settings loaded (lang={Settings.Language}, theme={Settings.Theme}, dark={Theme.Dark})");

            Log("main: constructing MainForm");
            var form = new MainForm();
            Log("main: MainForm constructed");

            Application.Run(form);
            Log("main: exited normally");
        }
        catch (Exception e)
        {
            Fatal(e);
        }
    }

    // ------------------------------------------------------------- diagnostics

    public static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(LogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch { /* logging must never be the reason the app dies */ }
    }

    private static void InstallCrashHandlers()
    {
        Application.ThreadException += (_, e) =>
        {
            Fatal(e.Exception, "UI thread");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Fatal(e.ExceptionObject as Exception, "AppDomain");
        };

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
    }

    private static void Fatal(Exception? e, string source = "startup")
    {
        var text = e?.ToString() ?? "unknown error";
        Log($"FATAL ({source}): {text}");

        try
        {
            var report = new StringBuilder();
            report.AppendLine("VoxQueue — błąd uruchomienia / startup error");
            report.AppendLine();
            report.AppendLine($"Źródło: {source}");
            report.AppendLine();
            report.AppendLine(text);
            report.AppendLine();
            report.AppendLine($"Pełny log: {LogPath}");
            report.AppendLine();
            report.AppendLine(EnvironmentInfo());

            MessageBox.Show(report.ToString(), "VoxQueue",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
            // Even the dialog can fail during a very early crash; the log file
            // above is then the only evidence, which is why it is written first.
        }
    }

    private static bool SystemLight()
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

    private static Icon? _appIcon;

    /// <summary>
    /// Loads the icon embedded in the assembly. Cached: creating an Icon from a
    /// stream on every call leaks the handle, and this is asked for once per
    /// window plus once for the taskbar.
    /// </summary>
    internal static Icon? LoadAppIcon()
    {
        if (_appIcon is not null) return _appIcon;
        try
        {
            var asm = typeof(Program).Assembly;
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.Contains("appicon", StringComparison.OrdinalIgnoreCase));
            if (name is null) return null;

            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) return null;

            // Icon(Stream) keeps a reference to the stream for later frames, so
            // the bytes are copied into a MemoryStream that stays alive.
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            _appIcon = new Icon(ms);
            return _appIcon;
        }
        catch (Exception e)
        {
            Log($"icon: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    internal static string EnvironmentInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine("--- środowisko ---");
        sb.AppendLine($"OS:        {Environment.OSVersion}");
        sb.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"64-bit exe:{Environment.Is64BitProcess}");
        sb.AppendLine($"CLR:       {Environment.Version}");
        sb.AppendLine($"katalog:   {AppContext.BaseDirectory}");
        sb.AppendLine($"exe:       {Environment.ProcessPath}");
        sb.AppendLine($"APPDATA:   {LogDir}");
        sb.AppendLine($"ffmpeg:    {AudioTools.FindFfmpeg() ?? "brak"}");
        return sb.ToString();
    }

    private static void WriteDiagnostics()
    {
        var text = EnvironmentInfo() + Environment.NewLine + $"(log: {LogPath})";
        Log("--diag requested");
        Log(EnvironmentInfo());

        try
        {
            Directory.CreateDirectory(LogDir);
            var diag = Path.Combine(LogDir, "diagnostics.txt");
            File.WriteAllText(diag, text, Encoding.UTF8);
            MessageBox.Show($"Zapisano:{Environment.NewLine}{diag}{Environment.NewLine}{Environment.NewLine}{text}",
                "VoxQueue — diagnostyka", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e)
        {
            MessageBox.Show(e.ToString(), "VoxQueue", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>
/// Checks that only make sense on Windows: the window builds, the tabs exist,
/// the icon loads and the panels survive a theme switch. A packaged exe that
/// fails here would fail for the user too.
///
/// The result goes to a file *and* a message box, because a WinExe has no
/// console — writing to stdout would show the user nothing at all.
/// </summary>
internal static class UiSelfTest
{
    private static int _failures;
    private static readonly StringBuilder Report = new();

    public static int Run()
    {
        Line("VoxQueue UI selftest");
        Line($"uruchomiono: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Line(Program.EnvironmentInfo());

        ApplicationConfiguration.Initialize();

        Program.Settings = new Settings
        {
            OutputDir = Path.Combine(Path.GetTempPath(), "vq_selftest_out")
        };
        L.Set("pl");

        Step("icon embedded", () =>
        {
            var names = typeof(Program).Assembly.GetManifestResourceNames();
            var hit = names.FirstOrDefault(n => n.Contains("appicon", StringComparison.OrdinalIgnoreCase));
            if (hit is null) throw new Exception("appicon not embedded; got: " + string.Join(", ", names));
            return hit;
        });

        MainForm? form = null;
        Step("main window", () =>
        {
            form = new MainForm();
            form.CreateControl();
            var tabs = form.Controls.OfType<FlatTabs>().FirstOrDefault();
            if (tabs is null) throw new Exception("tab strip missing");
            if (tabs.TabCount != 4) throw new Exception($"expected 4 tabs, got {tabs.TabCount}");
            return $"{tabs.TabCount} tabs";
        });

        // Constructing the panels is the check that matters most: a
        // size-dependent property set before layout throws in the constructor,
        // and that is how this app twice died before showing a window.
        // SplitterDistance / Panel*MinSize against a 150 px SplitContainer both
        // throw "must be between Panel1MinSize and Width - Panel2MinSize".
        // Constructing the panels is the check that matters most: a
        // size-dependent property set before layout throws in the constructor,
        // and that is how this app died three times before showing a window.
        // The sweep matters more than the build: the splitter is placed from
        // SizeChanged, so a width the arithmetic did not expect (a 150 px
        // constructed control, a squeezed window) is where it breaks.
        Step("work panels build + resize sweep", () =>
        {
            var sizes = new[] { 150, 200, 320, 480, 700, 1100, 1600, 2400 };
            var passed = 0;

            foreach (var mode in new[] { Core.Models.Mode.Train, Core.Models.Mode.Dub, Core.Models.Mode.Bulk })
            {
                var panel = new WorkPanel(mode);
                panel.CreateControl();

                foreach (var w in sizes)
                {
                    panel.Size = new Size(w, 700);
                    panel.PerformLayout();
                    Application.DoEvents();

                    // And back down again: growing is easy, shrinking is what
                    // hits "must be between Panel1MinSize and Width - Panel2MinSize".
                    panel.Size = new Size(w / 2 + 60, 420);
                    panel.PerformLayout();
                    Application.DoEvents();
                    passed++;
                }

                panel.Dispose();
            }

            return $"{passed} layouts across {sizes.Length} widths";
        });

        // Every button must stay reachable at any window width. The old row had
        // WrapContents=false, so the trailing buttons were clipped silently -
        // "Clear project" was simply not on screen and nothing said why.
        Step("all buttons fit at any width", () =>
        {
            var widths = new[] { 420, 560, 720, 900, 1200 };
            var checkedCount = 0;

            foreach (var mode in new[] { Core.Models.Mode.Train, Core.Models.Mode.Dub })
            {
                var panel = new WorkPanel(mode);
                panel.CreateControl();

                foreach (var w in widths)
                {
                    panel.Size = new Size(w, 800);
                    panel.PerformLayout();
                    Application.DoEvents();

                    foreach (var btn in Descendants(panel).OfType<FlatButton>())
                    {
                        if (!btn.Visible || btn.Width <= 0) continue;

                        // The button must sit inside its own container, and that
                        // container inside the panel.
                        var p2 = btn.PointToScreen(Point.Empty);
                        var panelOrigin = panel.PointToScreen(Point.Empty);
                        var right = p2.X - panelOrigin.X + btn.Width;
                        if (right > panel.ClientSize.Width + 2)
                            throw new Exception(
                                $"{mode} @{w}px: '{btn.Text}' ends at {right}, panel is {panel.ClientSize.Width}");
                        checkedCount++;
                    }
                }
                panel.Dispose();
            }
            return $"{checkedCount} button placements across {widths.Length} widths";
        });

        Step("settings panel builds", () =>
        {
            var panel = new SettingsPanel();
            panel.CreateControl();
            panel.Size = new Size(1100, 700);
            panel.PerformLayout();
            Application.DoEvents();
            panel.Dispose();
            return "ok";
        });

        Step("theme switch", () =>
        {
            if (form is null) throw new Exception("window not built");
            var apply = typeof(MainForm).GetMethod("ApplyTheme",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Program.Settings.Theme = "light";
            apply?.Invoke(form, null);
            Program.Settings.Theme = "dark";
            apply?.Invoke(form, null);
            return "light+dark";
        });

        // The editor must actually be on screen. A control that exists but is
        // never parented compiles, holds its text, and draws nothing — which is
        // how the pane came up empty while the list was full.
        // The bar must move visibly for ONE item out of ten thousand. Proportional
        // fill alone gives 0.01% — under a pixel — which is why it looked frozen
        // while clips were rendering. Checked numerically, no screen needed.
        Step("progress fill is visible at any scale", () =>
        {
            var bar = new FlatProgress { Width = 600 };

            bar.Maximum = 10000;
            bar.Value = 0;
            if (bar.FillWidthFor(599) != 0)
                throw new Exception("empty bar is not empty");

            bar.Value = 1;
            var one = bar.FillWidthFor(599);
            if (one < 3)
                throw new Exception($"1 of 10000 fills only {one}px");

            bar.Value = 5000;
            var half = bar.FillWidthFor(599);
            if (half < 290 || half > 310)
                throw new Exception($"half fills {half}px of 599");

            bar.Value = 10000;
            if (bar.FillWidthFor(599) != 599)
                throw new Exception("full bar is not full");

            // A one-item run must read as complete, not as 0%.
            bar.Maximum = 1;
            bar.Value = 1;
            if (bar.Fraction < 0.99) throw new Exception($"single item reads {bar.Fraction:P0}");

            return $"1/10000 -> {one}px, half -> {half}px";
        });

        Step("script editor is visible", () =>
        {
            if (form is null) throw new Exception("window not built");
            form.Size = new Size(1400, 900);
            form.PerformLayout();
            Application.DoEvents();

            var boxes = Descendants(form).OfType<ScriptBox>().ToList();
            if (boxes.Count == 0) throw new Exception("no ScriptBox in the window");

            var checkedBoxes = 0;
            foreach (var box in boxes)
            {
                var rtb = Descendants(box).OfType<RichTextBox>().FirstOrDefault();
                if (rtb is null) throw new Exception("ScriptBox has no RichTextBox child");
                if (box.Width <= 0 || box.Height <= 0)
                    throw new Exception($"ScriptBox has no size ({box.Width}x{box.Height})");
                if (rtb.Width <= 0 || rtb.Height <= 0)
                    throw new Exception($"editor has no size ({rtb.Width}x{rtb.Height})");
                if (!rtb.Visible) throw new Exception("editor is not visible");
                checkedBoxes++;
            }

            var bars = Descendants(form).OfType<FlatProgress>().ToList();
            if (bars.Count == 0) throw new Exception("no progress strip in the window");
            foreach (var b in bars)
                if (b.Fraction != 0) throw new Exception($"idle progress is not zero ({b.Fraction})");

            return $"{checkedBoxes} editors, {bars.Count} progress strip(s)";
        });

        // A control taller than its table row is drawn cut off at the bottom. That is
        // how the "..." button next to the output folder lost its lower edge: a
        // FlatButton measures itself at 32 px and sat in a row a text box made 26 px
        // tall. Checked by geometry, because reading the layout code did not reveal it.
        Step("no control is clipped by its row", () =>
        {
            if (form is null) throw new Exception("window not built");
            form.Size = new Size(1400, 900);
            form.PerformLayout();
            Application.DoEvents();

            var clipped = new List<string>();

            foreach (var table in Descendants(form).OfType<TableLayoutPanel>())
            {
                if (table.RowCount == 0 || table.Height <= 0) continue;

                var heights = table.GetRowHeights();
                if (heights.Length == 0) continue;

                // The rows themselves must fit the table. When a table defaults to an
                // AutoSize row it grows to its tallest child (32 px for a FlatButton)
                // even inside a 24 px cell, and the overflow is simply painted over by
                // whatever sits below. That is the "..." button losing its bottom edge
                // while every individual control still looked correctly parented.
                var needed = heights.Sum();
                var available = table.ClientSize.Height;

                if (available > 0 && needed > available + 1)
                    clipped.Add($"{table.Name} rows need {needed}px in {available}px");

                foreach (Control child in table.Controls)
                {
                    var row = table.GetRow(child);
                    if (row < 0 || row >= heights.Length) continue;

                    // A control that stretches vertically is never clipped by design.
                    if (child.Dock == DockStyle.Fill || child.Dock == DockStyle.Top
                        || child.Dock == DockStyle.Bottom) continue;

                    var fits = child.Anchor.HasFlag(AnchorStyles.Top)
                               && child.Anchor.HasFlag(AnchorStyles.Bottom);

                    if (fits) continue;

                    var room = heights[row] - child.Margin.Vertical;
                    if (room > 0 && child.Height > room + 1)
                        clipped.Add($"{child.GetType().Name} '{child.Text}' {child.Height}px in {room}px");
                }
            }

            if (clipped.Count > 0)
                throw new Exception($"{clipped.Count} clipped: {string.Join("; ", clipped.Take(4))}");

            return "every control fits its row";
        });

        // A caption that does not fit its button is drawn as "…" or not at all.
        // EndEllipsis made a one-glyph label vanish, and an AutoSize column could
        // collapse until there was no room left. Measured, not eyeballed.
        Step("button captions fit", () =>
        {
            if (form is null) throw new Exception("window not built");
            form.Size = new Size(1400, 900);
            form.PerformLayout();
            Application.DoEvents();

            var tight = new List<string>();

            foreach (var b in Descendants(form).OfType<FlatButton>())
            {
                if (b.Width <= 0 || string.IsNullOrEmpty(b.Text)) continue;

                var text = TextRenderer.MeasureText(b.Text, b.Font,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

                var usable = b.ClientSize.Width - 8;
                if (text.Width > usable)
                    tight.Add($"'{b.Text}' {text.Width}px in {usable}px");
            }

            if (tight.Count > 0)
                throw new Exception($"{tight.Count} caption(s) do not fit: {string.Join("; ", tight.Take(4))}");

            return "every caption fits its button";
        });

        // Switching to the light palette must leave no dark input surface behind.
        // Two separate causes made this fail at once: RichTextBox derives from
        // TextBoxBase and matched no case in the theme walker, and the log tab had no
        // ApplyTheme at all. Both looked like "the text area is still black".
        Step("light theme repaints every input", () =>
        {
            if (form is null) throw new Exception("window not built");

            var wasDark = Theme.Dark;
            var problems = new List<string>();

            try
            {
                Theme.Set(false);                       // light
                var apply = typeof(MainForm).GetMethod("ApplyTheme",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                apply?.Invoke(form, null);
                form.PerformLayout();
                Application.DoEvents();

                foreach (var box in Descendants(form).OfType<TextBoxBase>())
                {
                    if (box.Width <= 0 || box.Height <= 0) continue;

                    var b = box.BackColor;
                    var brightness = (b.R + b.G + b.B) / 3;

                    if (brightness < 128)
                        problems.Add($"{box.GetType().Name} background {b.Name} (brightness {brightness})");
                }
            }
            finally
            {
                Theme.Set(wasDark);
                var apply = typeof(MainForm).GetMethod("ApplyTheme",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                apply?.Invoke(form, null);
                Application.DoEvents();
            }

            if (problems.Count > 0)
                throw new Exception($"{problems.Count} dark input surface(s): {string.Join("; ", problems.Take(4))}");

            return "no dark text surface in light mode";
        });

        // Every caption must change when the language changes.
        //
        // The existing "language rebuild" step only proves that Retranslate does
        // not throw. It would have passed with half the window in the previous
        // language: exactly how one checkbox kept its Polish label inside an
        // English window while every other caption switched.
        Step("language switch rewrites every caption", () =>
        {
            if (form is null) throw new Exception("window not built");

            // Words spelled the same in both languages are not failures.
            var sameInBoth = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "start", "status", "log", "system", "search", "voxqueue" };

            List<string> Captions()
            {
                var list = new List<string>();

                foreach (var c in Descendants(form))
                {
                    var text = c switch
                    {
                        CheckBox cb => cb.Text,
                        Button b => b.Text,
                        Label lb => lb.Text,
                        _ => null
                    };

                    if (!string.IsNullOrWhiteSpace(text)) list.Add(text!);
                }

                return list;
            }

            var saved = L.Code;
            try
            {
                L.Set("pl");
                var apply = typeof(MainForm).GetMethod("Retranslate",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                apply?.Invoke(form, null);
                Application.DoEvents();
                var before = Captions();

                L.Set("de");
                apply?.Invoke(form, null);
                Application.DoEvents();
                var after = Captions();

                var stuck = new List<string>();
                for (var i = 0; i < Math.Min(before.Count, after.Count); i++)
                {
                    var a = before[i];
                    var b = after[i];

                    if (!a.Any(char.IsLetter)) continue;
                    if (a.Contains('{')) continue;
                    if (sameInBoth.Contains(a)) continue;
                    if (string.Equals(a, b, StringComparison.Ordinal)) stuck.Add(a);
                }

                if (stuck.Count > 0)
                    throw new Exception($"{stuck.Count} caption(s) did not switch: " +
                                        string.Join(", ", stuck.Take(5)));

                return $"{before.Count} captions pl -> de";
            }
            finally
            {
                L.Set(saved);
                var apply = typeof(MainForm).GetMethod("Retranslate",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                apply?.Invoke(form, null);
                Application.DoEvents();
            }
        });

        Step("language rebuild", () =>
        {
            if (form is null) throw new Exception("window not built");
            var m = typeof(MainForm).GetMethod("Retranslate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            foreach (var code in new[] { "en", "pl", "de", "ja", "zh" })
            {
                L.Set(code);
                m?.Invoke(form, null);
            }
            L.Set("pl");
            return "5 langs";
        });

        form?.Dispose();

        Line("");
        var ok = _failures == 0;
        Line(ok ? "WYNIK: ALL OK" : $"WYNIK: {_failures} błąd(ów)");

        // A WinExe cannot print to a console the user can see, so the report is
        // written to disk and shown in a dialog.
        try
        {
            Directory.CreateDirectory(Program.LogDir);
            var path = Path.Combine(Program.LogDir, "selftest.log");
            File.WriteAllText(path, Report.ToString(), Encoding.UTF8);
            MessageBox.Show(Report + Environment.NewLine + $"Zapisano: {path}",
                "VoxQueue — selftest", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch
        {
            MessageBox.Show(Report.ToString(), "VoxQueue — selftest");
        }

        return ok ? 0 : 1;
    }

    private static void Line(string text)
    {
        Report.AppendLine(text);
        try { Console.WriteLine(text); } catch { }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    private static void Step(string name, Func<string> fn)
    {
        try
        {
            var detail = fn();
            Line($"  OK   {name}" + (string.IsNullOrEmpty(detail) ? "" : $"  ({detail})"));
        }
        catch (Exception e)
        {
            _failures++;
            Line($"  FAIL {name}: {e.GetType().Name}: {e.Message}");
            Line("       " + (e.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? ""));
        }
    }
}
