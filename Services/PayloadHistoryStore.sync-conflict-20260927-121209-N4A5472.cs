using System.Text.Json;
using System.Text.Json.Serialization;

namespace Washmachine.Services;

public sealed class PayloadHistoryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("generatedAtUtc")]
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("sourceFilePath")]
    public string SourceFilePath { get; set; } = string.Empty;

    [JsonPropertyName("sourceFileSizeBytes")]
    public long SourceFileSizeBytes { get; set; }

    [JsonPropertyName("sourceFileSha256")]
    public string SourceFileSha256 { get; set; } = string.Empty;

    [JsonPropertyName("encoderIndex")]
    public int EncoderIndex { get; set; }

    [JsonPropertyName("encoderName")]
    public string EncoderName { get; set; } = string.Empty;

    [JsonPropertyName("encoderDescription")]
    public string EncoderDescription { get; set; } = string.Empty;

    [JsonPropertyName("envelopeIndex")]
    public int EnvelopeIndex { get; set; }

    [JsonPropertyName("envelopeName")]
    public string EnvelopeName { get; set; } = string.Empty;

    [JsonPropertyName("envelopeDescription")]
    public string EnvelopeDescription { get; set; } = string.Empty;

    [JsonPropertyName("webHelperIndex")]
    public int WebHelperIndex { get; set; }

    [JsonPropertyName("webHelperName")]
    public string WebHelperName { get; set; } = string.Empty;

    [JsonPropertyName("webHelperDescription")]
    public string WebHelperDescription { get; set; } = string.Empty;

    [JsonPropertyName("payloadUrl")]
    public string PayloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("payloadLengthBytes")]
    public int PayloadLengthBytes { get; set; }

    [JsonPropertyName("payloadChecksum")]
    public string PayloadChecksum { get; set; } = string.Empty;

    [JsonPropertyName("bin2shellCommandLine")]
    public string Bin2ShellCommandLine { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public string Payload { get; set; } = string.Empty;

    [JsonPropertyName("cppIncludes")]
    public string CppIncludes { get; set; } = string.Empty;

    [JsonPropertyName("cppDeclarations")]
    public string CppDeclarations { get; set; } = string.Empty;

    [JsonPropertyName("cppWebFetch")]
    public string CppWebFetch { get; set; } = string.Empty;

    [JsonPropertyName("cppPayloadInit")]
    public string CppPayloadInit { get; set; } = string.Empty;

    [JsonPropertyName("cppDecode")]
    public string CppDecode { get; set; } = string.Empty;

    [JsonPropertyName("cppPreamble")]
    public string CppPreamble { get; set; } = string.Empty;

    [JsonPropertyName("cppBody")]
    public string CppBody { get; set; } = string.Empty;

    // ── Session-folder fields (not serialised, populated at load time) ──────────

    /// <summary>Full path to the session directory (set when loaded from logging folder).</summary>
    [JsonIgnore]
    public string? SessionDir { get; set; }

    /// <summary>"session" for compilation sessions, "backdoor" for backdoor sessions, "" for JSON history.</summary>
    [JsonIgnore]
    public string SessionType { get; set; } = string.Empty;

    /// <summary>Success flag read from session_summary.json.</summary>
    [JsonIgnore]
    public bool? SessionSuccess { get; set; }

    /// <summary>Template display name from session_summary.json (compilation sessions only).</summary>
    [JsonIgnore]
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>Output path from session_summary.json.</summary>
    [JsonIgnore]
    public string SessionOutputPath { get; set; } = string.Empty;

    /// <summary>Shellcode / source path from session_summary.json.</summary>
    [JsonIgnore]
    public string SessionShellcodeSource { get; set; } = string.Empty;

    /// <summary>True when this entry was loaded from a session folder rather than the legacy JSON history.</summary>
    [JsonIgnore]
    public bool IsSessionEntry => !string.IsNullOrEmpty(SessionDir);

    /// <summary>"Compilation", "Backdoor", or "" for legacy entries.</summary>
    [JsonIgnore]
    public string SessionTypeDisplay => SessionType switch
    {
        "session"  => "Compilation",
        "backdoor" => "Backdoor",
        _          => string.Empty,
    };

    [JsonIgnore]
    public string SourceFileName => Path.GetFileName(SourceFilePath ?? string.Empty);

    [JsonIgnore]
    public string DisplaySourceName
    {
        get
        {
            if (IsSessionEntry)
            {
                if (!string.IsNullOrWhiteSpace(TemplateName)) return TemplateName;
                var src = Path.GetFileName(SessionShellcodeSource);
                if (!string.IsNullOrWhiteSpace(src)) return src;
                return SessionTypeDisplay;
            }
            return string.IsNullOrWhiteSpace(SourceFileName) ? "(unknown source)" : SourceFileName;
        }
    }

    [JsonIgnore]
    public string DisplayTimestamp => GeneratedAtUtc == default
        ? "unknown time"
        : GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    [JsonIgnore]
    public string DayText => GeneratedAtUtc == default
        ? "—"
        : GeneratedAtUtc.ToLocalTime().Day.ToString();

    [JsonIgnore]
    public string MonthText => GeneratedAtUtc == default
        ? "—"
        : GeneratedAtUtc.ToLocalTime().ToString("MMM").ToUpper();

    [JsonIgnore]
    public string TimeText => GeneratedAtUtc == default
        ? ""
        : GeneratedAtUtc.ToLocalTime().ToString("HH:mm");

    [JsonIgnore]
    public string EncoderTag => string.IsNullOrWhiteSpace(EncoderName)
        ? $"enc:{EncoderIndex}"
        : EncoderName;

    [JsonIgnore]
    public string EnvelopeTag => string.IsNullOrWhiteSpace(EnvelopeName)
        ? $"env:{EnvelopeIndex}"
        : EnvelopeName;

    [JsonIgnore]
    public string WebHelperTag => string.IsNullOrWhiteSpace(WebHelperName)
        ? $"wh:{WebHelperIndex}"
        : WebHelperName;

    [JsonIgnore]
    public string SourceSizeDisplay => SourceFileSizeBytes > 0
        ? FormatBytes(SourceFileSizeBytes)
        : string.Empty;

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes:N0} B";
        double v = bytes;
        string[] units = ["B", "KB", "MB", "GB"];
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:N1} {units[i]}";
    }

    // Legacy display helpers kept for backwards compat with any serialised references
    [JsonIgnore]
    public string DisplayTitle => $"{DisplayTimestamp}  |  {PayloadUrl}";

    [JsonIgnore]
    public string DisplaySubtitle =>
        $"{DisplaySourceName}  •  enc {EncoderIndex} / env {EnvelopeIndex} / wh {WebHelperIndex}";
}

