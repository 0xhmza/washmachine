using System.Text.Json.Serialization;

namespace Washmachine.Models;

public sealed record CompilerToolCandidate
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year { get; init; }

    [JsonPropertyName("edition")]
    public string Edition { get; init; } = "Unknown";

    [JsonPropertyName("instance_version")]
    public string? InstanceVersion { get; init; }

    [JsonPropertyName("installation_path")]
    public string InstallationPath { get; init; } = string.Empty;

    [JsonPropertyName("validated")]
    public bool Validated { get; init; }

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = string.Empty;
}
