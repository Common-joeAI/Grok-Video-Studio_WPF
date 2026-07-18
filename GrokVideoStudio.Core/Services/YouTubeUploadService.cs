using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Service interface for uploading videos to YouTube using the official Google API.
/// </summary>
public interface IYouTubeUploadService
{
    /// <summary>
    /// Uploads a video to YouTube using OAuth2 and resumable chunked upload.
    /// </summary>
    /// <param name="videoPath">The local path to the video file.</param>
    /// <param name="title">The video title.</param>
    /// <param name="description">The video description.</param>
    /// <param name="tags">The tags or keywords for the video.</param>
    /// <param name="categoryId">The YouTube category ID (e.g. "22" for People & Blogs).</param>
    /// <param name="clientSecretPath">Path to the client_secrets.json file downloaded from Google Cloud Console.</param>
    /// <param name="tokenPath">The directory path where access tokens will be stored securely.</param>
    /// <param name="progress">Progress reporter that returns percent progress and status messages.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The uploaded YouTube Video ID.</returns>
    Task<string> UploadAsync(
        string videoPath,
        string title,
        string description,
        string[] tags,
        string categoryId,
        string clientSecretPath,
        string tokenPath,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct);
}

/// <summary>
/// YouTube Data API v3 upload implementation using Google.Apis.YouTube.v3 and Google.Apis.Auth.
/// </summary>
public sealed class YouTubeUploadService : IYouTubeUploadService
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="YouTubeUploadService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client injected via constructor.</param>
    public YouTubeUploadService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(
        string videoPath,
        string title,
        string description,
        string[] tags,
        string categoryId,
        string clientSecretPath,
        string tokenPath,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            throw new ArgumentException("Video path cannot be null or empty.", nameof(videoPath));
        if (!File.Exists(videoPath))
            throw new FileNotFoundException("Local video file not found.", videoPath);
        if (string.IsNullOrWhiteSpace(clientSecretPath))
            throw new ArgumentException("Client secret path cannot be null or empty.", nameof(clientSecretPath));
        if (!File.Exists(clientSecretPath))
            throw new FileNotFoundException("Google client secrets JSON file not found.", clientSecretPath);
        if (string.IsNullOrWhiteSpace(tokenPath))
            throw new ArgumentException("Token path cannot be null or empty.", nameof(tokenPath));

        progress?.Report((5, "YouTube: Initializing OAuth2 authorization flow..."));

        UserCredential credential;
        using (var stream = new FileStream(clientSecretPath, FileMode.Open, FileAccess.Read))
        {
            // InstalledAppFlow handles the OAuth2 redirect and local server authorization.
            // Stores and manages credentials using a local file data store.
            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                (await GoogleClientSecrets.FromStreamAsync(stream, ct)).Secrets,
                [YouTubeService.Scope.YoutubeUpload],
                "user",
                ct,
                new FileDataStore(tokenPath, true)
            );
        }

        progress?.Report((15, "YouTube: Authorization successful. Constructing service..."));

        var youtubeService = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "GrokVideoStudio"
        });

        var video = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = title,
                Description = description,
                Tags = tags,
                CategoryId = categoryId
            },
            Status = new VideoStatus
            {
                PrivacyStatus = "unlisted" // Default to unlisted for safety; can be public or private.
            }
        };

        progress?.Report((20, "YouTube: Beginning resumable upload..."));

        string videoId = string.Empty;
        using (var fileStream = new FileStream(videoPath, FileMode.Open, FileAccess.Read))
        {
            var insertRequest = youtubeService.Videos.Insert(video, "snippet,status", fileStream, "video/*");
            
            insertRequest.ProgressChanged += (IUploadProgress uploadProgress) =>
            {
                switch (uploadProgress.Status)
                {
                    case UploadStatus.Uploading:
                        int percent = (int)((double)uploadProgress.BytesSent / fileStream.Length * 100);
                        // Scale percentage from 20% to 95%
                        int scaledPercent = 20 + (int)(percent * 0.75);
                        progress?.Report((scaledPercent, $"YouTube: Uploading {percent}% ({uploadProgress.BytesSent:N0} bytes sent)"));
                        break;

                    case UploadStatus.Completed:
                        progress?.Report((95, "YouTube: Upload finalized. Processing..."));
                        break;

                    case UploadStatus.Failed:
                        progress?.Report((0, $"YouTube: Resumable upload failed: {uploadProgress.Exception?.Message}"));
                        break;
                }
            };

            insertRequest.ResponseReceived += (Video responseVideo) =>
            {
                videoId = responseVideo.Id;
            };

            var uploadResult = await insertRequest.UploadAsync(ct).ConfigureAwait(false);

            if (uploadResult.Status == UploadStatus.Failed)
            {
                throw new InvalidOperationException($"YouTube upload failed: {uploadResult.Exception?.Message}", uploadResult.Exception);
            }
        }

        if (string.IsNullOrEmpty(videoId))
        {
            throw new InvalidOperationException("YouTube upload completed but did not return a valid video ID.");
        }

        progress?.Report((100, $"YouTube: Upload complete! Video ID: {videoId}"));
        return videoId;
    }
}
