using System;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using GrokVideoStudio.Core.Services;

namespace GrokVideoStudio.App.ViewModels;

/// <summary>
/// ViewModel for the video player page.
/// Manages playback state, properties, and buttons for controlling MediaElement.
/// </summary>
public partial class VideoPlayerViewModel : ObservableObject
{
    private readonly IActivityLogService _activityLog;
    private readonly ILogger<VideoPlayerViewModel> _logger;

    // ── Events to communicate playback requests to the View's MediaElement ──
    public event Action? PlayRequested;
    public event Action? PauseRequested;
    public event Action? StopRequested;
    public event Action? FullscreenToggleRequested;

    [ObservableProperty]
    private string? _videoPath;

    [ObservableProperty]
    private string _videoFileName = "No file selected";

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private double _position; // Current position in seconds

    [ObservableProperty]
    private double _duration; // Total duration in seconds

    [ObservableProperty]
    private double _volume = 0.8; // Default volume (0.0 to 1.0)

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private string _currentTimeText = "00:00";

    [ObservableProperty]
    private string _totalTimeText = "00:00";

    public VideoPlayerViewModel(IActivityLogService activityLog, ILogger<VideoPlayerViewModel> logger)
    {
        _activityLog = activityLog;
        _logger = logger;
    }

    partial void OnVideoPathChanged(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            VideoFileName = "No file selected";
            Duration = 0;
            Position = 0;
            CurrentTimeText = "00:00";
            TotalTimeText = "00:00";
        }
        else
        {
            VideoFileName = Path.GetFileName(value);
            _activityLog.Log($"Video loaded in player: {VideoFileName}", LogLevel.Information);
            _logger.LogInformation("Loaded video file: {Path}", value);
        }
    }

    [RelayCommand]
    private void Play()
    {
        if (string.IsNullOrEmpty(VideoPath)) return;
        IsPlaying = true;
        PlayRequested?.Invoke();
        _logger.LogDebug("Play command executed.");
    }

    [RelayCommand]
    private void Pause()
    {
        IsPlaying = false;
        PauseRequested?.Invoke();
        _logger.LogDebug("Pause command executed.");
    }

    [RelayCommand]
    private void Stop()
    {
        IsPlaying = false;
        Position = 0;
        CurrentTimeText = "00:00";
        StopRequested?.Invoke();
        _logger.LogDebug("Stop command executed.");
    }

    [RelayCommand]
    private void OpenVideo()
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "Video Files (*.mp4;*.mkv;*.avi;*.mov)|*.mp4;*.mkv;*.avi;*.mov|All Files (*.*)|*.*",
            Title = "Select Video to Play"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            VideoPath = openFileDialog.FileName;
            Stop();
            Play();
        }
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        _logger.LogDebug("Mute toggled. IsMuted: {Muted}", IsMuted);
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        FullscreenToggleRequested?.Invoke();
        _logger.LogDebug("Fullscreen toggle requested.");
    }
}
