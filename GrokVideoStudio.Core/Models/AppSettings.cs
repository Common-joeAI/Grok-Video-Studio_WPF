using System.Text.Json.Serialization;

namespace GrokVideoStudio.Core.Models;

/// <summary>
/// Application settings — modernized to match the actual Grok-Video-Studio repo.
/// The real app (Python/PySide6) stores preferences as JSON with API keys for
/// multiple providers. We preserve this capability while adding DPAPI encryption.
/// 
/// MODERNIZATION: Original Python app stored keys in plaintext JSON.
/// This .NET version encrypts all keys via DPAPI before writing to disk.
/// </summary>
public sealed record AppSettings
{
    // ── xAI Grok API ──
    public string GrokApiKey { get; init; } = string.Empty;
    public string GrokChatModel { get; init; } = "grok-3-mini";
    public string GrokVideoModel { get; init; } = "grok-video-latest";

    // ── OpenAI API ──
    public string OpenAiApiKey { get; init; } = string.Empty;
    public string OpenAiChatModel { get; init; } = "gpt-5.1-codex";

    // ── Ollama (local) ──
    public string OllamaApiBase { get; init; } = "http://127.0.0.1:11434/v1";
    public string OllamaChatModel { get; init; } = "llama3.1:8b";

    // ── Seedance API ──
    public string SeedanceApiKey { get; init; } = string.Empty;

    // ── Social Publishing ──
    public string YouTubeApiKey { get; init; } = string.Empty;
    public string FacebookAccessToken { get; init; } = string.Empty;
    public string FacebookPageId { get; init; } = string.Empty;
    public string InstagramUserId { get; init; } = string.Empty;
    public string InstagramAccessToken { get; init; } = string.Empty;
    public string TikTokAccessToken { get; init; } = string.Empty;

    // ── Generation defaults ──
    public int DefaultDuration { get; init; } = 8;
    public string DefaultAspectRatio { get; init; } = "16:9";
    public string DefaultResolution { get; init; } = "720p";

    // ── Video pipeline ──
    public string FfmpegPath { get; init; } = "ffmpeg";
    public bool EnableCrossfade { get; init; } = true;
    public int InterpolationFps { get; init; } = 0;  // 0=off, 48 or 60
    public string UpscalePreset { get; init; } = "none";  // none, 2x, 1080p, 1440p, 4K
    public bool EnableGpuEncode { get; init; } = false;
    public string MusicMixPath { get; init; } = string.Empty;

    // ── UI ──
    public string Theme { get; init; } = "Dark";

    // ── Polling ──
    public int PollIntervalSeconds { get; init; } = 5;
    public int MaxPollAttempts { get; init; } = 120;

    // ── Download ──
    public string PublishFolder { get; init; } = "publish";
    public string VideoDownloadFolder { get; init; } = "downloads";
}
