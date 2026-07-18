using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Publishes a video to a local folder — copies the file and writes a sidecar
/// JSON metadata file with title, description, tags, and timestamp.
/// </summary>
public interface IFolderPublishService
{
    /// <summary>
    /// Copies the video to the target folder and writes a metadata sidecar JSON.
    /// </summary>
    /// <param name="videoPath">Local path to the source video file.</param>
    /// <param name="targetFolder">Destination folder. Created if it does not exist.</param>
    /// <param name="title">Video title (used for the output filename).</param>
    /// <param name="description">Video description, written to the sidecar JSON.</param>
    /// <param name="tags">Tags array, written to the sidecar JSON.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The full path to the copied video file.</returns>
    Task<string> PublishAsync(
        string videoPath,
        string targetFolder,
        string title,
        string description,
        string[] tags,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct);
}

/// <summary>
/// Local folder publisher — copies the video file and writes a <c>.meta.json</c>
/// sidecar with publish metadata. No network calls, no credentials needed.
/// </summary>
public sealed class FolderPublishService : IFolderPublishService
{
    public async Task<string> PublishAsync(
        string videoPath,
        string targetFolder,
        string title,
        string description,
        string[] tags,
        IProgress<(int percent, string message)>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            throw new ArgumentException("Video path cannot be null or empty.", nameof(videoPath));
        if (!File.Exists(videoPath))
            throw new FileNotFoundException("Local video file not found.", videoPath);
        if (string.IsNullOrWhiteSpace(targetFolder))
            throw new ArgumentException("Target folder cannot be null or empty.", nameof(targetFolder));

        progress?.Report((5, "Folder: Creating destination directory…"));
        Directory.CreateDirectory(targetFolder);

        // Sanitize the title into a safe filename.
        var invalid = Path.GetInvalidFileNameChars();
        var safeName = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "video";
        var ext = Path.GetExtension(videoPath);
        if (string.IsNullOrEmpty(ext))
            ext = ".mp4";
        var destPath = Path.Combine(targetFolder, $"{safeName}{ext}");

        // Avoid clobbering existing files.
        var counter = 1;
        while (File.Exists(destPath))
        {
            destPath = Path.Combine(targetFolder, $"{safeName}_{counter}{ext}");
            counter++;
        }

        progress?.Report((10, $"Folder: Copying to {destPath}…"));

        var fileInfo = new FileInfo(videoPath);
        var totalBytes = fileInfo.Length;
        long copiedBytes = 0;
        var buffer = new byte[1024 * 1024]; // 1 MB buffer

        await using (var source = File.OpenRead(videoPath))
        await using (var dest = File.Create(destPath))
        {
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                copiedBytes += read;
                var pct = (int)((double)copiedBytes / totalBytes * 80) + 10; // 10–90
                progress?.Report((pct, $"Folder: Copying… {copiedBytes:N0} / {totalBytes:N0} bytes"));
            }
        }

        // Write sidecar metadata JSON.
        progress?.Report((92, "Folder: Writing metadata sidecar…"));
        var metaPath = Path.ChangeExtension(destPath, ".meta.json");
        var metadata = new
        {
            title,
            description,
            tags,
            sourcePath = videoPath,
            publishedAt = DateTimeOffset.UtcNow,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(metaPath, json, ct);

        progress?.Report((100, $"Folder: Published to {destPath}"));
        return destPath;
    }
}
