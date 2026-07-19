using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GrokVideoStudio.Core.Models;
using Microsoft.Extensions.Logging;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// xAI Grok Imagine Video API implementation.
/// 
/// Based on official docs (docs.x.ai/developers/rest-api-reference/inference/videos):
/// POST /v1/videos/generations → request_id (text-to-video and image-to-video)
/// GET  /v1/videos/{request_id} → poll until done/failed/expired
/// 
/// Models:
///   grok-imagine-video       — text-to-video, reference-to-video
///   grok-imagine-video-1.5   — text-to-video, image-to-video (better I2V)
/// 
/// Image source: public URL, base64 data URI, or file_id from Files API.
/// </summary>
public sealed class GrokVideoService : IVideoGenerationService
{
    private const string BaseUrl = "https://api.x.ai";
    private const string GenerationsEndpoint = "/v1/videos/generations";
    private const string VideoStatusEndpoint = "/v1/videos/";

    public VideoProvider Provider => VideoProvider.GrokImagine;

    private readonly HttpClient _httpClient;
    private readonly ILogger<GrokVideoService>? _logger;
    private readonly IActivityLogService? _activityLog;

    public GrokVideoService(HttpClient httpClient, ILogger<GrokVideoService>? logger = null, IActivityLogService? activityLog = null)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri(BaseUrl);
        _logger = logger;
        _activityLog = activityLog;
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

        // Build the API payload — only send fields the API recognizes.
        // The xAI API accepts: model, prompt, image (optional), duration.
        // aspect_ratio and resolution are NOT documented API fields and may
        // cause 400 errors, so we strip them from the actual payload.
        var payload = BuildPayload(request);

        var payloadJson = JsonSerializer.Serialize(payload);
        var hasImage = request.Image is not null;
        var imageDesc = hasImage
            ? $"data URI, {request.Image!.Url.Length} chars"
            : "none";

        _logger?.LogInformation("xAI submit: model={Model}, prompt={PromptLen} chars, image={Image}, duration={Duration}s",
            request.Model, request.Prompt.Length, imageDesc, request.Duration);
        _activityLog?.Log($"API submit: model={request.Model}, image={imageDesc}, duration={request.Duration}s", LogLevel.Debug);

        // Submit
        using var submitReq = new HttpRequestMessage(HttpMethod.Post, GenerationsEndpoint);
        submitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        submitReq.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        using var submitResp = await _httpClient.SendAsync(submitReq, ct);
        var submitJson = await submitResp.Content.ReadAsStringAsync(ct);

        if (!submitResp.IsSuccessStatusCode)
        {
            var errorDetail = TryExtractError(submitJson);
            _logger?.LogError("xAI submit failed: {Status} — {Error}\nFull response: {Body}", submitResp.StatusCode, errorDetail, submitJson);
            _activityLog?.Log($"API submit FAILED: {submitResp.StatusCode} — {errorDetail}", LogLevel.Error);
            throw new HttpRequestException($"xAI submit failed: {submitResp.StatusCode} — {errorDetail}");
        }

        var startResp = JsonSerializer.Deserialize<VideoGenerationStartResponse>(submitJson)
            ?? throw new InvalidOperationException("xAI returned null start response.");
        
        _logger?.LogInformation("xAI accepted: request_id={RequestId}", startResp.RequestId);
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
            {
                _logger?.LogError("xAI poll failed: {Status} — {Body}", pollResp.StatusCode, pollJson);
                throw new HttpRequestException($"xAI poll failed: {pollResp.StatusCode} — {TryExtractError(pollJson)}");
            }

            var pollResponse = JsonSerializer.Deserialize<VideoPollResponse>(pollJson)
                ?? throw new InvalidOperationException("xAI returned null poll response.");

            if (pollResponse.IsTerminal)
            {
                if (pollResponse.IsDone)
                {
                    _logger?.LogInformation("xAI video ready: {Url} (duration={Duration}s)",
                        pollResponse.Video?.Url, pollResponse.Video?.Duration);
                    progress?.Report($"✓ Video ready — {pollResponse.Video?.Url}");
                }
                else
                {
                    _logger?.LogError("xAI video failed: {Status} — {Error}", pollResponse.Status, pollResponse.Error?.Message);
                    progress?.Report($"✗ {pollResponse.Status}: {pollResponse.Error?.Message}");
                }
                return pollResponse;
            }

            // Log progress if available
            if (pollJson.Contains("\"progress\""))
            {
                try
                {
                    using var doc = JsonDocument.Parse(pollJson);
                    if (doc.RootElement.TryGetProperty("progress", out var progEl))
                        progress?.Report($"Status: {pollResponse.Status} ({progEl.GetInt32()}%) — waiting {pollIntervalSeconds}s…");
                }
                catch { /* ignore parse errors on progress */ }
            }
            else
            {
                progress?.Report($"Status: {pollResponse.Status} — waiting {pollIntervalSeconds}s…");
            }

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), ct);
        }

        _logger?.LogWarning("xAI timed out after {MaxAttempts} poll attempts", maxAttempts);
        progress?.Report($"✗ Timed out after {maxAttempts} attempts.");
        return new VideoPollResponse { Status = "expired" };
    }

    public async Task<string> DownloadVideoAsync(string videoUrl, string destinationPath, CancellationToken ct = default)
    {
        _logger?.LogInformation("Downloading video from {Url} to {Path}", videoUrl, destinationPath);

        using var resp = await _httpClient.GetAsync(videoUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        await using var fs = File.Create(destinationPath);
        await resp.Content.CopyToAsync(fs, ct);

        var fileSize = new FileInfo(destinationPath).Length;
        _logger?.LogInformation("Downloaded {Bytes} bytes to {Path}", fileSize, destinationPath);
        _activityLog?.Log($"Downloaded clip: {fileSize / 1024 / 1024:F1} MB", LogLevel.Debug);

        return destinationPath;
    }

    /// <summary>
    /// Build the API request payload with only fields the xAI API recognizes.
    /// Strips aspect_ratio and resolution (not documented API fields).
    /// </summary>
    private static Dictionary<string, object?> BuildPayload(VideoGenerationRequest request)
    {
        // Confirmed valid fields from xAI Imagine playground:
        // model, prompt, duration, resolution, image (optional)
        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["prompt"] = request.Prompt,
            ["duration"] = request.Duration,
            ["resolution"] = request.Resolution,
        };

        if (request.Image is not null)
        {
            payload["image"] = new { url = request.Image.Url };
        }

        return payload;
    }

    private static string TryExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String)
                    return err.GetString() ?? json;
                if (err.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? json;
                return err.GetRawText();
            }
            return json;
        }
        catch { return json; }
    }
}
