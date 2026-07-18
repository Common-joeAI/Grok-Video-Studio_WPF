using System.Net.Http;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Downloads videos from URLs to local disk with progress reporting.
/// Matches the original Python app's video download workflow.
/// </summary>
public interface IVideoDownloadService
{
    Task<string> DownloadAsync(
        string url,
        string destinationDir,
        IProgress<(int percent, string status)>? progress = null,
        CancellationToken ct = default);
}

public sealed class VideoDownloadService : IVideoDownloadService
{
    private readonly HttpClient _httpClient;

    public VideoDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> DownloadAsync(
        string url,
        string destinationDir,
        IProgress<(int percent, string status)>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationDir);

        // Determine filename from URL or generate one
        var uri = new Uri(url);
        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrEmpty(fileName) || !fileName.Contains('.'))
            fileName = $"video_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";

        var destPath = Path.Combine(destinationDir, fileName);

        // Don't re-download if already exists
        if (File.Exists(destPath))
        {
            // Check if it's a valid size
            var existingSize = new FileInfo(destPath).Length;
            if (existingSize > 1024) return destPath;
        }

        progress?.Report((0, "Starting download…"));

        using var resp = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var totalBytes = resp.Content.Headers.ContentLength ?? -1;
        var fileNameFinal = Path.GetFileName(destPath);
        progress?.Report((2, $"Downloading {fileNameFinal}…"));

        await using var contentStream = await resp.Content.ReadAsStreamAsync(ct);
        await using var fileStream = File.Create(destPath);

        var buffer = new byte[81920];
        long bytesRead = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            bytesRead += read;

            if (totalBytes > 0 && progress is not null)
            {
                var pct = (int)((bytesRead * 100) / totalBytes);
                progress?.Report((Math.Min(pct, 99), $"Downloading {pct}%"));
            }
        }

        progress?.Report((100, "Download complete."));
        return destPath;
    }
}
