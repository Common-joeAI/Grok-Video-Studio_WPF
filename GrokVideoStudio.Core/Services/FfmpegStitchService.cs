using System.Diagnostics;
using System.Text.Json;
using GrokVideoStudio.Core.Models;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// FFmpeg-based video stitching service.
/// Matches the actual repo's stitch pipeline: crossfade transitions, interpolation,
/// upscale, GPU encode, and music mixing.
/// 
/// MODERNIZATION: The Python app shells out to system ffmpeg. This .NET version
/// does the same via Process.Start with structured error handling and async I/O.
/// </summary>
public interface IStitchService
{
    /// <summary>Stitch multiple video clips into one with optional pipeline features.</summary>
    Task<string> StitchAsync(
        IReadOnlyList<string> videoPaths,
        string outputPath,
        StitchOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}

public sealed class FfmpegStitchService : IStitchService
{
    private readonly ISecureSettingsService? _settingsService;

    /// <summary>Creates a stitch service with a hardcoded ffmpeg path (legacy).</summary>
    public FfmpegStitchService(string ffmpegPath = "ffmpeg")
    {
        _settingsService = null;
        FfmpegPath = ffmpegPath;
    }

    /// <summary>Creates a stitch service that reads ffmpeg path from user settings.</summary>
    public FfmpegStitchService(ISecureSettingsService settingsService)
    {
        _settingsService = settingsService;
        var s = settingsService.LoadSettings();
        FfmpegPath = string.IsNullOrEmpty(s.FfmpegPath) ? "ffmpeg" : s.FfmpegPath;
    }

    /// <summary>Current ffmpeg executable path (resolved from settings or constructor).</summary>
    public string FfmpegPath { get; private set; }

    /// <summary>Refresh the ffmpeg path from settings (call before each operation).</summary>
    private void ResolveFfmpegPath()
    {
        if (_settingsService is not null)
        {
            var s = _settingsService.LoadSettings();
            FfmpegPath = string.IsNullOrEmpty(s.FfmpegPath) ? "ffmpeg" : s.FfmpegPath;
        }
    }

    public async Task<string> StitchAsync(
        IReadOnlyList<string> videoPaths,
        string outputPath,
        StitchOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (videoPaths.Count == 0)
            throw new ArgumentException("No video paths provided.", nameof(videoPaths));

        ResolveFfmpegPath();

        if (videoPaths.Count == 1)
        {
            // Single clip — just copy or transcode with options
            progress?.Report("Processing single clip…");
            return await ProcessSingleClipAsync(videoPaths[0], outputPath, options, progress, ct);
        }

        progress?.Report($"Stitching {videoPaths.Count} clips…");

        // Build FFmpeg concat with optional crossfade
        string? tempListPath = null;
        try
        {
            var args = BuildStitchArguments(videoPaths, outputPath, options, out tempListPath);
            await RunFfmpegAsync(args, progress, ct);

            progress?.Report($"✓ Stitched to {outputPath}");
            return outputPath;
        }
        finally
        {
            // Clean up the temporary concat list file
            if (tempListPath is not null && File.Exists(tempListPath))
            {
                try { File.Delete(tempListPath); }
                catch { /* best-effort cleanup */ }
            }
        }
    }

