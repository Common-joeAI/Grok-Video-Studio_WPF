using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

// Note: This service requires the 'Microsoft.Data.Sqlite' NuGet package (and 'Microsoft.Data.Sqlite.Core'),
// which is already referenced in GrokVideoStudio.Core.csproj.

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Interface for recording and retrieving usage statistics such as video generations and social uploads.
/// </summary>
public interface IUsageStatsService
{
    /// <summary>
    /// Records a video generation attempt.
    /// </summary>
    /// <param name="provider">The video generator provider name (e.g. GrokImagine, OpenAiSora, Seedance).</param>
    /// <param name="model">The model name used for generation.</param>
    /// <param name="duration">The requested duration of the video in seconds.</param>
    /// <param name="success">Whether the generation completed successfully.</param>
    Task RecordGenerationAsync(string provider, string model, int duration, bool success);

    /// <summary>
    /// Records a video upload attempt.
    /// </summary>
    /// <param name="platform">The target platform name (e.g. YouTube, TikTok, Facebook, Instagram).</param>
    /// <param name="success">Whether the upload completed successfully.</param>
    Task RecordUploadAsync(string platform, bool success);

    /// <summary>
    /// Gets aggregated video generation statistics (e.g., total count, successful, failed, total duration, and provider-specific counts).
    /// </summary>
    /// <returns>A dictionary of metric names and their integer values.</returns>
    Task<Dictionary<string, int>> GetGenerationStatsAsync();
}

/// <summary>
/// SQLite-backed implementation of <see cref="IUsageStatsService"/>.
/// Stores usage records in %LocalAppData%/GrokVideoStudio/usage_stats.db.
/// </summary>
public sealed class UsageStatsService : IUsageStatsService
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="UsageStatsService"/> class.
    /// Creates the database directory and tables if they do not exist.
    /// </summary>
    public UsageStatsService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbDir = Path.Combine(localAppData, "GrokVideoStudio");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "usage_stats.db");
        _connectionString = $"Data Source={dbPath}";

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = 
        """
        CREATE TABLE IF NOT EXISTS generations (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            provider TEXT NOT NULL,
            model TEXT NOT NULL,
            duration INTEGER NOT NULL,
            success INTEGER NOT NULL,
            timestamp TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS uploads (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            platform TEXT NOT NULL,
            success INTEGER NOT NULL,
            timestamp TEXT NOT NULL
        );
        """;
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public async Task RecordGenerationAsync(string provider, string model, int duration, bool success)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = 
        """
        INSERT INTO generations (provider, model, duration, success, timestamp)
        VALUES ($provider, $model, $duration, $success, $timestamp);
        """;
        command.Parameters.AddWithValue("$provider", provider);
        command.Parameters.AddWithValue("$model", model);
        command.Parameters.AddWithValue("$duration", duration);
        command.Parameters.AddWithValue("$success", success ? 1 : 0);
        command.Parameters.AddWithValue("$timestamp", DateTimeOffset.UtcNow.ToString("o"));

        await command.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async Task RecordUploadAsync(string platform, bool success)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = 
        """
        INSERT INTO uploads (platform, success, timestamp)
        VALUES ($platform, $success, $timestamp);
        """;
        command.Parameters.AddWithValue("$platform", platform);
        command.Parameters.AddWithValue("$success", success ? 1 : 0);
        command.Parameters.AddWithValue("$timestamp", DateTimeOffset.UtcNow.ToString("o"));

        await command.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, int>> GetGenerationStatsAsync()
    {
        var stats = new Dictionary<string, int>
        {
            ["TotalGenerations"] = 0,
            ["SuccessfulGenerations"] = 0,
            ["FailedGenerations"] = 0,
            ["TotalDurationSeconds"] = 0
        };

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Query main generation metrics
        using (var command = connection.CreateCommand())
        {
            command.CommandText = 
            """
            SELECT 
                COUNT(*) as total,
                SUM(CASE WHEN success = 1 THEN 1 ELSE 0 END) as successful,
                SUM(CASE WHEN success = 0 THEN 1 ELSE 0 END) as failed,
                SUM(duration) as total_duration
            FROM generations;
            """;

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                stats["TotalGenerations"] = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                stats["SuccessfulGenerations"] = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                stats["FailedGenerations"] = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                stats["TotalDurationSeconds"] = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            }
        }

        // Query provider-specific metrics
        using (var providerCommand = connection.CreateCommand())
        {
            providerCommand.CommandText = "SELECT provider, COUNT(*) FROM generations GROUP BY provider;";
            using var providerReader = await providerCommand.ExecuteReaderAsync();
            while (await providerReader.ReadAsync())
            {
                var provider = providerReader.GetString(0);
                var count = providerReader.GetInt32(1);
                stats[$"Provider_{provider}_Count"] = count;
            }
        }

        // Query platform-specific upload metrics
        using (var uploadCommand = connection.CreateCommand())
        {
            uploadCommand.CommandText = 
            """
            SELECT 
                COUNT(*) as total_uploads,
                SUM(CASE WHEN success = 1 THEN 1 ELSE 0 END) as successful_uploads
            FROM uploads;
            """;

            using var reader = await uploadCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                stats["TotalUploads"] = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                stats["SuccessfulUploads"] = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            }
        }

        return stats;
    }
}
