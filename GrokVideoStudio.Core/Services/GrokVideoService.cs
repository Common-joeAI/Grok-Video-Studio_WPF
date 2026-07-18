using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GrokVideoStudio.Core.Models;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// xAI Grok Imagine Video API implementation.
/// 
/// Based on official docs (docs.x.ai/developers/model-capabilities/video/generation):
/// POST /v1/videos/generations → request_id
/// GET  /v1/videos/{request_id} → poll until done/failed/expired
/// 
/// Supports text-to-video and image-to-video (model grok-imagine-video-1.5).
/// </summary>
public sealed class GrokVideoService : IVideoGenerationService
{
    private const string BaseUrl = "https://api.x.ai";
    private const string GenerationsEndpoint = "/v1/videos/generations";
    private string VideoStatusEndpoint = "/v1/videos/";

    public VideoProvider Provider => VideoProvider.GrokImagine;

    private readonly HttpClient _httpClient;

    public GrokVideoService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri(BaseUrl);
    }

    public async Task<VideoPollResponse> GenerateAsync(
        VideoGenerationRequest request,
        string apiKey,
        int pollIntervalSeconds = 5,
        int maxAttempts = 120,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("Submitting to xAI Grok Imagine API…");

        // Submit
        using var submitReq = new HttpRequestMessage(HttpMethod.Post, GenerationsEndpoint);
        submitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        submitReq.Content = CreateJsonContent(request);

        using var submitResp = await _httpClient.SendAsync(submitReq, ct);
        var submitJson = await submitResp.Content.ReadAsStringAsync(ct);

        if (!submitResp.IsSuccessStatusCode)
            throw new HttpRequestException($"xAI submit failed: {submitResp.StatusCode} — {TryExtractError(submitJson)}");

        var startResp = JsonSerializer.Deserialize<VideoGenerationStartResponse>(submitJson)
            ?? throw new InvalidOperationException("xAI returned null start response.");
        
        progress?.Report($"Request accepted — ID: {startResp.RequestId}");

        // Poll
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Polling {attempt}/{maxAttempts}…");

            using var pollReq = new HttpRequestMessage(HttpMethod.Get, $"{VideoStatusEndpoint}{startResp.RequestId}");
            pollReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var pollResp = await _httpClient.SendAsync(pollReq, ct);
            var pollJson = await pollResp.Content.ReadAsStringAsync(ct);

            if (!pollResp.IsSuccessStatusCode)
                throw new HttpRequestException($"xAI poll failed: {pollResp.StatusCode}");

            var pollResponse = JsonSerializer.Deserialize<VideoPollResponse>(pollJson)
                ?? throw new InvalidOperationException("xAI returned null poll response.");

            if (pollResponse.IsTerminal)
            {
                if (pollResponse.IsDone)
                    progress?.Report($"✓ Video ready — {pollResponse.Video?.Url}");
                else
                    progress?.Report($"✗ {pollResponse.Status}: {pollResponse.Error?.Message}");
                return pollResponse;
            }

            progress?.Report($"Status: {pollResponse.Status} — waiting {pollIntervalSeconds}s…");
            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), ct);
        }

        progress?.Report($"✗ Timed out after {maxAttempts} attempts.");
        return new VideoPollResponse { Status = "expired" };
    }

    public async Task<string> DownloadVideoAsync(string videoUrl, string destinationPath, CancellationToken ct = default)
    {
        using var resp = await _httpClient.GetAsync(videoUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var fs = File.Create(destinationPath);
        await resp.Content.CopyToAsync(fs, ct);
        return destinationPath;
    }

    private static HttpContent CreateJsonContent<T>(T data) =>
        new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");

    private static string TryExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.ValueKind == JsonValueKind.String ? err.GetString() ?? json
                     : err.TryGetProperty("message", out var msg) ? msg.GetString() ?? json : json;
            return json;
        }
        catch { return json; }
    }
}
