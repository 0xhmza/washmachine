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

    /// <summary>Absolute path to the compiled pass plugin DLL, or null when not yet built.</summary>
    public string? PluginPath { get; init; }

    /// <summary>True when the plugin DLL has been built and can be loaded by clang++.</summary>
    public bool IsBuilt => PluginPath is not null && File.Exists(PluginPath);
}
