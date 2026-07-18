using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Service interface for uploading videos to TikTok using the Content Posting API.
/// </summary>
public interface ITikTokUploadService
{
    /// <summary>
    /// Uploads and publishes a video to TikTok using the 3-step Direct Post Flow.
    /// </summary>
    /// <param name="accessToken">The user's TikTok OAuth2 access token.</param>
    /// <param name="videoPath">The local path to the video file.</param>
    /// <param name="caption">The video caption.</param>
    /// <param name="privacyLevel">The privacy level (PUBLIC_TO_EVERYONE, SELF_ONLY, MUTUAL_FOLLOW_FRIENDS, etc.).</param>
    /// <param name="progress">Progress reporter for step feedback.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The TikTok publish ID.</returns>
    Task<string> UploadAsync(
        string accessToken,
        string videoPath,
        string caption,
        string privacyLevel,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct);
}

/// <summary>
/// TikTok Content Posting API implementation.
/// </summary>
public sealed class TikTokUploadService : ITikTokUploadService
{
    private readonly HttpClient _httpClient;
    private const string TikTokApiBase = "https://open.tiktokapis.com/v2";

    /// <summary>
    /// Initializes a new instance of the <see cref="TikTokUploadService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client injected via constructor.</param>
    public TikTokUploadService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(
        string accessToken,
        string videoPath,
        string caption,
        string privacyLevel,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("Access token cannot be null or empty.", nameof(accessToken));
        if (string.IsNullOrWhiteSpace(videoPath))
            throw new ArgumentException("Video path cannot be null or empty.", nameof(videoPath));
        if (!File.Exists(videoPath))
            throw new FileNotFoundException("Local video file not found.", videoPath);

        long fileSize = new FileInfo(videoPath).Length;

        // Map and validate privacy level
        string formattedPrivacy = privacyLevel.ToUpperInvariant() switch
        {
            "PUBLIC" or "PUBLIC_TO_EVERYONE" => "PUBLIC_TO_EVERYONE",
            "PRIVATE" or "SELF_ONLY" => "SELF_ONLY",
            "FRIENDS" or "MUTUAL_FOLLOW_FRIENDS" => "MUTUAL_FOLLOW_FRIENDS",
            _ => "PUBLIC_TO_EVERYONE" // Default safe fallback
        };

        // ── STEP 1: Initialize Upload Session ──
        progress?.Report((5, "TikTok: Step 1/3 - Initializing posting session..."));

        string initUrl = $"{TikTokApiBase}/post/publish/video/init/";
        var initPayload = new TikTokInitRequest
        {
            PostInfo = new TikTokPostInfo
            {
                Title = caption,
                PrivacyLevel = formattedPrivacy,
                VideoCoverTimestampMs = 0
            },
            SourceInfo = new TikTokSourceInfo
            {
                Source = "FILE_UPLOAD",
                VideoSize = fileSize,
                ChunkSize = fileSize,
                TotalChunkCount = 1
            }
        };

        using var initRequest = new HttpRequestMessage(HttpMethod.Post, initUrl);
        initRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        string payloadJson = JsonSerializer.Serialize(initPayload);
        initRequest.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        using var initResponse = await _httpClient.SendAsync(initRequest, ct).ConfigureAwait(false);
        string initResultJson = await initResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!initResponse.IsSuccessStatusCode)
        {
            ThrowIfTikTokError(initResultJson, "Initialize Upload");
            throw new InvalidOperationException($"TikTok initialization failed with status {initResponse.StatusCode}. Details: {initResultJson}");
        }

        var initObj = JsonSerializer.Deserialize<TikTokInitResponse>(initResultJson);
        if (initObj == null || initObj.Error?.Code != "ok" || initObj.Data == null)
        {
            string errMsg = initObj?.Error?.Message ?? "Unknown API error";
            throw new InvalidOperationException($"TikTok API error during initialization: {errMsg} (Code: {initObj?.Error?.Code})");
        }

