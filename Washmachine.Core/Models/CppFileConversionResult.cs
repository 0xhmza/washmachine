namespace Washmachine.Models;

public sealed record CppFileConversionResult(bool Success, string? Error, string? OutputExePath = null);