public static class PayloadHistoryStore
{
    private static readonly object SyncRoot = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly string HistoryDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "washmachine");

    private static readonly string HistoryFile =
        Path.Combine(HistoryDirectory, "payload-history.json");

    // Session directories are under the app's executable directory
    private static readonly string LoggingDirectory =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logging");

    public static string FilePath => HistoryFile;
    public static string LoggingPath => LoggingDirectory;

    /// <summary>
    /// Loads all history: session folders (newest-first) plus legacy JSON entries,
    /// with duplicates removed (session entries take precedence over JSON entries
    /// for the same timestamp).
    /// </summary>
    public static IReadOnlyList<PayloadHistoryEntry> LoadAll()
    {
        lock (SyncRoot)
        {
            var sessions = LoadFromSessions();
            var json = LoadInternal();

            // Merge: sessions + json (sessions take priority; no de-dup needed since they
            // come from different sources)
            var all = sessions.Concat(json)
                .OrderByDescending(e => e.GeneratedAtUtc)
                .ToList();

            return all;
        }
    }

    /// <summary>Legacy load — returns only JSON-stored entries (sorted newest-first).</summary>
    public static IReadOnlyList<PayloadHistoryEntry> Load()
    {
        lock (SyncRoot)
        {
            return LoadInternal()
                .OrderByDescending(entry => entry.GeneratedAtUtc)
                .ToList();
        }
    }

    /// <summary>Scans the logging/ directory and returns one entry per session folder.</summary>
    private static List<PayloadHistoryEntry> LoadFromSessions()
    {
        var results = new List<PayloadHistoryEntry>();

        if (!Directory.Exists(LoggingDirectory))
            return results;

        try
        {
            foreach (var dir in Directory.GetDirectories(LoggingDirectory))
            {
                var entry = ParseSessionFolder(dir);
                if (entry != null)
                    results.Add(entry);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return results;
    }

    /// <summary>
    /// Parses a session folder into a <see cref="PayloadHistoryEntry"/>.
    /// Returns null if the folder doesn't look like a washmachine session.
    /// </summary>
    private static PayloadHistoryEntry? ParseSessionFolder(string dir)
    {
        try
        {
            var folderName = Path.GetFileName(dir);
            // Expected format: session_YYYYMMDD_HHMMSS_<slug>
            //                  backdoor_YYYYMMDD_HHMMSS_<guid>
            var parts = folderName.Split('_', 4);
            if (parts.Length < 3) return null;

            var sessionType = parts[0]; // "session" or "backdoor"
            if (sessionType != "session" && sessionType != "backdoor") return null;

            // Parse date + time from parts[1] and parts[2]
            DateTime generatedAt = default;
            if (parts[1].Length == 8 && parts[2].Length == 6 &&
                DateTime.TryParseExact(
                    parts[1] + parts[2],
                    "yyyyMMddHHmmss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeLocal,
                    out var parsed))
            {
                generatedAt = parsed.ToUniversalTime();
            }

            var entry = new PayloadHistoryEntry
            {
                Id = folderName,
                GeneratedAtUtc = generatedAt,
                SessionDir = dir,
                SessionType = sessionType,
            };

            // Read session_summary.json (written by both CompilerService and CLI backdoor)
            var summaryPath = Path.Combine(dir, "session_summary.json");
            if (File.Exists(summaryPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(summaryPath));
                    var root = doc.RootElement;

                    if (root.TryGetProperty("success", out var successProp))
                        entry.SessionSuccess = successProp.GetBoolean();

                    if (root.TryGetProperty("outputExe", out var outProp) && outProp.ValueKind == JsonValueKind.String)
                        entry.SessionOutputPath = outProp.GetString() ?? string.Empty;

                    // Compilation sessions
                    if (root.TryGetProperty("template", out var tmplProp) && tmplProp.ValueKind == JsonValueKind.Object)
                    {
                        if (tmplProp.TryGetProperty("Display", out var dispProp))
                            entry.TemplateName = dispProp.GetString() ?? string.Empty;
                        else if (tmplProp.TryGetProperty("display", out var dispProp2))
                            entry.TemplateName = dispProp2.GetString() ?? string.Empty;
                    }

                    if (root.TryGetProperty("shellcodeSource", out var srcProp) && srcProp.ValueKind == JsonValueKind.Object)
                    {
                        if (srcProp.TryGetProperty("Value", out var valProp))
                            entry.SessionShellcodeSource = valProp.GetString() ?? string.Empty;
                    }

                    // Backdoor sessions (flat fields from CLI-written summary)
                    if (root.TryGetProperty("targetPe", out var tpeProp) && string.IsNullOrEmpty(entry.SessionShellcodeSource))
                        entry.SourceFilePath = tpeProp.GetString() ?? string.Empty;

                    if (root.TryGetProperty("shellcode", out var scProp))
                        entry.SessionShellcodeSource = scProp.GetString() ?? string.Empty;

                    if (root.TryGetProperty("encoder", out var encProp))
                        entry.EncoderName = encProp.GetString() ?? string.Empty;

                    if (root.TryGetProperty("envelope", out var envProp))
                        entry.EnvelopeName = envProp.GetString() ?? string.Empty;

                    if (root.TryGetProperty("timestamp", out var tsProp) && tsProp.ValueKind == JsonValueKind.String)
                    {
                        if (DateTime.TryParse(tsProp.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
                            entry.GeneratedAtUtc = ts;
                    }
                }
                catch (JsonException) { }
                catch (IOException) { }
            }

            return entry;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Append(PayloadHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (SyncRoot)
        {
            var entries = LoadInternal();

            if (string.IsNullOrWhiteSpace(entry.Id))
                entry.Id = Guid.NewGuid().ToString("N");
            if (entry.GeneratedAtUtc == default)
                entry.GeneratedAtUtc = DateTime.UtcNow;

            entries.Insert(0, entry);

            Directory.CreateDirectory(HistoryDirectory);
            var json = JsonSerializer.Serialize(entries, JsonOptions);
            File.WriteAllText(HistoryFile, json);
        }
    }

    public static bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        lock (SyncRoot)
        {
            var entries = LoadInternal();
            int removed = entries.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return false;

            Directory.CreateDirectory(HistoryDirectory);
            var json = JsonSerializer.Serialize(entries, JsonOptions);
            File.WriteAllText(HistoryFile, json);
            return true;
        }
    }

    /// <summary>Delete a session folder from the logging directory.</summary>
    public static bool DeleteSession(string sessionDir)
    {
        if (string.IsNullOrWhiteSpace(sessionDir) || !Directory.Exists(sessionDir)) return false;
        try
        {
            Directory.Delete(sessionDir, recursive: true);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            Directory.CreateDirectory(HistoryDirectory);
            File.WriteAllText(HistoryFile, "[]");
        }
    }

    /// <summary>Deletes all session_* and backdoor_* directories from the logging folder.</summary>
    public static void ClearSessions()
    {
        if (!Directory.Exists(LoggingDirectory)) return;

        try
        {
            foreach (var dir in Directory.GetDirectories(LoggingDirectory))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith("session_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("backdoor_", StringComparison.OrdinalIgnoreCase))
                {
                    try { Directory.Delete(dir, recursive: true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static List<PayloadHistoryEntry> LoadInternal()
    {
        if (!File.Exists(HistoryFile))
            return new List<PayloadHistoryEntry>();

        var json = File.ReadAllText(HistoryFile);
        if (string.IsNullOrWhiteSpace(json))
            return new List<PayloadHistoryEntry>();

        return JsonSerializer.Deserialize<List<PayloadHistoryEntry>>(json) ?? new List<PayloadHistoryEntry>();
    }
}
