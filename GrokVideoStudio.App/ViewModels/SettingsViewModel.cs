using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrokVideoStudio.Core.Models;
using GrokVideoStudio.Core.Services;
using Microsoft.Extensions.Logging;

namespace GrokVideoStudio.App.ViewModels;

/// <summary>
/// Settings page ViewModel — manages all API keys and defaults.
/// 
/// MODERNIZATION: The original Python app stored everything in plaintext JSON.
/// This .NET version encrypts all keys via DPAPI before writing to disk.
/// Updated to include all providers and social platforms from the actual repo.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISecureSettingsService _settingsService;
    private readonly ILogger<SettingsViewModel> _logger;

    // ── xAI Grok ──
    [ObservableProperty] private string _grokApiKey = string.Empty;
    [ObservableProperty] private string _grokChatModel = "grok-3-mini";
    [ObservableProperty] private string _grokVideoModel = "grok-video-latest";

    // ── OpenAI ──
    [ObservableProperty] private string _openAiApiKey = string.Empty;
    [ObservableProperty] private string _openAiChatModel = "gpt-5.1-codex";

    // ── Ollama ──
    [ObservableProperty] private string _ollamaApiBase = "http://127.0.0.1:11434/v1";
    [ObservableProperty] private string _ollamaChatModel = "llama3.1:8b";

    // ── Seedance ──
    [ObservableProperty] private string _seedanceApiKey = string.Empty;

    // ── Social ──
    [ObservableProperty] private string _youTubeApiKey = string.Empty;
    [ObservableProperty] private string _facebookAccessToken = string.Empty;
    [ObservableProperty] private string _facebookPageId = string.Empty;
    [ObservableProperty] private string _instagramUserId = string.Empty;
    [ObservableProperty] private string _instagramAccessToken = string.Empty;
    [ObservableProperty] private string _tikTokAccessToken = string.Empty;

    // ── Generation defaults ──
    [ObservableProperty] private int _defaultDuration = 8;
    [ObservableProperty] private string _defaultAspectRatio = "16:9";
    [ObservableProperty] private string _defaultResolution = "720p";

    // ── Stitch ──
    [ObservableProperty] private bool _enableCrossfade = true;
    [ObservableProperty] private int _interpolationFps;
    [ObservableProperty] private string _upscalePreset = "none";
    [ObservableProperty] private bool _enableGpuEncode;
    [ObservableProperty] private string _musicMixPath = string.Empty;

    // ── Misc ──
    [ObservableProperty] private string _theme = "Dark";
    [ObservableProperty] private int _pollIntervalSeconds = 5;
    [ObservableProperty] private int _maxPollAttempts = 120;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isSaving;

    public ObservableCollection<string> AspectRatios { get; } = ["16:9", "9:16", "1:1", "4:3", "3:4", "3:2", "2:3"];
    public ObservableCollection<string> Resolutions { get; } = ["480p", "720p", "1080p"];
    public ObservableCollection<string> Themes { get; } = ["Dark", "Light"];
    public ObservableCollection<int> FpsOptions { get; } = [0, 48, 60];
    public ObservableCollection<string> UpscalePresets { get; } = ["none", "2x", "1080p", "1440p", "4K"];

    public SettingsViewModel(ISecureSettingsService settingsService, ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var s = _settingsService.LoadSettings();
        GrokApiKey = s.GrokApiKey;
        GrokChatModel = s.GrokChatModel;
        GrokVideoModel = s.GrokVideoModel;
        OpenAiApiKey = s.OpenAiApiKey;
        OpenAiChatModel = s.OpenAiChatModel;
        OllamaApiBase = s.OllamaApiBase;
        OllamaChatModel = s.OllamaChatModel;
        SeedanceApiKey = s.SeedanceApiKey;
        YouTubeApiKey = s.YouTubeApiKey;
        FacebookAccessToken = s.FacebookAccessToken;
        FacebookPageId = s.FacebookPageId;
        InstagramUserId = s.InstagramUserId;
        InstagramAccessToken = s.InstagramAccessToken;
        TikTokAccessToken = s.TikTokAccessToken;
        DefaultDuration = s.DefaultDuration;
        DefaultAspectRatio = s.DefaultAspectRatio;
        DefaultResolution = s.DefaultResolution;
        EnableCrossfade = s.EnableCrossfade;
        InterpolationFps = s.InterpolationFps;
        UpscalePreset = s.UpscalePreset;
        EnableGpuEncode = s.EnableGpuEncode;
        MusicMixPath = s.MusicMixPath;
        Theme = s.Theme;
        PollIntervalSeconds = s.PollIntervalSeconds;
        MaxPollAttempts = s.MaxPollAttempts;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;
        try
        {
            var settings = new AppSettings
            {
                GrokApiKey = GrokApiKey,
                GrokChatModel = GrokChatModel,
                GrokVideoModel = GrokVideoModel,
                OpenAiApiKey = OpenAiApiKey,
                OpenAiChatModel = OpenAiChatModel,
                OllamaApiBase = OllamaApiBase,
                OllamaChatModel = OllamaChatModel,
                SeedanceApiKey = SeedanceApiKey,
                YouTubeApiKey = YouTubeApiKey,
                FacebookAccessToken = FacebookAccessToken,
                FacebookPageId = FacebookPageId,
                InstagramUserId = InstagramUserId,
                InstagramAccessToken = InstagramAccessToken,
                TikTokAccessToken = TikTokAccessToken,
                DefaultDuration = DefaultDuration,
                DefaultAspectRatio = DefaultAspectRatio,
                DefaultResolution = DefaultResolution,
                EnableCrossfade = EnableCrossfade,
                InterpolationFps = InterpolationFps,
                UpscalePreset = UpscalePreset,
                EnableGpuEncode = EnableGpuEncode,
                MusicMixPath = MusicMixPath,
                Theme = Theme,
                PollIntervalSeconds = PollIntervalSeconds,
                MaxPollAttempts = MaxPollAttempts,
            };

            await _settingsService.SaveSettingsAsync(settings);
            StatusMessage = "✓ Settings saved securely (DPAPI encrypted).";
            _logger.LogInformation("Settings saved to DPAPI-encrypted storage.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Save failed: {ex.Message}";
            _logger.LogError(ex, "Settings save failed");
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void ClearAllKeys()
    {
        _settingsService.DeleteSettings();
        GrokApiKey = OpenAiApiKey = SeedanceApiKey = string.Empty;
        YouTubeApiKey = FacebookAccessToken = InstagramAccessToken = TikTokAccessToken = string.Empty;
        StatusMessage = "All keys cleared and settings file deleted.";
    }
}
