namespace Washmachine.Models;

/// <summary>
/// Describes a single LLVM IR obfuscation pass registered in the passes directory.
/// </summary>
public sealed record LlvmPassDefinition
{
    /// <summary>Short machine identifier (e.g. "cff").</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name shown in UI.</summary>
    public required string Name { get; init; }

    /// <summary>One-sentence description shown in tooltips.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// Absolute path to the compiled pass plugin DLL when present, otherwise
    /// null. The runtime LLVM pipeline does NOT load this DLL — it uses
    /// pass-runner.exe (which links every pass in statically). The property
    /// is retained for tooling that wants to know whether a developer has
    /// also built the standalone DLL.
    /// </summary>
    public string? PluginPath { get; init; }

    /// <summary>
    /// The pass name used in an explicit opt pipeline string (e.g. "bcf", "cff", "sub", "strenc").
    /// Populated from <c>opt_name</c> in pass.json; used by pass-runner.exe.
    /// </summary>
    public string? OptPassName { get; init; }

    /// <summary>
    /// True when the pass has the metadata pass-runner needs to schedule it
    /// (a non-empty <see cref="OptPassName"/>). Readiness of pass-runner.exe
    /// itself is validated separately in <c>LlvmPipelineService</c>; this
    /// property is purely about per-pass metadata.
    /// </summary>
    public bool IsBuilt => !string.IsNullOrWhiteSpace(OptPassName);
}
