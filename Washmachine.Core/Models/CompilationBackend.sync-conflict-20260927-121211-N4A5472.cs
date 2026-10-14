namespace Washmachine.Models;

/// <summary>
/// Selects the compilation backend used when producing an executable from generated C++ source.
/// </summary>
public enum CompilationBackend
{
    /// <summary>
    /// Standard, reproducible compilation via the discovered system toolchain (MSVC, MinGW, or Clang).
    /// This is the default behavior.
    /// </summary>
    Deterministic,

    /// <summary>
    /// LLVM-based compilation using the bundled clang++ with IR-level obfuscation passes applied
    /// before code generation. Output is non-deterministic across builds.
    /// </summary>
    LlvmObfuscated,
}
