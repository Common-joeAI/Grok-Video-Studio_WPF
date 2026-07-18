using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrokVideoStudio.Core.Models;
using Microsoft.Extensions.Logging;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// DPAPI-based secure settings storage.
/// Uses DPAPI with CurrentUser scope — encrypted blob can only be decrypted
/// by the same Windows user account. File stored in %AppData%/GrokVideoStudio/settings.dat
/// </summary>
public sealed class DpapiSettingsService : ISecureSettingsService
{
    private readonly string _settingsFilePath;
    private readonly string _settingsDir;
    private readonly ILogger<DpapiSettingsService> _logger;
    private readonly IActivityLogService _activityLog;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public DpapiSettingsService(ILogger<DpapiSettingsService> logger, IActivityLogService activityLog)
    {
        _logger = logger;
        _activityLog = activityLog;
        _settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GrokVideoStudio");
        _settingsFilePath = Path.Combine(_settingsDir, "settings.dat");
        _logger.LogInformation("Settings file path: {Path}", _settingsFilePath);
    }

    public AppSettings LoadSettings()
    {
        if (!SettingsExist())
        {
            _logger.LogInformation("No settings file found, returning defaults.");
            return new AppSettings();
        }

        try
        {
            var encryptedBytes = File.ReadAllBytes(_settingsFilePath);
            _logger.LogInformation("Settings file found, {Bytes} bytes, decrypting...", encryptedBytes.Length);
            
            var decryptedBytes = Unprotect(encryptedBytes);
            var json = Encoding.UTF8.GetString(decryptedBytes);
            _logger.LogDebug("Decrypted settings JSON: {Json}", json);
            
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null)
            {
                _logger.LogError("Deserialization returned null, returning defaults.");
                return new AppSettings();
            }
            
            _logger.LogInformation("Settings loaded successfully. GrokApiKey present: {HasGrok}, OpenAi present: {HasOpenAi}",
                !string.IsNullOrEmpty(settings.GrokApiKey),
                !string.IsNullOrEmpty(settings.OpenAiApiKey));
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings from {Path}. Returning defaults.", _settingsFilePath);
            _activityLog.Log($"Settings load FAILED: {ex.Message}", LogLevel.Error);
            return new AppSettings();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(_settingsDir);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var encryptedBytes = Protect(plainBytes);

            await using var fs = new FileStream(_settingsFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await fs.WriteAsync(encryptedBytes, ct);

            _logger.LogInformation("Settings saved successfully to {Path} ({Bytes} bytes encrypted).",
                _settingsFilePath, encryptedBytes.Length);
            _activityLog.Log($"Settings saved to disk ({encryptedBytes.Length} bytes encrypted)", LogLevel.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}.", _settingsFilePath);
            _activityLog.Log($"Settings save FAILED: {ex.Message}", LogLevel.Error);
            throw;  // Re-throw so the ViewModel's catch block can show the error to the user
        }
    }

    public bool SettingsExist() => File.Exists(_settingsFilePath);

    public void DeleteSettings()
    {
        if (SettingsExist())
            File.Delete(_settingsFilePath);
    }

    // ── DPAPI Protection ──────────────────────────────────────

    private static byte[] Protect(byte[] data)
    {
        return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
    }

    private static byte[] Unprotect(byte[] encryptedData)
    {
        return ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
    }
}
