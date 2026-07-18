using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Thread-safe activity entry representing a single log event.
/// </summary>
/// <param name="Timestamp">When the log entry was created.</param>
/// <param name="Message">The log message.</param>
/// <param name="Level">The log level.</param>
public sealed record ActivityEntry(DateTimeOffset Timestamp, string Message, LogLevel Level);

/// <summary>
/// Thread-safe interface for logging system and application events in the UI.
/// </summary>
public interface IActivityLogService
{
    /// <summary>
    /// Occurs when a new log entry is added.
    /// </summary>
    event Action<ActivityEntry>? EntryAdded;

    /// <summary>
    /// Adds a log entry with a message and log level.
    /// </summary>
    void Log(string message, LogLevel level);

    /// <summary>
    /// Gets a list of recent activity log entries.
    /// </summary>
    /// <param name="maxCount">The maximum number of entries to retrieve.</param>
    IReadOnlyList<ActivityEntry> GetRecentEntries(int maxCount = 200);
}

/// <summary>
/// Thread-safe implementation of <see cref="IActivityLogService"/> using <see cref="ConcurrentQueue{T}"/>.
/// </summary>
public sealed class ActivityLogService : IActivityLogService
{
    private readonly ConcurrentQueue<ActivityEntry> _queue = new();
    private const int MaxQueueSize = 1000;

    /// <inheritdoc />
    public event Action<ActivityEntry>? EntryAdded;

    /// <inheritdoc />
    public void Log(string message, LogLevel level)
    {
        var entry = new ActivityEntry(DateTimeOffset.UtcNow, message, level);
        _queue.Enqueue(entry);

        // Bound the queue to prevent infinite memory usage
        while (_queue.Count > MaxQueueSize)
        {
            _queue.TryDequeue(out _);
        }

        EntryAdded?.Invoke(entry);
    }

    /// <inheritdoc />
    public IReadOnlyList<ActivityEntry> GetRecentEntries(int maxCount = 200)
    {
        if (maxCount <= 0) return Array.Empty<ActivityEntry>();

        // Safely extract the last maxCount items
        var snapshot = _queue.ToArray();
        if (snapshot.Length <= maxCount)
        {
            return snapshot;
        }

        var result = new List<ActivityEntry>(maxCount);
        for (int i = snapshot.Length - maxCount; i < snapshot.Length; i++)
        {
            result.Add(snapshot[i]);
        }
        return result;
    }
}
