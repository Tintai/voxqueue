using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace VoxQueue.Core.Services;

public sealed class Analysis
{
    public double Duration { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public double PeakDb { get; set; } = -120;
    public double RmsDb { get; set; } = -120;
    public List<string> Defects { get; set; } = new();
    public bool Ok => Defects.Count == 0;
}

/// <summary>
/// WAV reading and writing, level normalisation, and the defect pass.
///
/// The defect pass exists because a training set must be clean: one click at the
/// end of a clip teaches the model to click. It is heuristic by design — it flags
/// for review, it never deletes.
/// </summary>
public static class AudioTools
{
    // ---------------------------------------------------------------- reading

    /// <summary>Read a WAV as float mono in [-1, 1]. Throws on anything it cannot parse.</summary>
    public static (float[] samples, int sampleRate, int channels) ReadWav(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms);

        if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("not a RIFF file");
        br.ReadInt32();
        if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("not a WAVE file");

        int channels = 1, sampleRate = 22050, bits = 16, format = 1;
        byte[]? data = null;

        while (ms.Position + 8 <= ms.Length)
        {
            var id = new string(br.ReadChars(4));
            var size = br.ReadInt32();
            if (size < 0 || ms.Position + size > ms.Length) size = (int)(ms.Length - ms.Position);

            if (id == "fmt ")
            {
                format = br.ReadInt16();
                channels = br.ReadInt16();
                sampleRate = br.ReadInt32();
                br.ReadInt32();          // byte rate
                br.ReadInt16();          // block align
                bits = br.ReadInt16();
                var skip = size - 16;
                if (skip > 0) br.ReadBytes(skip);
            }
            else if (id == "data")
            {
                data = br.ReadBytes(size);
            }
            else
            {
                br.ReadBytes(size);
            }
            if ((size & 1) == 1 && ms.Position < ms.Length) br.ReadByte();   // chunk padding
        }

        if (data is null) throw new InvalidDataException("no data chunk");

        var count = bits switch
        {
            8 => data.Length,
            16 => data.Length / 2,
            24 => data.Length / 3,
            32 => data.Length / 4,
            _ => throw new InvalidDataException($"unsupported bit depth: {bits}")
        };
        var mono = new float[count];
        var isFloat = format == 3;

        for (var i = 0; i < count; i++)
        {
            float v;
            if (bits == 8)
                v = (data[i] - 128) / 128f;
            else if (bits == 16)
                v = BitConverter.ToInt16(data, i * 2) / 32768f;
            else if (bits == 24)
            {
                var o = i * 3;
                var iv = data[o] | (data[o + 1] << 8) | (data[o + 2] << 16);
                if ((iv & 0x800000) != 0) iv -= 0x1000000;
                v = iv / 8388608f;
            }
            else
            {
                v = isFloat
                    ? BitConverter.ToSingle(data, i * 4)
                    : BitConverter.ToInt32(data, i * 4) / 2147483648f;
            }
            mono[i] = v;
        }

        if (channels <= 1) return (mono, sampleRate, channels);

