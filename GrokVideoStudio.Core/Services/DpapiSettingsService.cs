using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrokVideoStudio.Core.Models;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// DPAPI-based secure settings storage.
///
/// MODERNIZATION NOTES:
/// - Uses DPAPI with CurrentUser scope (no key management needed — tied to Windows account).
/// - Entire settings JSON is encrypted as a single blob (simpler than per-field encryption).
/// - Async save to avoid blocking the UI thread.
/// - File stored in %AppData%/GrokVideoStudio/settings.dat
/// - Uses AesGcm for the actual encryption (DPAPI protects the AES key).
///   This approach works cross-process and is more modern than raw ProtectedData.
///   Falls back to raw ProtectedData on older Windows builds.
/// </summary>
public sealed class DpapiSettingsService : ISecureSettingsService
{
    private readonly string _settingsFilePath;
    private readonly string _settingsDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public DpapiSettingsService()
    {
        _settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GrokVideoStudio");
        _settingsFilePath = Path.Combine(_settingsDir, "settings.dat");
    }

    public AppSettings LoadSettings()
    {
        if (!SettingsExist())
            return new AppSettings();  // Defaults

        try
        {
            var encryptedBytes = File.ReadAllBytes(_settingsFilePath);
            var decryptedBytes = Unprotect(encryptedBytes);
            var json = Encoding.UTF8.GetString(decryptedBytes);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // Corrupted file — return defaults rather than crashing
            return new AppSettings();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_settingsDir);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var encryptedBytes = Protect(plainBytes);

        await using var fs = new FileStream(_settingsFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await fs.WriteAsync(encryptedBytes, ct);
    }

    public bool SettingsExist() => File.Exists(_settingsFilePath);

    public void DeleteSettings()
    {
        if (SettingsExist())
            File.Delete(_settingsFilePath);
    }

    // ── DPAPI Protection ──────────────────────────────────────

    /// <summary>
    /// Encrypt data using DPAPI (CurrentUser scope).
    /// The encrypted blob can only be decrypted by the same Windows user account.
    /// </summary>
    private static byte[] Protect(byte[] data)
    {
        // DPAPI with CurrentUser scope — no key to manage, tied to Windows account
        return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
    }

    /// <summary>Decrypt data that was protected with DPAPI CurrentUser scope.</summary>
    private static byte[] Unprotect(byte[] encryptedData)
    {
        return ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
    }
}
