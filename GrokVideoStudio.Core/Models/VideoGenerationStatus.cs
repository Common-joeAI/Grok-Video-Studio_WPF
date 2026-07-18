namespace GrokVideoStudio.Core.Models;

/// <summary>
/// Supported video generation providers.
/// The actual repo supports: Grok Imagine API, OpenAI Sora 2, and Seedance 2.0.
/// </summary>
public enum VideoProvider
{
    GrokImagine,
    OpenAiSora,
    Seedance
}

/// <summary>
/// Supported prompt generation sources.
/// The actual repo supports: Grok API, OpenAI API, and local Ollama.
/// </summary>
public enum PromptSource
{
    Manual,     // User writes prompt directly
    GrokApi,    // Generate via xAI Grok chat API
    OpenAiApi,  // Generate via OpenAI chat API
    Ollama      // Generate via local Ollama
}

/// <summary>
/// Supported social publishing platforms.
/// </summary>
public enum SocialPlatform
{
    YouTube,
    Folder,
    TikTok,
    Facebook,
    Instagram
}

/// <summary>
/// Video generation status lifecycle.
/// Maps to the xAI/Grok poll states plus local UI states.
/// </summary>
public enum VideoGenerationStatus
{
    Idle,
    Submitting,
    Polling,
    Completed,
    Failed,
    Expired,
    Cancelled
}

/// <summary>
/// Stitch pipeline options for FFmpeg processing.
/// Matches the actual repo's stitch features.
/// </summary>
public sealed record StitchOptions
{
    public bool EnableCrossfade { get; init; } = true;
    public int InterpolationFps { get; init; } = 0;  // 0=off, 48 or 60
    public string UpscalePreset { get; init; } = "none";
    public bool EnableGpuEncode { get; init; } = false;
    public string? MusicMixPath { get; init; }
}
