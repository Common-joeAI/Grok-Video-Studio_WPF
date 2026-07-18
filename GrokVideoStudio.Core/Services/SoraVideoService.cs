using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GrokVideoStudio.Core.Models;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// OpenAI Sora 2 Video Generation API implementation.
/// Uses POST to https://api.openai.com/v1/videos/generations with model 'sora-2',
/// and polls GET /v1/videos/{id} until terminal state.
/// </summary>
public sealed class SoraVideoService(HttpClient httpClient) : IVideoGenerationService
{
    private const string BaseUrl = "https://api.openai.com";
    private const string GenerationsEndpoint = "/v1/videos/generations";
    private const string VideoStatusEndpoint = "/v1/videos/";

    /// <inheritdoc />
    public VideoProvider Provider => VideoProvider.OpenAiSora;

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    static SoraVideoService()
    {
        // Static constructor or initialization if needed
    }

    /// <inheritdoc />
    public async Task<VideoPollResponse> GenerateAsync(
        VideoGenerationRequest request,
        string apiKey,
        int pollIntervalSeconds = 5,
        int maxAttempts = 120,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        _httpClient.BaseAddress ??= new Uri(BaseUrl);
        progress?.Report("Submitting to OpenAI Sora 2 API…");

        var payload = new
        {
            model = "sora-2",
            prompt = request.Prompt,
            duration = request.Duration,
            aspect_ratio = request.AspectRatio,
            resolution = request.Resolution,
            image = request.Image != null ? new { url = request.Image.Url } : null
        };

        using var submitReq = new HttpRequestMessage(HttpMethod.Post, GenerationsEndpoint);
        submitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        submitReq.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var submitResp = await _httpClient.SendAsync(submitReq, ct);
        var submitJson = await submitResp.Content.ReadAsStringAsync(ct);

        if (!submitResp.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI Sora submit failed: {submitResp.StatusCode} — {TryExtractError(submitJson)}");

        using var startDoc = JsonDocument.Parse(submitJson);
        string id = "";
        if (startDoc.RootElement.TryGetProperty("id", out var idProp))
        {
            id = idProp.GetString() ?? "";
        }
        else if (startDoc.RootElement.TryGetProperty("request_id", out var reqIdProp))
        {
            id = reqIdProp.GetString() ?? "";
        }

        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("OpenAI Sora returned a response without a generation ID.");

        progress?.Report($"Request accepted — ID: {id}");

        // Poll
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Polling Sora {attempt}/{maxAttempts}…");

            using var pollReq = new HttpRequestMessage(HttpMethod.Get, $"{VideoStatusEndpoint}{id}");
            pollReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var pollResp = await _httpClient.SendAsync(pollReq, ct);
            var pollJson = await pollResp.Content.ReadAsStringAsync(ct);

            if (!pollResp.IsSuccessStatusCode)
                throw new HttpRequestException($"OpenAI Sora poll failed: {pollResp.StatusCode}");

            using var pollDoc = JsonDocument.Parse(pollJson);
            var statusStr = pollDoc.RootElement.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "pending" : "pending";

            // Map OpenAI status to our unified statuses
            string normalizedStatus = statusStr.ToLowerInvariant() switch
            {
                "completed" or "done" or "success" => "done",
                "failed" or "error" => "failed",
                "expired" => "expired",
                _ => "pending"
            };

            VideoResult? videoResult = null;
            if (pollDoc.RootElement.TryGetProperty("video", out var videoProp))
            {
                videoResult = JsonSerializer.Deserialize<VideoResult>(videoProp.GetRawText());
            }
            else if (pollDoc.RootElement.TryGetProperty("result", out var resultProp) && resultProp.TryGetProperty("url", out var urlProp))
            {
                videoResult = new VideoResult { Url = urlProp.GetString() ?? "" };
            }
            else if (pollDoc.RootElement.TryGetProperty("video_url", out var vUrlProp))
            {
                videoResult = new VideoResult { Url = vUrlProp.GetString() ?? "" };
            }

            ApiError? apiError = null;
            if (pollDoc.RootElement.TryGetProperty("error", out var errorProp))
            {
                apiError = JsonSerializer.Deserialize<ApiError>(errorProp.GetRawText());
            }

            var pollResponse = new VideoPollResponse
            {
                Status = normalizedStatus,
                Model = "sora-2",
                Video = videoResult,
                Error = apiError
            };

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

    /// <inheritdoc />
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
