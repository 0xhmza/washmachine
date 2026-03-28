using Washmachine.Models;

namespace Washmachine.Services;

public sealed record CompilerResult(
    bool Success,
    string? GeneratedSourcePath,
    string? GeneratedSourceCode,
    IReadOnlyList<string> Notes,
    CompilerToolDiscoveryResult? Discovery,
    CppFileConversionResult ConversionResult)
{
    /// <summary>Path to the compiled executable, if compilation succeeded.</summary>
    public string? OutputExePath => ConversionResult?.OutputExePath;
}
