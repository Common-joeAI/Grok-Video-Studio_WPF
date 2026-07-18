using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrokVideoStudio.App.Services;
using GrokVideoStudio.Core.Models;
using GrokVideoStudio.Core.Services;
using Microsoft.Extensions.Logging;

namespace GrokVideoStudio.App.ViewModels;

/// <summary>
/// History page ViewModel — shows all past video generations with thumbnails.
/// 
/// FEATURE PARITY: The original Python app shows a "Generated Videos" picker
/// with thumbnail previews. This ViewModel loads videos from storage and
/// generates thumbnails on demand via FFmpeg.
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly IVideoStorageService _storageService;
    private readonly IVideoThumbnailService _thumbnailService;
    private readonly IVideoDownloadService _downloadService;
    private readonly ILogger<HistoryViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<VideoItem> _videos = [];

    [ObservableProperty]
    private VideoItem? _selectedVideo;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public HistoryViewModel(
        IVideoStorageService storageService,
        IVideoThumbnailService thumbnailService,
        IVideoDownloadService downloadService,
        ILogger<HistoryViewModel> logger)
    {
        _storageService = storageService;
        _thumbnailService = thumbnailService;
        _downloadService = downloadService;
        _logger = logger;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        StatusMessage = "Loading videos…";
        var loaded = await _storageService.LoadVideosAsync();
        Videos = new ObservableCollection<VideoItem>(loaded.OrderByDescending(v => v.CreatedAt));
        StatusMessage = $"Loaded {Videos.Count} video(s).";

        // Generate thumbnails for completed local videos that don't have one yet
        foreach (var video in Videos.Where(v =>
            v.Status == VideoGenerationStatus.Completed &&
            !string.IsNullOrEmpty(v.LocalFilePath) &&
            File.Exists(v.LocalFilePath) &&
            (string.IsNullOrEmpty(v.ThumbnailPath) || !File.Exists(v.ThumbnailPath))))
        {
            try
            {
                var thumb = await _thumbnailService.GenerateThumbnailAsync(video.LocalFilePath);
                var updated = video with { ThumbnailPath = thumb };
                await _storageService.UpdateVideoAsync(updated);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thumbnail generation failed for {Path}", video.LocalFilePath);
            }
        }
    }

    [RelayCommand]
    private void OpenVideoUrl(VideoItem? video)
    {
        if (video is null) return;
        if (!string.IsNullOrEmpty(video.VideoUrl))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = video.VideoUrl,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    private void OpenLocalVideo(VideoItem? video)
    {
        if (video is null) return;
        if (!string.IsNullOrEmpty(video.LocalFilePath) && File.Exists(video.LocalFilePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = video.LocalFilePath,
                UseShellExecute = true
            });
        }
        else if (!string.IsNullOrEmpty(video.VideoUrl))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = video.VideoUrl,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    private async Task DeleteVideoAsync(VideoItem? video)
    {
        if (video is null) return;
        await _storageService.DeleteVideoAsync(video.Id);
        Videos.Remove(video);
        StatusMessage = $"Deleted video.";
    }

    [RelayCommand]
    private async Task DownloadVideoAsync(VideoItem? video)
    {
        if (video is null || string.IsNullOrEmpty(video.VideoUrl)) return;

        try
        {
            StatusMessage = "Downloading video…";
            var downloadDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "GrokVideoStudio");
            var localPath = await _downloadService.DownloadAsync(video.VideoUrl, downloadDir, null, default);
            var updated = video with { LocalFilePath = localPath };
            await _storageService.UpdateVideoAsync(updated);
            StatusMessage = $"✓ Downloaded to {localPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Download failed: {ex.Message}";
            _logger.LogError(ex, "Download failed");
        }
    }
}
