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
    public string GrokVideoModel { get; init; } = "grok-imagine-video";

    // ── OpenAI API ──
    public string OpenAiApiKey { get; init; } = string.Empty;
    public string OpenAiChatModel { get; init; } = "gpt-5.1-codex";

    // ── Ollama (local) ──
    public string OllamaApiBase { get; init; } = "http://127.0.0.1:11434/v1";
    public string OllamaChatModel { get; init; } = "llama3.1:8b";

    // ── Seedance API ──
    public string SeedanceApiKey { get; init; } = string.Empty;

    // ── Local GPU Server ──
    public string LocalServerUrl { get; init; } = "http://localhost:7860";

    // ── Vast.ai Cloud GPU ──
    public string VastApiKey { get; init; } = string.Empty;
    public string VastGpuTier { get; init; } = "4090";  // 4090, A100, H100, Auto

    // ── Social Publishing: OAuth Client Credentials ──
    // YouTube uses a client_secrets.json file path (Google's convention)
    public string YouTubeApiKey { get; init; } = string.Empty;

    // Facebook OAuth app credentials
    public string FacebookClientId { get; init; } = string.Empty;
    public string FacebookClientSecret { get; init; } = string.Empty;

    // Instagram OAuth app credentials (Meta/Facebook app, but separate ID for IG)
    public string InstagramClientId { get; init; } = string.Empty;
    public string InstagramClientSecret { get; init; } = string.Empty;

    // TikTok OAuth app credentials
    public string TikTokClientKey { get; init; } = string.Empty;
    public string TikTokClientSecret { get; init; } = string.Empty;

    // ── Social Publishing: OAuth Access Tokens (stored after successful auth) ──
    public string FacebookAccessToken { get; init; } = string.Empty;
    public string FacebookPageId { get; init; } = string.Empty;
    public string InstagramUserId { get; init; } = string.Empty;
    public string InstagramAccessToken { get; init; } = string.Empty;
    public string TikTokAccessToken { get; init; } = string.Empty;
    public string TikTokOpenId { get; init; } = string.Empty;

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