        string uploadUrl = initObj.Data.UploadUrl;
        string publishId = initObj.Data.PublishId;

        progress?.Report((15, $"TikTok: Upload session initialized. Publish ID: {publishId}"));

        // ── STEP 2: PUT Video Binary ──
        progress?.Report((20, "TikTok: Step 2/3 - Transferring video binary..."));

        using var fileStream = new FileStream(videoPath, FileMode.Open, FileAccess.Read);
        using var progressStream = new ProgressStream(fileStream, progress, "TikTok");
        using var streamContent = new StreamContent(progressStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        streamContent.Headers.ContentLength = fileSize;
        streamContent.Headers.Add("Content-Range", $"bytes 0-{fileSize - 1}/{fileSize}");

        using var putRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
        putRequest.Content = streamContent;

        using var putResponse = await _httpClient.SendAsync(putRequest, ct).ConfigureAwait(false);
        
        if (!putResponse.IsSuccessStatusCode)
        {
            string putErr = await putResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"TikTok binary transfer failed with status {putResponse.StatusCode}. Details: {putErr}");
        }

        progress?.Report((90, "TikTok: Binary upload finished. Waiting for processing..."));

        // ── STEP 3: Poll Publish Status ──
        progress?.Report((92, "TikTok: Step 3/3 - Polling publication status..."));

        string statusUrl = $"{TikTokApiBase}/post/publish/status/fetch/";
        bool isPublished = false;
        int attempts = 0;
        const int maxAttempts = 24; // 24 * 5s = 120 seconds max poll
        const int delayMs = 5000;

        while (!isPublished && attempts < maxAttempts)
        {
            ct.ThrowIfCancellationRequested();
            attempts++;

            progress?.Report((92, $"TikTok: Polling status (Attempt {attempts}/{maxAttempts})..."));

            using var statusRequest = new HttpRequestMessage(HttpMethod.Post, statusUrl);
            statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var statusPayload = new { publish_id = publishId };
            statusRequest.Content = new StringContent(JsonSerializer.Serialize(statusPayload), Encoding.UTF8, "application/json");

            using var statusResponse = await _httpClient.SendAsync(statusRequest, ct).ConfigureAwait(false);
            string statusResultJson = await statusResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!statusResponse.IsSuccessStatusCode)
            {
                ThrowIfTikTokError(statusResultJson, "Status Polling");
                throw new InvalidOperationException($"TikTok status check failed with status {statusResponse.StatusCode}. Details: {statusResultJson}");
            }

            var statusObj = JsonSerializer.Deserialize<TikTokStatusResponse>(statusResultJson);
            if (statusObj == null || statusObj.Error?.Code != "ok" || statusObj.Data == null)
            {
                string errMsg = statusObj?.Error?.Message ?? "Unknown API error";
                throw new InvalidOperationException($"TikTok API error during polling: {errMsg} (Code: {statusObj?.Error?.Code})");
            }

            string state = statusObj.Data.Status.ToUpperInvariant();
            if (state == "SUCCESS")
            {
                isPublished = true;
            }
            else if (state == "FAILED")
            {
                throw new InvalidOperationException($"TikTok post processing failed. Reason: {statusObj.Data.FailReason}");
            }
            else
            {
                // Status is PROCESSING or similar
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }

        if (!isPublished)
        {
            throw new TimeoutException("TikTok video processing timed out on the TikTok API side.");
        }

