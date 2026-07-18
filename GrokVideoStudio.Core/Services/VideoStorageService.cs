using System.Text.Json;
using GrokVideoStudio.Core.Models;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// JSON-file-based video history storage.
/// Stores all VideoItems in a single JSON file in %LocalAppData%/GrokVideoStudio/history.json.
///
/// MODERNIZATION NOTES:
/// - Async file I/O throughout (no blocking).
/// - Entire list loaded/saved atomically (simple, fine for typical desktop usage volumes).
/// - Easy to swap for SQLite later by implementing IVideoStorageService differently.
/// </summary>
public sealed class VideoStorageService : IVideoStorageService
{
    private readonly string _historyFilePath;
    private readonly string _historyDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public VideoStorageService()
    {
        _historyDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GrokVideoStudio");
        _historyFilePath = Path.Combine(_historyDir, "history.json");
    }

    public async Task<IReadOnlyList<VideoItem>> LoadVideosAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_historyFilePath))
            return Array.Empty<VideoItem>();

        try
        {
            await using var fs = File.OpenRead(_historyFilePath);
            var items = await JsonSerializer.DeserializeAsync<List<VideoItem>>(fs, JsonOptions, ct);
            return items?.OrderByDescending(v => v.CreatedAt).ToList() ?? new List<VideoItem>();
        }
        catch
        {
            return Array.Empty<VideoItem>();
        }
    }

    public async Task SaveVideoAsync(VideoItem item, CancellationToken ct = default)
    {
        var videos = (await LoadVideosAsync(ct)).ToList();

        // Replace if exists, otherwise add
        var existingIdx = videos.FindIndex(v => v.Id == item.Id);
        if (existingIdx >= 0)
            videos[existingIdx] = item;
        else
            videos.Insert(0, item);

        await WriteHistoryAsync(videos, ct);
    }

    public async Task DeleteVideoAsync(Guid id, CancellationToken ct = default)
    {
        var videos = (await LoadVideosAsync(ct))
            .Where(v => v.Id != id)
            .ToList();
        await WriteHistoryAsync(videos, ct);
    }

    public async Task UpdateVideoAsync(VideoItem item, CancellationToken ct = default)
    {
        var videos = (await LoadVideosAsync(ct)).ToList();
        var idx = videos.FindIndex(v => v.Id == item.Id);
        if (idx >= 0)
        {
            videos[idx] = item;
            await WriteHistoryAsync(videos, ct);
        }
        else
        {
            // Not found — just save as new
            await SaveVideoAsync(item, ct);
        }
    }

    private async Task WriteHistoryAsync(List<VideoItem> videos, CancellationToken ct)
    {
        Directory.CreateDirectory(_historyDir);
        await using var fs = new FileStream(_historyFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(fs, videos, JsonOptions, ct);
    }
}
