using System.Collections.Generic;

namespace Washmachine.Services;

public sealed record CompilerResult(
    bool Success,
    string? OutputExecutablePath,
    string? GeneratedSourcePath,
    string? GeneratedSourceCode,
    IReadOnlyList<string> Notes);
