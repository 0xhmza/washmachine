namespace Washmachine.Services;

/// <summary>
/// Captures all user choices for the PE/EXE shellcode source step.
/// Passed from <c>MainPage</c> to the build pipeline instead of reading
/// individual public properties on the View.
/// </summary>
public sealed class PeSourceOptions
{
    /// <summary>
    /// When true the source is a managed .NET assembly and donut should be
    /// used to convert it to shellcode.  When false the CLI strip path runs.
    /// </summary>
    public bool IsDonutConversion { get; init; }

    // ── Donut settings ────────────────────────────────────────────────────

    /// <summary>Donut target architecture: 1=x86, 2=x64, 3=x86+x64.</summary>
    public int DonutArch { get; init; } = 2;

    /// <summary>Optional fully-qualified class name (e.g. "MyApp.Program").</summary>
    public string? DonutClass { get; init; }

    /// <summary>Optional method to invoke (defaults to Main).</summary>
    public string? DonutMethod { get; init; }

    /// <summary>Optional comma-separated parameters to pass to the assembly.</summary>
    public string? DonutParams { get; init; }

    // ── Native PE strip settings ──────────────────────────────────────────

    /// <summary>CLI strip mode: "ep" (entry-point-to-end) or "section".</summary>
    public string PeStripMode { get; init; } = "ep";

    /// <summary>Named section for section strip mode (default: .text).</summary>
    public string PeStripSection { get; init; } = "";

    /// <summary>Remove trailing zero-byte padding after extraction.</summary>
    public bool PeStripTrimTrailingZeros { get; init; } = true;
}
