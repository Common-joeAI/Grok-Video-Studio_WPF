using System.Net.Http.Json;
using System.Text.Json;
using GrokVideoStudio.Core.Models;
using Microsoft.Extensions.Logging;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Local GPU video generation service.
/// 
/// Talks to a local Python server running LTX-Video (or compatible model)
/// on the user's GPU. The server exposes the same submit/poll/download
/// pattern as the xAI API, so this service mirrors GrokVideoService's
/// flow but targets localhost instead of api.x.ai.
/// 
/// Zero API cost per clip — generation runs entirely on local hardware.
/// </summary>
public sealed class LocalVideoService : IVideoGenerationService
{
    public VideoProvider Provider => VideoProvider.LocalGPU;

    private readonly HttpClient _httpClient;
    private readonly ILogger<LocalVideoService>? _logger;
    private readonly IActivityLogService? _activityLog;

    public LocalVideoService(
        HttpClient httpClient,
        ILogger<LocalVideoService>? logger = null,
        IActivityLogService? activityLog = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _activityLog = activityLog;
    }

    public async Task<VideoPollResponse> GenerateAsync(
        VideoGenerationRequest request,
        string apiKey, // ignored — local generation is free
        int pollIntervalSeconds = 3, // local is faster than remote
        int maxAttempts = 100, // ~5 min timeout for local gen
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        _activityLog?.Log($"Local GPU: submitting generation — model={request.Model}, prompt={TruncatePrompt(request.Prompt)}, I2V={request.Image is not null}", LogLevel.Information);

        // Build payload matching local server's expected format
        var payload = BuildPayload(request);

        // Submit
        var submitResponse = await _httpClient.PostAsJsonAsync("/v1/videos/generations", payload, ct);
        if (!submitResponse.IsSuccessStatusCode)
        {
            var errorBody = await submitResponse.Content.ReadAsStringAsync(ct);
            _activityLog?.Log($"Local GPU: submit failed ({submitResponse.StatusCode}): {errorBody}", LogLevel.Error);
            throw new InvalidOperationException($"Local server rejected request: {submitResponse.StatusCode} — {errorBody}");
        }

        var startResponse = await submitResponse.Content.ReadFromJsonAsync<VideoGenerationStartResponse>(cancellationToken: ct);
        if (startResponse is null || string.IsNullOrEmpty(startResponse.RequestId))
        {
            throw new InvalidOperationException("Local server returned no request_id");
        }

        var requestId = startResponse.RequestId;
        _activityLog?.Log($"Local GPU: job submitted — id={requestId}, polling every {pollIntervalSeconds}s", LogLevel.Debug);
        progress?.Report($"Local GPU job {requestId} — polling…");

        // Poll
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), ct);

            ct.ThrowIfCancellationRequested();

            var pollResponse = await _httpClient.GetAsync($"/v1/videos/{requestId}", ct);
            if (!pollResponse.IsSuccessStatusCode)
            {
                _activityLog?.Log($"Local GPU: poll error ({pollResponse.StatusCode}) on attempt {attempt + 1}", LogLevel.Warning);
                continue;
            }

            var pollResult = await pollResponse.Content.ReadFromJsonAsync<VideoPollResponse>(cancellationToken: ct);
            if (pollResult is null)
                continue;

            if (pollResult.IsTerminal)
            {
                if (pollResult.IsDone)
                {
                    // Convert relative video URL to full local URL
                    var videoUrl = pollResult.Video?.Url ?? "";
                    if (videoUrl.StartsWith("/"))
                        videoUrl = $"{_httpClient.BaseAddress?.ToString().TrimEnd('/')}{videoUrl}";

                    _activityLog?.Log($"Local GPU: generation complete — {videoUrl}", LogLevel.Information);

                    return new VideoPollResponse
                    {
                        Status = "done",
                        Video = new VideoResult
                        {
                            Url = videoUrl,
                            Duration = pollResult.Video?.Duration ?? request.Duration,
                            RespectModeration = true
                        }
                    };
                }

                // Failed
                var errorMsg = pollResult.Error?.Message ?? "Generation failed";
                _activityLog?.Log($"Local GPU: generation failed — {errorMsg}", LogLevel.Error);
                throw new InvalidOperationException($"Local GPU generation failed: {errorMsg}");
            }

            // Still running — report progress
            if (attempt % 4 == 0)
            {
                progress?.Report($"Local GPU: generating… ({attempt * pollIntervalSeconds}s elapsed)");
                _activityLog?.Log($"Local GPU: still running after {attempt * pollIntervalSeconds}s", LogLevel.Debug);
            }
        }

        throw new TimeoutException($"Local GPU generation timed out after {maxAttempts * pollIntervalSeconds}s");
    }

    public async Task<string> DownloadVideoAsync(string videoUrl, string destinationPath, CancellationToken ct = default)
    {
        // If it's a relative URL, make it absolute
        if (videoUrl.StartsWith("/"))
            videoUrl = $"{_httpClient.BaseAddress?.ToString().TrimEnd('/')}{videoUrl}";

        _activityLog?.Log($"Local GPU: downloading {videoUrl} → {Path.GetFileName(destinationPath)}", LogLevel.Debug);

        using var response = await _httpClient.GetAsync(videoUrl, ct);
        response.EnsureSuccessStatusCode();

        await using var fs = File.Create(destinationPath);
        await response.Content.CopyToAsync(fs, ct);

        var fileInfo = new FileInfo(destinationPath);
        _activityLog?.Log($"Local GPU: downloaded {fileInfo.Length / 1024.0 / 1024.0:F1} MB", LogLevel.Debug);

        return destinationPath;
    }

    private static Dictionary<string, object?> BuildPayload(VideoGenerationRequest request)
    {
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

    private static string TruncatePrompt(string prompt, int max = 80)
    {
        if (string.IsNullOrEmpty(prompt)) return "(empty)";
        return prompt.Length <= max ? prompt : prompt[..max] + "…";
    }
}
