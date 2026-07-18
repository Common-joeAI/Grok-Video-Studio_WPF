using GrokVideoStudio.Core.Models;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Unified video generation service interface supporting multiple providers.
/// Each provider (Grok, Sora, Seedance) implements this interface.
/// 
/// This matches the actual repo's multi-provider architecture where the Python
/// app calls different APIs based on the selected video provider.
/// </summary>
public interface IVideoGenerationService
{
    /// <summary>Provider this service handles.</summary>
    VideoProvider Provider { get; }

    /// <summary>Start a generation and poll until terminal state.</summary>
    Task<VideoPollResponse> GenerateAsync(
        VideoGenerationRequest request,
        string apiKey,
        int pollIntervalSeconds = 5,
        int maxAttempts = 120,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    /// <summary>Download a completed video to local disk.</summary>
    Task<string> DownloadVideoAsync(string videoUrl, string destinationPath, CancellationToken ct = default);
}

/// <summary>
/// Factory for resolving the correct video generation service by provider.
/// </summary>
public interface IVideoGenerationFactory
{
    IVideoGenerationService GetService(VideoProvider provider);
}
