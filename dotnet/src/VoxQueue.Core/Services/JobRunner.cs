using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoxQueue.Core.Models;

namespace VoxQueue.Core.Services;

public sealed class ProgressArgs : EventArgs
{
    public int Done { get; init; }
    public int Total { get; init; }
    public int Review { get; init; }
}

/// <summary>
/// The generation queue.
///
/// Two behaviours worth knowing:
///  * Pause takes effect between items, never mid-request. Cutting a request in
///    half leaves the server rendering into a socket nobody reads.
///  * A retry happens only for transport failures. A clip that came back with a
///    defect is kept and flagged — the detector is a heuristic, and discarding
///    silently takes the decision away from the person.
/// </summary>
public sealed class JobRunner
{
    private readonly TtsClient _client;
    private readonly Job _job;
    private CancellationTokenSource? _cts;
    private volatile bool _paused;

    public event EventHandler<ItemRow>? ItemStarted;
    public event EventHandler<ItemRow>? ItemFinished;
    public event EventHandler<ItemRow>? ItemFailed;
    public event EventHandler<ProgressArgs>? ProgressChanged;
    public event EventHandler<string>? Log;
    public event EventHandler<bool>? Finished;

    public JobRunner(TtsClient client, Job job)
    {
        _client = client;
        _job = job;
    }

    public bool IsPaused => _paused;

    /// <summary>
    /// When set, only these item indices are rendered. Used by "generate this
    /// line" — a full queue run would redo everything, and the user asked for
    /// one clip. Null means no filter.
    /// </summary>
    public HashSet<int>? OnlyIndices { get; set; }

    public void Pause() => _paused = true;

    public void Resume() => _paused = false;

    public void Cancel() => _cts?.Cancel();

    private string VoiceFor(ItemRow item)
    {
        if (_job.Mode == Mode.Dub && !string.IsNullOrEmpty(item.Speaker) &&
            _job.VoiceMap.TryGetValue(item.Speaker, out var v) && !string.IsNullOrEmpty(v))
            return v;
        return _job.Voice;
    }

    /// <summary>
    /// Where this clip goes.
    ///
    /// train/bulk: `0000.wav` so metadata.csv can reference it by stem.
    /// dub:        `voices/&lt;SPEAKER&gt;/&lt;SPEAKER&gt;_0000_2.4s.wav` — speaker first so a
    ///             directory listing is readable and one voice's lines ship as a folder.
    /// </summary>
    /// <summary>The container format this job writes.</summary>
    private string Format =>
        string.IsNullOrEmpty(_job.Options.AudioFormat) ? "wav" : _job.Options.AudioFormat;

    /// <summary>
    /// Where this clip goes.
    ///
    /// The extension is optional because Piper's training docs describe the
    /// dataset as extensionless stems with the text living in metadata.csv. The
    /// format is therefore carried separately — never derived from the name,
    /// which may not have one.
    /// </summary>
    private string OutputPath(ItemRow item, double duration)
    {
        var num = item.Index + _job.Options.IndexStart;
        var ext = _job.Options.AddExtension ? "." + Format : "";

        if (_job.Mode == Mode.Dub)
        {
            var spk = TextSplitter.SafeFilename(item.Speaker);
            var dir = Path.Combine(_job.OutDir, "voices", spk);
            Directory.CreateDirectory(dir);
            // Duration must not follow the machine locale: on a Polish system
            // "0.00" renders as "2,41", so the filename gains a comma that no
            // other machine will reproduce.
            var durText = SafeFile.Invariant(duration, "0.00");
            return Path.Combine(dir, $"{spk}_{num:0000}_{durText}s{ext}");
        }

        var tpl = string.IsNullOrEmpty(_job.Options.TrainTemplate) ? "{index}" : _job.Options.TrainTemplate;
        var raw = tpl.Replace("{index}", num.ToString("0000")).Replace("{index_raw}", num.ToString());

        // The template is free text from Settings, and it is concatenated straight
        // onto the output folder. A template of "../outside/{index}" wrote outside
        // the chosen folder; one containing ':' or '?' threw on the first item and
        // failed the whole run. Substitution happens first, so "{index}" survives
        // as the number it produces.
        var stem = TextSplitter.SafeFilename(raw, num.ToString("0000"));
        var wavDir = Path.Combine(_job.OutDir, "wav");
        Directory.CreateDirectory(wavDir);
        return Path.Combine(wavDir, stem + ext);
    }

