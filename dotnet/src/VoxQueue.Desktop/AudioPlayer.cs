using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows.Forms;
using VoxQueue.Core.Services;

namespace VoxQueue.Desktop;

/// <summary>
/// Plays a rendered clip, including one whose name has no extension.
///
/// A dataset for Piper is allowed to be extensionless, and that breaks every
/// player that decides the format from the filename. The approach here is:
///
///   WAV  -> SoundPlayer over a FileStream. It reads the RIFF header, not the
///           name, so an extensionless WAV plays exactly like any other.
///   other -> the OS handler is invoked on a temporary copy that carries the
///           real extension. A copy rather than a rename, because the file
///           belongs to the user's output folder and must not be touched.
///
/// One player per app: starting a clip stops the previous one, and the owner
/// calls Stop() before closing so no handle outlives the window.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private SoundPlayer? _sound;
    private string? _tempCopy;
    private string? _current;

    /// <summary>Length of the clip being played. Zero when it cannot be measured.</summary>
    private TimeSpan _duration;

    private readonly Stopwatch _elapsed = new();
    private readonly System.Windows.Forms.Timer _ticker = new();

    /// <summary>Raised when playback ends, so the UI can restore its button.</summary>
    public event EventHandler? Finished;

    /// <summary>
    /// Raised about ten times a second while a clip plays, carrying the position
    /// and the length so the owner can drive its progress bar with real time.
    ///
    /// SoundPlayer cannot report a position, so the position is measured with a
    /// Stopwatch against a length read from the WAV header. That is exact for the
    /// format this app writes, needs no extra dependency, and works for a file with
    /// no extension at all.
    /// </summary>
    public event EventHandler<(TimeSpan elapsed, TimeSpan total)>? Progress;

    /// <summary>Length of the current clip, or zero when unknown.</summary>
    public TimeSpan Duration => _duration;

    public bool IsPlaying { get; private set; }

    public AudioPlayer()
    {
        _ticker.Interval = 100;
        _ticker.Tick += (_, _) =>
        {
            if (!IsPlaying) { _ticker.Stop(); return; }

            var at = _elapsed.Elapsed;

            if (_duration > TimeSpan.Zero && at >= _duration)
            {
                // The player gives no completion callback, so the measured length is
                // also what tells us the clip is over.
                Progress?.Invoke(this, (_duration, _duration));
                Stop();
                return;
            }

            // A format opened through a shell handler reports no length here, so the
            // ticker has nothing to stop on. It gives up after ten minutes rather
            // than counting forever — a wrong number that never ends is worse than
            // no number at all.
            if (_duration <= TimeSpan.Zero && at > TimeSpan.FromMinutes(10))
            {
                Stop();
                return;
            }

            Progress?.Invoke(this, (at, _duration));
        };
    }

    public void Play(string path, string format)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        Stop();

        var fmt = (format ?? "").Trim().TrimStart('.').ToLowerInvariant();

        // An extensionless file is judged by content, not by the setting: a job
        // may have been written with the extension option on and opened later
        // with it off.
        if (fmt.Length == 0) fmt = SniffFormat(path) ?? "wav";

        try
        {
            if (fmt == "wav")
            {
                // FileStream, not the path overload: SoundPlayer opens the file
                // itself and would refuse a name it cannot parse.
                //
                // Load() pulls the whole file into memory, so the handle is
                // released immediately. Leaving it open held the clip locked for
                // the duration of playback — regenerating that item then failed
                // with UnauthorizedAccessException, and the only workaround was
                // to play something else first.
                var stream = File.OpenRead(path);
                try
                {
                    _sound = new SoundPlayer(stream);
                    _sound.Load();
                }
                finally
                {
                    stream.Dispose();
                }
                _duration = AudioTools.DurationOfWavFile(path);
                _sound.Play();
                IsPlaying = true;
                _current = path;

                _elapsed.Restart();
                _ticker.Start();
                Progress?.Invoke(this, (TimeSpan.Zero, _duration));
                return;
            }

            var temp = Path.Combine(Path.GetTempPath(),
                "voxqueue-play-" + Guid.NewGuid().ToString("N")[..8] + "." + fmt);
            File.Copy(path, temp, overwrite: true);
            _tempCopy = temp;

            Process.Start(new ProcessStartInfo { FileName = temp, UseShellExecute = true });
            _current = path;
            IsPlaying = true;
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "VoxQueue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Stops WAV playback. Temp copies are removed either way.</summary>
    public void Stop()
    {
        try { _sound?.Stop(); } catch { }
        try { _sound?.Dispose(); } catch { }
        _sound = null;

        if (_tempCopy is not null)
        {
            try { File.Delete(_tempCopy); } catch { }
            _tempCopy = null;
        }

        _ticker.Stop();
        _elapsed.Reset();
        _duration = TimeSpan.Zero;

        if (IsPlaying)
        {
            IsPlaying = false;
            Finished?.Invoke(this, EventArgs.Empty);
        }
        _current = null;
    }

    public string? Current => _current;

    /// <summary>
    /// Reads the first bytes to identify the container. Only the formats this
    /// app can write are recognised; anything else returns null and the caller
    /// falls back to WAV.
    /// </summary>
    private static string? SniffFormat(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var head = new byte[12];
            if (fs.Read(head, 0, head.Length) < 12) return null;

            // RIFF....WAVE
            if (head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' &&
                head[8] == 'W' && head[9] == 'A' && head[10] == 'V' && head[11] == 'E')
                return "wav";

            // OggS
            if (head[0] == 'O' && head[1] == 'g' && head[2] == 'g' && head[3] == 'S')
                return "ogg";

            // fLaC
            if (head[0] == 'f' && head[1] == 'L' && head[2] == 'a' && head[3] == 'C')
                return "flac";

            // ID3 or a bare MPEG frame header
            if ((head[0] == 'I' && head[1] == 'D' && head[2] == '3') ||
                (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0))
                return "mp3";

            // ....ftyp (MP4 / M4A)
            if (head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p')
                return "m4a";

            return null;
        }
        catch { return null; }
    }

    public void Dispose() => Stop();
}
