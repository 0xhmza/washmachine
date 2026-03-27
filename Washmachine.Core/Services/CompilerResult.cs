using Washmachine.Models;

namespace Washmachine.Services;

public sealed record CompilerResult(
    bool Success,
    string? GeneratedSourcePath,
    string? GeneratedSourceCode,
    IReadOnlyList<string> Notes,
    CompilerToolDiscoveryResult? Discovery,
    CppFileConversionResult ConversionResult);
