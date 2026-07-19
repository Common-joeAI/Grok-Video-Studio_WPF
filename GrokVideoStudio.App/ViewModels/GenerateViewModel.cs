using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrokVideoStudio.Core.Models;
using GrokVideoStudio.Core.Services;
using Microsoft.Extensions.Logging;

namespace GrokVideoStudio.App.ViewModels;

/// <summary>
/// Generate page ViewModel — FULL FEATURE PARITY with the original Python app.
/// 
/// Features ported from Python/PySide6:
/// - Multi-provider video generation (Grok Imagine, Sora 2, Seedance)
/// - Multi-source prompt generation (Grok API, OpenAI API, Ollama)
/// - Batch and variant queue execution (generate N variants at once)
/// - Continue-from-last-frame (uses last generated video's final frame as source image)
/// - Continue-from-local-image (browse for source image)
/// - Image-to-video (sends base64 data URI)
/// - Progress reporting with cancellation
/// - Auto-download completed videos to local disk
/// - Activity log integration
/// - Usage stats recording
/// </summary>
public partial class GenerateViewModel : ObservableObject
{
    private readonly ISecureSettingsService _settingsService;
    private readonly IVideoStorageService _storageService;
    private readonly IVideoDownloadService _downloadService;
    private readonly IActivityLogService _activityLog;
    private readonly IUsageStatsService _usageStats;
    private readonly IBrandingService _brandingService;
    private readonly ILogger<GenerateViewModel> _logger;
    private readonly IVideoGenerationFactory _videoFactory;
    private readonly IPromptGenerationService _grokPrompt;
    private readonly IPromptGenerationService _openAiPrompt;
    private readonly IPromptGenerationService _ollamaPrompt;
    private CancellationTokenSource? _cts;

    // ── Form fields ──
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private string _concept = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private string _prompt = string.Empty;

    [ObservableProperty] private PromptSource _selectedPromptSource = PromptSource.Manual;
    [ObservableProperty] private VideoProvider _selectedProvider = VideoProvider.GrokImagine;
    [ObservableProperty] private string _selectedModel = "grok-imagine-video";
    [ObservableProperty] private int _duration = 8;
    [ObservableProperty] private string _aspectRatio = "16:9";
    [ObservableProperty] private string _resolution = "720p";
    [ObservableProperty] private string? _sourceImagePath;

    // ── Batch / Variant ──
    [ObservableProperty] private int _variantCount = 1;          // How many variants to generate

    // ── Continue from last frame ──
    [ObservableProperty] private bool _continueFromLastFrame;
    [ObservableProperty] private string? _lastFrameImagePath;     // Path to extracted last frame

    // ── State ──
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isGenerating;

    [ObservableProperty] private string _statusMessage = "Ready.";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string? _resultVideoUrl;

    // ── Batch results ──
    [ObservableProperty] private ObservableCollection<VideoItem> _batchResults = [];

    // ── Collections ──
    public ObservableCollection<PromptSource> PromptSources { get; } =
    [
        PromptSource.Manual, PromptSource.GrokApi, PromptSource.OpenAiApi, PromptSource.Ollama
    ];
    public ObservableCollection<VideoProvider> VideoProviders { get; } =
    [
        VideoProvider.GrokImagine, VideoProvider.OpenAiSora, VideoProvider.Seedance
    ];
    public ObservableCollection<string> Models { get; } =
    [
        "grok-imagine-video", "grok-imagine-video-1.5",
        "sora-2", "sora-2-pro", "seedance-2.0"
    ];
    public ObservableCollection<string> AspectRatios { get; } = ["16:9", "9:16", "1:1", "4:3", "3:4", "3:2", "2:3"];
    public ObservableCollection<string> Resolutions { get; } = ["480p", "720p", "1080p"];
    public ObservableCollection<int> Durations { get; } = [1,2,3,4,5,6,7,8,9,10,11,12,13,14,15];
    public ObservableCollection<int> VariantCounts { get; } = [1, 2, 3, 4, 5, 6, 8, 10];

