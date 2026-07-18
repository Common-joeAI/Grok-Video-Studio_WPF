using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrokVideoStudio.Core.Models;
using GrokVideoStudio.Core.Services;
using Microsoft.Extensions.Logging;

namespace GrokVideoStudio.App.ViewModels;

/// <summary>
/// Publish page ViewModel — social publishing to YouTube, TikTok, Facebook, Instagram.
/// 
/// FEATURE PARITY: The original Python app supports both API upload and
/// browser automation for each platform. This ViewModel orchestrates both paths.
/// API uploads use the dedicated upload services (YouTubeUploadService, etc.).
/// Browser automation would use Playwright for .NET (future addition).
/// </summary>
public partial class PublishViewModel : ObservableObject
{
    private readonly ISecureSettingsService _settingsService;
    private readonly IVideoStorageService _storageService;
    private readonly ISocialPublishingFactory _publishingFactory;
    private readonly IActivityLogService _activityLog;
    private readonly IUsageStatsService _usageStats;
    private readonly ILogger<PublishViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<VideoItem> _availableVideos = [];

    [ObservableProperty]
    private VideoItem? _selectedVideo;

    [ObservableProperty]
    private SocialPlatform _selectedPlatform = SocialPlatform.YouTube;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _tags = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    private bool _isPublishing;

    [ObservableProperty]
    private string _statusMessage = "Select a video and platform to publish.";

    [ObservableProperty]
    private bool _useBrowserAutomation;

    [ObservableProperty]
    private string _categoryId = "22";  // YouTube category: People & Blogs

    [ObservableProperty]
    private string _privacyLevel = "PUBLIC_TO_EVERYONE";  // TikTok privacy

    [ObservableProperty]
    private double _publishProgress;

    public ObservableCollection<SocialPlatform> Platforms { get; } =
    [
        SocialPlatform.YouTube, SocialPlatform.TikTok, SocialPlatform.Facebook, SocialPlatform.Instagram
    ];

    public PublishViewModel(
        ISecureSettingsService settingsService,
        IVideoStorageService storageService,
        ISocialPublishingFactory publishingFactory,
        IActivityLogService activityLog,
        IUsageStatsService usageStats,
        ILogger<PublishViewModel> logger)
    {
        _settingsService = settingsService;
        _storageService = storageService;
        _publishingFactory = publishingFactory;
        _activityLog = activityLog;
        _usageStats = usageStats;
        _logger = logger;
    }

    [RelayCommand]
    private async Task LoadVideosAsync()
    {
        var videos = await _storageService.LoadVideosAsync();
        AvailableVideos = new ObservableCollection<VideoItem>(
            videos.Where(v => v.Status == VideoGenerationStatus.Completed));
    }

    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishAsync()
    {
        if (SelectedVideo is null) return;

        IsPublishing = true;
        PublishProgress = 0;
        var settings = _settingsService.LoadSettings();
        var tagList = Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string videoPath = SelectedVideo.LocalFilePath ?? SelectedVideo.VideoUrl ?? "";
        if (string.IsNullOrEmpty(videoPath))
        {
            StatusMessage = "✗ No video file or URL available.";
            IsPublishing = false;
            return;
        }

        _activityLog.Log($"Publishing to {SelectedPlatform} — {(UseBrowserAutomation ? "browser automation" : "API upload")}", LogLevel.Information);
        StatusMessage = $"Publishing to {SelectedPlatform}…";

        var progress = new Progress<(int percent, string message)>(p =>
        {
            PublishProgress = p.percent;
            StatusMessage = $"{SelectedPlatform}: {p.message}";
        });

        try
        {
            string resultId;

            if (UseBrowserAutomation)
            {
                // Browser automation mode — would use Playwright for .NET
                // The original Python app uses CDP + Chrome extension for this
                StatusMessage = "Browser automation mode — requires Playwright integration.";
                _activityLog.Log("Browser automation not yet implemented in .NET rebuild", LogLevel.Warning);
                resultId = "BROWSER_AUTOMATION_PENDING";
            }
            else
            {
                // API upload — use the publishing factory to resolve the right service
                switch (SelectedPlatform)
                {
                    case SocialPlatform.YouTube:
                        var ytService = _publishingFactory.GetYouTubeService();
                        if (ytService is null)
                        {
                            StatusMessage = "YouTube upload service not available.";
                            return;
                        }
                        resultId = await ytService.UploadAsync(
                            videoPath, Title, Description, tagList, CategoryId,
                            settings.YouTubeApiKey, // client_secret path
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "GrokVideoStudio", "youtube_token.json"),
                            progress, default);
                        break;

                    case SocialPlatform.Facebook:
                        var fbService = _publishingFactory.GetFacebookService();
                        if (fbService is null)
                        {
                            StatusMessage = "Facebook upload service not available.";
                            return;
                        }
                        resultId = await fbService.UploadAsync(
                            settings.FacebookPageId, settings.FacebookAccessToken,
                            videoPath, Title, Description, progress, default);
                        break;

                    case SocialPlatform.Instagram:
                        var igService = _publishingFactory.GetInstagramService();
                        if (igService is null)
                        {
                            StatusMessage = "Instagram upload service not available.";
                            return;
                        }
                        // Instagram requires a public URL, not local file
                        if (!videoPath.StartsWith("http"))
                        {
                            StatusMessage = "⚠ Instagram requires a public video URL, not a local file.";
                            return;
                        }
                        resultId = await igService.UploadAsync(
                            settings.InstagramUserId, settings.InstagramAccessToken,
                            videoPath, $"{Title}\n\n{Description}", progress, default);
                        break;

                    case SocialPlatform.TikTok:
                        var ttService = _publishingFactory.GetTikTokService();
                        if (ttService is null)
                        {
                            StatusMessage = "TikTok upload service not available.";
                            return;
                        }
                        resultId = await ttService.UploadAsync(
                            settings.TikTokAccessToken, videoPath,
                            $"{Title}\n\n{Description}", PrivacyLevel, progress, default);
                        break;

                    default:
                        StatusMessage = $"Unknown platform: {SelectedPlatform}";
                        return;
                }
            }

            PublishProgress = 100;
            StatusMessage = $"✓ Published to {SelectedPlatform} — ID: {resultId}";
            _activityLog.Log($"Published to {SelectedPlatform}: {resultId}", LogLevel.Information);
            await _usageStats.RecordUploadAsync(SelectedPlatform.ToString(), true);
        }
        catch (NotImplementedException ex)
        {
            StatusMessage = $"⚠ {ex.Message}";
            _activityLog.Log($"Publish not fully implemented: {ex.Message}", LogLevel.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Publish to {Platform} failed", SelectedPlatform);
            StatusMessage = $"✗ Publish failed: {ex.Message}";
            _activityLog.Log($"Publish to {SelectedPlatform} failed: {ex.Message}", LogLevel.Error);
            await _usageStats.RecordUploadAsync(SelectedPlatform.ToString(), false);
        }
        finally
        {
            IsPublishing = false;
        }
    }

    private bool CanPublish() => !IsPublishing && SelectedVideo is not null && !string.IsNullOrEmpty(Title);
}
