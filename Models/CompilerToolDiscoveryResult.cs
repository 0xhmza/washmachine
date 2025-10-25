using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Washmachine.Models;

public sealed class CompilerToolDiscoveryResult
{
    [JsonPropertyName("best")]
    public CompilerToolCandidate? Best { get; init; }

    [JsonPropertyName("candidates")]
    public IReadOnlyList<CompilerToolCandidate> Candidates { get; init; } = Array.Empty<CompilerToolCandidate>();

    [JsonPropertyName("errors")]
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    [JsonIgnore]
    public string Json { get; init; } = "{}";
}
