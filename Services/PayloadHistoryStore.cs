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

    [JsonIgnore]
    public string SourceFileName => Path.GetFileName(SourceFilePath ?? string.Empty);

    [JsonIgnore]
    public string DisplaySourceName =>
        string.IsNullOrWhiteSpace(SourceFileName) ? "(unknown source)" : SourceFileName;

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

    public static string FilePath => HistoryFile;

    public static IReadOnlyList<PayloadHistoryEntry> Load()
    {
        lock (SyncRoot)
        {
            var entries = LoadInternal();
            return entries
                .OrderByDescending(entry => entry.GeneratedAtUtc)
                .ToList();
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

    public static void Clear()
    {
        lock (SyncRoot)
        {
            Directory.CreateDirectory(HistoryDirectory);
            File.WriteAllText(HistoryFile, "[]");
        }
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
