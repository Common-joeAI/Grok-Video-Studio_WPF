using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrokVideoStudio.Core.Models;
using GrokVideoStudio.Core.Services;
using Microsoft.Extensions.Logging;

namespace GrokVideoStudio.App.ViewModels;

/// <summary>
/// Stitch page ViewModel — FFmpeg video stitching with crossfade, interpolation,
/// upscale, GPU encode, and music mixing. Matches the actual repo's stitch pipeline.
/// </summary>
public partial class StitchViewModel : ObservableObject
{
    private readonly IStitchService _stitchService;
    private readonly IVideoStorageService _storageService;
    private readonly ISecureSettingsService _settingsService;
    private readonly ILogger<StitchViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<VideoItem> _availableVideos = [];

    [ObservableProperty]
    private ObservableCollection<VideoItem> _selectedVideos = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StitchCommand))]
    private bool _isStitching;

    [ObservableProperty]
    private bool _enableCrossfade = true;

    [ObservableProperty]
    private int _interpolationFps;

    [ObservableProperty]
    private string _upscalePreset = "none";

    [ObservableProperty]
    private bool _enableGpuEncode;

    [ObservableProperty]
    private string? _musicMixPath;

    [ObservableProperty]
    private string _statusMessage = "Select clips to stitch.";

    [ObservableProperty]
    private string? _outputPath;

    public ObservableCollection<int> FpsOptions { get; } = [0, 48, 60];
    public ObservableCollection<string> UpscalePresets { get; } = ["none", "2x", "1080p", "1440p", "4K"];

    public StitchViewModel(
        IStitchService stitchService,
        IVideoStorageService storageService,
        ISecureSettingsService settingsService,
        ILogger<StitchViewModel> logger)
    {
        _stitchService = stitchService;
        _storageService = storageService;
        _settingsService = settingsService;
        _logger = logger;

        var s = settingsService.LoadSettings();
        EnableCrossfade = s.EnableCrossfade;
        InterpolationFps = s.InterpolationFps;
        UpscalePreset = s.UpscalePreset;
        EnableGpuEncode = s.EnableGpuEncode;
        MusicMixPath = string.IsNullOrEmpty(s.MusicMixPath) ? null : s.MusicMixPath;
    }

    [RelayCommand]
    private async Task LoadVideosAsync()
    {
        var videos = await _storageService.LoadVideosAsync();
        AvailableVideos = new ObservableCollection<VideoItem>(
            videos.Where(v => v.Status == VideoGenerationStatus.Completed));
    }

    [RelayCommand]
    private void AddToStitch(VideoItem? item)
    {
        if (item is not null && !SelectedVideos.Contains(item))
            SelectedVideos.Add(item);
    }

    [RelayCommand]
    private void RemoveFromStitch(VideoItem? item)
    {
        if (item is not null)
            SelectedVideos.Remove(item);
    }

    [RelayCommand]
    private void BrowseMusic()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Music Track",
            Filter = "Audio Files|*.mp3;*.wav;*.aac;*.flac|All Files|*.*"
        };
        if (dialog.ShowDialog() == true)
            MusicMixPath = dialog.FileName;
    }

    [RelayCommand(CanExecute = nameof(CanStitch))]
    private async Task StitchAsync()
    {
        if (SelectedVideos.Count == 0) return;

        IsStitching = true;
        StatusMessage = "Stitching videos…";

        var settings = _settingsService.LoadSettings();
        var outputPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            $"GrokVideoStudio_Stitched_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

        var options = new StitchOptions
        {
            EnableCrossfade = EnableCrossfade,
            InterpolationFps = InterpolationFps,
            UpscalePreset = UpscalePreset,
            EnableGpuEncode = EnableGpuEncode,
            MusicMixPath = MusicMixPath
        };

        // Download remote videos first if needed
        var localPaths = new List<string>();
        foreach (var video in SelectedVideos)
        {
            if (!string.IsNullOrEmpty(video.LocalFilePath) && File.Exists(video.LocalFilePath))
                localPaths.Add(video.LocalFilePath);
            else if (!string.IsNullOrEmpty(video.VideoUrl))
            {
                // Would download from URL in production
                StatusMessage = $"Would download {video.VideoUrl} — implement download in production.";
                return;
            }
        }

        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            await _stitchService.StitchAsync(localPaths, outputPath, options, progress, default);
            OutputPath = outputPath;
            StatusMessage = $"✓ Stitched to {outputPath}";

            // Open in file explorer
            if (File.Exists(outputPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{outputPath}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stitch failed");
            StatusMessage = $"✗ Stitch failed: {ex.Message}";
        }
        finally
        {
            IsStitching = false;
        }
    }

    private bool CanStitch() => !IsStitching && SelectedVideos.Count > 0;
}
