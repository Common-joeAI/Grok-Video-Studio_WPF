using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrokVideoStudio.Core.Models;
using GrokVideoStudio.Core.Services;
using Microsoft.Extensions.Logging;

namespace GrokVideoStudio.App.ViewModels;

/// <summary>
/// Settings page ViewModel — manages all API keys, OAuth connections, and defaults.
/// 
/// AUTH: Social platforms now support browser-based OAuth2 authentication via
/// ISocialAuthService. Each platform has a "Connect" button that opens the
/// browser, captures the callback, and stores the resulting access token.
/// Connection status is shown as a boolean badge in the UI.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISecureSettingsService _settingsService;
    private readonly ISocialAuthService _socialAuth;
    private readonly IActivityLogService _activityLog;
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

    // ── Social: YouTube ──
    [ObservableProperty] private string _youTubeApiKey = string.Empty;  // path to client_secrets.json
    [ObservableProperty] private bool _isYouTubeConnected;
    [ObservableProperty] private bool _isYouTubeConnecting;

    // ── Social: Facebook OAuth credentials ──
    [ObservableProperty] private string _facebookClientId = string.Empty;
    [ObservableProperty] private string _facebookClientSecret = string.Empty;
    [ObservableProperty] private string _facebookAccessToken = string.Empty;
    [ObservableProperty] private string _facebookPageId = string.Empty;
    [ObservableProperty] private bool _isFacebookConnected;
    [ObservableProperty] private bool _isFacebookConnecting;

    // ── Social: Instagram OAuth credentials ──
    [ObservableProperty] private string _instagramClientId = string.Empty;
    [ObservableProperty] private string _instagramClientSecret = string.Empty;
    [ObservableProperty] private string _instagramUserId = string.Empty;
    [ObservableProperty] private string _instagramAccessToken = string.Empty;
    [ObservableProperty] private bool _isInstagramConnected;
    [ObservableProperty] private bool _isInstagramConnecting;

    // ── Social: TikTok OAuth credentials ──
    [ObservableProperty] private string _tikTokClientKey = string.Empty;
    [ObservableProperty] private string _tikTokClientSecret = string.Empty;
    [ObservableProperty] private string _tikTokAccessToken = string.Empty;
    [ObservableProperty] private string _tikTokOpenId = string.Empty;
    [ObservableProperty] private bool _isTikTokConnected;
    [ObservableProperty] private bool _isTikTokConnecting;

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
    [ObservableProperty] private string _ffmpegPath = "ffmpeg";
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

    public SettingsViewModel(
        ISecureSettingsService settingsService,
        ISocialAuthService socialAuth,
        IActivityLogService activityLog,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _socialAuth = socialAuth;
        _activityLog = activityLog;
        _logger = logger;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var s = _settingsService.LoadSettings();
        var hasKeys = !string.IsNullOrEmpty(s.GrokApiKey) || !string.IsNullOrEmpty(s.OpenAiApiKey) || !string.IsNullOrEmpty(s.SeedanceApiKey);
        _activityLog.Log(hasKeys ? "Settings loaded from disk (API keys present)" : "Settings loaded from disk (no API keys configured)", Microsoft.Extensions.Logging.LogLevel.Information);
        GrokApiKey = s.GrokApiKey;
        GrokChatModel = s.GrokChatModel;
        GrokVideoModel = s.GrokVideoModel;
        OpenAiApiKey = s.OpenAiApiKey;
        OpenAiChatModel = s.OpenAiChatModel;
        OllamaApiBase = s.OllamaApiBase;
        OllamaChatModel = s.OllamaChatModel;
        SeedanceApiKey = s.SeedanceApiKey;

        // YouTube
        YouTubeApiKey = s.YouTubeApiKey;
        IsYouTubeConnected = !string.IsNullOrEmpty(s.YouTubeApiKey);

        // Facebook
        FacebookClientId = s.FacebookClientId;
        FacebookClientSecret = s.FacebookClientSecret;
        FacebookAccessToken = s.FacebookAccessToken;
        FacebookPageId = s.FacebookPageId;
        IsFacebookConnected = !string.IsNullOrEmpty(s.FacebookAccessToken);

        // Instagram
        InstagramClientId = s.InstagramClientId;
        InstagramClientSecret = s.InstagramClientSecret;
        InstagramUserId = s.InstagramUserId;
        InstagramAccessToken = s.InstagramAccessToken;
        IsInstagramConnected = !string.IsNullOrEmpty(s.InstagramAccessToken);

        // TikTok
        TikTokClientKey = s.TikTokClientKey;
        TikTokClientSecret = s.TikTokClientSecret;
        TikTokAccessToken = s.TikTokAccessToken;
        TikTokOpenId = s.TikTokOpenId;
        IsTikTokConnected = !string.IsNullOrEmpty(s.TikTokAccessToken);

        // Defaults
        DefaultDuration = s.DefaultDuration;
        DefaultAspectRatio = s.DefaultAspectRatio;
        DefaultResolution = s.DefaultResolution;
        EnableCrossfade = s.EnableCrossfade;
        InterpolationFps = s.InterpolationFps;
        UpscalePreset = s.UpscalePreset;
        EnableGpuEncode = s.EnableGpuEncode;
        MusicMixPath = s.MusicMixPath;
        FfmpegPath = s.FfmpegPath;
        Theme = s.Theme;
        PollIntervalSeconds = s.PollIntervalSeconds;
        MaxPollAttempts = s.MaxPollAttempts;
    }

    private AppSettings BuildSettings() => new()
    {
        GrokApiKey = GrokApiKey,
        GrokChatModel = GrokChatModel,
        GrokVideoModel = GrokVideoModel,
        OpenAiApiKey = OpenAiApiKey,
        OpenAiChatModel = OpenAiChatModel,
        OllamaApiBase = OllamaApiBase,
        OllamaChatModel = OllamaChatModel,
        SeedanceApiKey = SeedanceApiKey,
        // YouTube
        YouTubeApiKey = YouTubeApiKey,
        // Facebook
        FacebookClientId = FacebookClientId,
        FacebookClientSecret = FacebookClientSecret,
        FacebookAccessToken = FacebookAccessToken,
        FacebookPageId = FacebookPageId,
        // Instagram
        InstagramClientId = InstagramClientId,
        InstagramClientSecret = InstagramClientSecret,
        InstagramUserId = InstagramUserId,
        InstagramAccessToken = InstagramAccessToken,
        // TikTok
        TikTokClientKey = TikTokClientKey,
        TikTokClientSecret = TikTokClientSecret,
        TikTokAccessToken = TikTokAccessToken,
        TikTokOpenId = TikTokOpenId,
        // Defaults
        DefaultDuration = DefaultDuration,
        DefaultAspectRatio = DefaultAspectRatio,
        DefaultResolution = DefaultResolution,
        EnableCrossfade = EnableCrossfade,
        InterpolationFps = InterpolationFps,
        UpscalePreset = UpscalePreset,
        EnableGpuEncode = EnableGpuEncode,
        MusicMixPath = MusicMixPath,
        FfmpegPath = FfmpegPath,
        Theme = Theme,
        PollIntervalSeconds = PollIntervalSeconds,
        MaxPollAttempts = MaxPollAttempts,
    };

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;
        try
        {
            await _settingsService.SaveSettingsAsync(BuildSettings());
            StatusMessage = "✓ Settings saved securely (DPAPI encrypted).";
            _logger.LogInformation("Settings saved to DPAPI-encrypted storage.");
            _activityLog.Log("Settings saved (DPAPI encrypted)", Microsoft.Extensions.Logging.LogLevel.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Save failed: {ex.Message}";
            _logger.LogError(ex, "Settings save failed");
            _activityLog.Log($"Settings save failed: {ex.Message}", Microsoft.Extensions.Logging.LogLevel.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    // ── OAuth Connect Commands ──

    [RelayCommand]
    private async Task ConnectYouTubeAsync()
    {
        if (string.IsNullOrWhiteSpace(YouTubeApiKey))
        {
            StatusMessage = "⚠ Set the path to your Google client_secrets.json first.";
            return;
        }

        IsYouTubeConnecting = true;
        StatusMessage = "Opening browser for YouTube authentication…";
        try
        {
            // Save current settings before auth so the token store path is available
            await _settingsService.SaveSettingsAsync(BuildSettings());

            var result = await _socialAuth.AuthenticateYouTubeAsync(YouTubeApiKey);
            if (result.Success)
            {
                IsYouTubeConnected = true;
                StatusMessage = "✓ YouTube connected successfully!";
            }
            else
            {
                StatusMessage = $"✗ YouTube auth failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ YouTube auth error: {ex.Message}";
            _logger.LogError(ex, "YouTube OAuth failed");
        }
        finally
        {
            IsYouTubeConnecting = false;
        }
    }

    [RelayCommand]
    private async Task ConnectFacebookAsync()
    {
        if (string.IsNullOrWhiteSpace(FacebookClientId) || string.IsNullOrWhiteSpace(FacebookClientSecret))
        {
            StatusMessage = "⚠ Enter Facebook Client ID and Client Secret first.";
            return;
        }

        IsFacebookConnecting = true;
        StatusMessage = "Opening browser for Facebook authentication…";
        try
        {
            var result = await _socialAuth.AuthenticateFacebookAsync(FacebookClientId, FacebookClientSecret);

            if (result.Success)
            {
                FacebookAccessToken = result.AccessToken;
                if (!string.IsNullOrEmpty(result.UserId))
                    FacebookPageId = result.UserId;
                IsFacebookConnected = true;

                // Persist tokens immediately
                await _settingsService.SaveSettingsAsync(BuildSettings());
                StatusMessage = $"✓ Facebook connected{(string.IsNullOrEmpty(result.ErrorMessage) ? "!" : $" — {result.ErrorMessage}")}";
            }
            else
            {
                StatusMessage = $"✗ Facebook auth failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Facebook auth error: {ex.Message}";
            _logger.LogError(ex, "Facebook OAuth failed");
        }
        finally
        {
            IsFacebookConnecting = false;
        }
    }

    [RelayCommand]
    private async Task ConnectInstagramAsync()
    {
        if (string.IsNullOrWhiteSpace(InstagramClientId) || string.IsNullOrWhiteSpace(InstagramClientSecret))
        {
            StatusMessage = "⚠ Enter Instagram Client ID and Client Secret first.";
            return;
        }

        IsInstagramConnecting = true;
        StatusMessage = "Opening browser for Instagram authentication…";
        try
        {
            var result = await _socialAuth.AuthenticateInstagramAsync(InstagramClientId, InstagramClientSecret);

            if (result.Success)
            {
                InstagramAccessToken = result.AccessToken;
                if (!string.IsNullOrEmpty(result.UserId))
                    InstagramUserId = result.UserId;
                IsInstagramConnected = true;

                await _settingsService.SaveSettingsAsync(BuildSettings());
                StatusMessage = "✓ Instagram connected successfully!";
            }
            else
            {
                StatusMessage = $"✗ Instagram auth failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Instagram auth error: {ex.Message}";
            _logger.LogError(ex, "Instagram OAuth failed");
        }
        finally
        {
            IsInstagramConnecting = false;
        }
    }

    [RelayCommand]
    private async Task ConnectTikTokAsync()
    {
        if (string.IsNullOrWhiteSpace(TikTokClientKey) || string.IsNullOrWhiteSpace(TikTokClientSecret))
        {
            StatusMessage = "⚠ Enter TikTok Client Key and Client Secret first.";
            return;
        }

        IsTikTokConnecting = true;
        StatusMessage = "Opening browser for TikTok authentication…";
        try
        {
            var result = await _socialAuth.AuthenticateTikTokAsync(TikTokClientKey, TikTokClientSecret);

            if (result.Success)
            {
                TikTokAccessToken = result.AccessToken;
                if (!string.IsNullOrEmpty(result.UserId))
                    TikTokOpenId = result.UserId;
                IsTikTokConnected = true;

                await _settingsService.SaveSettingsAsync(BuildSettings());
                StatusMessage = "✓ TikTok connected successfully!";
            }
            else
            {
                StatusMessage = $"✗ TikTok auth failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ TikTok auth error: {ex.Message}";
            _logger.LogError(ex, "TikTok OAuth failed");
        }
        finally
        {
            IsTikTokConnecting = false;
        }
    }

    // ── Disconnect Commands ──

    [RelayCommand]
    private async Task DisconnectYouTubeAsync()
    {
        IsYouTubeConnected = false;
        YouTubeApiKey = string.Empty;
        await _settingsService.SaveSettingsAsync(BuildSettings());
        StatusMessage = "YouTube disconnected.";
    }

    [RelayCommand]
    private async Task DisconnectFacebookAsync()
    {
        IsFacebookConnected = false;
        FacebookAccessToken = string.Empty;
        FacebookPageId = string.Empty;
        await _settingsService.SaveSettingsAsync(BuildSettings());
        StatusMessage = "Facebook disconnected.";
    }

    [RelayCommand]
    private async Task DisconnectInstagramAsync()
    {
        IsInstagramConnected = false;
        InstagramAccessToken = string.Empty;
        InstagramUserId = string.Empty;
        await _settingsService.SaveSettingsAsync(BuildSettings());
        StatusMessage = "Instagram disconnected.";
    }

    [RelayCommand]
    private async Task DisconnectTikTokAsync()
    {
        IsTikTokConnected = false;
        TikTokAccessToken = string.Empty;
        TikTokOpenId = string.Empty;
        await _settingsService.SaveSettingsAsync(BuildSettings());
        StatusMessage = "TikTok disconnected.";
    }

    [RelayCommand]
    private void ClearAllKeys()
    {
        _settingsService.DeleteSettings();
        GrokApiKey = OpenAiApiKey = SeedanceApiKey = string.Empty;
        YouTubeApiKey = FacebookAccessToken = InstagramAccessToken = TikTokAccessToken = string.Empty;
        FacebookClientId = FacebookClientSecret = string.Empty;
        InstagramClientId = InstagramClientSecret = string.Empty;
        TikTokClientKey = TikTokClientSecret = string.Empty;
        IsYouTubeConnected = IsFacebookConnected = IsInstagramConnected = IsTikTokConnected = false;
        StatusMessage = "All keys cleared and settings file deleted.";
    }

    [RelayCommand]
    private async Task TestFfmpegAsync()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = string.IsNullOrEmpty(FfmpegPath) ? "ffmpeg" : FfmpegPath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
            {
                StatusMessage = "✗ Could not start FFmpeg.";
                return;
            }
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "FFmpeg found.";
            StatusMessage = $"✓ {firstLine}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ FFmpeg not found: {ex.Message}";
            _logger.LogWarning(ex, "FFmpeg test failed");
        }
    }

    // ── Auto-save on key field changes ─────────────────────────────────────────
    // CommunityToolkit.Mvvm calls these partials whenever the backing property changes.
    // We debounce slightly (300ms) so rapid keystrokes don't hammer disk.

    private System.Threading.Timer? _autoSaveTimer;

    private void ScheduleAutoSave()
    {
        // Cancel any pending save and restart the 600ms timer
        _autoSaveTimer?.Change(600, System.Threading.Timeout.Infinite);
        _autoSaveTimer ??= new System.Threading.Timer(
            _ => System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () => await SaveAsync()),
            null, 600, System.Threading.Timeout.Infinite);
        _autoSaveTimer.Change(600, System.Threading.Timeout.Infinite);
    }

    partial void OnGrokApiKeyChanged(string value)         { _activityLog.Log("xAI Grok API key updated", Microsoft.Extensions.Logging.LogLevel.Information); ScheduleAutoSave(); }
    partial void OnOpenAiApiKeyChanged(string value)       { _activityLog.Log("OpenAI API key updated", Microsoft.Extensions.Logging.LogLevel.Information); ScheduleAutoSave(); }
    partial void OnSeedanceApiKeyChanged(string value)     { _activityLog.Log("Seedance API key updated", Microsoft.Extensions.Logging.LogLevel.Information); ScheduleAutoSave(); }
    partial void OnGrokChatModelChanged(string value)      => ScheduleAutoSave();
    partial void OnGrokVideoModelChanged(string value)     => ScheduleAutoSave();
    partial void OnOllamaApiBaseChanged(string value)      => ScheduleAutoSave();
    partial void OnOllamaChatModelChanged(string value)    => ScheduleAutoSave();
    partial void OnFfmpegPathChanged(string value)         => ScheduleAutoSave();
    partial void OnDefaultAspectRatioChanged(string value) => ScheduleAutoSave();
    partial void OnDefaultResolutionChanged(string value)  => ScheduleAutoSave();
    partial void OnDefaultDurationChanged(int value)       => ScheduleAutoSave();
    partial void OnThemeChanged(string value)              => ScheduleAutoSave();

}
