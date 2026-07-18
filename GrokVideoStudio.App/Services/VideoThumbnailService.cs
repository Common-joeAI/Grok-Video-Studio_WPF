using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace GrokVideoStudio.App.Services;

/// <summary>
/// WPF-specific service that extracts thumbnails from video files using FFmpeg
/// and creates BitmapImages for UI rendering with proper caching to avoid file locks.
/// </summary>
public interface IVideoThumbnailService
{
    /// <summary>
    /// Generates a thumbnail JPG file from a video and returns its file path.
    /// </summary>
    Task<string> GenerateThumbnailAsync(string videoPath);

    /// <summary>
    /// Loads a thumbnail image file into a frozen WPF BitmapImage to prevent file locking and ensure thread safety.
    /// </summary>
    BitmapImage LoadThumbnail(string thumbnailPath);
}

public sealed class VideoThumbnailService : IVideoThumbnailService
{
    private readonly string _ffmpegPath;
    private readonly string _thumbnailFolder;

    public VideoThumbnailService(string ffmpegPath = "ffmpeg")
    {
        _ffmpegPath = ffmpegPath;
        _thumbnailFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GrokVideoStudio",
            "thumbnails"
        );

        if (!Directory.Exists(_thumbnailFolder))
        {
            Directory.CreateDirectory(_thumbnailFolder);
        }
    }

    /// <inheritdoc />
    public async Task<string> GenerateThumbnailAsync(string videoPath)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
        {
            throw new FileNotFoundException("Video file not found for thumbnail generation.", videoPath);
        }

        // Generate a deterministic filename based on the video file name and size to avoid collision
        var fileInfo = new FileInfo(videoPath);
        var uniqueName = $"{Path.GetFileNameWithoutExtension(videoPath)}_{fileInfo.Length}";
        var thumbnailPath = Path.Combine(_thumbnailFolder, $"{uniqueName}_thumb.jpg");

        // If thumbnail already exists, return its path directly
        if (File.Exists(thumbnailPath))
        {
            return thumbnailPath;
        }

        // Run FFmpeg to extract the first frame (at 0 seconds)
        var args = $"-ss 00:00:00 -i \"{videoPath}\" -vframes 1 -q:v 2 \"{thumbnailPath}\" -y";

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || !File.Exists(thumbnailPath))
        {
            // Fallback: try without fast-seeking if 0.0s seek failed
            var fallbackArgs = $"-i \"{videoPath}\" -vframes 1 -q:v 2 \"{thumbnailPath}\" -y";
            psi.Arguments = fallbackArgs;
            using var fallbackProcess = new Process { StartInfo = psi };
            fallbackProcess.Start();
            await fallbackProcess.WaitForExitAsync();
        }

        if (!File.Exists(thumbnailPath))
        {
            throw new InvalidOperationException($"FFmpeg failed to extract a thumbnail from: {videoPath}");
        }

        return thumbnailPath;
    }

    /// <inheritdoc />
    public BitmapImage LoadThumbnail(string thumbnailPath)
    {
        if (!File.Exists(thumbnailPath))
        {
            throw new FileNotFoundException("Thumbnail image file not found.", thumbnailPath);
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(thumbnailPath, UriKind.Absolute);
        
        // Critical: CacheOnLoad loads the image into memory and closes the file stream,
        // which prevents WPF from locking the file on disk.
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        
        // Critical: Freeze the image so it becomes thread-safe and can be passed to/from different threads.
        bitmap.Freeze();
        
        return bitmap;
    }
}
