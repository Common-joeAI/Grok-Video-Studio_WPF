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
/// Chain Generation ViewModel — orchestrates audio-driven or manual chained generation.
///
/// Two modes:
/// 1. Audio-driven: Upload an MP3/WAV, AI analyzes it and generates a scene plan,
///    then each scene is generated as a clip continuing from the previous one's last frame.
/// 2. Manual: Specify a base prompt and clip count, each clip chains from the previous.
///
/// All clips are then stitched together with FFmpeg, with the audio mixed in if provided.
/// </summary>
public partial class ChainGenerationViewModel : ObservableObject
{
    private readonly ISecureSettingsService _settingsService;
    private readonly IAudioAnalysisService _audioAnalysis;
    private readonly IChainedGenerationService _chainedGeneration;
    private readonly IActivityLogService _activityLog;
    private readonly ILogger<ChainGenerationViewModel> _logger;
    private CancellationTokenSource? _cts;

    // ── Mode ──
    [ObservableProperty] private bool _audioDrivenMode = true;

    // ── Audio input ──
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeAudioCommand))]
    private string? _audioFilePath;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeAudioCommand))]
    private string _concept = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeAudioCommand))]
    private double _audioDuration;
    [ObservableProperty] private int _calculatedClipCount;

    // ── Generation settings ──
    [ObservableProperty] private VideoProvider _selectedProvider = VideoProvider.GrokImagine;
    [ObservableProperty] private string _selectedModel = "grok-imagine-video";
    [ObservableProperty] private int _clipDuration = 8;
    [ObservableProperty] private string _aspectRatio = "16:9";
    [ObservableProperty] private string _resolution = "720p";

    // ── Manual mode ──
    [ObservableProperty] private string _basePrompt = string.Empty;
    [ObservableProperty] private int _manualClipCount = 4;

    // ── State ──
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunChainCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeAudioCommand))]
    private bool _isRunning;

    [ObservableProperty] private string _statusMessage = "Ready.";
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private int _currentClip;
    [ObservableProperty] private int _totalClips;
    [ObservableProperty] private string _currentStage = string.Empty;
    [ObservableProperty] private string _timeEstimate = string.Empty;
    [ObservableProperty] private string? _resultVideoPath;

    // ── Scene plan ──
    public ObservableCollection<SceneSegment> ScenePlan { get; } = [];

    // ── Collections ──
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
    public ObservableCollection<int> ClipDurations { get; } = [4, 5, 6, 7, 8, 10, 12, 15];

    public ChainGenerationViewModel(
        ISecureSettingsService settingsService,
        IAudioAnalysisService audioAnalysis,
        IChainedGenerationService chainedGeneration,
        IActivityLogService activityLog,
        ILogger<ChainGenerationViewModel> logger)
    {
        _settingsService = settingsService;
        _audioAnalysis = audioAnalysis;
        _chainedGeneration = chainedGeneration;
        _activityLog = activityLog;
        _logger = logger;

        // When scene plan items are added/removed, re-evaluate whether Run is enabled
        ScenePlan.CollectionChanged += (_, _) => RunChainCommand.NotifyCanExecuteChanged();

        LoadDefaults();
    }

    private void LoadDefaults()
    {
        var s = _settingsService.LoadSettings();
        ClipDuration = s.DefaultDuration > 0 ? s.DefaultDuration : 8;
        AspectRatio = string.IsNullOrEmpty(s.DefaultAspectRatio) ? "16:9" : s.DefaultAspectRatio;
        Resolution = string.IsNullOrEmpty(s.DefaultResolution) ? "720p" : s.DefaultResolution;
        SelectedModel = string.IsNullOrEmpty(s.GrokVideoModel) ? "grok-imagine-video" : s.GrokVideoModel;
    }

    // ── Browse Audio ─────────────────────────────────────────

    [RelayCommand]
    private void BrowseAudio()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav;*.m4a;*.aac;*.ogg;*.flac|All Files|*.*",
            Title = "Select Audio File"
        };
        if (dialog.ShowDialog() == true)
        {
            AudioFilePath = dialog.FileName;
            _ = LoadAudioDurationAsync();
        }
    }

    private async Task LoadAudioDurationAsync()
    {
        if (string.IsNullOrEmpty(AudioFilePath)) return;
        try
        {
            AudioDuration = await _audioAnalysis.GetDurationAsync(AudioFilePath);
            CalculatedClipCount = (int)Math.Ceiling(AudioDuration / ClipDuration);
            StatusMessage = $"Audio loaded: {AudioDuration:F1}s → {CalculatedClipCount} clips needed at {ClipDuration}s each.";
            _activityLog.Log($"Audio loaded: {AudioFilePath} ({AudioDuration:F1}s)", LogLevel.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"⚠ Could not read audio: {ex.Message}";
            _activityLog.Log($"Audio analysis failed: {ex.Message}", LogLevel.Error);
        }
    }

    partial void OnClipDurationChanged(int value)
    {
        if (AudioDuration > 0)
        {
            CalculatedClipCount = (int)Math.Ceiling(AudioDuration / value);
            StatusMessage = $"Recalculated: {CalculatedClipCount} clips needed at {value}s each.";
        }
    }

    // ── Analyze Audio (generate scene plan) ──────────────────

    private bool CanAnalyzeAudio() =>
        !string.IsNullOrWhiteSpace(AudioFilePath) &&
        !string.IsNullOrWhiteSpace(Concept) &&
        AudioDuration > 0 &&
        !IsRunning;

    [RelayCommand(CanExecute = nameof(CanAnalyzeAudio))]
    private async Task AnalyzeAudioAsync()
    {
        if (string.IsNullOrEmpty(AudioFilePath))
        {
            StatusMessage = "Browse an audio file first.";
            return;
        }
        if (string.IsNullOrEmpty(Concept))
        {
            StatusMessage = "Enter a concept/theme for the video.";
            return;
        }

        var settings = _settingsService.LoadSettings();
        var apiKey = SelectedProvider switch
        {
            VideoProvider.GrokImagine => settings.GrokApiKey,
            VideoProvider.OpenAiSora => settings.OpenAiApiKey,
            _ => settings.GrokApiKey
        };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusMessage = "⚠ No API key configured for AI analysis. Set one in Settings.";
            return;
        }

        try
        {
            StatusMessage = "Analyzing audio and generating scene plan…";
            _activityLog.Log("Audio analysis started", LogLevel.Information);

            var aiModel = SelectedProvider == VideoProvider.OpenAiSora
                ? settings.OpenAiChatModel
                : settings.GrokChatModel;
            var aiProvider = SelectedProvider == VideoProvider.OpenAiSora ? "openai" : "grok";

            var result = await _audioAnalysis.AnalyzeAsync(
                AudioFilePath, Concept, ClipDuration, apiKey, aiModel, aiProvider);

            ScenePlan.Clear();
            foreach (var scene in result.Scenes)
                ScenePlan.Add(scene);

            CalculatedClipCount = result.ClipCount;
            RunChainCommand.NotifyCanExecuteChanged();  // ScenePlan.Count changed — re-evaluate CanExecute
            StatusMessage = $"✓ Scene plan ready: {result.ClipCount} scenes for {result.TotalDuration:F1}s of audio.";
            _activityLog.Log($"Scene plan generated: {result.ClipCount} scenes", LogLevel.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Analysis failed: {ex.Message}";
            _activityLog.Log($"Audio analysis failed: {ex.Message}", LogLevel.Error);
            _logger.LogError(ex, "Audio analysis failed");
        }
    }

    // ── Run Chained Generation ──────────────────────────────

    [RelayCommand(CanExecute = nameof(CanRunChain))]
    private async Task RunChainAsync()
    {
        var settings = _settingsService.LoadSettings();
        var apiKey = SelectedProvider switch
        {
            VideoProvider.GrokImagine => settings.GrokApiKey,
            VideoProvider.OpenAiSora => settings.OpenAiApiKey,
            VideoProvider.Seedance => settings.SeedanceApiKey,
            _ => ""
        };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusMessage = $"⚠ No API key for {SelectedProvider}. Configure in Settings.";
            return;
        }

        // Build the prompt list
        List<string> prompts;
        if (AudioDrivenMode && ScenePlan.Count > 0)
        {
            prompts = ScenePlan.Select(s => s.Prompt).ToList();
        }
        else if (!AudioDrivenMode && !string.IsNullOrEmpty(BasePrompt))
        {
            // Manual mode: generate N variations of the base prompt
            prompts = Enumerable.Range(0, ManualClipCount)
                .Select(i => i == 0 ? BasePrompt : $"{BasePrompt} — continuation scene {i + 1}")
                .ToList();
        }
        else
        {
            StatusMessage = "Generate a scene plan first (audio mode) or enter a base prompt (manual mode).";
            return;
        }

        _cts = new CancellationTokenSource();
        IsRunning = true;
        OverallProgress = 0;
        ResultVideoPath = null;
        StatusMessage = $"Starting chained generation of {prompts.Count} clips…";
                TimeEstimate = "";
        _activityLog.Log($"Chained generation started: {prompts.Count} clips, {SelectedProvider}/{SelectedModel}", LogLevel.Information);

        try
        {
            var downloadDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GrokVideoStudio", "chains", DateTime.Now.ToString("yyyyMMdd_HHmmss"));

            var stitchOpts = new StitchOptions
            {
                EnableCrossfade = settings.EnableCrossfade,
                InterpolationFps = settings.InterpolationFps,
                UpscalePreset = settings.UpscalePreset,
                EnableGpuEncode = settings.EnableGpuEncode,
                MusicMixPath = AudioDrivenMode ? AudioFilePath : null
            };

            var request = new ChainedGenerationRequest
            {
                Prompts = prompts,
                Provider = SelectedProvider,
                Model = SelectedModel,
                ClipDuration = ClipDuration,
                AspectRatio = AspectRatio,
                Resolution = Resolution,
                ApiKey = apiKey,
                AudioPath = AudioDrivenMode ? AudioFilePath : null,
                StitchOptions = stitchOpts,
                OutputDirectory = downloadDir,
                FfmpegPath = string.IsNullOrEmpty(settings.FfmpegPath) ? "ffmpeg" : settings.FfmpegPath
            };

            var progress = new Progress<ChainedGenerationProgress>(p =>
            {
                CurrentClip = p.CurrentClip;
                TotalClips = p.TotalClips;
                CurrentStage = p.Stage;
                StatusMessage = p.Message;
                OverallProgress = p.OverallPercent;
                TimeEstimate = p.EstimatedRemaining is not null
                    ? $"~{p.EstimatedRemaining.Value.TotalMinutes:F0} min remaining"
                    : "";
            });

            var result = await _chainedGeneration.RunChainedGenerationAsync(request, progress, _cts.Token);

            if (result.Success)
            {
                ResultVideoPath = result.FinalVideoPath;
                StatusMessage = $"✓ Complete! {result.ClipsGenerated} clips stitched → {result.FinalVideoPath}";
                TimeEstimate = "";
                _activityLog.Log($"Chained generation complete: {result.FinalVideoPath}", LogLevel.Information);
            }
            else
            {
                StatusMessage = $"✗ Failed: {result.ErrorMessage}";
                _activityLog.Log($"Chained generation failed: {result.ErrorMessage}", LogLevel.Error);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
                TimeEstimate = "";
            _activityLog.Log("Chained generation cancelled", LogLevel.Warning);
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Error: {ex.Message}";
            _activityLog.Log($"Chained generation error: {ex.Message}", LogLevel.Error);
            _logger.LogError(ex, "Chained generation failed");
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanRunChain => !IsRunning && (
        (AudioDrivenMode && ScenePlan.Count > 0) ||
        (!AudioDrivenMode && !string.IsNullOrWhiteSpace(BasePrompt))
    );

    [RelayCommand(CanExecute = nameof(CanCancelChain))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusMessage = "Cancelling…";
        _activityLog.Log("Chain generation cancelled by user", LogLevel.Warning);
    }

    private bool CanCancelChain => IsRunning;

    [RelayCommand(CanExecute = nameof(CanClearChain))]
    private void ClearChain()
    {
        // Stop any running generation first
        if (IsRunning)
        {
            _cts?.Cancel();
            _activityLog.Log("Chain generation stopped and cleared by user", LogLevel.Warning);
        }

        // Reset all state
        ScenePlan.Clear();
        AudioFilePath = null;
        AudioDuration = 0;
        Concept = string.Empty;
        CurrentClip = 0;
        TotalClips = 0;
        CurrentStage = string.Empty;
        OverallProgress = 0;
        ResultVideoPath = null;
        StatusMessage = "Ready.";
        CalculatedClipCount = 0;

        // Re-evaluate command states
        RunChainCommand.NotifyCanExecuteChanged();
        AnalyzeAudioCommand.NotifyCanExecuteChanged();

        _activityLog.Log("Chain state cleared", LogLevel.Information);
    }

    private bool CanClearChain =>
        ScenePlan.Count > 0 || !string.IsNullOrEmpty(AudioFilePath) ||
        !string.IsNullOrEmpty(Concept) || IsRunning;

    // ── Open Result ──────────────────────────────────────────

    [RelayCommand]
    private void OpenResult()
    {
        if (!string.IsNullOrEmpty(ResultVideoPath) && File.Exists(ResultVideoPath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ResultVideoPath,
                UseShellExecute = true
            });
        }
    }
}
