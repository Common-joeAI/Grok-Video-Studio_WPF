using System.Text.Json.Serialization;

namespace GrokVideoStudio.Core.Models;

/// <summary>
/// Request payload for video generation.
/// Unified across providers (Grok Imagine, Sora 2, Seedance) — each
/// service implementation maps to its provider-specific format.
/// </summary>
public sealed record VideoGenerationRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "grok-imagine-video";

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    /// <summary>Optional source image for image-to-video (base64 data URI or URL).</summary>
    [JsonPropertyName("image")]
    public ImageSource? Image { get; init; }

    [JsonPropertyName("duration")]
    public int Duration { get; init; } = 8;

    [JsonPropertyName("aspect_ratio")]
    public string AspectRatio { get; init; } = "16:9";

    [JsonPropertyName("resolution")]
    public string Resolution { get; init; } = "720p";

    /// <summary>Which provider to use.</summary>
    [JsonIgnore]
    public VideoProvider Provider { get; init; } = VideoProvider.GrokImagine;
}

public sealed record ImageSource
{
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;
}

/// <summary>
/// Unified poll response across providers.
/// </summary>
public sealed record VideoPollResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "pending";

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("video")]
    public VideoResult? Video { get; init; }

    [JsonPropertyName("error")]
    public ApiError? Error { get; init; }

    [JsonIgnore]
    public bool IsDone => Status.Equals("done", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsTerminal => Status.Equals("done", StringComparison.OrdinalIgnoreCase)
                           || Status.Equals("failed", StringComparison.OrdinalIgnoreCase)
                           || Status.Equals("expired", StringComparison.OrdinalIgnoreCase);
}

public sealed record VideoResult
{
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("duration")]
    public double Duration { get; init; }

    [JsonPropertyName("respect_moderation")]
    public bool RespectModeration { get; init; }
}

public sealed record ApiError
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }
}

/// <summary>
/// Response from the start endpoint (request_id).
/// </summary>
public sealed record VideoGenerationStartResponse
{
    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = string.Empty;
}