    public GenerateViewModel(
        ISecureSettingsService settingsService,
        IVideoStorageService storageService,
        IVideoDownloadService downloadService,
        IActivityLogService activityLog,
        IUsageStatsService usageStats,
        IVideoGenerationFactory videoFactory,
        GrokPromptService grokPrompt,
        OpenAiPromptService openAiPrompt,
        OllamaPromptService ollamaPrompt,
        IBrandingService brandingService,
        ILogger<GenerateViewModel> logger)
    {
        _settingsService = settingsService;
        _storageService = storageService;
        _downloadService = downloadService;
        _activityLog = activityLog;
        _usageStats = usageStats;
        _videoFactory = videoFactory;
        _grokPrompt = grokPrompt;
        _openAiPrompt = openAiPrompt;
        _ollamaPrompt = ollamaPrompt;
        _brandingService = brandingService;
        _logger = logger;

        LoadDefaults();
    }

    private void LoadDefaults()
    {
        var s = _settingsService.LoadSettings();
        Duration = s.DefaultDuration > 0 ? s.DefaultDuration : 8;
        AspectRatio = string.IsNullOrEmpty(s.DefaultAspectRatio) ? "16:9" : s.DefaultAspectRatio;
        Resolution = string.IsNullOrEmpty(s.DefaultResolution) ? "720p" : s.DefaultResolution;
        SelectedModel = string.IsNullOrEmpty(s.GrokVideoModel) ? "grok-imagine-video" : s.GrokVideoModel;
    }

    // ── Generate Prompt (from concept) ──────────────────────

    [RelayCommand]
    private async Task GeneratePromptAsync()
    {
        var settings = _settingsService.LoadSettings();

        if (SelectedPromptSource == PromptSource.Manual || string.IsNullOrEmpty(Concept))
        {
            StatusMessage = "Enter a concept and pick a prompt source.";
            return;
        }

        try
        {
            StatusMessage = $"Generating prompt via {SelectedPromptSource}…";
            _activityLog.Log($"Prompt generation requested via {SelectedPromptSource}", LogLevel.Information);

            IPromptGenerationService promptSvc = SelectedPromptSource switch
            {
                PromptSource.GrokApi => _grokPrompt,
                PromptSource.OpenAiApi => _openAiPrompt,
                PromptSource.Ollama => _ollamaPrompt,
                _ => _grokPrompt
            };

            string apiKey = SelectedPromptSource switch
            {
                PromptSource.GrokApi => settings.GrokApiKey,
                PromptSource.OpenAiApi => settings.OpenAiApiKey,
                _ => ""
            };

            string generated = await promptSvc.GeneratePromptAsync(Concept, apiKey);
            Prompt = generated;
            StatusMessage = "✓ Prompt generated.";
            _activityLog.Log("Prompt generated successfully", LogLevel.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Prompt generation failed: {ex.Message}";
            _activityLog.Log($"Prompt generation failed: {ex.Message}", LogLevel.Error);
            _logger.LogError(ex, "Prompt generation failed");
        }
    }

    // ── Generate Video(s) ────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        var settings = _settingsService.LoadSettings();
        string apiKey = SelectedProvider switch
        {
            VideoProvider.GrokImagine => settings.GrokApiKey,
            VideoProvider.OpenAiSora => settings.OpenAiApiKey,
            VideoProvider.Seedance => settings.SeedanceApiKey,
            _ => ""
        };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusMessage = $"⚠ No API key set for {SelectedProvider}. Configure in Settings.";
            _activityLog.Log($"No API key for {SelectedProvider}", LogLevel.Warning);
            return;
        }

        _cts = new CancellationTokenSource();
        IsGenerating = true;
        ProgressValue = 0;
        ResultVideoUrl = null;
        BatchResults.Clear();
        StatusMessage = $"Starting generation of {VariantCount} variant(s)…";
        _activityLog.Log($"Video generation started: {SelectedProvider}/{SelectedModel}, {VariantCount} variant(s)", LogLevel.Information);

        // Resolve the correct video service via factory
        var videoService = _videoFactory.GetService(SelectedProvider);

        // Handle continue-from-last-frame
        string? imageForRequest = SourceImagePath;
        if (ContinueFromLastFrame && !string.IsNullOrEmpty(LastFrameImagePath))
            imageForRequest = LastFrameImagePath;

