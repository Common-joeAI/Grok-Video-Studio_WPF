using System;

namespace GrokVideoStudio.Core.Models;

/// <summary>
/// A completed or in-progress video generation record.
/// Stored locally to provide history / gallery functionality.
/// </summary>
public sealed record VideoItem
{
    /// <summary>Unique identifier for the video item.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The prompt used for this generation.</summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>Model used (e.g. grok-imagine-video, sora-2, seedance-2.0).</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Duration in seconds requested.</summary>
    public int Duration { get; init; }

    /// <summary>Resolution requested.</summary>
    public string Resolution { get; init; } = string.Empty;

    /// <summary>Aspect ratio requested.</summary>
    public string AspectRatio { get; init; } = string.Empty;

    /// <summary>Source image path if image-to-video, null for text-to-video.</summary>
    public string? SourceImagePath { get; init; }

    /// <summary>Generation request ID returned by the provider API.</summary>
    public string? RequestId { get; init; }

    /// <summary>Remote video URL (temporary).</summary>
    public string? VideoUrl { get; init; }

    /// <summary>Local file path after download (if downloaded).</summary>
    public string? LocalFilePath { get; init; }

    /// <summary>Local path to the generated thumbnail PNG.</summary>
    public string? ThumbnailPath { get; init; }

    /// <summary>Status of this generation.</summary>
    public VideoGenerationStatus Status { get; init; }

    /// <summary>The provider that generated this video.</summary>
    public VideoProvider SourceProvider { get; init; }

    /// <summary>The prompt source that was used.</summary>
    public PromptSource PromptSourceName { get; init; }

    /// <summary>Error message if the generation failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>When the request was submitted.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When the video became available.</summary>
    public DateTimeOffset? CompletedAt { get; init; }
}
