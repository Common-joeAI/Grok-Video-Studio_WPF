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

            // Image-to-video model strategy:
            // Try grok-imagine-video-1.5 first (best I2V model).
            // If the account doesn't have access, fall back to grok-imagine-video (base).
            // If the base model also rejects the image, fall back to text-to-video (no image).
            var clipModel = request.Model;
            var triedModels = new List<(string model, string error)>();

            var videoRequest = new VideoGenerationRequest
            {
                Model = clipModel,
                Prompt = prompt,
                Duration = request.ClipDuration,
                AspectRatio = request.AspectRatio,
                Resolution = request.Resolution,
                Provider = request.Provider,
                Image = continuationImage
            };

            // If doing I2V, try upgrading to 1.5 first
            if (continuationImage is not null && request.Provider == VideoProvider.GrokImagine && !clipModel.Contains("1.5"))
            {
                clipModel = "grok-imagine-video-1.5";
                videoRequest = videoRequest with { Model = clipModel };
                _activityLog.Log($"Clip {i + 1}: trying {clipModel} for image-to-video", LogLevel.Information);
            }

            // Per-clip try-catch with multi-model fallback
            string? clipPath = null;
            try
            {
                VideoPollResponse? pollResponse = null;

                // Attempt 1: I2V with preferred model (1.5 or user-selected)
                // Attempt 2: I2V with base model (grok-imagine-video)
                // Attempt 3: text-to-video (drop image entirely)
                var attemptConfigs = new List<(string model, ImageSource? image, string label)>
                {
                    (clipModel, continuationImage, "I2V"),
                };

                // Add base model fallback if we're using 1.5
                if (continuationImage is not null && clipModel.Contains("1.5"))
                    attemptConfigs.Add(("grok-imagine-video", continuationImage, "I2V (base model)"));

                // Add text-to-video fallback if we have an image
                if (continuationImage is not null)
                    attemptConfigs.Add(("grok-imagine-video", null, "text-to-video fallback"));

                foreach (var (tryModel, tryImage, label) in attemptConfigs)
                {
                    var tryRequest = videoRequest with { Model = tryModel, Image = tryImage };
                    var attemptProgress = new Progress<string>(msg =>
                        progress?.Report(new ChainedGenerationProgress
                        {
                            CurrentClip = i + 1,
                            TotalClips = clipCount,
                            Stage = "generating",
                            Message = $"Clip {i + 1} ({label}): {msg}",
                            OverallPercent = overallPct + (100.0 / clipCount * 0.7)
                        }));

                    try
                    {
                        pollResponse = await videoService.GenerateAsync(
                            tryRequest, request.ApiKey, pollInterval, maxAttempts, attemptProgress, ct);

                        if (pollResponse.IsDone && pollResponse.Video is not null)
                            break; // Success!

                        _activityLog.Log($"Clip {i + 1}: {label} with {tryModel} returned {pollResponse.Status}", LogLevel.Warning);
                    }
                    catch (Exception tryEx)
                    {
                        _activityLog.Log($"Clip {i + 1}: {label} with {tryModel} failed — {tryEx.Message}", LogLevel.Warning);
                        triedModels.Add((tryModel, tryEx.Message));
                    }
                }

                if (pollResponse is null || !pollResponse.IsDone || pollResponse.Video is null)
                {
                    var errors = triedModels.Count > 0
                        ? string.Join("; ", triedModels.Select(t => $"{t.model}: {t.error}"))
                        : "all attempts exhausted";
                    throw new Exception($"Clip {i + 1} failed after all retries: {errors}");
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
    private async Task<string> ExtractLastFrameAsDataUriAsync(
        string videoPath, string ffmpegPath, CancellationToken ct)
    {
        // Validate ffmpeg exists
        var resolvedPath = ffmpegPath;
        if (string.IsNullOrEmpty(resolvedPath) || resolvedPath == "ffmpeg")
        {
            // Check if ffmpeg is on PATH
            var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            var found = pathDirs.Select(d => Path.Combine(d, "ffmpeg.exe"))
                                .FirstOrDefault(File.Exists);
            if (found is not null)
                resolvedPath = found;
            else if (!File.Exists("ffmpeg") && !File.Exists("ffmpeg.exe"))
            {
                _activityLog.Log("FFmpeg not found on PATH and no custom path set — frame extraction will fail. Set FFmpeg path in Settings.", LogLevel.Error);
                throw new FileNotFoundException("FFmpeg not found. Set the FFmpeg path in Settings.");
            }
        }
        else if (!File.Exists(resolvedPath))
        {
            _activityLog.Log($"FFmpeg not found at '{resolvedPath}' — check Settings path.", LogLevel.Error);
            throw new FileNotFoundException($"FFmpeg not found at '{resolvedPath}'");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "GrokVideoStudio", "frames");
        Directory.CreateDirectory(tempDir);
        var framePath = Path.Combine(tempDir, $"frame_{Guid.NewGuid():N}.png");

        _activityLog.Log($"Extracting last frame: {resolvedPath} -sseof -3 -i \"{Path.GetFileName(videoPath)}\" ...", LogLevel.Debug);

        var psi = new ProcessStartInfo
        {
            FileName = resolvedPath,
            Arguments = $"-sseof -3 -i \"{videoPath}\" -frames:v 1 -q:v 2 \"{framePath}\" -y",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = new Process { StartInfo = psi };
        
        var stderrBuilder = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                stderrBuilder.AppendLine(e.Data);
        };

        try
        {
            process.Start();
            process.BeginErrorReadLine();

            // Timeout after 30 seconds
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(); } catch { }
            _activityLog.Log("FFmpeg frame extraction timed out after 30s", LogLevel.Error);
            throw new TimeoutException("FFmpeg frame extraction timed out after 30 seconds");
        }

        if (process.ExitCode != 0 || !File.Exists(framePath))
        {
            var stderr = stderrBuilder.ToString().Trim();
            _activityLog.Log($"FFmpeg frame extraction failed (exit {process.ExitCode}): {stderr}", LogLevel.Error);
            throw new InvalidOperationException($"Failed to extract last frame from {Path.GetFileName(videoPath)}: {stderr}");
        }

        var bytes = await File.ReadAllBytesAsync(framePath, ct);
        var base64 = Convert.ToBase64String(bytes);
        
        // Clean up temp frame
        try { File.Delete(framePath); } catch { }

        _activityLog.Log($"Frame extracted: {bytes.Length / 1024} KB PNG, base64 {base64.Length} chars", LogLevel.Debug);
        return $"data:image/png;base64,{base64}";
    }
}
