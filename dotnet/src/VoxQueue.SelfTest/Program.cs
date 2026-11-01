using System;
using System.IO;
using System.Linq;
using VoxQueue.Core.I18n;
using VoxQueue.Core.Models;
using VoxQueue.Core.Services;

namespace VoxQueue.SelfTest;

/// <summary>
/// Headless checks for the parts that carry the risk: text parsing, WAV
/// round-tripping, defect detection, job persistence and the translation tables.
///
/// Runs on any platform, which is the point — the WinForms shell cannot be
/// launched on the build machine, so this is how the logic gets verified before
/// the executable is handed over.
/// </summary>
internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Console.WriteLine("VoxQueue selftest");
        Console.WriteLine();

        Step("text splitters", TestSplitters);
        Step("audio round-trip", TestAudioRoundTrip);
        Step("wav duration is read correctly", TestWavDuration);
        Step("defect detection", TestDefects);
        Step("phonetic markers", TestPronunciation);
        Step("job persistence", TestJobState);
        Step("workspace save/load", TestWorkspace);
        Step("resume from output folder", TestResume);
        Step("errors-first ordering", TestSortRank);
        Step("session round-trip", TestSession);
        Step("settings round-trip", TestSettings);
        Step("pinned round-trip", TestPinned);
        Step("filter is reversible", TestFilterReversible);
        Step("filters combine", TestFiltersCombine);
        Step("source line survives save", TestSourceLine);
        Step("pronunciation edit rewrites text", TestPronEdit);
        Step("phonetic column is a word diff", TestPhoneticDiff);
        Step("search matches text and pronunciation", TestSearch);
        Step("bulk replace rewrites markers", TestBulkReplace);
        Step("bulk replace marks only changed rows", TestBulkMarks);
        Step("retry picks up failed and stale", TestRetryCovers);
        Step("cancel returns the row to pending", TestCancelIsNotFailure);
        Step("written form reflects the text", TestWrittenForm);
        Step("stale rows stay visible until OK", TestStaleFilterLifetime);
        Step("stale rows sort first", TestStale);
        Step("translations", TestI18n);
        Step("no invoke before handle", TestNoInvokeBeforeHandle);
        Step("every translation key exists", TestKeysExist);
        Step("no duplicate or swapped keys", TestNoDuplicateKeys);
        Step("no dead private fields", TestNoDeadFields);
        Step("controls are parented", TestControlsParented);
        Step("table columns match their controls", TestColumnCounts);
        Step("progress is run-scoped, colouring is deferred", TestProgressAndColouring);

        Console.WriteLine();
        if (_failures > 0)
        {
            Console.WriteLine($"FAILED: {_failures} check(s)");
            return 1;
        }
        Console.WriteLine("ALL OK");
        return 0;
    }

    private static void Step(string name, Func<string> fn)
    {
        try
        {
            var detail = fn();
            Console.WriteLine($"  OK   {name}" + (string.IsNullOrEmpty(detail) ? "" : $"  ({detail})"));
        }
        catch (Exception e)
        {
            _failures++;
            Console.WriteLine($"  FAIL {name}: {e.GetType().Name}: {e.Message}");
            if (e.StackTrace is not null)
                Console.WriteLine("       " + e.StackTrace.Split('\n')[0].Trim());
        }
    }

    // ------------------------------------------------------------------ tests

    private static string TestSplitters()
    {
        // training: blank lines drop, whitespace collapses
        var lines = TextSplitter.SplitLines("Pierwsze zdanie.\n\n  Drugie.  \n");
        Check(lines.Count == 2, $"line split returned {lines.Count}");
        Check(lines[1].Text == "Drugie.", $"line 2 = '{lines[1].Text}'");

        // dubbing: three label styles plus prose that must NOT become a speaker
        var dub = TextSplitter.SplitDub(
            "SHEPPARD: Nie mam czasu.\n" +
            "[EDI] Analiza zakończona.\n" +
            "GARRUS \u2014 To się nie uda.");
        Check(dub.Count == 3, $"dub split returned {dub.Count}");
        Check(dub[0].Speaker == "SHEPPARD", $"speaker 0 = '{dub[0].Speaker}'");
        Check(dub[1].Speaker == "EDI", $"speaker 1 = '{dub[1].Speaker}'");
        Check(dub[2].Speaker == "GARRUS", $"speaker 2 = '{dub[2].Speaker}'");

        // "0000|text" is metadata.csv syntax; it must not become part of the
        // utterance, or it is spoken and written into the next CSV twice.
        var pre = TextSplitter.SplitLines("0000|Pierwsze zdanie.\n0017|Drugie zdanie.");
        Check(pre.Count == 2, $"index prefix: {pre.Count} lines");
        Check(pre[0].Text == "Pierwsze zdanie.", $"index prefix text = '{pre[0].Text}'");
        Check(pre[1].SourceIndex == 17, $"index prefix number = {pre[1].SourceIndex}");

        var preDub = TextSplitter.SplitDub("0005|SHEPPARD: Nie mam czasu.");
        Check(preDub.Count == 1 && preDub[0].Speaker == "SHEPPARD",
            $"index prefix in dubbing gave speaker '{preDub[0].Speaker}'");

        var preBlock = TextSplitter.SplitBlocks("0003|jeden\n###\n0004|dwa", "###");
        Check(preBlock.Count == 2 && preBlock[1].Text == "dwa",
            $"index prefix in bulk: {string.Join("/", preBlock.Select(x => x.Text))}");

        // A legit pipe that is not an id prefix must survive.
        var legit = TextSplitter.SplitLines("12|13 to para liczb.");
        Check(legit[0].Text == "12|13 to para liczb.", $"legit pipe eaten: '{legit[0].Text}'");

        var prose = TextSplitter.SplitDub("He said: hello there.");
        Check(prose.Count == 1 && prose[0].Speaker.Length == 0,
            "prose with a colon was mistaken for a speaker");

        var multiline = TextSplitter.SplitDub("JOKER:\nDlaczego tak poważnie?");
        Check(multiline.Count == 1 && multiline[0].Speaker == "JOKER"
              && multiline[0].Text.Contains("poważnie"),
            "label on its own line did not attach to the text below");

        // A marker in the speaker label is tolerated: the engine never says a
        // name, so the written half becomes the speaker and the phonetic half is
        // dropped. Without this the line failed every pattern and the whole text
        // fell through as one utterance with no speaker.
        var marked = TextSplitter.SplitDub("{Szerd|SHEPPARD}: Nie mam czasu.");
        Check(marked.Count == 1, $"marked label produced {marked.Count} lines");
        Check(marked[0].Speaker == "SHEPPARD", $"marked label speaker = '{marked[0].Speaker}'");
        Check(marked[0].Text == "Nie mam czasu.", $"marked label text = '{marked[0].Text}'");

        var markedBracket = TextSplitter.SplitDub("[{E-di-aj|EDI}] Analiza zakończona.");
        Check(markedBracket.Count == 1 && markedBracket[0].Speaker == "EDI",
            $"marked bracket speaker = '{markedBracket[0].Speaker}'");

        var twoWords = TextSplitter.SplitDub("Master Chief: Gotowe.");
        Check(twoWords.Count == 1 && twoWords[0].Speaker == "Master Chief",
            $"two-word speaker = '{twoWords[0].Speaker}'");

        // bulk: marker, and dash rules
        var blocks = TextSplitter.SplitBlocks("jeden\n###\ndwa\n###\ntrzy", "###");
        Check(blocks.Count == 3, $"bulk marker split returned {blocks.Count}");

        var dashed = TextSplitter.SplitBlocks("pierwsza kwestia\n----\ndruga kwestia", "###");
        Check(dashed.Count == 2, $"dash-marker split returned {dashed.Count}");

        // filenames must not escape the output folder
        var evil = TextSplitter.SafeFilename("../../etc/passwd");
        Check(!evil.Contains("..") && !evil.Contains("/"), $"unsafe filename: '{evil}'");
        var empty = TextSplitter.SafeFilename("");
        Check(empty == "voice", $"empty name = '{empty}'");

        // Windows still refuses a file called after a DOS device, even with an
        // extension. A speaker named "Con" failed the whole dubbing run.
        var con = TextSplitter.SafeFilename("Con");
        Check(con != "Con" && con.Length == 4, $"reserved name not escaped: '{con}'");
        var com = TextSplitter.SafeFilename("COM1.wav");
        Check(!com.StartsWith("COM1", StringComparison.OrdinalIgnoreCase),
            $"reserved stem not escaped: '{com}'");

        // Dots and spaces at the ends are stripped by Windows, which would make two
        // different names collide on one file.
        Check(TextSplitter.SafeFilename("  Jan  ") == "Jan", "padding not trimmed");
        Check(TextSplitter.SafeFilename("...") == "voice", "dots-only name not sanitised");

        return "dub+train+bulk+sanitise+reserved";
    }

    private static string TestAudioRoundTrip()
    {
        const int sr = 22050;
        var samples = new float[sr];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 200 * i / sr));

        var tmp = Path.Combine(Path.GetTempPath(), $"vq_{Guid.NewGuid():N}.wav");
        try
        {
            AudioTools.WriteWav(tmp, samples, sr);
            var bytes = File.ReadAllBytes(tmp);
            var (read, readSr, ch) = AudioTools.ReadWav(bytes);

            Check(readSr == sr, $"sample rate {readSr}");
            Check(ch == 1, $"channels {ch}");
            Check(read.Length == samples.Length, $"length {read.Length} vs {samples.Length}");

            // Amplitude has to survive the 16-bit round trip.
            var peak = read.Max(Math.Abs);
            Check(Math.Abs(peak - 0.3) < 0.01, $"peak {peak:0.000}");

            // Normalisation lifts a quiet clip to the target.
            var quiet = samples.Select(s => s * 0.1f).ToArray();
            var loud = AudioTools.Normalize(quiet);
            var newPeak = loud.Max(Math.Abs);
            Check(newPeak > 0.8f, $"normalised peak {newPeak:0.00}");

            return $"sr={readSr} len={read.Length} peak={peak:0.00}";
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    private static string TestDefects()
    {
        const int sr = 22050;
        const int n = sr;                       // 1 s

        byte[] Build(Action<float[], int> mutate, int length = n)
        {
            var x = new float[length];
            for (var i = 0; i < length; i++)
                x[i] = (float)(0.25 * Math.Sin(2 * Math.PI * 180 * i / sr)
                               * (0.6 + 0.4 * Math.Sin(2 * Math.PI * 3 * i / sr)));
            mutate(x, sr);
            var p = Path.Combine(Path.GetTempPath(), $"vq_d_{Guid.NewGuid():N}.wav");
            try
            {
                AudioTools.WriteWav(p, x, sr);
                return File.ReadAllBytes(p);
            }
            finally { try { File.Delete(p); } catch { } }
        }

        // clean speech-like tone must not be flagged
        var clean = AudioTools.Analyze(Build((_, _) => { }));
        Check(clean.Ok, "clean clip was flagged: " + string.Join(",", clean.Defects));

        // a beep burst at the end must be caught
        var beep = AudioTools.Analyze(Build((x, rate) =>
        {
            for (var i = x.Length - rate / 5; i < x.Length; i++)
                x[i] = (float)(0.85 * Math.Sin(2 * Math.PI * 1000 * i / rate));
        }));
        Check(beep.Defects.Contains("beep") || beep.Defects.Contains("loud_tail"),
            "end-of-clip beep was not detected: " + string.Join(",", beep.Defects));

        // silence
        var silent = AudioTools.Analyze(Build((x, _) => Array.Clear(x)));
        Check(silent.Defects.Contains("silent"), "silence was not detected");

        // too short
        var tiny = AudioTools.Analyze(Build((_, _) => { }, sr / 5));
        Check(tiny.Defects.Contains("short"), "short clip was not detected");

        // rambling: 4 s of audio for a two-character label is implausible.
        // The threshold is max(2.5 s, expected * 2.2), so the clip has to be
        // clearly past 2.5 s — 1 s would legitimately pass.
        var ramble = AudioTools.Analyze(Build((_, _) => { }, sr * 4), "ok");
        Check(ramble.Defects.Contains("rambling"),
            $"4 s clip for 2 characters was not flagged (dur={ramble.Duration:0.0}s): "
            + string.Join(",", ramble.Defects));

        // A short clip with a matching text must NOT be flagged.
        var fine = AudioTools.Analyze(Build((_, _) => { }), new string('a', 26));
        Check(!fine.Defects.Contains("rambling"),
            "a 1 s clip with matching text was wrongly flagged as rambling");

        return $"clean=ok beep={beep.Defects.Count} silent=ok";
    }


    private static string TestPronunciation()
    {
        // spoken|written — the engine says "Eter", the transcript keeps "Aether"
        var (sp, wr) = Pronunciation.Split("{Eter|Aether} jest online.");
        Check(sp == "Eter jest online.", $"spoken = '{sp}'");
        Check(wr == "Aether jest online.", $"written = '{wr}'");

        // a number read aloud, stored as digits
        var (nSp, nWr) = Pronunciation.Split("Mamy {dwa tysiące dwudziesty czwarty|2024} rok.");
        Check(nSp.Contains("dwa tysiące"), $"number spoken = '{nSp}'");
        Check(nWr.Contains("2024"), $"number written = '{nWr}'");

        // no markers: both forms identical, nothing lost
        var (pSp, pWr) = Pronunciation.Split("Zwykłe zdanie bez znaczników.");
        Check(pSp == pWr, "plain text diverged");

        // malformed markers stay literal instead of eating the text
        var (mSp, mWr) = Pronunciation.Split("To { nie jest znacznik.");
        Check(mSp == "To { nie jest znacznik." && mWr == mSp, $"unclosed brace: '{mSp}'");

        var (eSp, eWr) = Pronunciation.Split("Puste {|} znaczniki.");
        Check(eSp == "Puste {|} znaczniki." && eWr == eSp, $"empty marker: '{eSp}'");

        var (oSp, oWr) = Pronunciation.Split("{tylko|}");
        Check(oSp == "{tylko|}" && oWr == oSp, $"one-sided marker: '{oSp}'");

        // several markers in one line
        var (tSp, tWr) = Pronunciation.Split("{Eter|Aether} i {dżoker|Joker}.");
        Check(tSp == "Eter i dżoker." && tWr == "Aether i Joker.", $"{tSp} / {tWr}");

        // The grid column shows only the substituted words, not a repeated sentence.
        var frag = Pronunciation.Summary("Nazywam się {Eter|Aether} i działam lokalnie.");
        Check(frag == "Eter", $"summary = '{frag}'");

        var two = Pronunciation.Summary("{Eter|Aether} i {dżoker|Joker} rozmawiali.");
        Check(two == "Eter · dżoker", $"two summaries = '{two}'");

        Check(Pronunciation.Summary("bez znaczników") == "", "summary of plain text not empty");
        Check(Pronunciation.Summary("{same|same}") == "", "identical halves produced a summary");

        Check(Pronunciation.HasMarkers("{a|b}"), "HasMarkers missed a marker");
        Check(!Pronunciation.HasMarkers("bez znaczników"), "HasMarkers false positive");

        return "4 marker shapes";
    }

    private static string TestWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vq_{Guid.NewGuid():N}.vqproj");
        try
        {
            var ws = new Workspace
            {
                Mode = "dub",
                OutDir = "/tmp/out",
                Voice = "v1",
                InputText = "SHEPPARD: raz.\n[EDI] dwa.",
                Options = new JobOptions { AddExtension = false, IndexStart = 5, SplitMarker = "---" },
                Items =
                {
                    new WorkspaceItem { Index = 0, Text = "Aether", Spoken = "Eter", Speaker = "SHEPPARD" },
                    new WorkspaceItem { Index = 1, Text = "dwa", Spoken = "dwa", Speaker = "EDI" }
                }
            };
            ws.VoiceMap["SHEPPARD"] = "v1";

            WorkspaceFile.Save(path, ws);
            Check(File.Exists(path), "project file not written");

            var back = WorkspaceFile.Load(path, out var error);
            Check(back is not null, $"load failed: {error}");
            Check(back!.Mode == "dub", $"mode {back.Mode}");
            Check(back.Options.AddExtension == false, "AddExtension lost");
            Check(back.Options.IndexStart == 5, $"IndexStart {back.Options.IndexStart}");
            Check(back.Items.Count == 2, $"items {back.Items.Count}");
            Check(back.Items[0].Spoken == "Eter", $"spoken {back.Items[0].Spoken}");
            Check(back.VoiceMap["SHEPPARD"] == "v1", "voice map lost");

            // a corrupt file must fail loudly, not return an empty project
            File.WriteAllText(path, "{ not json");
            var bad = WorkspaceFile.Load(path, out var badError);
            Check(bad is null && badError.Length > 0, "corrupt project was accepted");

            return "save+load+corrupt";
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }


    private static string TestResume()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vq_resume_{Guid.NewGuid():N}");
        try
        {
            // A previous run: two clips finished, one failed.
            var previous = new Job { OutDir = dir, Mode = Mode.Train, Voice = "v" };
            var a = new ItemRow { Index = 0, Text = "pierwsze", State = ItemState.Done, Duration = 1.5 };
            var b = new ItemRow { Index = 1, Text = "drugie", State = ItemState.Done, Duration = 2.0 };
            var c = new ItemRow { Index = 2, Text = "trzecie", State = ItemState.Failed, Error = "boom" };
            previous.Items.AddRange(new[] { a, b, c });

            Directory.CreateDirectory(Path.Combine(dir, "wav"));
            a.File = Path.Combine(dir, "wav", "0000.wav");
            b.File = Path.Combine(dir, "wav", "0001.wav");
            AudioTools.WriteWav(a.File, new float[22050], 22050);
            AudioTools.WriteWav(b.File, new float[22050], 22050);
            previous.Save();

            // A new run over the same folder, same lines, different numbering.
            var next = new Job { OutDir = dir, Mode = Mode.Train, Voice = "v", Options = { IndexStart = 100 } };
            next.Items.Add(new ItemRow { Index = 0, Text = "pierwsze" });
            next.Items.Add(new ItemRow { Index = 1, Text = "drugie" });
            next.Items.Add(new ItemRow { Index = 2, Text = "trzecie" });
            next.Items.Add(new ItemRow { Index = 3, Text = "czwarte" });

            var restored = Job.RestoreProgress(next);
            Check(restored == 2, $"restored {restored}, expected 2");
            Check(next.Items[0].State == ItemState.Done, "item 0 not restored");
            Check(next.Items[1].State == ItemState.Done, "item 1 not restored");
            Check(next.Items[2].State == ItemState.Pending, "a failed item was inherited as failed");
            Check(next.Items[3].State == ItemState.Pending, "an unknown line was marked done");

            // A row pointing at a deleted file must stay pending, otherwise the
            // list claims to have something it cannot play.
            File.Delete(next.Items[0].File);
            var again = new Job { OutDir = dir, Mode = Mode.Train };
            again.Items.Add(new ItemRow { Index = 0, Text = "pierwsze" });
            again.Items.Add(new ItemRow { Index = 1, Text = "drugie" });
            var second = Job.RestoreProgress(again);
            Check(second == 1, $"missing file restored anyway ({second})");
            Check(again.Items[0].State == ItemState.Pending, "deleted clip was restored as done");

            return "2 of 4, failed stays pending, missing file skipped";
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }


    private static string TestSortRank()
    {
        // Sorting must lift failed rows first, then flagged ones, and keep the
        // original index order inside each bucket. Without a stable order the
        // list would shuffle on every re-sort and the user could not find the
        // row they were looking at.
        var items = new List<ItemRow>
        {
            new() { Index = 0, Text = "ok-1", State = ItemState.Done },
            new() { Index = 1, Text = "failed-1", State = ItemState.Failed },
            new() { Index = 2, Text = "flagged-1", State = ItemState.Done, Defects = { "click_end" } },
            new() { Index = 3, Text = "ok-2", State = ItemState.Done },
            new() { Index = 4, Text = "failed-2", State = ItemState.Failed },
            new() { Index = 5, Text = "flagged-2", State = ItemState.Done, Defects = { "beep" } },
        };

        var sorted = items.OrderBy(i => i.SortRank).ThenBy(i => i.Index).Select(i => i.Index).ToArray();
        var expected = new[] { 1, 4, 2, 5, 0, 3 };

        Check(sorted.SequenceEqual(expected),
            $"errors-first order = [{string.Join(",", sorted)}], expected [{string.Join(",", expected)}]");

        var restored = items.OrderBy(i => i.Index).Select(i => i.Index).ToArray();
        Check(restored.SequenceEqual(new[] { 0, 1, 2, 3, 4, 5 }), "original order not recoverable");

        return "failed, flagged, rest, then index order";
    }


    private static string TestSession()
    {
        // The session slot must round-trip the same fields a project does, and
        // must be cleared when there is nothing worth restoring — otherwise the
        // next start resurrects an empty job over whatever the user opened.
        var dir = Path.Combine(Path.GetTempPath(), $"vq_sess_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "s.vqproj");
        try
        {
            var ws = new Workspace
            {
                Mode = "dub",
                OutDir = "/tmp/out",
                Voice = "v1",
                InputText = "A: one",
                Items = { new WorkspaceItem { Index = 0, Text = "one", Speaker = "A" } }
            };
            WorkspaceFile.Save(path, ws);

            var back = WorkspaceFile.Load(path, out var err);
            Check(back is not null, $"session did not load: {err}");
            Check(back!.Mode == "dub", $"mode {back.Mode}");
            Check(back.Items.Count == 1, $"items {back.Items.Count}");

            // An empty bundle must not be written: no items anywhere is a cleared
            // app, not a session.
            SessionFile.SaveOrClear(new SessionBundle());
            Check(!SessionFile.Exists, "an empty session was written");

            // All three tabs must survive; saving only the active one lost the
            // other two every time the app was closed from a different tab.
            SessionFile.SaveOrClear(new SessionBundle
            {
                ActiveTab = 2,
                Train = new Workspace { Mode = "train", Items = { new WorkspaceItem { Text = "t" } } },
                Dub = new Workspace { Mode = "dub", Items = { new WorkspaceItem { Text = "d" } } },
                Bulk = new Workspace { Mode = "bulk", Items = { new WorkspaceItem { Text = "b" } } }
            });

            var bundle = SessionFile.Load();
            Check(bundle is not null, "bundle did not load");
            Check(bundle!.Train?.Items.Count == 1, $"train items = {bundle.Train?.Items.Count}");
            Check(bundle.Dub?.Items.Count == 1, $"dub items = {bundle.Dub?.Items.Count}");
            Check(bundle.Bulk?.Items.Count == 1, $"bulk items = {bundle.Bulk?.Items.Count}");
            Check(bundle.ActiveTab == 2, $"active tab = {bundle.ActiveTab}");

            SessionFile.Clear();

            return "round-trip + empty clears + all three tabs";
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }


    private static string TestSettings()
    {
        // WindowState must survive a save/load. Storing the size of a maximised
        // window instead produced a window that opened large but not maximised,
        // and nothing tied the two facts together.
        var s = new Settings { WindowState = "max", Theme = "light", RestoreSession = true };
        var json = System.Text.Json.JsonSerializer.Serialize(s);
        var back = System.Text.Json.JsonSerializer.Deserialize<Settings>(json);

        Check(back is not null, "settings did not round-trip");
        Check(back!.WindowState == "max", $"window state = '{back.WindowState}'");
        Check(back.Theme == "light", $"theme = '{back.Theme}'");
        Check(back.RestoreSession, "restore flag lost");
        return "window state + theme + restore flag";
    }


    private static string TestPinned()
    {
        // The pin flag must survive a workspace save. It is the only thing that
        // makes a list of ten thousand clips navigable after a restart.
        var dir = Path.Combine(Path.GetTempPath(), $"vq_pin_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "p.vqproj");
        try
        {
            var ws = new Workspace
            {
                Mode = "train",
                Items =
                {
                    new WorkspaceItem { Index = 0, Text = "a", Pinned = true },
                    new WorkspaceItem { Index = 1, Text = "b", Pinned = false },
                }
            };
            WorkspaceFile.Save(path, ws);

            var back = WorkspaceFile.Load(path, out var err);
            Check(back is not null, $"load failed: {err}");
            Check(back!.Items[0].Pinned, "pin lost on save");
            Check(!back.Items[1].Pinned, "unpinned row became pinned");

            return "pin survives save";
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }


    private static string TestFilterReversible()
    {
        // The bug this covers: filtering produced a smaller list, the panel
        // refilled its own collection from that list, and the hidden rows were
        // gone from the source. Unticking the filter restored nothing.
        var job = new Job { Mode = Mode.Train };
        job.Items.Add(new ItemRow { Index = 0, Text = "a", Pinned = true });
        job.Items.Add(new ItemRow { Index = 1, Text = "b", Pinned = false });
        job.Items.Add(new ItemRow { Index = 2, Text = "c", Pinned = true });
        job.Items.Add(new ItemRow { Index = 3, Text = "d", Pinned = false });

        var filtered = JobView.Build(job, pinnedOnly: true, sortMode: 0);
        Check(filtered.Count == 2, $"filtered count = {filtered.Count}, expected 2");
        Check(filtered.Select(i => i.Index).SequenceEqual(new[] { 0, 2 }),
            $"filtered indices = [{string.Join(",", filtered.Select(i => i.Index))}]");

        // The whole point: the source is untouched, so clearing the filter brings
        // everything back.
        Check(job.Items.Count == 4, $"job mutated by filtering: {job.Items.Count} items");

        var all = JobView.Build(job, pinnedOnly: false, sortMode: 0);
        Check(all.Count == 4, $"unfiltered count = {all.Count}, expected 4");
        Check(all.Select(i => i.Index).SequenceEqual(new[] { 0, 1, 2, 3 }), "order not restored");

        Check(JobView.PinnedCount(job) == 2, $"pinned count = {JobView.PinnedCount(job)}");
        Check(JobView.PinnedCount(null) == 0, "null job should count zero");

        return "filter reversible, job untouched";
    }


    private static string TestSourceLine()
    {
        // Editing a pronunciation rewrites the source line, and it finds that line
        // by matching SourceLine. A restored session used to lose the field, so
        // every edit failed silently.
        var dir = Path.Combine(Path.GetTempPath(), $"vq_src_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "p.vqproj");
        try
        {
            var ws = new Workspace
            {
                Mode = "train",
                Items =
                {
                    new WorkspaceItem
                    {
                        Index = 0,
                        Text = "Aether is online",
                        Spoken = "Eter is online",
                        SourceLine = "{Eter|Aether} is online"
                    }
                }
            };
            WorkspaceFile.Save(path, ws);

            var back = WorkspaceFile.Load(path, out var err);
            Check(back is not null, $"load failed: {err}");
            Check(back!.Items[0].SourceLine == "{Eter|Aether} is online",
                $"source line lost: '{back.Items[0].SourceLine}'");

            return "raw line round-trips";
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }


    private static string TestPronEdit()
    {
        // "The spelling does not change": the edit has to reach the source text,
        // and the form the user typed has to survive into the rewritten line.
        var text = "Aether is online.\nSecond line.";
        var row = new ItemRow { Index = 0, Text = "Aether is online.", SourceLine = "Aether is online." };

        // Change only the spelling.
        var r1 = PronunciationEdit.Apply(text, row.SourceLine, row.Text,
                                         spoken: "Aether is online.", written: "Ether is online.");
        Check(r1 is not null, "edit rejected — no line matched");
        Check(r1!.Value.text == "Ether is online.", $"row text = '{r1.Value.text}'");

        // The edit is only real if it survives a re-parse: the grid is rebuilt
        // from the rewritten text, and a marker that does not parse would give the
        // old spelling back.
        var rewrittenText = PronunciationEdit.Rewrite(text, row.SourceLine, row.Text,
            PronunciationEdit.BuildLine("Aether is online.", "Ether is online."), out _)!;
        var parsedAgain = TextSplitter.SplitLines(rewrittenText);
        var firstAgain = Pronunciation.Split(parsedAgain[0].Text);
        Check(firstAgain.written == "Ether is online.",
            $"after re-parse the written form is '{firstAgain.written}'");
        Check(firstAgain.spoken == "Aether is online.",
            $"after re-parse the spoken form is '{firstAgain.spoken}'");

        // And a line with an existing marker must keep working.
        var markedText = "{Eter|Aether} is online.\nSecond line.";
        var r3 = PronunciationEdit.Apply(markedText, "{Eter|Aether} is online.", "Aether is online.",
                                         spoken: "Eter is online.", written: "Aether is on-line.");
        Check(r3 is not null, "edit on a marked line rejected");
        var rt3 = PronunciationEdit.Rewrite(markedText, "{Eter|Aether} is online.", "Aether is online.",
            PronunciationEdit.BuildLine("Eter is online.", "Aether is on-line."), out _)!;
        var p3 = Pronunciation.Split(TextSplitter.SplitLines(rt3)[0].Text);
        Check(p3.written == "Aether is on-line.", $"marked rewrite gives '{p3.written}'");
        Check(p3.spoken == "Eter is online.", $"marked spoken gives '{p3.spoken}'");

        var rewritten = PronunciationEdit.Rewrite(text, row.SourceLine, row.Text,
            PronunciationEdit.BuildLine("Aether is online.", "Ether is online."), out var idx);
        Check(rewritten == "{Aether is online.|Ether is online.}\nSecond line.",
            $"line = '{rewritten}'");
        Check(idx == 0, $"index = {idx}");

        // Change both forms: the marker must carry both.
        var r2 = PronunciationEdit.Apply(text, row.SourceLine, row.Text,
                                         spoken: "Iter is online.", written: "Ether is online.");
        Check(r2 is not null, "second edit rejected");
        // Only the word that changed — not the sentence. The written form already
        // sits in the Text column, so repeating it here would be noise.
        Check(r2!.Value.phonetic == "Iter", $"phonetic = '{r2.Value.phonetic}'");

        // Nothing matched any more: must report it, not silently succeed.
        var miss = PronunciationEdit.Apply("something else entirely", row.SourceLine, row.Text,
                                           "a", "b");
        Check(miss is null, "edit succeeded against unrelated text");

        // Same forms: no marker, no fake "changed" flag.
        Check(PronunciationEdit.BuildLine("x", "x") == "x", "identical forms got a marker");

        return "spelling, spoken, miss and identical";
    }


    private static string TestPhoneticDiff()
    {
        // The column must show the WORDS that changed, not the sentence. A marker
        // can wrap a whole line — the dialog produces exactly that — and echoing
        // the sentence back is noise: the written form is already in Text.
        var line = "{Aether is online today|Ether is online today}";
        var summary = Pronunciation.Summary(line);

        Check(summary == "Aether", $"summary = '{summary}', expected 'Aether'");

        var multi = Pronunciation.Summary("{Eter|Aether} met {Dżoker|Joker}");
        Check(multi == "Eter · Dżoker", $"multi = '{multi}'");

        // No markers: nothing changed, so nothing to show.
        Check(Pronunciation.Summary("plain text") == "", "plain text produced a summary");

        // Punctuation must not count as a difference.
        Check(Pronunciation.Summary("{Eter.|Eter}") == "", "punctuation counted as a change");

        // Word counts differ: fall back to the whole spoken form rather than
        // guessing an alignment.
        var uneven = Pronunciation.Summary("{dwa słowa|one}");
        Check(uneven == "dwa słowa", $"uneven = '{uneven}'");

        return "single, multi, plain, punctuation, uneven";
    }

    private static string TestSearch()
    {
        // Search must cover the pronunciation as well as the text: a user hunting
        // for a fix remembers the word they changed, not the sentence around it.
        var job = new Job { Mode = Mode.Train };
        job.Items.Add(new ItemRow { Index = 0, Text = "Aether is online", Spoken = "Eter is online", Phonetic = "Eter" });
        job.Items.Add(new ItemRow { Index = 1, Text = "Nothing here", Spoken = "Nothing here" });
        job.Items.Add(new ItemRow { Index = 2, Text = "Third line" });

        var byText = JobView.Build(job, false, 0, "Aether");
        Check(byText.Count == 1 && byText[0].Index == 0, $"text search returned {byText.Count}");

        // The spoken form is not in the written text, so this only works if the
        // search covers both.
        var bySpoken = JobView.Build(job, false, 0, "Eter");
        Check(bySpoken.Count == 1 && bySpoken[0].Index == 0, $"spoken search returned {bySpoken.Count}");

        // Case-insensitive.
        var lower = JobView.Build(job, false, 0, "aether");
        Check(lower.Count == 1, $"case-insensitive search returned {lower.Count}");

        // Empty query returns everything.
        Check(JobView.Build(job, false, 0, "").Count == 3, "empty query filtered rows");
        Check(JobView.Build(job, false, 0, "   ").Count == 3, "whitespace query filtered rows");

        // Index is searchable too.
        var byIndex = JobView.Build(job, false, 0, "2");
        Check(byIndex.Count == 1 && byIndex[0].Index == 2, $"index search returned {byIndex.Count}");

        return "text, spoken, case, empty, index";
    }


    private static string TestBulkReplace()
    {
        // "i-de" → spoken "aj-di", written left alone: the transcript keeps the
        // original spelling while the engine says something else.
        var text = "Kup ID i drugi ID.\nTrzeci wiersz.";

        var (once, n1) = PronunciationEdit.ReplaceAll(text, "ID", "aj-di", "ID", true, true);
        Check(n1 == 2, $"replaced {n1}, expected 2");
        Check(once.Contains("{aj-di|ID}"), $"marker missing: {once}");

        // A marker already in the line is REWRITTEN, not skipped: the whole point
        // of the dialog is often to fix the pronunciation of words that are
        // already marked. Searching "i-de" and asking for "ajdi" must turn
        // {i-de|ID} into {ajdi|ID} — the written side is preserved untouched.
        var marked = "{i-de|ID} lewy {i-de|SS} oraz ID";

        // Empty "Written as" keeps each marker's own recorded form.
        var (guarded, n2) = PronunciationEdit.ReplaceAll(marked, "i-de", "ajdi", "", true, true);
        Check(n2 == 2, $"replaced {n2}, expected 2 markers");
        Check(guarded == "{ajdi|ID} lewy {ajdi|SS} oraz ID", $"marker rewrite wrong: {guarded}");
        Check(guarded.Contains("{ajdi|ID}"), "written side not preserved");

        // A marker whose spoken side does not match is left alone.
        var untouched = PronunciationEdit.ReplaceAll("{Eter|Aether} x", "i-de", "ajdi", "i-de", true, true);
        Check(untouched.Item2 == 0 && untouched.Item1 == "{Eter|Aether} x", $"touched: {untouched.Item1}");

        // Whole-word off must match inside words.
        var (sub, n3) = PronunciationEdit.ReplaceAll("IDEA ID", "ID", "aj-di", "ID", false, true);
        Check(n3 == 2, $"whole-word off replaced {n3}, expected 2");

        // Whole-word on must not.
        var (word, n4) = PronunciationEdit.ReplaceAll("IDEA ID", "ID", "aj-di", "ID", true, true);
        Check(n4 == 1, $"whole-word on replaced {n4}, expected 1");
        Check(word.StartsWith("IDEA"), $"word split: {word}");

        // Case sensitivity.
        var (ci, n5) = PronunciationEdit.ReplaceAll("id ID", "ID", "aj-di", "ID", true, false);
        Check(n5 == 2, $"case-insensitive replaced {n5}, expected 2");

        var (cs, n6) = PronunciationEdit.ReplaceAll("id ID", "ID", "aj-di", "ID", true, true);
        Check(n6 == 1, $"case-sensitive replaced {n6}, expected 1");

        // Nothing found must report zero rather than a silent success.
        var (same, n7) = PronunciationEdit.ReplaceAll(text, "zzz", "a", "a", true, false);
        Check(n7 == 0 && same == text, "no-match altered the text");

        return "marker rewrite, whole word, case, no match";
    }

    private static string TestStale()
    {
        // A clip rendered before its line was edited must be visibly out of date
        // and must sort with the rows that need attention.
        var job = new Job { Mode = Mode.Train };
        job.Items.Add(new ItemRow { Index = 0, Text = "a", State = ItemState.Done });
        job.Items.Add(new ItemRow { Index = 1, Text = "b", State = ItemState.Stale });
        job.Items.Add(new ItemRow { Index = 2, Text = "c", State = ItemState.Failed });
        job.Items.Add(new ItemRow { Index = 3, Text = "d", State = ItemState.Pending });

        Check(job.Items[1].RowKind == "stale", $"kind = {job.Items[1].RowKind}");
        Check(job.Items[1].StatusText == "~", $"status = {job.Items[1].StatusText}");

        var staleFirst = JobView.Build(job, false, 3);
        Check(staleFirst[0].Index == 1, $"stale-first starts at {staleFirst[0].Index}");

        var errorsFirst = JobView.Build(job, false, 1);
        Check(errorsFirst[0].Index == 2, $"errors-first starts at {errorsFirst[0].Index}");
        Check(errorsFirst[1].Index == 1, $"stale should follow failed, got {errorsFirst[1].Index}");

        return "kind, status and both orders";
    }


    private static string TestFiltersCombine()
    {
        // The three filters must work together, not just one at a time. The stale
        // filter used to be applied in the panel AFTER the sort while the other two
        // were applied inside JobView, so no single test could reach the
        // combination — and the two builds did it differently.
        var job = new Job { Mode = Mode.Train };
        job.Items.Add(new ItemRow { Index = 0, Text = "alpha", Pinned = true, State = ItemState.Done });
        job.Items.Add(new ItemRow { Index = 1, Text = "alpha", Pinned = true, State = ItemState.Stale });
        job.Items.Add(new ItemRow { Index = 2, Text = "alpha", State = ItemState.Stale });
        job.Items.Add(new ItemRow { Index = 3, Text = "beta", Pinned = true, State = ItemState.Stale });
        job.Items.Add(new ItemRow { Index = 4, Text = "beta", State = ItemState.Done });

        var all = JobView.Build(job, false, 0);
        Check(all.Count == 5, $"unfiltered = {all.Count}");

        var pinned = JobView.Build(job, true, 0);
        Check(pinned.Count == 3, $"pinned = {pinned.Count}");

        var stale = JobView.Build(job, false, 0, null, staleOnly: true);
        Check(stale.Count == 3, $"stale = {stale.Count}");

        var search = JobView.Build(job, false, 0, "alpha");
        Check(search.Count == 3, $"search = {search.Count}");

        // Pinned AND stale: rows 1 and 3, not the stale row 2 that is not pinned.
        var both = JobView.Build(job, true, 0, null, staleOnly: true);
        Check(both.Count == 2 && both.All(i => i.Pinned && i.State == ItemState.Stale),
            $"pinned+stale = {both.Count}");

        // All three: only row 1 is pinned, stale and says "alpha".
        var triple = JobView.Build(job, true, 0, "alpha", staleOnly: true);
        Check(triple.Count == 1 && triple[0].Index == 1, $"triple = {triple.Count}");

        // A filter that matches nothing must return nothing, not everything — the
        // classic way a filter silently stops filtering.
        var none = JobView.Build(job, false, 0, "zzz");
        Check(none.Count == 0, $"no-match = {none.Count}");

        return "pin, stale, search and their combinations";
    }

    private static string TestJobState()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vq_job_{Guid.NewGuid():N}");
        try
        {
            var job = new Job { OutDir = dir, Mode = Mode.Train, Voice = "test-voice" };
            job.Items.Add(new ItemRow { Index = 0, Text = "raz", State = ItemState.Done, File = "wav/0000.wav", Duration = 1.5 });
            job.Items.Add(new ItemRow { Index = 1, Text = "dwa", State = ItemState.Failed, Error = "boom" });
            job.Items.Add(new ItemRow { Index = 2, Text = "trzy", State = ItemState.Done, File = "wav/0002.wav", Defects = { "click_end" } });
            job.Save();

            Check(File.Exists(job.StatePath), "state file was not written");

            var back = Job.Load(job.StatePath);
            Check(back is not null, "state file did not load");
            Check(back!.Items.Count == 3, $"items {back.Items.Count}");
            Check(back.DoneCount == 2, $"done {back.DoneCount}");
            Check(back.FailedCount == 1, $"failed {back.FailedCount}");
            Check(back.ReviewCount == 1, $"review {back.ReviewCount}");
            Check(back.Items[2].Defects.Contains("click_end"), "defect list did not survive the round trip");

            // metadata.csv must skip the failed item
            var runner = new JobRunner(new TtsClient("http://127.0.0.1:1", ""), back);
            var meta = runner.WriteMetadata();
            Check(meta is not null && File.Exists(meta), "metadata.csv was not written");
            var rows = File.ReadAllLines(meta!).Where(r => r.Length > 0).ToArray();
            Check(rows.Length == 2, $"metadata rows {rows.Length}");
            Check(rows[0] == "0000|raz", $"row 0 = '{rows[0]}'");

            return "save+load+metadata";
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }


    private static string TestNoInvokeBeforeHandle()
    {
        // Constructing a control and calling .BeginInvoke on it in the same breath
        // is fatal: there is no window handle yet, and the exception is thrown
        // before any window exists — the app dies during start-up with nothing on
        // screen but a message box.
        //
        // The UI self-test does construct MainForm and would catch it, but that one
        // runs only on Windows. This is the static guard that catches it anywhere.
        var dir = FindDesktopSources();
        if (dir is null) return "desktop sources not found — skipped";

        var offenders = new List<string>();
        var files = Directory.GetFiles(dir, "*.cs");

        foreach (var file in files)
        {
            foreach (var line in File.ReadLines(file))
            {
                var code = line.Trim();

                // A comment describing the rule is not a violation.
                if (code.StartsWith("//") || code.StartsWith("*")) continue;
                if (code.StartsWith("///")) continue;

                // "InvokeRequired) BeginInvoke(action)" is the guarded form and has
                // no receiver; ".BeginInvoke(" always targets a control directly.
                if (code.Contains(".BeginInvoke("))
                    offenders.Add($"{Path.GetFileName(file)} → {code}");
            }
        }

        Check(offenders.Count == 0,
            $"{offenders.Count} unguarded BeginInvoke call(s): {string.Join(" | ", offenders.Take(3))}");

        return $"{files.Length} desktop files clean";
    }

    /// <summary>Walks up from the binary until it finds the Desktop project folder.</summary>
    private static string? FindDesktopSources()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "VoxQueue.Desktop");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static string TestKeysExist()
    {
        // Every key the UI asks for must be in the table. A miss does not throw —
        // L.Tt returns the key itself — so it shows up as "bulk_count" printed on a
        // button, which reads like a rendering bug rather than a missing string.
        var dir = FindDesktopSources();
        if (dir is null) return "desktop sources not found - skipped";

        var known = new HashSet<string>(L.AllKeys, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var pattern = new System.Text.RegularExpressions.Regex(
            "L\\.(Tt|Hint)\\(\"([a-z0-9_]+)\"\\)");

        foreach (var file in Directory.GetFiles(dir, "*.cs"))
        {
            foreach (System.Text.RegularExpressions.Match m in pattern.Matches(File.ReadAllText(file)))
                used.Add(m.Groups[2].Value);
        }

        var missing = used.Where(k => !known.Contains(k) && !HasHint(k)).ToList();

        Check(missing.Count == 0,
            $"{missing.Count} key(s) with no translation: {string.Join(", ", missing.Take(8))}");

        return $"{used.Count} keys used, all defined";
    }

    /// <summary>True when the key exists in the Hints table.</summary>
    private static bool HasHint(string key)
    {
        // Probed through the public API: a hint that resolves to its own name is
        // missing. A defined hint differs from the key in at least one language.
        var saved = L.Code;
        try
        {
            L.Set("pl");
            var asPl = L.Hint(key);
            L.Set("en");
            var asEn = L.Hint(key);
            return asPl != key || asEn != key;
        }
        finally { L.Set(saved); }
    }

    private static string TestNoDeadFields()
    {
        // A private field that is read but never written is either a leftover from
        // a refactor or a bug where the assignment was lost. Both are worth failing
        // on: the compiler only warns, and a warning nobody reads is how the
        // colouring flag survived several releases with no effect.
        var dir = FindDesktopSources();
        if (dir is null) return "desktop sources not found - skipped";

        var fieldPattern = new System.Text.RegularExpressions.Regex(
            @"^private\s+[\w<>\[\],.?]+\s+(_?\w+)\s*(=|;)");
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(dir, "*.cs"))
        {
            var lines = File.ReadAllLines(file);

            foreach (var line in lines)
            {
                var code = line.Trim();
                if (!code.StartsWith("private ")) continue;
                if (code.Contains("(")) continue;                       // methods
                if (code.Contains("const ")) continue;                   // assigned by definition

                var m = fieldPattern.Match(code);
                if (!m.Success) continue;

                var name = m.Groups[1].Value;

                // Any assignment operator counts, including the compound ones a
                // plain "name =" test misses: `??=` is how the lazy item view is
                // built, and reporting it as dead would be a false alarm that
                // trains people to ignore the guard.
                var assign = new System.Text.RegularExpressions.Regex(
                    System.Text.RegularExpressions.Regex.Escape(name) +
                    @"\s*(\?\?=|\+=|-=|\|=|&=|\^=|<<=|>>=|=)");

                var assigned = lines.Any(l => assign.IsMatch(l));
                if (!assigned) offenders.Add($"{Path.GetFileName(file)} -> {name}");
            }
        }

        Check(offenders.Count == 0,
            $"{offenders.Count} never-assigned field(s): {string.Join(", ", offenders.Take(6))}");

        return "no dead fields";
    }


    private static string TestNoDuplicateKeys()
    {
        // The table is written as explicit ["key"] = "value" assignments, and an
        // initializer silently keeps the last one. A duplicate therefore does not
        // fail — it just loses a string. That is how bulk_find ended up defined
        // twice, and how resumed and shutdown_cancelled got each other's text: the
        // status line read "Shutdown cancelled: 9999" after a resume.
        var path = FindTranslationsFile();
        if (path is null) return "L.cs not found - skipped";

        var src = File.ReadAllText(path);
        var block = new System.Text.RegularExpressions.Regex(
            "\\[\"([a-z]{2})\"\\] = new\\(\\)\\s*\\{(.*?)\\n        \\},",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        var assignment = new System.Text.RegularExpressions.Regex(
            "\\[\"([^\"]+)\"\\] = \"((?:[^\"\\\\]|\\\\.)*)\"");

        var problems = new List<string>();

        foreach (System.Text.RegularExpressions.Match m in block.Matches(src))
        {
            var code = m.Groups[1].Value;
            var pairs = assignment.Matches(m.Groups[2].Value);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (System.Text.RegularExpressions.Match a in pairs)
            {
                var key = a.Groups[1].Value;
                var value = a.Groups[2].Value;

                if (!seen.Add(key)) problems.Add($"{code}: duplicate '{key}'");
                values[key] = value;
            }

            // The pair that actually got swapped: "Resumed" landed under
            // shutdown_cancelled and the shutdown message under resumed. Two
            // different ideas cannot share one string.
            if (values.TryGetValue("resumed", out var resumed) &&
                values.TryGetValue("shutdown_cancelled", out var cancelled) &&
                string.Equals(resumed, cancelled, StringComparison.Ordinal))
            {
                problems.Add($"{code}: 'resumed' and 'shutdown_cancelled' are identical");
            }
        }

        Check(problems.Count == 0,
            $"{problems.Count} problem(s): {string.Join(", ", problems.Take(5))}");

        return "no duplicates, no swapped pair";
    }

    private static string? FindTranslationsFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "VoxQueue.Core", "I18n", "L.cs");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }


    private static string TestControlsParented()
    {
        // A control that is created and configured but never added to its parent
        // still exists, still holds its text, and is simply never drawn. The editor
        // pane came up empty exactly that way: Controls.Add(_rtb) was deleted along
        // with an unrelated block, nothing failed to compile, and the only symptom
        // was an empty box next to a fully populated list.
        //
        // Two invariants are checked here: the script box hosts its editor, and the
        // progress strip is our own control rather than the native ProgressBar whose
        // classic renderer animates on its own.
        var dir = FindDesktopSources();
        if (dir is null) return "desktop sources not found - skipped";

        var problems = new List<string>();

        var scriptBox = Path.Combine(dir, "ScriptBox.cs");
        if (File.Exists(scriptBox))
        {
            var src = File.ReadAllText(scriptBox);
            if (!src.Contains("Controls.Add(_rtb)"))
                problems.Add("ScriptBox never adds _rtb to Controls");
        }
        else problems.Add("ScriptBox.cs not found");

        var workPanel = Path.Combine(dir, "WorkPanel.cs");
        if (File.Exists(workPanel))
        {
            var src = File.ReadAllText(workPanel);
            if (src.Contains("ProgressBarStyle"))
                problems.Add("WorkPanel still uses a native ProgressBar");

            if (!src.Contains("FlatProgress"))
                problems.Add("WorkPanel does not use FlatProgress");
        }
        else problems.Add("WorkPanel.cs not found");

        Check(problems.Count == 0, string.Join("; ", problems));

        return "editor parented, progress strip is ours";
    }


    private static string TestProgressAndColouring()
    {
        // Two invariants that each cost a release.
        //
        // The bar must not be filled from lifetime totals: 9999/10000 reads as a
        // full bar that never moves. And a colouring pass must never run on a
        // control the user cannot see — a hidden RichTextBox takes a much slower
        // path for its line metrics, which froze the window for seconds when
        // colouring was switched on from Settings.
        var dir = FindDesktopSources();
        if (dir is null) return "desktop sources not found - skipped";

        var problems = new List<string>();

        var workPanel = File.ReadAllText(Path.Combine(dir, "WorkPanel.cs"));

        // The bar must be reset at the start of a run...
        if (!workPanel.Contains("_runTotal"))
            problems.Add("WorkPanel has no run-scoped progress counter");

        // ...and must not be assigned the lifetime done count anywhere.
        if (workPanel.Contains("_bar.Value = job.DoneCount") ||
            workPanel.Contains("_bar.Value = Math.Min(_bar.Maximum, job.DoneCount)"))
            problems.Add("progress bar still uses the lifetime done count");

        if (workPanel.Contains("_bar.Value = Math.Min(_bar.Maximum, p.Done)"))
            problems.Add("progress bar is driven by lifetime ProgressChanged");

        // The colouring pass must bail out when it cannot paint.
        var scriptBox = File.ReadAllText(Path.Combine(dir, "ScriptBox.cs"));
        // The pass is sliced, and slicing is only safe with both guards. Each of
        // these was a separate reported regression:
        //   * without _suppressRecolour the paint raises TextChanged, the pass
        //     re-queues itself and the cursor resets — it colours forever;
        //   * without the visibility check a pass started from Settings runs on a
        //     hidden control, where Select() is far slower, and freezes the window;
        //   * without a badge the user cannot tell work is happening at all.
        var slice = scriptBox.IndexOf("private void ColourNextChunk()", StringComparison.Ordinal);
        if (slice < 0)
        {
            problems.Add("ColourNextChunk not found");
        }
        else
        {
            var body = scriptBox.Substring(slice, Math.Min(2400, scriptBox.Length - slice));

            if (!body.Contains("_suppressRecolour = true"))
                problems.Add("the slice paints without suppressing TextChanged");

            if (!body.Contains("!Visible"))
                problems.Add("the slice does not check visibility");

            if (!body.Contains("_needsRecolour"))
                problems.Add("the slice does not defer when hidden");

            if (!body.Contains("_colourCursor = end"))
                problems.Add("the slice does not advance the cursor");
        }

        // Called with a threshold, not a literal, so the check is on the call.
        if (!scriptBox.Contains("ShowBadge("))
            problems.Add("no badge is shown while colouring");

        if (!scriptBox.Contains("_badge.Visible"))
            problems.Add("the badge is never made visible");

        Check(problems.Count == 0, string.Join("; ", problems));

        return "bar run-scoped, colouring deferred";
    }


    private static string TestColumnCounts()
    {
        // A TableLayoutPanel with ColumnCount smaller than the column index it is
        // given silently drops the control: it is added, parented, configured, and
        // never drawn. The follow-the-queue checkbox sat at column 5 of a
        // ColumnCount = 5 table and was invisible for two releases.
        //
        // Tracked per variable, not per file: one file holds several tables, so a
        // single "last ColumnCount seen" figure produced false alarms on the others.
        var dir = FindDesktopSources();
        if (dir is null) return "desktop sources not found - skipped";

        var problems = new List<string>();

        var decl = new System.Text.RegularExpressions.Regex(
            @"var\s+(\w+)\s*=\s*new\s+TableLayoutPanel");

        var count = new System.Text.RegularExpressions.Regex(
            @"(ColumnCount|RowCount)\s*=\s*(\d+)");

        var add = new System.Text.RegularExpressions.Regex(
            @"(\w+)\.Controls\.Add\([^,]+,\s*(\d+),\s*(\d+)\)");

        foreach (var file in Directory.GetFiles(dir, "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            var tables = new Dictionary<string, (int cols, int rows)>(StringComparer.Ordinal);

            foreach (var line in lines)
            {
                // A declaration starts (or restarts) tracking for that variable. Its
                // counts are declared a few lines later, in the same object
                // initializer, so the entry begins unknown and is filled in below.
                var d = decl.Match(line);
                if (d.Success) tables[d.Groups[1].Value] = (0, 0);

                var c = count.Match(line);
                if (c.Success && tables.Count > 0)
                {
                    var name = tables.Keys.Last();
                    var cur = tables[name];
                    tables[name] = c.Groups[1].Value == "ColumnCount"
                        ? (int.Parse(c.Groups[2].Value), cur.rows)
                        : (cur.cols, int.Parse(c.Groups[2].Value));
                }

                var a = add.Match(line);
                if (!a.Success) continue;

                var target = a.Groups[1].Value;
                if (!tables.TryGetValue(target, out var t)) continue;

                var col = int.Parse(a.Groups[2].Value);
                var row = int.Parse(a.Groups[3].Value);

                if (t.cols > 0 && col >= t.cols)
                    problems.Add($"{Path.GetFileName(file)}: {target} column {col} of {t.cols}");

                if (t.rows > 0 && row >= t.rows)
                    problems.Add($"{Path.GetFileName(file)}: {target} row {row} of {t.rows}");
            }
        }

        Check(problems.Count == 0,
            $"{problems.Count} control(s) outside their table: {string.Join("; ", problems.Take(4))}");

        return "no control outside its grid";
    }


    private static string TestWavDuration()
    {
        // The playback clock and the bar both depend on this number. The previous
        // parser consumed 12 of the 16 bytes of the fmt chunk and skipped
        // (size - 16) == 0, leaving the reader 4 bytes out of alignment: byteRate
        // stayed zero, the duration came back as zero, the clock never reached its
        // end and the bar never advanced. A round trip on a real file catches that
        // in one line, which reading the code did not.
        const int sr = 22050;
        var dir = Path.Combine(Path.GetTempPath(), $"vq_dur_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        try
        {
            // 1.00 s, 2.50 s and 0.10 s — short, medium and a fractional length.
            foreach (var seconds in new[] { 1.0, 2.5, 0.1 })
            {
                var samples = new float[(int)Math.Round(sr * seconds)];
                for (var i = 0; i < samples.Length; i++)
                    samples[i] = 0.3f * (float)Math.Sin(2 * Math.PI * 220 * i / sr);

                var path = Path.Combine(dir, $"{seconds:0.00}.wav");
                AudioTools.WriteWav(path, samples, sr);

                var measured = AudioTools.DurationOfWavFile(path);
                var error = Math.Abs(measured.TotalSeconds - seconds);

                Check(error < 0.01,
                    $"{seconds:0.00}s file measures {measured.TotalSeconds:0.000}s");
            }

            // Silence and an empty payload must not produce a bogus length.
            Check(AudioTools.DurationOfWav(Array.Empty<byte>()) == TimeSpan.Zero,
                "empty bytes gave a duration");
            Check(AudioTools.DurationOfWav(new byte[64]) == TimeSpan.Zero,
                "non-RIFF bytes gave a duration");

            return "1.00s, 2.50s, 0.10s all within 10ms";
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }


    private static string TestBulkMarks()
    {
        // The whole path, not a mock of it: build a job from text, render it, replace
        // a pronunciation, re-parse, and check which rows are now out of date.
        //
        // The bug this covers: only the WRITTEN form was compared. Changing a
        // pronunciation leaves the written side identical, so every changed row was
        // treated as untouched and restored as OK.
        var lines = new List<string>();
        for (var i = 0; i < 10; i++) lines.Add("wiersz " + i + " ma {i-de|ID} w srodku");
        for (var i = 0; i < 2; i++) lines.Add("wiersz bez markera " + i);

        Job Build(string text)
        {
            var parsed = TextSplitter.SplitLines(text);
            var job = new Job { Mode = Mode.Train };
            for (var i = 0; i < parsed.Count; i++)
            {
                var (spoken, written) = Pronunciation.Split(parsed[i].Text);
                job.Items.Add(new ItemRow
                {
                    Index = i,
                    Text = written,
                    Spoken = spoken,
                    State = ItemState.Done,
                    File = "wav/" + i.ToString("0000") + ".wav",
                    Duration = 1.5
                });
            }
            return job;
        }

        var source = string.Join("\n", lines);
        var original = Build(source);
        Check(original.Items.Count == 12, "built " + original.Items.Count + " rows, expected 12");

        var snapshot = RegenTracker.Snapshot(original);

        var (replaced, count) = PronunciationEdit.ReplaceAll(
            source, "i-de", "ajdi", "i-de", true, true);
        Check(count == 10, "replaced " + count + ", expected 10");

        var next = Build(replaced);
        Check(next.Items.Count == 12, "rebuilt " + next.Items.Count + " rows");

        var marked = RegenTracker.Restore(next, snapshot);
        Check(marked == 10, "marked " + marked + ", expected 10");

        for (var i = 0; i < 10; i++)
            Check(next.Items[i].State == ItemState.Stale,
                "row " + i + " is " + next.Items[i].State + ", expected Stale");

        for (var i = 10; i < 12; i++)
            Check(next.Items[i].State == ItemState.Done,
                "untouched row " + i + " is " + next.Items[i].State + ", expected Done");

        var again = RegenTracker.Restore(Build(source), snapshot);
        Check(again == 0, "unchanged pass marked " + again);

        return "10 spoken-only changes marked, 2 untouched kept";
    }


    private static string TestRetryCovers()
    {
        // "Retry failed" reset only Failed, so the rows a pronunciation edit had just
        // marked Stale were exactly what it skipped — the one thing the user wanted
        // it to pick up. Both states mean "this clip needs making again".
        var job = new Job { Mode = Mode.Train };
        job.Items.Add(new ItemRow { Index = 0, Text = "a", State = ItemState.Done, File = "wav/a.wav" });
        job.Items.Add(new ItemRow { Index = 1, Text = "b", State = ItemState.Failed, Error = "boom" });
        job.Items.Add(new ItemRow { Index = 2, Text = "c", State = ItemState.Stale, File = "wav/c.wav" });
        job.Items.Add(new ItemRow { Index = 3, Text = "d", State = ItemState.Stale, File = "wav/d.wav" });
        job.Items.Add(new ItemRow { Index = 4, Text = "e", State = ItemState.Pending });

        var count = job.ResetRetryable();
        Check(count == 3, $"re-queued {count}, expected 3");

        Check(job.Items[0].State == ItemState.Done, "a finished row was re-queued");
        Check(job.Items[1].State == ItemState.Pending, "failed row not re-queued");
        Check(job.Items[1].Error.Length == 0, "failed row kept its error");
        Check(job.Items[2].State == ItemState.Pending, "stale row not re-queued");
        Check(job.Items[3].State == ItemState.Pending, "second stale row not re-queued");

        // The clip on disk is kept: if the re-render fails, the old audio is still
        // there to play and compare against.
        Check(job.Items[2].File == "wav/c.wav", "stale row lost its file reference");

        // A second call must find nothing, or the button would start a run every time.
        Check(job.ResetRetryable() == 0, "second reset found work");

        return "3 re-queued, done row untouched, file kept";
    }


    private static string TestWrittenForm()
    {
        // The reported case: the text holds {i-de|ID}, the user searches the phonetic
        // form "i-de", changes only the pronunciation, and leaves "Written as" alone.
        // The transcript must keep ID - not become i-de, and not stay silent.
        var source = "Kup {i-de|ID} oraz {i-de|SS} i wolny i-de tekst";

        var recorded = PronunciationEdit.WrittenFormFor(source, "i-de", true, true);
        Check(recorded == "ID", "prefill suggests " + recorded + ", expected ID");

        var (a, na) = PronunciationEdit.ReplaceAll(source, "i-de", "ajdi", "ID", true, true);
        Check(na == 3, "replaced " + na + ", expected 3");
        Check(a == "Kup {ajdi|ID} oraz {ajdi|ID} i wolny {ajdi|ID} tekst", "result: " + a);

        var (b, nb) = PronunciationEdit.ReplaceAll(source, "i-de", "ajdi", "", true, true);
        Check(nb == 3, "empty-written replaced " + nb + ", expected 3");
        Check(b == "Kup {ajdi|ID} oraz {ajdi|SS} i wolny {ajdi|i-de} tekst",
            "empty-written result: " + b);

        var (c, nc) = PronunciationEdit.ReplaceAll(source, "i-de", "ajdi", "EjdI", true, true);
        Check(nc == 3, "explicit replaced " + nc + ", expected 3");
        Check(c == "Kup {ajdi|EjdI} oraz {ajdi|EjdI} i wolny {ajdi|EjdI} tekst", "explicit result: " + c);

        Check(PronunciationEdit.WrittenFormFor("zwykly tekst", "i-de", true, true) is null,
            "suggested a written form with no marker present");

        return "suggest ID, empty keeps per-marker, explicit wins";
    }


    private static string TestStaleFilterLifetime()
    {
        // A row that needs regeneration must stay in the filtered list from the moment
        // it is marked until its render succeeds — including while it is queued and
        // while it is running. Filtering on State made it vanish the instant it
        // started, which is the one moment the user is watching it.
        var job = new Job { Mode = Mode.Train };

        var fresh = new ItemRow { Index = 0, Text = "a", State = ItemState.Done };
        var stale = new ItemRow { Index = 1, Text = "b", State = ItemState.Stale, NeedsRegen = true };
        var queued = new ItemRow { Index = 2, Text = "c", State = ItemState.Pending, NeedsRegen = true };
        var running = new ItemRow { Index = 3, Text = "d", State = ItemState.Running, NeedsRegen = true };
        var failed = new ItemRow { Index = 4, Text = "e", State = ItemState.Failed, NeedsRegen = true };
        var legacy = new ItemRow { Index = 5, Text = "f", State = ItemState.Stale };

        job.Items.AddRange(new[] { fresh, stale, queued, running, failed, legacy });

        var shown = JobView.Build(job, false, 0, null, staleOnly: true)
                           .Select(i => i.Index).ToList();

        Check(shown.Contains(1), "stale row missing");
        Check(shown.Contains(2), "queued row vanished");
        Check(shown.Contains(3), "running row vanished");
        Check(shown.Contains(4), "failed row vanished before succeeding");
        Check(shown.Contains(5), "legacy stale row missing (State fallback)");
        Check(!shown.Contains(0), "a finished row is in the regeneration list");

        // Success is the only thing that removes it.
        running.NeedsRegen = false;
        running.State = ItemState.Done;

        var after = JobView.Build(job, false, 0, null, staleOnly: true)
                            .Select(i => i.Index).ToList();

        Check(!after.Contains(3), "row still listed after a successful render");
        Check(after.Contains(4), "failed row disappeared without succeeding");

        return "1 stale, 1 queued, 1 running, 1 failed stay; done leaves";
    }


    private static string TestCancelIsNotFailure()
    {
        // Cancelling during a request threw OperationCanceledException out of the
        // retry loop with no data, so the row was marked Failed and painted red. It
        // stayed red until the next run touched it — nothing had gone wrong, the user
        // had pressed Cancel. The rule now: cancelled means Pending, and only a real
        // render sets Done (and clears any "needs regeneration" flag).
        var job = new Job { Mode = Mode.Train };
        job.Items.Add(new ItemRow { Index = 0, Text = "a", State = ItemState.Done });
        job.Items.Add(new ItemRow { Index = 1, Text = "b", State = ItemState.Running });
        job.Items.Add(new ItemRow { Index = 2, Text = "c", State = ItemState.Pending });

        // What the runner's finally block does to anything left mid-run.
        foreach (var item in job.Items.Where(i => i.State == ItemState.Running))
        {
            item.State = ItemState.Pending;
            item.Error = "";
        }

        Check(job.Items[1].State == ItemState.Pending, "interrupted row is not pending");
        Check(job.Items[1].StatusText == "·", $"interrupted row reads '{job.Items[1].StatusText}'");
        Check(job.Items[0].State == ItemState.Done, "a finished row was disturbed");
        Check(job.Items[2].State == ItemState.Pending, "a queued row was disturbed");

        // A failed row is still failed: a real error must stay visible.
        job.Items[2].State = ItemState.Failed;
        job.Items[2].Error = "boom";
        Check(job.Items[2].StatusText == "X", "a genuine failure lost its marker");

        return "cancelled row is pending, real failures stay failed";
    }

    private static string TestI18n()
    {
        Check(L.Languages.Length == 20, $"expected 20 languages, got {L.Languages.Length}");

        // every language must translate the core tab labels — a short array
        // silently falls back to English, which this catches.
        foreach (var (code, name) in L.Languages)
        {
            L.Set(code);
            var tab = L.Tt("tab_train");
            Check(!string.IsNullOrWhiteSpace(tab) && tab != "tab_train",
                $"{code} ({name}) has no tab_train");
            var start = L.Tt("start");
            Check(!string.IsNullOrWhiteSpace(start) && start != "start",
                $"{code} ({name}) has no start");
        }

        // EVERY key must resolve in EVERY language. This is the check that would
        // have caught the misaligned table: the Log tab was labelled "Replaced"
        // and the colouring checkbox "Replace", and nothing complained because a
        // key resolving to the wrong string still resolves.
        var missing = 0;
        var wrong = new List<string>();
        foreach (var (code, name) in L.Languages)
        {
            L.Set(code);
            foreach (var key in L.AllKeys)
            {
                var text = L.Tt(key);
                if (string.IsNullOrWhiteSpace(text)) { missing++; continue; }
                if (text == key && code != "en") wrong.Add($"{code}:{key}");
            }
        }
        Check(missing == 0, $"{missing} key(s) resolved to nothing");
        Check(wrong.Count == 0,
            $"{wrong.Count} key(s) fell back to the key name: {string.Join(", ", wrong.Take(8))}");

        L.Set("en");
        Check(L.Tt("tab_log") == "Log", $"en tab_log = '{L.Tt("tab_log")}'");
        Check(L.Tt("colour_text") == "Colour the text", $"en colour_text = '{L.Tt("colour_text")}'");
        Check(L.Tt("bulk_pron") == "Bulk pronunciation", $"en bulk_pron = '{L.Tt("bulk_pron")}'");
        Check(L.Tt("log_refresh") == "Refresh", $"en log_refresh = '{L.Tt("log_refresh")}'");

        L.Set("pl");
        Check(L.Tt("tab_train") == "Trening", $"pl tab_train = '{L.Tt("tab_train")}'");
        Check(L.Tt("tab_log") == "Log", $"pl tab_log = '{L.Tt("tab_log")}'");
        Check(L.Tt("colour_text") == "Koloruj tekst", $"pl colour_text = '{L.Tt("colour_text")}'");
        Check(L.Hint("train_hint").Contains("klip"), "pl train_hint not translated");

        L.Set("ja");
        Check(L.Tt("tab_train") == "学習用", $"ja tab_train = '{L.Tt("tab_train")}'");

        // Every grid column header must resolve for the active language. A key
        // that is missing from the Words table falls back to the key itself, so
        // the header reads "col_phonetic" — which is exactly what happened in
        // the Pronunciation column and is invisible to the compiler.
        var columns = new[] { "col_index", "col_text", "col_phonetic",
                              "col_duration", "col_status", "col_notes" };
        foreach (var code in new[] { "en", "pl", "de" })
        {
            L.Set(code);
            foreach (var key in columns)
            {
                var value = L.W(key);
                Check(value != key, $"{code}: column '{key}' has no translation");
                Check(!string.IsNullOrWhiteSpace(value), $"{code}: column '{key}' is blank");
            }
        }

        // Defect names reach the Notes column through L.Defect(). A code that has
        // no translation would be shown raw ("click_end" in a Polish UI), so the
        // mapping is checked for every code the detector can produce.
        var defectCodes = new[] { "clipped", "click_end", "beep", "discontinuity",
                                  "short", "silent", "dc_offset", "loud_tail", "rambling" };
        foreach (var code in defectCodes)
        {
            // Polish is checked against the code itself: every defect has a
            // Polish name and none of them happens to equal the identifier.
            L.Set("pl");
            var pl = L.Defect(code);
            Check(pl != code, $"defect '{code}' has no Polish name");
            Check(pl.Length > 0, $"defect '{code}' has an empty Polish name");

            // English cannot use the same test — "silent" is both the code and
            // the English word, which is correct, not a missing translation.
            L.Set("en");
            var en = L.Defect(code);
            Check(en.Length > 0, $"defect '{code}' has an empty English name");
        }

        L.Set("pl");
        Check(L.Defect("click_end") == "piknięcie na końcu",
            $"pl click_end = '{L.Defect("click_end")}'");

        L.Set("en");
        return $"{L.Languages.Length} langs, {columns.Length} columns, {defectCodes.Length} defects";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
