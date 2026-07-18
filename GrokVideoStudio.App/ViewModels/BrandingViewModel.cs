using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrokVideoStudio.Core.Models;
using GrokVideoStudio.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GrokVideoStudio.App.ViewModels;

/// <summary>
/// Branding ViewModel — manages brand identity applied to all generated videos.
/// Includes colors, logo, watermark, color grading, and audio identity.
/// </summary>
public partial class BrandingViewModel : ObservableObject
{
    private readonly IBrandingService _brandingService;
    private readonly IActivityLogService _activityLog;
    private readonly ILogger<BrandingViewModel> _logger;

    // ── Identity ──
    [ObservableProperty] private string _brandName = string.Empty;
    [ObservableProperty] private bool _applyToAllVideos = true;

    // ── Colors ──
    [ObservableProperty] private string _primaryColor = "#FFFFFF";
    [ObservableProperty] private string _secondaryColor = "#FFFFFF";
    [ObservableProperty] private string _backgroundColor = "#000000";

    // ── Logo ──
    [ObservableProperty] private string? _logoPath;
    [ObservableProperty] private LogoPosition _logoPosition = LogoPosition.BottomRight;
    [ObservableProperty] private double _logoOpacity = 0.8;
    [ObservableProperty] private double _logoScale = 0.15;
    [ObservableProperty] private int _logoPadding = 20;

    // ── Watermark ──
    [ObservableProperty] private string _watermarkText = string.Empty;
    [ObservableProperty] private LogoPosition _watermarkPosition = LogoPosition.BottomLeft;
    [ObservableProperty] private double _watermarkFontSize = 0.03;
    [ObservableProperty] private double _watermarkOpacity = 0.5;

    // ── Color Grading ──
    [ObservableProperty] private bool _enableColorGrading;
    [ObservableProperty] private ColorGradePreset _colorGradePreset = ColorGradePreset.None;

    // ── Audio Identity ──
    [ObservableProperty] private string? _introAudioPath;
    [ObservableProperty] private string? _outroAudioPath;
    [ObservableProperty] private string? _backgroundMusicPath;
    [ObservableProperty] private double _musicVolume = 0.3;

    // ── Video Bumpers ──
    [ObservableProperty] private string? _introVideoPath;
    [ObservableProperty] private string? _outroVideoPath;

    // ── State ──
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _statusMessage = "Ready.";

    // ── Collections ──
    public ObservableCollection<LogoPosition> Positions { get; } =
    [
        LogoPosition.TopLeft, LogoPosition.TopRight,
        LogoPosition.BottomLeft, LogoPosition.BottomRight,
        LogoPosition.Center
    ];
    public ObservableCollection<ColorGradePreset> ColorGrades { get; } =
    [
        ColorGradePreset.None, ColorGradePreset.Warm, ColorGradePreset.Cool,
        ColorGradePreset.Cinematic, ColorGradePreset.Vibrant,
        ColorGradePreset.Vintage, ColorGradePreset.Noir
    ];

    public BrandingViewModel(
        IBrandingService brandingService,
        IActivityLogService activityLog,
        ILogger<BrandingViewModel> logger)
    {
        _brandingService = brandingService;
        _activityLog = activityLog;
        _logger = logger;
        LoadFromBrand();
    }

    private void LoadFromBrand()
    {
        var b = _brandingService.LoadBrand();
        BrandName = b.BrandName;
        ApplyToAllVideos = b.ApplyToAllVideos;
        PrimaryColor = b.PrimaryColor;
        SecondaryColor = b.SecondaryColor;
        BackgroundColor = b.BackgroundColor;
        LogoPath = b.LogoPath;
        LogoPosition = b.LogoPosition;
        LogoOpacity = b.LogoOpacity;
        LogoScale = b.LogoScale;
        LogoPadding = b.LogoPadding;
        WatermarkText = b.WatermarkText;
        WatermarkPosition = b.WatermarkPosition;
        WatermarkFontSize = b.WatermarkFontSize;
        WatermarkOpacity = b.WatermarkOpacity;
        EnableColorGrading = b.EnableColorGrading;
        ColorGradePreset = b.ColorGradePreset;
        IntroAudioPath = b.IntroAudioPath;
        OutroAudioPath = b.OutroAudioPath;
        BackgroundMusicPath = b.BackgroundMusicPath;
        MusicVolume = b.MusicVolume;
        IntroVideoPath = b.IntroVideoPath;
        OutroVideoPath = b.OutroVideoPath;
    }