        for (int variant = 1; variant <= VariantCount; variant++)
        {
            if (_cts?.Token.IsCancellationRequested == true) break;

            if (VariantCount > 1)
                StatusMessage = $"Generating variant {variant}/{VariantCount}…";

            var request = new VideoGenerationRequest
            {
                Model = SelectedModel,
                Prompt = Prompt,
                Provider = SelectedProvider,
                Image = imageForRequest is not null
                    ? new ImageSource { Url = ConvertImageToDataUri(imageForRequest) }
                    : null,
                Duration = Duration,
                AspectRatio = AspectRatio,
                Resolution = Resolution
            };

            var videoItem = new VideoItem
            {
                Prompt = Prompt,
                Model = SelectedModel,
                Duration = Duration,
                Resolution = Resolution,
                AspectRatio = AspectRatio,
                SourceImagePath = imageForRequest,
                SourceProvider = SelectedProvider,
                PromptSourceName = SelectedPromptSource,
                Status = VideoGenerationStatus.Polling,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _storageService.SaveVideoAsync(videoItem);

            var progress = new Progress<string>(msg =>
            {
                StatusMessage = msg;
                ProgressValue = Math.Min(ProgressValue + (100.0 / (VariantCount * 20)), 95);
            });

            try
            {
                var result = await videoService.GenerateAsync(
                    request, apiKey,
                    settings.PollIntervalSeconds, settings.MaxPollAttempts,
                    progress, _cts!.Token);

                if (result.IsDone && result.Video is not null)
                {
                    ResultVideoUrl = result.Video.Url;
                    StatusMessage = $"✓ Variant {variant} ready — {result.Video.Url}";
                    _activityLog.Log($"Variant {variant} completed: {result.Video.Url}", LogLevel.Information);

                    // Auto-download to local disk
                    string? localPath = null;
                    try
                    {
                        var downloadDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                            "GrokVideoStudio");
                        localPath = await _downloadService.DownloadAsync(
                            result.Video.Url, downloadDir, null, _cts!.Token);
                        _activityLog.Log($"Downloaded to {localPath}", LogLevel.Information);

                        // Apply branding if enabled
                        try
                        {
                            var brand = _brandingService.LoadBrand();
                            if (brand.ApplyToAllVideos)
                            {
                                _activityLog.Log("Applying brand identity…", LogLevel.Information);
                                var brandedPath = Path.Combine(
                                    Path.GetDirectoryName(localPath)!,
                                    $"branded_{Path.GetFileName(localPath)}");
                                localPath = await _brandingService.ApplyFullBrandingAsync(
                                    localPath, brandedPath, brand, null, _cts!.Token);
                                _activityLog.Log($"Branding applied: {localPath}", LogLevel.Information);
                            }
                        }
                        catch (Exception brandEx)
                        {
                            _activityLog.Log($"Branding failed: {brandEx.Message}", LogLevel.Warning);
                        }
                    }
                    catch (Exception dlEx)
                    {
                        _activityLog.Log($"Download failed: {dlEx.Message}", LogLevel.Warning);
                    }

                    var completedItem = videoItem with
                    {
                        Status = VideoGenerationStatus.Completed,
                        VideoUrl = result.Video.Url,
                        LocalFilePath = localPath,
                        CompletedAt = DateTimeOffset.UtcNow
                    };
                    await _storageService.UpdateVideoAsync(completedItem);
                    BatchResults.Add(completedItem);

                    // Record usage stats
                    await _usageStats.RecordGenerationAsync(SelectedProvider.ToString(), SelectedModel, Duration, true);
                }
                else
                {
                    StatusMessage = $"✗ Variant {variant} failed: {result.Error?.Message ?? result.Status}";
                    _activityLog.Log($"Variant {variant} failed: {result.Error?.Message ?? result.Status}", LogLevel.Error);
                    await _storageService.UpdateVideoAsync(videoItem with
                    {
                        Status = VideoGenerationStatus.Failed,
                        ErrorMessage = result.Error?.Message ?? result.Status
                    });
                    await _usageStats.RecordGenerationAsync(SelectedProvider.ToString(), SelectedModel, Duration, false);
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Cancelled.";
                _activityLog.Log("Generation cancelled by user", LogLevel.Warning);
                await _storageService.UpdateVideoAsync(videoItem with { Status = VideoGenerationStatus.Cancelled });
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generation failed for variant {Variant}", variant);
                StatusMessage = $"✗ Variant {variant} error: {ex.Message}";
                _activityLog.Log($"Variant {variant} error: {ex.Message}", LogLevel.Error);
                await _storageService.UpdateVideoAsync(videoItem with
                {
                    Status = VideoGenerationStatus.Failed,
                    ErrorMessage = ex.Message
                });
                await _usageStats.RecordGenerationAsync(SelectedProvider.ToString(), SelectedModel, Duration, false);
            }
        }

        ProgressValue = 100;
        IsGenerating = false;
        _cts?.Dispose();
        _cts = null;

        if (BatchResults.Count > 0)
            StatusMessage = $"✓ {BatchResults.Count} video(s) generated. Check History.";
    }

