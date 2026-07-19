using System.Diagnostics;
using Microsoft.Extensions.Logging;
using GrokVideoStudio.Core.Models;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Orchestrates chained video generation: each clip continues from the last frame
/// of the previous clip, then all clips are stitched together.
/// Supports audio-driven generation where an audio file determines the total duration
/// and number of clips needed.
/// </summary>
public interface IChainedGenerationService
{
    /// <summary>
    /// Run a full chained generation pipeline.
    /// Generates N clips where each continues from the last frame of the previous one,
    /// then stitches them together with optional audio mixing.
    /// </summary>
    Task<ChainedGenerationResult> RunChainedGenerationAsync(
        ChainedGenerationRequest request,
        IProgress<ChainedGenerationProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>Request for a chained generation run.</summary>
public sealed record ChainedGenerationRequest
{
    /// <summary>Scene prompts for each clip. If null, a single prompt is used for all clips.</summary>
    public required IReadOnlyList<string> Prompts { get; init; }

    /// <summary>Video provider to use.</summary>
    public VideoProvider Provider { get; init; } = VideoProvider.GrokImagine;

    /// <summary>Video model name.</summary>
    public string Model { get; init; } = "grok-imagine-video";

    /// <summary>Duration per clip in seconds.</summary>
    public int ClipDuration { get; init; } = 8;

    /// <summary>Aspect ratio (e.g. "16:9").</summary>
    public string AspectRatio { get; init; } = "16:9";

    /// <summary>Resolution (e.g. "720p").</summary>
    public string Resolution { get; init; } = "720p";

    /// <summary>API key for the video provider.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Optional audio file path to mix into the final stitched video.</summary>
    public string? AudioPath { get; init; }

    /// <summary>Stitch options for the final FFmpeg stitch.</summary>
    public StitchOptions StitchOptions { get; init; } = new();

    /// <summary>Output directory for clips and final video.</summary>
    public string OutputDirectory { get; init; } = "downloads";

    /// <summary>FFmpeg path.</summary>
    public string FfmpegPath { get; init; } = "ffmpeg";

    /// <summary>Optional brand settings to apply after stitching.</summary>
    public BrandSettings? Brand { get; init; }
}

/// <summary>Result of a chained generation run.</summary>
public sealed record ChainedGenerationResult
{
    public bool Success { get; init; }
    public string? FinalVideoPath { get; init; }
    public IReadOnlyList<string> ClipPaths { get; init; } = Array.Empty<string>();
    public string? ErrorMessage { get; init; }
    public int ClipsGenerated { get; init; }
    public double TotalDurationSeconds { get; init; }
}

/// <summary>Progress reporting for chained generation.</summary>
public sealed record ChainedGenerationProgress
{
    public int CurrentClip { get; init; }
    public int TotalClips { get; init; }
    public string Stage { get; init; } = "";
    public string Message { get; init; } = "";
    public double OverallPercent { get; init; }
}

public sealed class ChainedGenerationService : IChainedGenerationService
{
    private readonly IVideoGenerationFactory _videoFactory;
    private readonly IVideoDownloadService _downloadService;
    private readonly IVideoStorageService _storageService;
    private readonly IActivityLogService _activityLog;
    private readonly IBrandingService? _brandingService;

    public ChainedGenerationService(
        IVideoGenerationFactory videoFactory,
        IVideoDownloadService downloadService,
        IVideoStorageService storageService,
        IActivityLogService activityLog,
        IBrandingService? brandingService = null)
    {
        _videoFactory = videoFactory;
        _downloadService = downloadService;
        _storageService = storageService;
        _activityLog = activityLog;
        _brandingService = brandingService;
    }

    public async Task<ChainedGenerationResult> RunChainedGenerationAsync(
        ChainedGenerationRequest request,
        IProgress<ChainedGenerationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var clipCount = request.Prompts.Count;
        if (clipCount == 0)
            return new ChainedGenerationResult { Success = false, ErrorMessage = "No prompts provided." };

        Directory.CreateDirectory(request.OutputDirectory);

        var clipPaths = new List<string>();
        var videoService = _videoFactory.GetService(request.Provider);
        var pollInterval = 5;
        var maxAttempts = 120;

        _activityLog.Log($"Chained generation started: {clipCount} clips, {request.Provider}/{request.Model}", LogLevel.Information);

        for (int i = 0; i < clipCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var prompt = request.Prompts[i];
            var overallPct = (double)i / clipCount * 100;

            progress?.Report(new ChainedGenerationProgress
            {
                CurrentClip = i + 1,
                TotalClips = clipCount,
                Stage = i == 0 ? "generating" : "chaining",
                Message = i == 0
                    ? $"Generating clip {i + 1}/{clipCount}…"
                    : $"Generating clip {i + 1}/{clipCount} (continued from clip {i})…",
                OverallPercent = overallPct
            });

            // Build the generation request
            // Try image-to-video continuation from clip 2 onwards, but fall back
            // to text-to-video if frame extraction or the API rejects the image.
            ImageSource? continuationImage = null;
            if (i > 0 && File.Exists(clipPaths[i - 1]))
            {
                try
                {
                    var dataUri = await ExtractLastFrameAsDataUriAsync(clipPaths[i - 1], request.FfmpegPath, ct);
                    continuationImage = new ImageSource { Url = dataUri };
                    _activityLog.Log($"Clip {i + 1}: extracted last frame for continuation", LogLevel.Information);
                }
                catch (Exception frameEx)
                {
                    _activityLog.Log($"Clip {i + 1}: frame extraction failed, generating as standalone — {frameEx.Message}", LogLevel.Warning);
                }
            }

            var videoRequest = new VideoGenerationRequest
            {
                Model = request.Model,
                Prompt = prompt,
                Duration = request.ClipDuration,
                AspectRatio = request.AspectRatio,
                Resolution = request.Resolution,
                Provider = request.Provider,
                Image = continuationImage
            };

            // Generate
            var progressAdapter = new Progress<string>(msg =>
                progress?.Report(new ChainedGenerationProgress
                {
                    CurrentClip = i + 1,
                    TotalClips = clipCount,
                    Stage = "generating",
                    Message = $"Clip {i + 1}: {msg}",
                    OverallPercent = overallPct + (100.0 / clipCount * 0.7) // generation is 70% of each clip's work
                }));

            // Per-clip try-catch: a single clip failure should skip, not kill the chain
            string? clipPath = null;
            try
            {
                var pollResponse = await videoService.GenerateAsync(
                    videoRequest, request.ApiKey, pollInterval, maxAttempts, progressAdapter, ct);

                if (!pollResponse.IsDone || pollResponse.Video is null)
                {
                    // If this was an image-to-video attempt, retry as text-to-video before failing
                    if (continuationImage is not null)
                    {
                        _activityLog.Log($"Clip {i + 1}: image-to-video failed ({pollResponse.Status}), retrying as text-to-video…", LogLevel.Warning);
                        continuationImage = null;
                        videoRequest = videoRequest with { Image = null };

                        var retryProgress = new Progress<string>(msg =>
                            progress?.Report(new ChainedGenerationProgress
                            {
                                CurrentClip = i + 1,
                                TotalClips = clipCount,
                                Stage = "generating",
                                Message = $"Clip {i + 1} (text-only retry): {msg}",
                                OverallPercent = overallPct + (100.0 / clipCount * 0.7)
                            }));

                        pollResponse = await videoService.GenerateAsync(
                            videoRequest, request.ApiKey, pollInterval, maxAttempts, retryProgress, ct);
                    }

                    if (!pollResponse.IsDone || pollResponse.Video is null)
                    {
                        throw new Exception($"Clip {i + 1} failed: {pollResponse.Status} — {pollResponse.Error?.Message}");
                    }
                }

                // Download the clip
                progress?.Report(new ChainedGenerationProgress
                {
                    CurrentClip = i + 1,
                    TotalClips = clipCount,
                    Stage = "downloading",
                    Message = $"Downloading clip {i + 1}…",
                    OverallPercent = overallPct + (100.0 / clipCount * 0.8)
                });

                clipPath = Path.Combine(request.OutputDirectory, $"chain_clip_{i + 1:D3}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
                await videoService.DownloadVideoAsync(pollResponse.Video.Url, clipPath, ct);
            }
            catch (OperationCanceledException)
            {
                throw; // Don't swallow cancellation
            }
            catch (Exception clipEx)
            {
                _activityLog.Log($"Clip {i + 1} skipped: {clipEx.Message}", LogLevel.Error);
                progress?.Report(new ChainedGenerationProgress
                {
                    CurrentClip = i + 1,
                    TotalClips = clipCount,
                    Stage = "skipped",
                    Message = $"Clip {i + 1} failed, skipping: {clipEx.Message}",
                    OverallPercent = (double)(i + 1) / clipCount * 100
                });
                continue; // Skip this clip, move to the next
            }

            clipPaths.Add(clipPath!);

            // Save to history
            var videoItem = new VideoItem
            {
                Prompt = prompt,
                Model = request.Model,
                Duration = request.ClipDuration,
                Resolution = request.Resolution,
                AspectRatio = request.AspectRatio,
                SourceImagePath = i > 0 ? "chained-from-previous" : null,
                VideoUrl = string.Empty, // URL was inside the try scope; clip path is what matters
                LocalFilePath = clipPath,
                Status = VideoGenerationStatus.Completed,
                SourceProvider = request.Provider,
                PromptSourceName = PromptSource.Manual,
                CompletedAt = DateTimeOffset.UtcNow
            };
            await _storageService.SaveVideoAsync(videoItem, ct);

            _activityLog.Log($"Clip {i + 1}/{clipCount} generated and downloaded: {clipPath}", LogLevel.Information);

            progress?.Report(new ChainedGenerationProgress
            {
                CurrentClip = i + 1,
                TotalClips = clipCount,
                Stage = "clip-done",
                Message = $"Clip {i + 1} complete.",
                OverallPercent = overallPct + (100.0 / clipCount * 0.9)
            });
        }

        // If all clips failed, abort
        if (clipPaths.Count == 0)
        {
            _activityLog.Log("All clips failed — nothing to stitch.", LogLevel.Error);
            return new ChainedGenerationResult
            {
                Success = false,
                ErrorMessage = "All clips failed during generation.",
                ClipsGenerated = 0,
                ClipPaths = clipPaths
            };
        }

        if (clipPaths.Count < clipCount)
        {
            _activityLog.Log($"Note: {clipCount - clipPaths.Count} clip(s) were skipped due to errors. Stitching {clipPaths.Count} successful clips.", LogLevel.Warning);
        }

        // Stitch all clips together
        if (clipPaths.Count == 1)
        {
            progress?.Report(new ChainedGenerationProgress
            {
                CurrentClip = clipCount,
                TotalClips = clipCount,
                Stage = "done",
                Message = "Single clip — no stitching needed.",
                OverallPercent = 100
            });
            return new ChainedGenerationResult
            {
                Success = true,
                FinalVideoPath = clipPaths[0],
                ClipPaths = clipPaths,
                ClipsGenerated = 1,
                TotalDurationSeconds = clipCount * request.ClipDuration
            };
        }

        progress?.Report(new ChainedGenerationProgress
        {
            CurrentClip = clipCount,
            TotalClips = clipCount,
            Stage = "stitching",
            Message = $"Stitching {clipPaths.Count} clips together…",
            OverallPercent = 95
        });

        var finalPath = Path.Combine(request.OutputDirectory, $"chain_final_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

        // Build stitch options — include audio if provided
        var stitchOpts = request.StitchOptions;
        if (!string.IsNullOrEmpty(request.AudioPath))
        {
            stitchOpts = stitchOpts with { MusicMixPath = request.AudioPath };
        }

        var stitchService = new FfmpegStitchService(request.FfmpegPath);
        var stitchProgress = new Progress<string>(msg =>
            progress?.Report(new ChainedGenerationProgress
            {
                CurrentClip = clipCount,
                TotalClips = clipCount,
                Stage = "stitching",
                Message = msg,
                OverallPercent = 97
            }));

        await stitchService.StitchAsync(clipPaths, finalPath, stitchOpts, stitchProgress, ct);

        // Apply branding if configured
        var brand = request.Brand;
        if (brand is null && _brandingService is not null)
            brand = _brandingService.LoadBrand();
        
        if (brand is { ApplyToAllVideos: true } && _brandingService is not null)
        {
            progress?.Report(new ChainedGenerationProgress
            {
                CurrentClip = clipCount,
                TotalClips = clipCount,
                Stage = "branding",
                Message = "Applying brand identity (logo, watermark, color grade, bumpers)…",
                OverallPercent = 98
            });

            var brandedPath = Path.Combine(request.OutputDirectory, $"chain_branded_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            try
            {
                await _brandingService.ApplyFullBrandingAsync(finalPath, brandedPath, brand,
                    new Progress<string>(msg => progress?.Report(new ChainedGenerationProgress
                    {
                        CurrentClip = clipCount,
                        TotalClips = clipCount,
                        Stage = "branding",
                        Message = msg,
                        OverallPercent = 99
                    })), ct);
                finalPath = brandedPath;
            }
            catch (Exception brandEx)
            {
                _activityLog.Log($"Branding failed (video still usable): {brandEx.Message}", LogLevel.Warning);
                // Continue with unbranded video
            }
        }

        _activityLog.Log($"Chained generation complete: {finalPath} ({clipPaths.Count} clips stitched)", LogLevel.Information);

        progress?.Report(new ChainedGenerationProgress
        {
            CurrentClip = clipCount,
            TotalClips = clipCount,
            Stage = "done",
            Message = $"✓ Complete: {finalPath}",
            OverallPercent = 100
        });

        return new ChainedGenerationResult
        {
            Success = true,
            FinalVideoPath = finalPath,
            ClipPaths = clipPaths,
            ClipsGenerated = clipPaths.Count,
            TotalDurationSeconds = clipPaths.Count * request.ClipDuration
        };
    }

    /// <summary>
    /// Extract the last frame of a video as a base64 data URI for image-to-video.
    /// </summary>
    private static async Task<string> ExtractLastFrameAsDataUriAsync(
        string videoPath, string ffmpegPath, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GrokVideoStudio", "frames");
        Directory.CreateDirectory(tempDir);
        var framePath = Path.Combine(tempDir, $"frame_{Guid.NewGuid():N}.png");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-sseof -3 -i \"{videoPath}\" -frames:v 1 -q:v 2 \"{framePath}\" -y",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0 || !File.Exists(framePath))
            throw new InvalidOperationException($"Failed to extract last frame from {videoPath}");

        var bytes = await File.ReadAllBytesAsync(framePath, ct);
        var base64 = Convert.ToBase64String(bytes);
        return $"data:image/png;base64,{base64}";
    }
}
