using GrokVideoStudio.Core.Models;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Manages the local video history — a JSON-based store of all VideoItems.
/// Supports listing, adding, and updating video generation records.
/// This replaces any in-memory-only history and persists across app restarts.
/// </summary>
public interface IVideoStorageService
{
    /// <summary>Load all video items from local storage (newest first).</summary>
    Task<IReadOnlyList<VideoItem>> LoadVideosAsync(CancellationToken ct = default);

    /// <summary>Add or update a video item.</summary>
    Task SaveVideoAsync(VideoItem item, CancellationToken ct = default);

    /// <summary>Delete a video item by ID.</summary>
    Task DeleteVideoAsync(Guid id, CancellationToken ct = default);

    /// <summary>Update an existing video item (e.g. after download completes).</summary>
    Task UpdateVideoAsync(VideoItem item, CancellationToken ct = default);
}