        // Downmix: the model learns one voice, and stereo only doubles the data.
        var frames = count / channels;
        var mixed = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            for (var c = 0; c < channels; c++) sum += mono[f * channels + c];
            mixed[f] = (float)(sum / channels);
        }
        return (mixed, sampleRate, channels);
    }

    /// <summary>Write 16-bit PCM mono, atomically through a temp file.</summary>
    /// <summary>
    /// Length of a WAV file, read from its header.
    ///
    /// Parsed from a buffer, not a live stream: the previous version in the UI
    /// consumed 12 of the 16 bytes of the fmt chunk and then skipped
    /// (size - 16) == 0 bytes, leaving the reader 4 bytes out of alignment. Every
    /// chunk after that was read from the wrong offset, byteRate stayed zero, and
    /// the duration came back as zero for every file. A zero duration meant the
    /// playback clock never reached its end and the bar never advanced.
    ///
    /// Each chunk is skipped by exactly its declared size plus the padding byte
    /// that RIFF requires for odd sizes, so alignment cannot drift.
    /// </summary>
    public static TimeSpan DurationOfWav(byte[] bytes)
    {
        if (bytes is null || bytes.Length < 44) return TimeSpan.Zero;
        if (bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F') return TimeSpan.Zero;
        if (bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E') return TimeSpan.Zero;

        var byteRate = 0;
        var dataSize = 0;

        var at = 12;
        while (at + 8 <= bytes.Length)
        {
            var id = "" + (char)bytes[at] + (char)bytes[at + 1] + (char)bytes[at + 2] + (char)bytes[at + 3];
            var size = BitConverter.ToInt32(bytes, at + 4);
            at += 8;

            if (size < 0 || at + size > bytes.Length) break;

            if (id == "fmt ")
            {
                if (size >= 12) byteRate = BitConverter.ToInt32(bytes, at + 8);
            }
            else if (id == "data")
            {
                dataSize = size;
                break;
            }

            // Odd-sized chunks carry one padding byte.
            at += size + (size & 1);
        }

        if (byteRate <= 0 || dataSize <= 0) return TimeSpan.Zero;
        return TimeSpan.FromSeconds((double)dataSize / byteRate);
    }

    public static TimeSpan DurationOfWavFile(string path)
    {
        try { return DurationOfWav(File.ReadAllBytes(path)); }
        catch { return TimeSpan.Zero; }
    }

    public static void WriteWav(string path, float[] samples, int sampleRate)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Unique temp name: a fixed ".tmp" collides when the same clip is
        // re-rendered while the previous write is still settling.
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..6] + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            var dataBytes = samples.Length * 2;
            bw.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            bw.Write(36 + dataBytes);
            bw.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
            bw.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            bw.Write(16);
            bw.Write((short)1);              // PCM
            bw.Write((short)1);              // mono
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2);        // byte rate
            bw.Write((short)2);              // block align
            bw.Write((short)16);             // bits
            bw.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            bw.Write(dataBytes);

            var buf = new byte[dataBytes];
            for (var i = 0; i < samples.Length; i++)
            {
                var v = Math.Clamp(samples[i], -1f, 1f);
                var s = (short)Math.Round(v * 32767f);
                buf[i * 2] = (byte)(s & 0xFF);
                buf[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            bw.Write(buf);
        }
        SafeFile.Replace(tmp, path);
    }

    // ------------------------------------------------------------ normalising

    /// <summary>
    /// Peak-normalise after removing DC. Consistent loudness across clips matters
    /// because the model learns level along with timbre — clips at different levels
    /// yield an unstable voice.
    /// </summary>
    public static float[] Normalize(float[] samples, double targetPeakDb = -1.5)
    {
        if (samples.Length == 0) return samples;
        var mean = samples.Average(s => (double)s);
        var x = new float[samples.Length];
        var peak = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var v = (float)(samples[i] - mean);
            x[i] = v;
            var a = Math.Abs(v);
            if (a > peak) peak = a;
        }
        if (peak < 1e-6f) return x;

        var target = (float)Math.Pow(10, targetPeakDb / 20.0);
        var gain = target / peak;
        for (var i = 0; i < x.Length; i++) x[i] *= gain;
        return x;
    }

    public static string? FindFfmpeg()
    {
        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                var p = Path.Combine(dir, name);
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Preferred normalisation: highpass + EBU R128 loudness via ffmpeg. Returns
    /// false when ffmpeg is absent or fails, so the caller can fall back to the
    /// built-in peak normaliser instead of losing the clip.
    /// </summary>
    public static bool NormalizeWithFfmpeg(string srcPath, string dstPath, int sampleRate)
    {
        var ff = FindFfmpeg();
        if (ff is null) return false;
        try
        {
            var psi = new ProcessStartInfo(ff)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in new[]
            {
                "-hide_banner", "-v", "error", "-y", "-i", srcPath,
                "-af", "highpass=f=70:p=2,loudnorm=I=-16:TP=-1.5:LRA=11",
                "-ar", sampleRate.ToString(), "-ac", "1", "-c:a", "pcm_s16le", dstPath
            }) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(120_000);
            return p.ExitCode == 0 && File.Exists(dstPath) && new FileInfo(dstPath).Length > 44;
        }
        catch { return false; }
    }

    /// <summary>Convert a finished WAV to mp3/ogg/flac. False when it cannot.</summary>
    public static bool Transcode(string srcPath, string dstPath, string format)
    {
        if (format == "wav")
        {
            try { File.Copy(srcPath, dstPath, overwrite: true); return true; }
            catch { return false; }
        }
        var ff = FindFfmpeg();
        if (ff is null) return false;

        var codec = format switch
        {
            "mp3" => new[] { "-c:a", "libmp3lame", "-q:a", "2" },
            // m4a is AAC inside an MP4 container; -f ipod is ffmpeg's name for
            // the M4A flavour and is required or it writes a bare .m4a that
            // some players refuse.
            "m4a" => new[] { "-c:a", "aac", "-b:a", "192k", "-f", "ipod" },
            "ogg" => new[] { "-c:a", "libvorbis", "-q:a", "5" },
            "flac" => new[] { "-c:a", "flac" },
            _ => Array.Empty<string>()
        };
        if (codec.Length == 0) return false;

        try
        {
            var psi = new ProcessStartInfo(ff)
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in new[] { "-hide_banner", "-v", "error", "-y", "-i", srcPath, "-ac", "1" }
                     .Concat(codec).Append(dstPath)) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(180_000);
            return p.ExitCode == 0 && File.Exists(dstPath);
        }
        catch { return false; }
    }

    // --------------------------------------------------------------- analysis

    private static double Db(double x) => 20.0 * Math.Log10(Math.Max(x, 1e-9));

    /// <summary>Measure a clip and list every defect. Never throws on bad audio.</summary>
    public static Analysis Analyze(byte[] wav, string text = "")
    {
        var a = new Analysis();
        float[] x;
        int sr, ch;
        try { (x, sr, ch) = ReadWav(wav); }
        catch { a.Defects.Add("silent"); return a; }

        a.SampleRate = sr;
        a.Channels = ch;
        if (x.Length == 0 || sr <= 0) { a.Defects.Add("silent"); return a; }

        a.Duration = x.Length / (double)sr;
        var peak = 0f;
        double sumSq = 0, sum = 0;
        foreach (var v in x)
        {
            var av = Math.Abs(v);
            if (av > peak) peak = av;
            sumSq += v * v;
            sum += v;
        }
        var rms = Math.Sqrt(sumSq / x.Length);
        a.PeakDb = Db(peak);
        a.RmsDb = Db(rms);

        if (peak < 0.02) { a.Defects.Add("silent"); return a; }
        if (a.Duration < 0.4) a.Defects.Add("short");

        var clipped = x.Count(v => Math.Abs(v) >= 0.999f) / (double)x.Length;
        if (clipped > 0.0005) a.Defects.Add("clipped");

        if (Math.Abs(sum / x.Length) > 0.05) a.Defects.Add("dc_offset");

        if (x.Length > 2)
        {
            var diffs = new float[x.Length - 1];
            for (var i = 1; i < x.Length; i++) diffs[i - 1] = Math.Abs(x[i] - x[i - 1]);
            var maxJump = diffs.Max();
            var sorted = (float[])diffs.Clone();
            Array.Sort(sorted);
            var p99 = sorted[(int)(sorted.Length * 0.99)];
            if (p99 <= 0) p99 = 1e-6f;
            if (maxJump > 0.6f && maxJump > 8f * p99) a.Defects.Add("discontinuity");
        }

        const int frameMs = 10;
        var frameLen = Math.Max(1, sr * frameMs / 1000);
        var framePeaks = FramePeaks(x, frameLen);

        if (framePeaks.Count >= 4)
        {
            var bodyCount = Math.Max(1, (int)(framePeaks.Count * 0.6));
            var bodyMed = Median(framePeaks.Take(bodyCount).ToList());
            if (bodyMed < 1e-6f) bodyMed = 1e-6f;

            var tailLen = Math.Min(x.Length, sr * 6 / 10);
            var tailStart = x.Length - tailLen;
            var tail = x.Skip(tailStart).ToArray();

            // The ending is judged against the BODY, not the whole clip. Comparing
            // a 0.6 s tail with the average of a 30 s clip dilutes exactly the
            // defect being looked for: a loud burst at the end barely moves the
            // overall RMS, so the ratio never crosses the threshold.
            var bodyRms = tailStart > 0
                ? Math.Sqrt(x.Take(tailStart).Sum(v => (double)v * v) / tailStart)
                : rms;
            if (bodyRms < 1e-6) bodyRms = 1e-6;

            if (tail.Length > frameLen * 3)
            {
                var tpeaks = FramePeaks(tail, frameLen);
                if (tpeaks.Count >= 3)
                {
                    var idx = 0;
                    for (var i = 1; i < tpeaks.Count; i++) if (tpeaks[i] > tpeaks[idx]) idx = i;
                    var loc = tpeaks[idx];
                    var left = idx > 0 ? tpeaks[idx - 1] : 0f;
                    var right = idx < tpeaks.Count - 1 ? tpeaks[idx + 1] : 0f;
                    var neighbours = Math.Max(left, right);
                    if (loc > Math.Max(0.12f, 4f * bodyMed) && neighbours < 0.6f * loc)
                        a.Defects.Add("click_end");
                }

                var tonality = Tonality(tail);
                var tailRms = Math.Sqrt(tail.Sum(v => (double)v * v) / tail.Length);
                if (tonality > 0.55 && tailRms > Math.Max(0.02, 1.6 * bodyRms))
                    a.Defects.Add("beep");
                else if (tailRms > Math.Max(0.05, 3.0 * bodyRms))
                    a.Defects.Add("loud_tail");
            }
        }

        // Plausibility: a clip several times longer than the text can need means
        // the engine rambled. That is the failure mode which silently poisons a
        // dataset, so it is caught here rather than left to the eye.
        if (!string.IsNullOrEmpty(text))
        {
            var expected = Math.Max(0.8, text.Length / 13.0);
            if (a.Duration > Math.Max(2.5, expected * 2.2)) a.Defects.Add("rambling");
        }

        return a;
    }

    private static List<float> FramePeaks(float[] x, int frameLen)
    {
        var n = x.Length / frameLen;
        var outp = new List<float>(Math.Max(1, n));
        if (n < 1)
        {
            outp.Add(x.Length > 0 ? x.Max(Math.Abs) : 0f);
            return outp;
        }
        for (var f = 0; f < n; f++)
        {
            var peak = 0f;
            for (var i = f * frameLen; i < (f + 1) * frameLen; i++)
            {
                var av = Math.Abs(x[i]);
                if (av > peak) peak = av;
            }
            outp.Add(peak);
        }
        return outp;
    }

    private static float Median(List<float> v)
    {
        if (v.Count == 0) return 0f;
        var s = v.ToArray();
        Array.Sort(s);
        return s[s.Length / 2];
    }

    /// <summary>
    /// How much energy sits in one narrow spectral peak (0..1). A steady beep is
    /// nearly a single frequency, so one bin dominates; speech spreads energy
    /// across many bins even in a sustained vowel.
    /// </summary>
    private static double Tonality(float[] seg)
    {
        var n = 1;
        while (n * 2 <= Math.Min(seg.Length, 4096)) n *= 2;
        if (n < 128) return 0;

        var re = new double[n];
        var im = new double[n];
        var start = Math.Max(0, seg.Length - n);
        for (var i = 0; i < n; i++)
        {
            var w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1));   // Hann
            re[i] = seg[start + i] * w;
        }
        Fft(re, im);

        var mags = new double[n / 2];
        double total = 0;
        for (var i = 0; i < n / 2; i++)
        {
            mags[i] = Math.Sqrt(re[i] * re[i] + im[i] * im[i]);
            total += mags[i];
        }
        if (total <= 1e-9) return 0;

        var peakBin = 0;
        for (var i = 1; i < mags.Length; i++) if (mags[i] > mags[peakBin]) peakBin = i;

        var lo = Math.Max(0, peakBin - 2);
        var hi = Math.Min(mags.Length, peakBin + 3);
        double band = 0;
        for (var i = lo; i < hi; i++) band += mags[i];
        return band / total;
    }

    /// <summary>In-place iterative radix-2 FFT. Length must be a power of two.</summary>
    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2 * Math.PI / len;
            var wr = Math.Cos(ang);
            var wi = Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var ur = re[i + k];
                    var ui = im[i + k];
                    var vr = re[i + k + len / 2] * cr - im[i + k + len / 2] * ci;
                    var vi = re[i + k + len / 2] * ci + im[i + k + len / 2] * cr;
                    re[i + k] = ur + vr; im[i + k] = ui + vi;
                    re[i + k + len / 2] = ur - vr; im[i + k + len / 2] = ui - vi;
                    var ncr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = ncr;
                }
            }
        }
    }
}
