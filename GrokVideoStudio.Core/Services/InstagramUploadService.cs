using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Service interface for publishing videos to Instagram Business/Creator accounts via Graph API.
/// </summary>
public interface IInstagramUploadService
{
    /// <summary>
    /// Uploads and publishes a video to Instagram using the 3-step Container Flow.
    /// Note: Instagram API requires a publicly accessible video URL.
    /// </summary>
    /// <param name="igUserId">The Instagram User ID (not username).</param>
    /// <param name="accessToken">The Facebook Access Token with instagram_basic and instagram_content_publish permissions.</param>
    /// <param name="videoUrl">A publicly accessible URL where Instagram can download the video.</param>
    /// <param name="caption">The caption/text for the Instagram post.</param>
    /// <param name="progress">Progress reporter for step feedback.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The published Instagram Media ID.</returns>
    Task<string> UploadAsync(
        string igUserId,
        string accessToken,
        string videoUrl,
        string caption,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct);
}

/// <summary>
/// Instagram Graph API implementation using the 3-step flow:
/// 1. Create Media Container
/// 2. Poll Container Status until Finished
/// 3. Publish Media Container
/// </summary>
public sealed class InstagramUploadService : IInstagramUploadService
{
    private readonly HttpClient _httpClient;
    private const string GraphApiBase = "https://graph.facebook.com/v21.0";

    /// <summary>
    /// Initializes a new instance of the <see cref="InstagramUploadService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client injected via constructor.</param>
    public InstagramUploadService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(
        string igUserId,
        string accessToken,
        string videoUrl,
        string caption,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(igUserId))
            throw new ArgumentException("Instagram User ID cannot be null or empty.", nameof(igUserId));
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("Access token cannot be null or empty.", nameof(accessToken));
        if (string.IsNullOrWhiteSpace(videoUrl))
            throw new ArgumentException("Video URL cannot be null or empty.", nameof(videoUrl));

        // ── STEP 1: Create Media Container ──
        progress?.Report((10, "Instagram: Step 1/3 - Initializing media container..."));

        string createUrl = $"{GraphApiBase}/{igUserId}/media";
        var createParams = new Dictionary<string, string>
        {
            { "media_type", "VIDEO" },
            { "video_url", videoUrl },
            { "caption", caption },
            { "access_token", accessToken }
        };

        using var createForm = new FormUrlEncodedContent(createParams);
        using var createResponse = await _httpClient.PostAsync(createUrl, createForm, ct).ConfigureAwait(false);
        string createJson = await createResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!createResponse.IsSuccessStatusCode)
        {
            ThrowIfGraphError(createJson, "Create Container");
            throw new InvalidOperationException($"Instagram container creation failed with status {createResponse.StatusCode}. Details: {createJson}");
        }

        var container = JsonSerializer.Deserialize<InstagramContainerResponse>(createJson);
        if (container == null || string.IsNullOrWhiteSpace(container.Id))
        {
            throw new InvalidOperationException("Instagram container created, but the API returned an invalid response.");
        }

        string creationId = container.Id;
        progress?.Report((25, $"Instagram: Container created successfully (ID: {creationId})."));

        // ── STEP 2: Poll Container Status ──
        progress?.Report((30, "Instagram: Step 2/3 - Polling container compilation status..."));

        string statusUrl = $"{GraphApiBase}/{creationId}?fields=status_code,error_info&access_token={accessToken}";
        bool isFinished = false;
        int attempts = 0;
        const int maxAttempts = 30; // 30 * 5s = 150 seconds max wait
        const int delayMs = 5000;

        while (!isFinished && attempts < maxAttempts)
        {
            ct.ThrowIfCancellationRequested();
            attempts++;

            progress?.Report((30 + (int)(attempts * 1.5), $"Instagram: Polling status (Attempt {attempts}/{maxAttempts})..."));

            using var statusResponse = await _httpClient.GetAsync(statusUrl, ct).ConfigureAwait(false);
            string statusJson = await statusResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!statusResponse.IsSuccessStatusCode)
            {
                ThrowIfGraphError(statusJson, "Status Polling");
                throw new InvalidOperationException($"Instagram status check failed with status {statusResponse.StatusCode}. Details: {statusJson}");
            }

            var statusObj = JsonSerializer.Deserialize<InstagramStatusResponse>(statusJson);
            if (statusObj == null)
            {
                throw new InvalidOperationException("Instagram returned an unparseable status response.");
            }

            string code = statusObj.StatusCode.ToUpperInvariant();
            if (code == "FINISHED")
            {
                isFinished = true;
                progress?.Report((75, "Instagram: Video processed and ready for publication."));
            }
            else if (code == "ERROR" || code == "EXPIRED")
            {
                string errorDetail = statusObj.ErrorInfo?.Message ?? "Unknown processing error";
                throw new InvalidOperationException($"Instagram video processing failed. Status: {code}. Details: {errorDetail}");
            }
            else
            {
                // Status is likely IN_PROGRESS or similar
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }

        if (!isFinished)
        {
            throw new TimeoutException("Instagram video processing timed out on the Facebook Graph API side.");
        }

        // ── STEP 3: Publish Media Container ──
        progress?.Report((80, "Instagram: Step 3/3 - Publishing media container..."));

        string publishUrl = $"{GraphApiBase}/{igUserId}/media_publish";
        var publishParams = new Dictionary<string, string>
        {
            { "creation_id", creationId },
            { "access_token", accessToken }
        };

        using var publishForm = new FormUrlEncodedContent(publishParams);
        using var publishResponse = await _httpClient.PostAsync(publishUrl, publishForm, ct).ConfigureAwait(false);
        string publishJson = await publishResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!publishResponse.IsSuccessStatusCode)
        {
            ThrowIfGraphError(publishJson, "Publish Container");
            throw new InvalidOperationException($"Instagram publication failed with status {publishResponse.StatusCode}. Details: {publishJson}");
        }

        var publishResult = JsonSerializer.Deserialize<InstagramPublishResponse>(publishJson);
        if (publishResult == null || string.IsNullOrWhiteSpace(publishResult.Id))
        {
            throw new InvalidOperationException("Instagram publication completed, but returned an invalid ID.");
        }

        progress?.Report((100, $"Instagram: Post published successfully! Media ID: {publishResult.Id}"));
        return publishResult.Id;
    }

    private static void ThrowIfGraphError(string json, string context)
    {
        try
        {
            var errorResponse = JsonSerializer.Deserialize<InstagramErrorResponse>(json);
            if (errorResponse?.Error != null)
            {
                throw new InvalidOperationException($"Instagram {context} error: {errorResponse.Error.Message} (Code: {errorResponse.Error.Code}, Subcode: {errorResponse.Error.ErrorSubcode})");
            }
        }
        catch (JsonException)
        {
            // Fall through if not a JSON error response
        }
    }

    private sealed class InstagramContainerResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    private sealed class InstagramStatusResponse
    {
        [JsonPropertyName("status_code")]
        public string StatusCode { get; set; } = string.Empty;

        [JsonPropertyName("error_info")]
        public InstagramErrorInfo? ErrorInfo { get; set; }
    }

    private sealed class InstagramErrorInfo
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class InstagramPublishResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    private sealed class InstagramErrorResponse
    {
        [JsonPropertyName("error")]
        public InstagramErrorDetail? Error { get; set; }
    }

    private sealed class InstagramErrorDetail
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("error_subcode")]
        public int ErrorSubcode { get; set; }
    }
}
