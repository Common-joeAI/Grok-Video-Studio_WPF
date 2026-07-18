using System.Diagnostics;
using System.Text.Json;
using GrokVideoStudio.Core.Models;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Applies brand identity (logo, watermark, color grading, audio) to videos via FFmpeg.
/// Brand settings are persisted to a JSON file in the app data directory.
/// </summary>
public interface IBrandingService
{
    /// <summary>Load brand settings from disk (or return defaults).</summary>
    BrandSettings LoadBrand();

    /// <summary>Save brand settings to disk.</summary>
    Task SaveBrandAsync(BrandSettings brand, CancellationToken ct = default);

    /// <summary>Apply branding to a single video file. Returns the output path.</summary>
    Task<string> ApplyBrandingAsync(
        string videoPath,
        string outputPath,
        BrandSettings? brand = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    /// <summary>Merge brand audio settings into existing StitchOptions.</summary>
    StitchOptions MergeIntoStitchOptions(StitchOptions options, BrandSettings? brand = null);

    /// <summary>Prepend intro video and append outro video to a stitched video, if configured.</summary>
    Task<string> ApplyBumpersAsync(
        string videoPath,
        string outputPath,
        BrandSettings? brand = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    /// <summary>Full brand pipeline: bumpers → branding (logo, watermark, color grade).</summary>
    Task<string> ApplyFullBrandingAsync(
        string videoPath,
        string outputPath,
        BrandSettings? brand = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}

public sealed class BrandingService : IBrandingService
{
    private readonly string _brandDir;
    private readonly string _brandFilePath;
    private readonly string _ffmpegPath;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public BrandingService(ISecureSettingsService settingsService)
    {
        _brandDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GrokVideoStudio");
        _brandFilePath = Path.Combine(_brandDir, "brand.json");

        var s = settingsService.LoadSettings();
        _ffmpegPath = string.IsNullOrEmpty(s.FfmpegPath) ? "ffmpeg" : s.FfmpegPath;
    }

    public BrandSettings LoadBrand()
    {
        if (!File.Exists(_brandFilePath))
            return new BrandSettings();

        try
        {
            var json = File.ReadAllText(_brandFilePath);
            return JsonSerializer.Deserialize<BrandSettings>(json, JsonOpts) ?? new BrandSettings();
        }
        catch
        {
            return new BrandSettings();
        }
    }

    public async Task SaveBrandAsync(BrandSettings brand, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_brandDir);
        var json = JsonSerializer.Serialize(brand, JsonOpts);
        await File.WriteAllTextAsync(_brandFilePath, json, ct);
    }

    public StitchOptions MergeIntoStitchOptions(StitchOptions options, BrandSettings? brand = null)
    {
        brand ??= LoadBrand();
        if (!brand.ApplyToAllVideos)
            return options;

        // Use brand background music if no music is already set
        var musicPath = !string.IsNullOrEmpty(options.MusicMixPath)
            ? options.MusicMixPath
            : brand.BackgroundMusicPath;

        return options with { MusicMixPath = musicPath };
    }

    public async Task<string> ApplyBrandingAsync(
        string videoPath,
        string outputPath,
        BrandSettings? brand = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        brand ??= LoadBrand();
        if (!brand.ApplyToAllVideos || !HasVisualBranding(brand))
        {
            // No branding to apply — just copy
            if (videoPath != outputPath)
                File.Copy(videoPath, outputPath, overwrite: true);
            return outputPath;
        }

        progress?.Report("Applying brand overlay (logo, watermark, color grade)…");

        var filters = new List<string>();

        // Logo overlay
        if (!string.IsNullOrEmpty(brand.LogoPath) && File.Exists(brand.LogoPath))
        {
            var (x, y) = GetOverlayPosition(brand.LogoPosition, brand.LogoPadding, brand.LogoScale, brand.LogoOpacity);
            filters.Add($"[0:v][1:v]scale={brand.LogoScale * 100:.0}:-1:flags=lanczos[logo];[0:v][logo]overlay={x}:{y}[logovideo]");
        }

        // Watermark text
        if (!string.IsNullOrWhiteSpace(brand.WatermarkText))
        {
            var baseVideo = filters.Count > 0 ? "[logovideo]" : "[0:v]";
            var (wx, wy) = GetTextPosition(brand.WatermarkPosition, brand.WatermarkFontSize);
            var fontSize = brand.WatermarkFontSize * 1000;
            var wmColor = ToFfmpegColor(brand.PrimaryColor, brand.WatermarkOpacity);
            // drawtext needs the fontfile on Windows — use Segoe UI
            var fontFile = @"C\:/Windows/Fonts/segoeui.ttf";
            var escapedText = brand.WatermarkText.Replace(":", "\\:").Replace("'", "\\'");
            filters.Add($"{baseVideo}drawtext=fontfile='{fontFile}':text='{escapedText}':fontsize={fontSize:.0}:fontcolor={wmColor}:x={wx}:y={wy}[wmvideo]");
        }

        // Color grading
        if (brand.EnableColorGrading && brand.ColorGradePreset != ColorGradePreset.None)
        {
            var gradeFilter = GetColorGradeFilter(brand.ColorGradePreset);
            var input = filters.Count > 0 ? "[wmvideo]" : (filters.Count > 0 ? "[logovideo]" : "[0:v]");
            if (filters.Count == 0) input = "[0:v]";
            filters.Add($"{input}{gradeFilter}[graded]");
        }

        // Build the FFmpeg command
        var sb = new System.Text.StringBuilder();
        sb.Append($"-i \"{videoPath}\" ");

        // Logo input if present
        if (!string.IsNullOrEmpty(brand.LogoPath) && File.Exists(brand.LogoPath))
            sb.Append($"-i \"{brand.LogoPath}\" ");

        var lastLabel = brand.EnableColorGrading && brand.ColorGradePreset != ColorGradePreset.None
            ? "[graded]"
            : (filters.Count > 0
                ? (filters.Last().Contains("[wmvideo]") ? "[wmvideo]" : "[logovideo]")
                : "[0:v]");

        if (filters.Count > 0)
        {
            sb.Append($"-filter_complex \"{string.Join(";", filters)}\" -map \"{lastLabel}\" -map 0:a? ");
        }

        // Encoding
        sb.Append("-c:v libx264 -preset medium -c:a copy ");
        sb.Append($"-y \"{outputPath}\"");

        await RunFfmpegAsync(sb.ToString(), progress, ct);
        progress?.Report("✓ Brand overlay applied.");
        return outputPath;
    }

    public async Task<string> ApplyBumpersAsync(
        string videoPath,
        string outputPath,
        BrandSettings? brand = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        brand ??= LoadBrand();
        var hasIntro = !string.IsNullOrEmpty(brand.IntroVideoPath) && File.Exists(brand.IntroVideoPath);
        var hasOutro = !string.IsNullOrEmpty(brand.OutroVideoPath) && File.Exists(brand.OutroVideoPath);

        if (!hasIntro && !hasOutro)
            return videoPath; // Nothing to do

        progress?.Report("Applying intro/outro bumpers…");

        var tempDir = Path.Combine(Path.GetTempPath(), "GrokVideoStudio", "branding");
        Directory.CreateDirectory(tempDir);

        var segments = new List<string>();
        if (hasIntro) segments.Add(brand.IntroVideoPath!);
        segments.Add(videoPath);
        if (hasOutro) segments.Add(brand.OutroVideoPath!);

        if (segments.Count == 1)
            return videoPath;

        // Use concat demuxer
        var listPath = Path.Combine(tempDir, $"bumpers_{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(listPath,
            segments.Select(s => $"file '{s}'"), ct);

        var args = $"-f concat -safe 0 -i \"{listPath}\" -c copy -y \"{outputPath}\"";
        try
        {
            await RunFfmpegAsync(args, progress, ct);
            progress?.Report("✓ Bumpers applied.");
            return outputPath;
        }
        finally
        {
            if (File.Exists(listPath)) try { File.Delete(listPath); } catch { }
        }
    }

    public async Task<string> ApplyFullBrandingAsync(
        string videoPath,
        string outputPath,
        BrandSettings? brand = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        brand ??= LoadBrand();
        if (!brand.ApplyToAllVideos)
        {
            if (videoPath != outputPath)
                File.Copy(videoPath, outputPath, overwrite: true);
            return outputPath;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "GrokVideoStudio", "branding");
        Directory.CreateDirectory(tempDir);

        // Step 1: Apply bumpers (intro/outro video)
        var afterBumpers = outputPath;
        if (HasBumpers(brand))
        {
            afterBumpers = Path.Combine(tempDir, $"bumpers_{Guid.NewGuid():N}.mp4");
            afterBumpers = await ApplyBumpersAsync(videoPath, afterBumpers, brand, progress, ct);
        }
        else
        {
            afterBumpers = videoPath;
        }

        // Step 2: Apply visual branding (logo, watermark, color grade)
        if (HasVisualBranding(brand))
        {
            var afterVisual = Path.Combine(tempDir, $"branded_{Guid.NewGuid():N}.mp4");
            afterVisual = await ApplyBrandingAsync(afterBumpers, afterVisual, brand, progress, ct);
            // Copy to final output
            if (afterVisual != outputPath)
                File.Copy(afterVisual, outputPath, overwrite: true);
        }
        else if (afterBumpers != outputPath)
        {
            File.Copy(afterBumpers, outputPath, overwrite: true);
        }

        return outputPath;
    }

    // ── Helpers ──

    private static bool HasVisualBranding(BrandSettings brand) =>
        (!string.IsNullOrEmpty(brand.LogoPath) && File.Exists(brand.LogoPath)) ||
        !string.IsNullOrWhiteSpace(brand.WatermarkText) ||
        (brand.EnableColorGrading && brand.ColorGradePreset != ColorGradePreset.None);

    private static bool HasBumpers(BrandSettings brand) =>
        (!string.IsNullOrEmpty(brand.IntroVideoPath) && File.Exists(brand.IntroVideoPath)) ||
        (!string.IsNullOrEmpty(brand.OutroVideoPath) && File.Exists(brand.OutroVideoPath));

    private static (string x, string y) GetOverlayPosition(LogoPosition pos, int padding, double scale, double opacity)
    {
        // FFmpeg overlay position expressions
        // scale is relative to video width, we use -1 for height to maintain aspect ratio
        return pos switch
        {
            LogoPosition.TopLeft => ($"{padding}", $"{padding}"),
            LogoPosition.TopRight => ($"W-w-{padding}", $"{padding}"),
            LogoPosition.BottomLeft => ($"{padding}", $"H-h-{padding}"),
            LogoPosition.BottomRight => ($"W-w-{padding}", $"H-h-{padding}"),
            LogoPosition.Center => ($"(W-w)/2", $"(H-h)/2"),
            _ => ($"W-w-{padding}", $"H-h-{padding}")
        };
    }

    private static (string x, string y) GetTextPosition(LogoPosition pos, double fontSize)
    {
        return pos switch
        {
            LogoPosition.TopLeft => ("10", "10"),
            LogoPosition.TopRight => ("w-tw-10", "10"),
            LogoPosition.BottomLeft => ("10", "h-th-10"),
            LogoPosition.BottomRight => ("w-tw-10", "h-th-10"),
            LogoPosition.Center => ("(w-tw)/2", "(h-th)/2"),
            _ => ("10", "h-th-10")
        };
    }

    private static string ToFfmpegColor(string hex, double opacity)
    {
        // Convert hex (#RRGGBB) to FFmpeg color with alpha (0xRRGGBB@opacity)
        var clean = hex.TrimStart('#');
        if (clean.Length == 6)
            return $"0x{clean}@{opacity:F2}";
        return $"white@{opacity:F2}";
    }

    private static string GetColorGradeFilter(ColorGradePreset preset) => preset switch
    {
        ColorGradePreset.Warm => "curves=preset=increase_red,eq=saturation=1.2:brightness=0.03",
        ColorGradePreset.Cool => "curves=preset=increase_blue,eq=saturation=0.9:brightness=-0.02",
        ColorGradePreset.Cinematic => "eq=contrast=1.2:saturation=0.85:gamma=0.95,curves=preset=darker",
        ColorGradePreset.Vibrant => "eq=saturation=1.5:contrast=1.1",
        ColorGradePreset.Vintage => "curves=preset=lighter,eq=saturation=0.7:contrast=0.9:brightness=0.05,format=gray",
        ColorGradePreset.Noir => "format=gray,eq=contrast=1.3:brightness=-0.05",
        _ => "null"
    };

    private async Task RunFfmpegAsync(string args, IProgress<string>? progress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);
        var stderr = await stderrTask;
        _ = await stdoutTask;

        if (process.ExitCode != 0)
        {
            var errMsg = stderr.Length > 500 ? stderr[..500] : stderr;
            progress?.Report($"FFmpeg error: {errMsg}");
            throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}: {errMsg}");
        }
    }
}
