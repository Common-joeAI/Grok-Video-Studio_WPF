using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Service interface for uploading videos to Facebook Pages via Graph API.
/// </summary>
public interface IFacebookUploadService
{
    /// <summary>
    /// Uploads a video to a Facebook Page using the Graph API.
    /// </summary>
    /// <param name="pageId">The target Facebook Page ID.</param>
    /// <param name="accessToken">The Facebook Page Access Token.</param>
    /// <param name="videoPath">The local path to the video file.</param>
    /// <param name="title">The video title.</param>
    /// <param name="description">The video description (post caption).</param>
    /// <param name="progress">Progress reporter for upload feedback.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The uploaded Facebook Video ID.</returns>
    Task<string> UploadAsync(
        string pageId,
        string accessToken,
        string videoPath,
        string title,
        string description,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct);
}

/// <summary>
/// Facebook Graph API v21.0 upload implementation.
/// </summary>
public sealed class FacebookUploadService : IFacebookUploadService
{
    private readonly HttpClient _httpClient;
    private const string GraphApiBase = "https://graph.facebook.com/v21.0";

    /// <summary>
    /// Initializes a new instance of the <see cref="FacebookUploadService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client injected via constructor.</param>
    public FacebookUploadService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(
        string pageId,
        string accessToken,
        string videoPath,
        string title,
        string description,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pageId))
            throw new ArgumentException("Page ID cannot be null or empty.", nameof(pageId));
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("Access token cannot be null or empty.", nameof(accessToken));
        if (string.IsNullOrWhiteSpace(videoPath))
            throw new ArgumentException("Video path cannot be null or empty.", nameof(videoPath));
        if (!File.Exists(videoPath))
            throw new FileNotFoundException("Local video file not found.", videoPath);

        progress?.Report((5, "Facebook: Initializing upload request..."));

        string url = $"{GraphApiBase}/{pageId}/videos";

        using var fileStream = new FileStream(videoPath, FileMode.Open, FileAccess.Read);
        using var progressStream = new ProgressStream(fileStream, progress, "Facebook");
        using var streamContent = new StreamContent(progressStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(accessToken), "access_token");
        form.Add(new StringContent(title), "title");
        form.Add(new StringContent(description), "description");
        
        // Facebook Graph API expects the file in the "source" parameter
        form.Add(streamContent, "source", Path.GetFileName(videoPath));

        progress?.Report((10, "Facebook: Transmitting multipart payload..."));

        using var response = await _httpClient.PostAsync(url, form, ct).ConfigureAwait(false);
        
        string jsonResponse = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var errorObj = JsonSerializer.Deserialize<FacebookErrorResponse>(jsonResponse);
                if (errorObj?.Error != null)
                {
                    throw new InvalidOperationException($"Facebook API error: {errorObj.Error.Message} (Code: {errorObj.Error.Code}, Subcode: {errorObj.Error.ErrorSubcode})");
                }
            }
            catch (JsonException)
            {
                // Fallback to generic status error if JSON parsing fails
            }

            throw new InvalidOperationException($"Facebook upload failed with status code {response.StatusCode}. Details: {jsonResponse}");
        }

        var successObj = JsonSerializer.Deserialize<FacebookSuccessResponse>(jsonResponse);
        if (successObj == null || string.IsNullOrWhiteSpace(successObj.Id))
        {
            throw new InvalidOperationException("Facebook upload was successful, but the API response did not contain a valid ID.");
        }

        progress?.Report((100, $"Facebook: Upload completed successfully. Video ID: {successObj.Id}"));
        return successObj.Id;
    }

    private sealed class FacebookSuccessResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    private sealed class FacebookErrorResponse
    {
        [JsonPropertyName("error")]
        public FacebookErrorDetail? Error { get; set; }
    }

    private sealed class FacebookErrorDetail
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("error_subcode")]
        public int ErrorSubcode { get; set; }

        [JsonPropertyName("fbtrace_id")]
        public string FbTraceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Custom stream wrapper that intercept read operations to report upload progress.
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
                // Scale from 10% to 95%
                int percent = (int)((double)_totalBytesSent / _totalLength * 100);
                if (percent != _lastPercent)
                {
                    _lastPercent = percent;
                    int scaledPercent = 10 + (int)(percent * 0.85);
                    _progress?.Report((scaledPercent, $"{_platformPrefix}: Uploading {percent}% ({_totalBytesSent:N0} / {_totalLength:N0} bytes)"));
                }
            }
        }
    }
}