    // ── Save ──

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;
        try
        {
            var brand = new BrandSettings
            {
                BrandName = BrandName,
                ApplyToAllVideos = ApplyToAllVideos,
                PrimaryColor = PrimaryColor,
                SecondaryColor = SecondaryColor,
                BackgroundColor = BackgroundColor,
                LogoPath = LogoPath,
                LogoPosition = LogoPosition,
                LogoOpacity = LogoOpacity,
                LogoScale = LogoScale,
                LogoPadding = LogoPadding,
                WatermarkText = WatermarkText,
                WatermarkPosition = WatermarkPosition,
                WatermarkFontSize = WatermarkFontSize,
                WatermarkOpacity = WatermarkOpacity,
                EnableColorGrading = EnableColorGrading,
                ColorGradePreset = ColorGradePreset,
                IntroAudioPath = IntroAudioPath,
                OutroAudioPath = OutroAudioPath,
                BackgroundMusicPath = BackgroundMusicPath,
                MusicVolume = MusicVolume,
                IntroVideoPath = IntroVideoPath,
                OutroVideoPath = OutroVideoPath
            };

            await _brandingService.SaveBrandAsync(brand);
            StatusMessage = "✓ Brand saved. Will be applied to all generated videos.";
            _activityLog.Log($"Brand settings saved: {BrandName}", LogLevel.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Save failed: {ex.Message}";
            _logger.LogError(ex, "Brand save failed");
        }
        finally
        {
            IsSaving = false;
        }
    }

    // ── Browse commands ──

    [RelayCommand]
    private void BrowseLogo()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.svg;*.webp|All Files|*.*",
            Title = "Select Logo/Image"
        };
        if (dlg.ShowDialog() == true)
            LogoPath = dlg.FileName;
    }

    [RelayCommand]
    private void BrowseIntroAudio()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav;*.m4a;*.aac|All Files|*.*",
            Title = "Select Intro Audio"
        };
        if (dlg.ShowDialog() == true)
            IntroAudioPath = dlg.FileName;
    }

    [RelayCommand]
    private void BrowseOutroAudio()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav;*.m4a;*.aac|All Files|*.*",
            Title = "Select Outro Audio"
        };
        if (dlg.ShowDialog() == true)
            OutroAudioPath = dlg.FileName;
    }

    [RelayCommand]
    private void BrowseMusic()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav;*.m4a;*.aac|All Files|*.*",
            Title = "Select Background Music"
        };
        if (dlg.ShowDialog() == true)
            BackgroundMusicPath = dlg.FileName;
    }

    [RelayCommand]
    private void BrowseIntroVideo()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Video Files|*.mp4;*.mov;*.mkv;*.avi|All Files|*.*",
            Title = "Select Intro Video Bumper"
        };
        if (dlg.ShowDialog() == true)
            IntroVideoPath = dlg.FileName;
    }

    [RelayCommand]
    private void BrowseOutroVideo()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Video Files|*.mp4;*.mov;*.mkv;*.avi|All Files|*.*",
            Title = "Select Outro Video Bumper"
        };
        if (dlg.ShowDialog() == true)
            OutroVideoPath = dlg.FileName;
    }

    // ── Clear ──

    [RelayCommand]
    private void ClearLogo()
    {
        LogoPath = null;
    }

    [RelayCommand]
    private void ClearBranding()
    {
        LogoPath = null;
        WatermarkText = string.Empty;
        EnableColorGrading = false;
        ColorGradePreset = ColorGradePreset.None;
        IntroAudioPath = null;
        OutroAudioPath = null;
        BackgroundMusicPath = null;
        IntroVideoPath = null;
        OutroVideoPath = null;
        StatusMessage = "All brand assets cleared. Click Save to apply.";
    }
}