    private bool CanGenerate() => !IsGenerating && !string.IsNullOrWhiteSpace(Prompt);

    [RelayCommand(CanExecute = nameof(IsGenerating))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusMessage = "Cancelling…";
    }

    // ── Image source actions ────────────────────────────────

    [RelayCommand]
    private void BrowseImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Source Image",
            Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.webp|All Files|*.*"
        };
        if (dialog.ShowDialog() == true)
            SourceImagePath = dialog.FileName;
    }

    [RelayCommand]
    private void ClearImage()
    {
        SourceImagePath = null;
        LastFrameImagePath = null;
        ContinueFromLastFrame = false;
    }

    /// <summary>
    /// Continue from last frame — extracts the final frame from the most recent
    /// completed video using FFmpeg, then uses it as the source image for the next
    /// generation. Matches the original Python app's "continue-from-last-frame" tool.
    /// </summary>
    [RelayCommand]
    private async Task ContinueFromLastFrameAsync()
    {
        try
        {
            var videos = await _storageService.LoadVideosAsync();
            var lastCompleted = videos.FirstOrDefault(v =>
                v.Status == VideoGenerationStatus.Completed &&
                !string.IsNullOrEmpty(v.LocalFilePath) &&
                File.Exists(v.LocalFilePath));

            if (lastCompleted?.LocalFilePath is null)
            {
                StatusMessage = "No completed local video found to extract last frame from.";
                _activityLog.Log("Continue-from-last-frame: no local video found", LogLevel.Warning);
                return;
            }

            StatusMessage = "Extracting last frame from previous video…";
            _activityLog.Log($"Extracting last frame from {lastCompleted.LocalFilePath}", LogLevel.Information);

            // Use FFmpeg to extract the last frame
            var thumbDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GrokVideoStudio", "frames");
            Directory.CreateDirectory(thumbDir);

            var framePath = Path.Combine(thumbDir, $"lastframe_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-sseof -3 -i \"{lastCompleted.LocalFilePath}\" -frames:v 1 -q:v 2 \"{framePath}\" -y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 0 && File.Exists(framePath))
                {
                    LastFrameImagePath = framePath;
                    ContinueFromLastFrame = true;
                    StatusMessage = "✓ Last frame extracted. Will use as source image.";
                    _activityLog.Log($"Last frame saved to {framePath}", LogLevel.Information);
                }
                else
                {
                    StatusMessage = "✗ Failed to extract last frame. Is FFmpeg in PATH?";
                    _activityLog.Log("Last frame extraction failed", LogLevel.Error);
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error: {ex.Message}";
            _activityLog.Log($"Continue-from-last-frame error: {ex.Message}", LogLevel.Error);
        }
    }

    // ── Helper ──────────────────────────────────────────────

    private static string ConvertImageToDataUri(string imagePath)
    {
        var bytes = File.ReadAllBytes(imagePath);
        var base64 = Convert.ToBase64String(bytes);
        var ext = Path.GetExtension(imagePath).TrimStart('.').ToLowerInvariant();
        var mime = ext switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "webp" => "image/webp",
            _ => "image/jpeg"
        };
        return $"data:{mime};base64,{base64}";
    }
}
