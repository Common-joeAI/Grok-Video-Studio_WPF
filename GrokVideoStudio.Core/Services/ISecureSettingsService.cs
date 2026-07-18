using GrokVideoStudio.Core.Models;

namespace GrokVideoStudio.Core.Services;

/// <summary>
/// Secure settings persistence using DPAPI (Data Protection API).
/// The API key and other sensitive settings are encrypted before writing to disk,
/// ensuring secrets never appear in plaintext on the user's machine.
/// </summary>
public interface ISecureSettingsService
{
    /// <summary>Load settings from encrypted storage. Returns defaults if no file exists.</summary>
    AppSettings LoadSettings();

    /// <summary>Persist settings to encrypted storage.</summary>
    Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default);

    /// <summary>Check whether a settings file exists.</summary>
    bool SettingsExist();

    /// <summary>Delete the settings file (e.g. when clearing credentials).</summary>
    void DeleteSettings();
}
