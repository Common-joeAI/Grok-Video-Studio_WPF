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
    [ObservableProperty] private string _grokVideoModel = "grok-imagine-video";

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
    [ObservableProperty] private string _localServerUrl = "http://localhost:7860";
    [ObservableProperty] private string _vastApiKey = string.Empty;
    [ObservableProperty] private string _vastGpuTier = "4090";  // 4090, A100, H100, Auto
    public ObservableCollection<string> VastGpuTiers { get; } = ["4090", "A100", "H100", "Auto"];
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
        _isLoading = true;
        try
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
            LocalServerUrl = string.IsNullOrEmpty(s.LocalServerUrl) ? "http://localhost:7860" : s.LocalServerUrl;
            VastApiKey = s.VastApiKey ?? string.Empty;
            VastGpuTier = string.IsNullOrEmpty(s.VastGpuTier) ? "4090" : s.VastGpuTier;
        Theme = s.Theme;
        PollIntervalSeconds = s.PollIntervalSeconds;
        MaxPollAttempts = s.MaxPollAttempts;
        }
        finally
        {
            _isLoading = false;
        }
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
            LocalServerUrl = LocalServerUrl,
            VastApiKey = VastApiKey,
            VastGpuTier = VastGpuTier,
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
        var exe = string.IsNullOrEmpty(FfmpegPath) ? "ffmpeg" : FfmpegPath;

        // If ffmpeg not found on PATH, check common winget/install locations
        if (!System.IO.File.Exists(exe) && !exe.Contains('\\') && !exe.Contains('/'))
        {
            var searchPaths = new[]
            {
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages"),
                @"C:\ProgramData\chocolatey\bin",
                @"C:\ffmpeg\bin",
            };

            foreach (var searchDir in searchPaths)
            {
                if (!System.IO.Directory.Exists(searchDir)) continue;
                var found = System.IO.Directory.GetFiles(searchDir, "ffmpeg.exe", System.IO.SearchOption.AllDirectories).FirstOrDefault();
                if (found != null)
                {
                    exe = found;
                    FfmpegPath = found;  // persist for future use
                    _activityLog.Log($"FFmpeg auto-detected at: {found}", LogLevel.Information);
                    break;
                }
            }
        }

        _activityLog.Log($"Testing FFmpeg at: {exe}", LogLevel.Information);

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
            {
                _activityLog.Log("FFmpeg test failed: could not start process.", LogLevel.Error);
                StatusMessage = "✗ Could not start FFmpeg.";
                return;
            }
            var output = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var combined = string.IsNullOrWhiteSpace(output) ? stderr : output;
            var firstLine = combined.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "FFmpeg responded.";

            if (proc.ExitCode == 0)
            {
                _activityLog.Log($"FFmpeg OK: {firstLine}", LogLevel.Information);
                StatusMessage = $"✓ {firstLine}";
            }
            else
            {
                _activityLog.Log($"FFmpeg exited with code {proc.ExitCode}: {firstLine}", LogLevel.Warning);
                StatusMessage = $"✗ FFmpeg error (exit {proc.ExitCode})";
            }
        }
        catch (Exception ex)
        {
            _activityLog.Log($"FFmpeg not found at '{exe}': {ex.Message}", LogLevel.Error);
            StatusMessage = $"✗ FFmpeg not found: {ex.Message}";
            _logger.LogWarning(ex, "FFmpeg test failed");
        }
    }


    // ── API Key Test Results ────────────────────────────────────────────────────
    [ObservableProperty] private string _grokTestResult = string.Empty;
    [ObservableProperty] private string _openAiTestResult = string.Empty;
    [ObservableProperty] private string _seedanceTestResult = string.Empty;
    [ObservableProperty] private bool _isTestingGrok;
    [ObservableProperty] private bool _isTestingOpenAi;
    [ObservableProperty] private bool _isTestingSeedance;

    [RelayCommand]
    private async Task TestGrokKeyAsync()
    {
        _activityLog.Log($"Test Grok key called — key length: {GrokApiKey?.Length ?? 0}", LogLevel.Information);
        if (string.IsNullOrWhiteSpace(GrokApiKey)) { GrokTestResult = "✗ No key entered"; return; }
        IsTestingGrok = true;
        GrokTestResult = "Testing…";
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GrokApiKey);
            http.Timeout = TimeSpan.FromSeconds(10);
            // Simple models list call — no tokens consumed
            var resp = await http.GetAsync("https://api.x.ai/v1/models");
            if (resp.IsSuccessStatusCode)
            {
                GrokTestResult = "✓ Key valid — xAI API reachable";
                _activityLog.Log("xAI Grok API key test PASSED", LogLevel.Information);
            }
            else
            {
                GrokTestResult = $"✗ {(int)resp.StatusCode} {resp.ReasonPhrase}";
                _activityLog.Log($"xAI Grok API key test FAILED: {(int)resp.StatusCode} {resp.ReasonPhrase}", LogLevel.Warning);
            }
        }
        catch (Exception ex)
        {
            GrokTestResult = $"✗ {ex.Message}";
            _activityLog.Log($"xAI Grok API key test ERROR: {ex.Message}", LogLevel.Error);
        }
        finally { IsTestingGrok = false; }
    }

    [RelayCommand]
    private async Task TestOpenAiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(OpenAiApiKey)) { OpenAiTestResult = "✗ No key entered"; return; }
        IsTestingOpenAi = true;
        OpenAiTestResult = "Testing…";
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", OpenAiApiKey);
            http.Timeout = TimeSpan.FromSeconds(10);
            var resp = await http.GetAsync("https://api.openai.com/v1/models");
            if (resp.IsSuccessStatusCode)
            {
                OpenAiTestResult = "✓ Key valid — OpenAI API reachable";
                _activityLog.Log("OpenAI API key test PASSED", LogLevel.Information);
            }
            else
            {
                OpenAiTestResult = $"✗ {(int)resp.StatusCode} {resp.ReasonPhrase}";
                _activityLog.Log($"OpenAI API key test FAILED: {(int)resp.StatusCode} {resp.ReasonPhrase}", LogLevel.Warning);
            }
        }
        catch (Exception ex)
        {
            OpenAiTestResult = $"✗ {ex.Message}";
            _activityLog.Log($"OpenAI API key test ERROR: {ex.Message}", LogLevel.Error);
        }
        finally { IsTestingOpenAi = false; }
    }

    [RelayCommand]
    private async Task TestSeedanceKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(SeedanceApiKey)) { SeedanceTestResult = "✗ No key entered"; return; }
        IsTestingSeedance = true;
        SeedanceTestResult = "Testing…";
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SeedanceApiKey);
            http.Timeout = TimeSpan.FromSeconds(10);
            // Seedance uses ByteDance API
            var resp = await http.GetAsync("https://ark.cn-beijing.volces.com/api/v3/models");
            if (resp.IsSuccessStatusCode)
            {
                SeedanceTestResult = "✓ Key valid — Seedance API reachable";
                _activityLog.Log("Seedance API key test PASSED", LogLevel.Information);
            }
            else
            {
                SeedanceTestResult = $"✗ {(int)resp.StatusCode} {resp.ReasonPhrase}";
                _activityLog.Log($"Seedance API key test FAILED: {(int)resp.StatusCode} {resp.ReasonPhrase}", LogLevel.Warning);
            }
        }
        catch (Exception ex)
        {
            SeedanceTestResult = $"✗ {ex.Message}";
            _activityLog.Log($"Seedance API key test ERROR: {ex.Message}", LogLevel.Error);
        }
        finally { IsTestingSeedance = false; }
    }

    /// <summary>
    /// Searches up to 8 directory levels from the exe to find a script
    /// in the local_server folder — works from both repo dev and published layouts.
    /// </summary>
    private static string? FindScript(string scriptFileName)
    {
        var exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? "";
        var dir = new System.IO.DirectoryInfo(exeDir);

        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "local_server", scriptFileName);
            if (System.IO.File.Exists(candidate))
                return candidate;

            // Also check without the local_server subfolder (published layout)
            var flat = System.IO.Path.Combine(dir.FullName, scriptFileName);
            if (System.IO.File.Exists(flat))
                return flat;

            dir = dir.Parent;
        }
        return null;
    }

    [RelayCommand]
    private async Task StartLocalServerAsync()
    {
        // Find start_server.ps1 relative to the app or in common repo locations
        var scriptPath = FindScript("start_server.ps1");
        if (scriptPath is null)
        {
            _activityLog.Log("Could not find start_server.ps1. Make sure the local_server folder exists in the repo.", LogLevel.Error);
            return;
        }

        _activityLog.Log($"Starting local GPU server: {scriptPath}", LogLevel.Information);

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c start \"GrokVideoStudio\" cmd /k powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"",
                UseShellExecute = true,
                CreateNoWindow = false,
                WorkingDirectory = System.IO.Path.GetDirectoryName(scriptPath)
            };
            System.Diagnostics.Process.Start(psi);
            _activityLog.Log("Local GPU server launching in new window...", LogLevel.Information);
        }
        catch (Exception ex)
        {
            _activityLog.Log($"Failed to start local server: {ex.Message}", LogLevel.Error);
        }
    }

    [RelayCommand]
    private async Task TestLocalServerAsync()
    {
        _activityLog.Log($"Testing local GPU server at {LocalServerUrl}…", LogLevel.Information);

        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromSeconds(5);

            var url = LocalServerUrl.TrimEnd('/') + "/health";
            var response = await http.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _activityLog.Log($"Local GPU server ONLINE — {body}", LogLevel.Information);
            }
            else
            {
                _activityLog.Log($"Local GPU server responded with {(int)response.StatusCode} {response.ReasonPhrase}", LogLevel.Warning);
            }
        }
        catch (TaskCanceledException)
        {
            _activityLog.Log("Local GPU server timeout — is the server running? Start it with start_server.bat", LogLevel.Error);
        }
        catch (Exception ex)
        {
            _activityLog.Log($"Local GPU server connection failed: {ex.Message}", LogLevel.Error);
        }
    }

    // ── Auto-save on key field changes ─────────────────────────────────────────
    // CommunityToolkit.Mvvm calls these partials whenever the backing property changes.
    // We debounce slightly (300ms) so rapid keystrokes don't hammer disk.

    private System.Threading.Timer? _autoSaveTimer;
    private bool _isLoading;  // suppresses auto-save during LoadFromSettings

    private void ScheduleAutoSave()
    {
        if (_isLoading) return;  // don't save while loading initial values
        // Debounce: restart the 800ms timer on every keystroke, only save once typing stops
        if (_autoSaveTimer is null)
        {
            _autoSaveTimer = new System.Threading.Timer(
                _ => System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () => await SaveAsync()),
                null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        }
        _autoSaveTimer.Change(800, System.Threading.Timeout.Infinite);
    }

    [RelayCommand]
    private async Task StartVastServerAsync()
    {
        var scriptPath = FindScript("vast_provision.ps1");
        if (scriptPath is null)
        {
            _activityLog.Log("Could not find vast_provision.ps1. Make sure the local_server folder exists in the repo.", LogLevel.Error);
            return;
        }

        _activityLog.Log($"Starting Vast.ai cloud GPU provisioning (tier: {VastGpuTier})...", LogLevel.Information);

        try
        {
            // Pass tier and API key as arguments
            var psArgs = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\" -Tier " + VastGpuTier;
            if (!string.IsNullOrEmpty(VastApiKey))
                psArgs += " -VastApiKey \"" + VastApiKey + "\"";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c start \"GrokVideoStudio\" cmd /k powershell.exe " + psArgs,
                UseShellExecute = true,
                CreateNoWindow = false,
                WorkingDirectory = System.IO.Path.GetDirectoryName(scriptPath)
            };
            System.Diagnostics.Process.Start(psi);
            _activityLog.Log("Vast.ai provisioning window launching...", LogLevel.Information);
        }
        catch (Exception ex)
        {
            _activityLog.Log($"Failed to start Vast.ai provisioning: {ex.Message}", LogLevel.Error);
        }
    }

    [RelayCommand]
    private async Task StopVastServerAsync()
    {
        var scriptPath = FindScript("vast_provision.ps1");
        if (scriptPath is null)
        {
            _activityLog.Log("Could not find vast_provision.ps1.", LogLevel.Error);
            return;
        }

        _activityLog.Log("Tearing down Vast.ai instance...", LogLevel.Information);

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c start \"GrokVideoStudio\" cmd /k powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\" -Teardown",
                UseShellExecute = true,
                CreateNoWindow = false,
                WorkingDirectory = System.IO.Path.GetDirectoryName(scriptPath)
            };
            System.Diagnostics.Process.Start(psi);
            _activityLog.Log("Vast.ai teardown window launching...", LogLevel.Information);
        }
        catch (Exception ex)
        {
            _activityLog.Log($"Failed to teardown Vast.ai instance: {ex.Message}", LogLevel.Error);
        }
    }


    partial void OnGrokApiKeyChanged(string value)         { _activityLog.Log("xAI Grok API key updated", Microsoft.Extensions.Logging.LogLevel.Information); ScheduleAutoSave(); }
    partial void OnOpenAiApiKeyChanged(string value)       { _activityLog.Log("OpenAI API key updated", Microsoft.Extensions.Logging.LogLevel.Information); ScheduleAutoSave(); }
    partial void OnSeedanceApiKeyChanged(string value)     { _activityLog.Log("Seedance API key updated", Microsoft.Extensions.Logging.LogLevel.Information); ScheduleAutoSave(); }
    partial void OnGrokChatModelChanged(string value)      => ScheduleAutoSave();
    partial void OnGrokVideoModelChanged(string value)     => ScheduleAutoSave();
    partial void OnOllamaApiBaseChanged(string value)      => ScheduleAutoSave();
    partial void OnOllamaChatModelChanged(string value)    => ScheduleAutoSave();
    partial void OnFfmpegPathChanged(string value)         => ScheduleAutoSave();
    partial void OnLocalServerUrlChanged(string value)     => ScheduleAutoSave();
    partial void OnVastApiKeyChanged(string value)           => ScheduleAutoSave();
    partial void OnVastGpuTierChanged(string value)          => ScheduleAutoSave();
    partial void OnDefaultAspectRatioChanged(string value) => ScheduleAutoSave();
    partial void OnDefaultResolutionChanged(string value)  => ScheduleAutoSave();
    partial void OnDefaultDurationChanged(int value)       => ScheduleAutoSave();
    partial void OnThemeChanged(string value)              => ScheduleAutoSave();

}
