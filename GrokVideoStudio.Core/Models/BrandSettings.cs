using System.Text.Json.Serialization;

namespace GrokVideoStudio.Core.Models;

/// <summary>
/// Brand identity settings — applied to every generated/stitched video.
/// Includes logo overlay, watermark, color grading, and audio identity.
/// </summary>
public sealed record BrandSettings
{
    /// <summary>Brand/display name.</summary>
    public string BrandName { get; init; } = string.Empty;

    // ── Colors ──
    /// <summary>Primary brand color (hex, e.g. "#FF6B35").</summary>
    public string PrimaryColor { get; init; } = "#FFFFFF";

    /// <summary>Secondary/accent color (hex).</summary>
    public string SecondaryColor { get; init; } = "#FFFFFF";

    /// <summary>Background color for text overlays (hex).</summary>
    public string BackgroundColor { get; init; } = "#000000";

    // ── Logo / Iconography ──
    /// <summary>Path to logo/icon image file (PNG with transparency recommended).</summary>
    public string? LogoPath { get; init; }

    /// <summary>Where to place the logo on the video.</summary>
    public LogoPosition LogoPosition { get; init; } = LogoPosition.BottomRight;

    /// <summary>Logo opacity (0.0–1.0).</summary>
    public double LogoOpacity { get; init; } = 0.8;

    /// <summary>Logo scale relative to video width (e.g. 0.15 = 15% of video width).</summary>
    public double LogoScale { get; init; } = 0.15;

    /// <summary>Padding from the edge in pixels.</summary>
    public int LogoPadding { get; init; } = 20;

    // ── Watermark ──
    /// <summary>Text watermark (e.g. brand name or URL).</summary>
    public string WatermarkText { get; init; } = string.Empty;

    /// <summary>Watermark position.</summary>
    public LogoPosition WatermarkPosition { get; init; } = LogoPosition.BottomLeft;

    /// <summary>Watermark font size relative to video height (e.g. 0.03 = 3%).</summary>
    public double WatermarkFontSize { get; init; } = 0.03;

    /// <summary>Watermark opacity (0.0–1.0).</summary>
    public double WatermarkOpacity { get; init; } = 0.5;

    // ── Color Grading ──
    /// <summary>Enable color grading preset.</summary>
    public bool EnableColorGrading { get; init; }

    /// <summary>Color grade preset name.</summary>
    public ColorGradePreset ColorGradePreset { get; init; } = ColorGradePreset.None;

    // ── Audio Identity ──
    /// <summary>Audio clip to prepend as intro (e.g. brand sting/swoosh).</summary>
    public string? IntroAudioPath { get; init; }

    /// <summary>Audio clip to append as outro.</summary>
    public string? OutroAudioPath { get; init; }

    /// <summary>Default background music track.</summary>
    public string? BackgroundMusicPath { get; init; }

    /// <summary>Music volume relative to video audio (0.0–1.0).</summary>
    public double MusicVolume { get; init; } = 0.3;

    // ── Intro/Outro Video ──
    /// <summary>Video to prepend as a branded intro bumper.</summary>
    public string? IntroVideoPath { get; init; }

    /// <summary>Video to append as a branded outro bumper.</summary>
    public string? OutroVideoPath { get; init; }

    // ── Master Toggle ──
    /// <summary>If true, branding is applied to all generated videos.</summary>
    public bool ApplyToAllVideos { get; init; } = true;
}

/// <summary>Logo/watermark position on the video frame.</summary>
public enum LogoPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Center
}

/// <summary>Color grading presets (FFmpeg filter names).</summary>
public enum ColorGradePreset
{
    None,
    Warm,
    Cool,
    Cinematic,
    Vibrant,
    Vintage,
    Noir
}
