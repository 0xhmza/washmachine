using System.Collections.Generic;
using System.IO;

namespace Washmachine.Services;

/// <summary>
/// Provides strongly-typed access to application-specific paths derived from the executable directory.
/// Implementations should validate existence of the resolved paths.
/// </summary>
public interface IAppPaths
{
    string ExecutableDirectory { get; }
    string ApiHeaderFile { get; }
    string MainCppDirectory { get; }
    string MainCppFile { get; }
    string TemplateCppFile { get; }
    string Bin2ShellScript { get; }
    string Bin2ShellAlgos { get; }

    /// <summary>
    /// Returns the directory used to store temporary shellcode artifacts, creating it if necessary.
    /// </summary>
    /// <exception cref="IOException">Thrown when the directory cannot be created.</exception>
    string EnsureTempShellcodeDirectory();

    /// <summary>
    /// Validates that all critical paths exist on disk and returns a list of user-facing error messages.
    /// </summary>
    IReadOnlyList<string> Validate();
}