    public async Task RunAsync()
    {
        // The previous source is disposed before a new one is made: Cancel() from
        // the UI reads this field, and a live token from a finished run would let a
        // late click cancel the run that is just starting.
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var cancelled = false;

        try
        {
            foreach (var item in _job.Items)
            {
                if (ct.IsCancellationRequested) { cancelled = true; break; }

                // The delay is passed the token so pausing still responds to Cancel
                // immediately, but that makes it THROW OperationCanceledException
                // rather than return. Unhandled, the exception escaped the loop into
                // the finally block with `cancelled` still false — so a run cancelled
                // while paused reported "finished normally" and the app then ran the
                // shutdown sequence. Cancelling must never power the machine off.
                try
                {
                    while (_paused && !ct.IsCancellationRequested)
                        await Task.Delay(120, ct);
                }
                catch (OperationCanceledException) { /* cancelled while paused */ }

                if (ct.IsCancellationRequested) { cancelled = true; break; }

                // Filter first: an unselected item must not be touched at all,
                // not even marked as running.
                if (OnlyIndices is not null && !OnlyIndices.Contains(item.Index)) continue;

                if (item.State == ItemState.Done) continue;

                ItemStarted?.Invoke(this, item);
                item.State = ItemState.Running;
                _job.Save();

                var voice = VoiceFor(item);
                if (string.IsNullOrEmpty(voice))
                {
                    item.State = ItemState.Failed;
                    item.Error = "no voice assigned";
                    _job.Save();
                    ItemFailed?.Invoke(this, item);
                    continue;
                }

                byte[]? data = null;
                var lastError = "";
                for (var attempt = 0; attempt <= 2; attempt++)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        // The phonetic form is what the engine can pronounce; the written form
                        // never leaves the transcript.
                        data = await _client.SpeakAsync(item.SpokenOrText, voice, "wav", ct);
                        break;
                    }
                    catch (TtsException e)
                    {
                        lastError = e.Message;
                        Log?.Invoke(this, $"[{item.Index}] attempt {attempt + 1}: {lastError}");
                        await Task.Delay(1500 * (attempt + 1), CancellationToken.None);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception e)
                    {
                        lastError = $"{e.GetType().Name}: {e.Message}";
                        Log?.Invoke(this, lastError);
                    }
                }

                if (data is null)
                {
                    // A cancel is not a failure.
                    //
                    // Cancelling during a request makes SpeakAsync throw
                    // OperationCanceledException, which the retry loop breaks on with
                    // `data` still null — so the row was marked Failed and painted
                    // red, and stayed red until the next run touched it. Nothing had
                    // gone wrong: the user stopped it. The row goes back to Pending,
                    // which is neutral, and stays there until a real render succeeds.
                    if (ct.IsCancellationRequested)
                    {
                        item.State = ItemState.Pending;
                        item.Error = "";
                        _job.Save();
                        cancelled = true;
                        break;
                    }

                    item.State = ItemState.Failed;
                    item.Error = string.IsNullOrEmpty(lastError) ? "generation failed" : lastError;
                    _job.Save();
                    ItemFailed?.Invoke(this, item);
                    RaiseProgress();
                    continue;
                }

                try
                {
                    await FinishAsync(item, data);
                }
                catch (Exception e)
                {
                    item.State = ItemState.Failed;
                    item.Error = $"{e.GetType().Name}: {e.Message}";
                    _job.Save();
                    ItemFailed?.Invoke(this, item);
                    continue;
                }

                _job.Save();
                ItemFinished?.Invoke(this, item);
                RaiseProgress();
            }
        }
        finally
        {
            // Whatever happened, no row may be left mid-run. A row stuck in Running
            // keeps the "…" marker and an accent tint forever, and the only way it
            // ever cleared was the next run rewriting it.
            var stranded = false;
            foreach (var item in _job.Items)
            {
                if (item.State != ItemState.Running) continue;
                item.State = ItemState.Pending;
                item.Error = "";
                stranded = true;
            }

            if (stranded) Log?.Invoke(this, "an interrupted row was returned to the queue");

            _job.Save();
            Finished?.Invoke(this, cancelled);
        }
    }

    private void RaiseProgress() => ProgressChanged?.Invoke(this, new ProgressArgs
    {
        Done = _job.DoneCount,
        Total = _job.Items.Count,
        Review = _job.ReviewCount
    });

    /// <summary>Normalise, measure, flag defects, write.</summary>
    private async Task FinishAsync(ItemRow item, byte[] data)
    {
        var tmpDir = Path.Combine(_job.OutDir, ".tmp");
        Directory.CreateDirectory(tmpDir);
        var rawPath = Path.Combine(tmpDir, $"raw_{item.Index:000000}.wav");
        // Raw bytes are transient — a plain write is enough, but it still goes
        // through the retrying writer so a scanner cannot fail the item.
        SafeFile.WriteAllBytes(rawPath, data);

        var (samples, sr, _) = AudioTools.ReadWav(data);
        if (samples.Length == 0) throw new InvalidDataException("decoded audio is empty");

        var targetSr = _job.Options.SampleRate > 0 ? _job.Options.SampleRate : sr;

        // Normalise first: the defect pass must judge the clip as it will be
        // stored, otherwise a level change invalidates the "loud tail" measure.
        if (_job.Options.Normalize)
        {
            if (AudioTools.NormalizeWithFfmpeg(rawPath, rawPath, targetSr))
            {
                (samples, sr, _) = AudioTools.ReadWav(File.ReadAllBytes(rawPath));
            }
            else
            {
                samples = AudioTools.Normalize(samples);
                AudioTools.WriteWav(rawPath, samples, sr);
            }
        }

        var defects = new List<string>();
        if (_job.Options.DetectDefects)
        {
            // The length estimate has to use the spoken form: "2024" is four
            // characters written but six syllables spoken.
            var a = AudioTools.Analyze(File.ReadAllBytes(rawPath), item.SpokenOrText);
            defects.AddRange(a.Defects);
        }

        var duration = samples.Length / (double)sr;
        var final = OutputPath(item, duration);
        var fmt = Format;

        if (fmt == "wav")
        {
            AudioTools.WriteWav(final, samples, sr);
        }
        else
        {
            // The encoder needs a real .wav to read, so the intermediate gets
            // one even when the final name has no extension at all.
            var tmpWav = Path.Combine(Path.GetDirectoryName(final) ?? ".",
                                      Path.GetFileNameWithoutExtension(final) + ".wav");
            AudioTools.WriteWav(tmpWav, samples, sr);
            if (!AudioTools.Transcode(tmpWav, final, fmt))
                throw new InvalidOperationException($"cannot encode {fmt} (ffmpeg missing?)");
            SafeFile.TryDelete(tmpWav);
        }

        // The transcript keeps its extension even when the audio does not —
        // otherwise a text file sits next to a nameless binary and nothing can
        // tell them apart.
        if (_job.Mode == Mode.Bulk && _job.Options.WriteTranscript)
            SafeFile.WriteAllText(TranscriptPath(final), item.Text + Environment.NewLine);

        SafeFile.TryDelete(rawPath);

        item.Duration = duration;
        item.Defects = defects;
        item.File = final;
        item.Error = "";

        // The only place the flag is cleared. Until the clip is actually written and
        // the row is Done, it stays in the "needs regeneration" list: while queued,
        // while running, and after a failure that has to be retried again.
        item.NeedsRegen = false;

        item.State = ItemState.Done;
    }

    /// <summary>Transcript path for a clip, whether or not the audio has an extension.</summary>
    public static string TranscriptPath(string audioPath) =>
        Path.ChangeExtension(audioPath, ".txt");

    /// <summary>
    /// metadata.csv in LJSpeech form: `&lt;stem&gt;|&lt;text&gt;`, no header, only clips that
    /// passed. Including a flagged clip would train the model on exactly the
    /// artefact the detector found.
    /// </summary>
    public string? WriteMetadata()
    {
        if (_job.Mode == Mode.Dub) return null;
        var path = Path.Combine(_job.OutDir, "metadata.csv");
        var rows = _job.Items
            .Where(i => i.State == ItemState.Done && !string.IsNullOrEmpty(i.File))
            .OrderBy(i => i.Index)
            // The phonetic form is the one the audio actually says, so it is the
            // only correct transcript for training. See MetadataWithSpoken.
            .Select(i =>
            {
                var transcript = _job.Options.MetadataWithSpoken ? i.SpokenOrText : i.Text;
                return $"{Path.GetFileNameWithoutExtension(i.File)}|{transcript}";
            })
            .ToList();
        try
        {
            Directory.CreateDirectory(_job.OutDir);
            SafeFile.WriteAllText(path, rows.Count > 0 ? string.Join("\n", rows) + "\n" : "");
            return path;
        }
        catch { return null; }
    }
}
