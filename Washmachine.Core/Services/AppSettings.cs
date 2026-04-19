using System.Text.Json;
using System.Text.Json.Serialization;

namespace Washmachine.Services;

/// <summary>
/// Persisted application settings. Stored as JSON at
/// <c>%LOCALAPPDATA%/washmachine/settings.json</c> so both the GUI
/// and the CLI share the same configuration.
/// </summary>
public sealed class AppSettings
{
    // ── Session Logging ──────────────────────────────────────────

    /// <summary>Enable automatic session logging for backdoor operations.</summary>
    [JsonPropertyName("sessionLoggingEnabled")]
    public bool SessionLoggingEnabled { get; set; } = true;

    /// <summary>Copy the backdoored binary into the session folder.</summary>
    [JsonPropertyName("saveBinaryArtifact")]
    public bool SaveBinaryArtifact { get; set; } = true;

    /// <summary>Copy the original shellcode .bin into the session folder.</summary>
    [JsonPropertyName("saveShellcodeCopy")]
    public bool SaveShellcodeCopy { get; set; } = true;

    /// <summary>Include debug-level messages in the session log file.</summary>
    [JsonPropertyName("verboseFileLogging")]
    public bool VerboseFileLogging { get; set; } = false;

    // ── Build artifacts ──────────────────────────────────────────

    /// <summary>
    /// When true, retain the timestamp+hash-named build artifact copies under
    /// <c>temp/cpp/compiled/</c> across builds (default: false — wipe after each build
    /// so the directory does not accumulate stale binaries).
    /// </summary>
    [JsonPropertyName("keepBuildArtifacts")]
    public bool KeepBuildArtifacts { get; set; } = false;
}

/// <summary>
/// Reads and writes <see cref="AppSettings"/> to a JSON file on disk.
/// Thread-safe for typical single-writer usage (GUI or CLI).
/// </summary>
public static class AppSettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "washmachine");

    private static readonly string SettingsFile =
        Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>Path to the JSON settings file on disk.</summary>
    public static string FilePath => SettingsFile;

    /// <summary>
    /// Load settings from disk. Returns defaults if the file is missing
    /// or malformed (never throws).
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return new AppSettings();

            var json = File.ReadAllText(SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Persist settings to disk. Creates the directory if needed.
    /// </summary>
    public static void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, JsonOpts);
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // Silently ignore write failures (read-only FS, permissions, etc.)
        }
    }
}