        progress?.Report((100, $"TikTok: Video published successfully! Publish ID: {publishId}"));
        return publishId;
    }

    private static void ThrowIfTikTokError(string json, string context)
    {
        try
        {
            var errorResponse = JsonSerializer.Deserialize<TikTokErrorOuterResponse>(json);
            if (errorResponse?.Error != null && errorResponse.Error.Code != "ok")
            {
                throw new InvalidOperationException($"TikTok {context} error: {errorResponse.Error.Message} (Code: {errorResponse.Error.Code})");
            }
        }
        catch (JsonException)
        {
            // Fall through if not a standard TikTok error JSON
        }
    }

    private sealed class TikTokInitRequest
    {
        [JsonPropertyName("post_info")]
        public required TikTokPostInfo PostInfo { get; set; }

        [JsonPropertyName("source_info")]
        public required TikTokSourceInfo SourceInfo { get; set; }
    }

    private sealed class TikTokPostInfo
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("privacy_level")]
        public string PrivacyLevel { get; set; } = "PUBLIC_TO_EVERYONE";

        [JsonPropertyName("video_cover_timestamp_ms")]
        public int VideoCoverTimestampMs { get; set; }
    }

    private sealed class TikTokSourceInfo
    {
        [JsonPropertyName("source")]
        public string Source { get; set; } = "FILE_UPLOAD";

        [JsonPropertyName("video_size")]
        public long VideoSize { get; set; }

        [JsonPropertyName("chunk_size")]
        public long ChunkSize { get; set; }

        [JsonPropertyName("total_chunk_count")]
        public int TotalChunkCount { get; set; } = 1;
    }

    private sealed class TikTokInitResponse
    {
        [JsonPropertyName("data")]
        public TikTokInitData? Data { get; set; }

        [JsonPropertyName("error")]
        public TikTokErrorDetail? Error { get; set; }
    }

    private sealed class TikTokInitData
    {
        [JsonPropertyName("publish_id")]
        public string PublishId { get; set; } = string.Empty;

        [JsonPropertyName("upload_url")]
        public string UploadUrl { get; set; } = string.Empty;
    }

    private sealed class TikTokStatusResponse
    {
        [JsonPropertyName("data")]
        public TikTokStatusData? Data { get; set; }

        [JsonPropertyName("error")]
        public TikTokErrorDetail? Error { get; set; }
    }

    private sealed class TikTokStatusData
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("fail_reason")]
        public string FailReason { get; set; } = string.Empty;
    }

    private sealed class TikTokErrorOuterResponse
    {
        [JsonPropertyName("error")]
        public TikTokErrorDetail? Error { get; set; }
    }

    private sealed class TikTokErrorDetail
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Custom stream wrapper that intercepts read operations to report upload progress.
    /// </summary>
    private sealed class ProgressStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly IProgress<(int percent, string message)>? _progress;
        private readonly string _platformPrefix;
        private readonly long _totalLength;
        private long _totalBytesSent;
        private int _lastPercent = -1;

        public ProgressStream(Stream innerStream, IProgress<(int percent, string message)>? progress, string platformPrefix)
        {
            _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
            _progress = progress;
            _platformPrefix = platformPrefix;
            _totalLength = innerStream.Length;
        }

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;
        public override long Position
        {
            get => _innerStream.Position;
            set => _innerStream.Position = value;
        }

        public override void Flush() => _innerStream.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _innerStream.Read(buffer, offset, count);
            ReportProgress(bytesRead);
            return bytesRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int bytesRead = await _innerStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            ReportProgress(bytesRead);
            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            int bytesRead = await _innerStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            ReportProgress(bytesRead);
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => _innerStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);

        private void ReportProgress(int bytesRead)
        {
            if (bytesRead > 0 && _totalLength > 0)
            {
                _totalBytesSent += bytesRead;
                // Scale from 20% to 90%
                int percent = (int)((double)_totalBytesSent / _totalLength * 100);
                if (percent != _lastPercent)
                {
                    _lastPercent = percent;
                    int scaledPercent = 20 + (int)(percent * 0.70);
                    _progress?.Report((scaledPercent, $"{_platformPrefix}: Uploading {percent}% ({_totalBytesSent:N0} / {_totalLength:N0} bytes)"));
                }
            }
        }
    }
}
