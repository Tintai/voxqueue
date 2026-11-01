using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace VoxQueue.Core.Services;

public sealed class TtsException : Exception
{
    public TtsException(string message) : base(message) { }
}

public sealed class VoiceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Label => string.IsNullOrEmpty(Name) ? Id : Name;
    public override string ToString() => Label;
}

/// <summary>
/// Client for any OpenAI-compatible /v1/audio endpoint.
///
/// Written against the protocol rather than one server: VoiceStudio, the Aether
/// voice hub and Piper all speak this shape, and the same UI has to drive
/// whichever happens to be running.
/// </summary>
public sealed class TtsClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _base;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public TtsClient(string baseUrl, string apiKey, int timeoutSeconds = 900)
    {
        var b = (baseUrl ?? "").Trim().TrimEnd('/');
        if (b.Length > 0 && !b.StartsWith("http://") && !b.StartsWith("https://")) b = "http://" + b;
        if (b.EndsWith("/v1")) b = b[..^3];
        _base = b;

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        var key = (apiKey ?? "").Trim();
        if (key.Length > 0)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    public string BaseUrl => _base;

    /// <summary>Cheap reachability probe. A 401 still proves the server is up.</summary>
    public async Task<(bool ok, string message)> PingAsync(CancellationToken ct = default)
    {
        foreach (var path in new[] { "/v1/audio/voices", "/v1/models", "/health" })
        {
            try
            {
                using var resp = await _http.GetAsync(_base + path, ct);
                if (resp.IsSuccessStatusCode) return (true, _base + path);
                if ((int)resp.StatusCode is 401 or 403) return (true, _base + path + " (auth required)");
            }
            catch (Exception e) { return (false, Describe(e)); }
        }
        return (false, "unreachable");
    }

    public async Task<List<VoiceInfo>> VoicesAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(_base + "/v1/audio/voices", ct);
        resp.EnsureSuccessStatusCode();
        var text = await resp.Content.ReadAsStringAsync(ct);
        var payload = JsonSerializer.Deserialize<VoicesPayload>(text, Json);
        var outp = new List<VoiceInfo>();
        if (payload?.Voices is null) return outp;
        foreach (var v in payload.Voices)
        {
            if (v is null) continue;
            var id = v.Id ?? v.VoiceId ?? "";
            if (id.Length == 0) continue;
            outp.Add(new VoiceInfo { Id = id, Name = v.Name ?? "" });
        }
        return outp;
    }

    /// <summary>One utterance. Returns the encoded audio bytes.</summary>
    public async Task<byte[]> SpeakAsync(string text, string voice, string format = "wav",
                                         CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new TtsException("empty text");
        if (string.IsNullOrWhiteSpace(voice)) throw new TtsException("no voice selected");

        var body = JsonSerializer.Serialize(new
        {
            model = "tts-1",
            voice,
            input = text,
            response_format = format
        });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        HttpResponseMessage resp;
        try
        {
            resp = await _http.PostAsync(_base + "/v1/audio/speech", content, ct);
        }
        catch (Exception e)
        {
            throw new TtsException(Describe(e));
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                var detail = "";
                try { detail = await resp.Content.ReadAsStringAsync(ct); } catch { }
                if (detail.Length > 300) detail = detail[..300];
                throw new TtsException($"HTTP {(int)resp.StatusCode} — {detail}".TrimEnd(' ', '—'));
            }
            var data = await resp.Content.ReadAsByteArrayAsync(ct);
            if (data.Length < 128) throw new TtsException("server returned an empty or truncated response");
            return data;
        }
    }

    private string Describe(Exception e) => e switch
    {
        TaskCanceledException => "timeout",
        HttpRequestException h => $"cannot reach {_base} ({h.Message})",
        _ => $"{e.GetType().Name}: {e.Message}"
    };

    public void Dispose() => _http.Dispose();

    private sealed class VoicesPayload
    {
        [JsonPropertyName("voices")] public List<VoiceDto>? Voices { get; set; }
    }

    private sealed class VoiceDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("voice_id")] public string? VoiceId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
}