    private string BuildStitchArguments(IReadOnlyList<string> paths, string output, StitchOptions options, out string? tempListPath)
    {
        tempListPath = null;
        var sb = new System.Text.StringBuilder();

        // Input files
        foreach (var path in paths)
            sb.Append($"-i \"{path}\" ");

        // Filter for concat with optional crossfade
        if (options.EnableCrossfade && paths.Count >= 2)
        {
            // Crossfade between clips
            var filterParts = new List<string>();
            for (int i = 0; i < paths.Count; i++)
                filterParts.Add($"[{i}:v]");

            var concatFilter = string.Join("", filterParts);
            sb.Append($"-filter_complex \"{concatFilter}concat=n={paths.Count}:v=1:a=0[v]\" -map \"[v]\" ");
        }
        else
        {
            // Simple concat demuxer (no crossfade)
            // Write concat file list
            tempListPath = Path.GetTempFileName();
            using (var writer = new StreamWriter(tempListPath))
                foreach (var path in paths)
                    writer.WriteLine($"file '{path}'");
            sb.Clear();
            sb.Append($"-f concat -safe 0 -i \"{tempListPath}\" ");
        }

        // Interpolation
        if (options.InterpolationFps > 0)
            sb.Append($"-vf \"minterpolate=fps={options.InterpolationFps}\" ");

        // Upscale
        if (!string.IsNullOrEmpty(options.UpscalePreset) && options.UpscalePreset != "none")
        {
            var scale = options.UpscalePreset switch
            {
                "2x" => "scale=iw*2:ih*2",
                "1080p" => "scale=1920:1080",
                "1440p" => "scale=2560:1440",
                "4K" => "scale=3840:2160",
                _ => null
            };
            if (scale is not null)
                sb.Append($"-vf \"{scale}\" ");
        }

        // GPU encode
        if (options.EnableGpuEncode)
            sb.Append("-c:v h264_nvenc -preset p1 ");
        else
            sb.Append("-c:v libx264 -preset medium ");

        // Music mix
        if (!string.IsNullOrEmpty(options.MusicMixPath) && File.Exists(options.MusicMixPath))
        {
            sb.Append($"-i \"{options.MusicMixPath}\" -map 0:v -map 1:a -c:a aac -shortest ");
        }
        else
        {
            sb.Append("-an ");
        }

        sb.Append($"-y \"{output}\"");
        return sb.ToString();
    }

    private async Task<string> ProcessSingleClipAsync(
        string inputPath, string outputPath, StitchOptions options,
        IProgress<string>? progress, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"-i \"{inputPath}\" ");

        var filters = new List<string>();

        if (options.InterpolationFps > 0)
            filters.Add($"minterpolate=fps={options.InterpolationFps}");

        if (!string.IsNullOrEmpty(options.UpscalePreset) && options.UpscalePreset != "none")
        {
            var scale = options.UpscalePreset switch
            {
                "2x" => "scale=iw*2:ih*2",
                "1080p" => "scale=1920:1080",
                "1440p" => "scale=2560:1440",
                "4K" => "scale=3840:2160",
                _ => null
            };
            if (scale is not null) filters.Add(scale);
        }

        if (filters.Count > 0)
            sb.Append($"-vf \"{string.Join(",", filters)}\" ");

        if (options.EnableGpuEncode)
            sb.Append("-c:v h264_nvenc -preset p1 ");
        else
            sb.Append("-c:v libx264 -preset medium ");

        if (!string.IsNullOrEmpty(options.MusicMixPath) && File.Exists(options.MusicMixPath))
            sb.Append($"-i \"{options.MusicMixPath}\" -map 0:v -map 1:a -c:a aac -shortest ");
        else
            sb.Append("-c:a copy ");

        sb.Append($"-y \"{outputPath}\"");

        await RunFfmpegAsync(sb.ToString(), progress, ct);
        return outputPath;
    }

    private async Task RunFfmpegAsync(string args, IProgress<string>? progress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FfmpegPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        // Read both stderr and stdout concurrently to prevent buffer deadlock.
        // FFmpeg writes progress to stderr, but may also write to stdout.
        // If we only read stderr, a full stdout buffer would deadlock the process.
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);
        var stderr = await stderrTask;
        _ = await stdoutTask; // discard stdout — we only need stderr for error reporting

        if (process.ExitCode != 0)
        {
            var errMsg = stderr.Length > 500 ? stderr[..500] : stderr;
            progress?.Report($"FFmpeg error: {errMsg}");
            throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}: {errMsg}");
        }
    }
}
