namespace Washmachine.Services;

/// <summary>
/// Captures every user-controlled input that affects how an .exe shellcode source
/// is converted into a flat <c>.bin</c> before encoding.
///
/// <para>
/// Two routes are supported:
/// <list type="bullet">
///   <item><description><b>Donut</b> — managed (.NET) assemblies are converted to PIC shellcode via <c>donut.exe</c>.</description></item>
///   <item><description><b>Strip</b> — native shellcode-format PEs are flattened by the CLI's <c>strip</c> command.</description></item>
/// </list>
/// The View (<c>MainPage</c>) builds an instance of this record from its current control state and hands it
/// to the build pipeline, so View→View coupling stays out of <c>CompilePage</c>.
/// </para>
/// </summary>
public sealed record PeSourceOptions
{
    /// <summary>
    /// When <c>true</c> the source is a managed .NET assembly and the donut path runs.
    /// When <c>false</c> the native CLI-strip path runs.
    /// </summary>
    public bool IsDonutConversion { get; init; }

    // ── Donut settings (used only when IsDonutConversion is true) ────────

    /// <summary>Donut target architecture: 1 = x86, 2 = x64, 3 = x86+x64.</summary>
    public int DonutArch { get; init; } = 2;

    /// <summary>Optional fully-qualified class name (e.g. <c>"MyApp.Program"</c>).</summary>
    public string? DonutClass { get; init; }

    /// <summary>Optional method to invoke on the class. Defaults to <c>Main</c> when omitted.</summary>
    public string? DonutMethod { get; init; }

    /// <summary>Optional comma-separated parameters passed to the assembly entry point.</summary>
    public string? DonutParams { get; init; }

    // ── Native PE strip settings (used only when IsDonutConversion is false) ──

    /// <summary>CLI strip mode — see <see cref="PeStripModes"/> for valid values.</summary>
    public string PeStripMode { get; init; } = PeStripModes.EntryPoint;

    /// <summary>Named section for <see cref="PeStripModes.Section"/> mode (default: <c>.text</c>).</summary>
    public string PeStripSection { get; init; } = "";

    /// <summary>Whether to remove trailing zero-byte padding after extraction.</summary>
    public bool PeStripTrimTrailingZeros { get; init; } = true;
}

/// <summary>Canonical strip-mode tokens shared by the GUI and the CLI.</summary>
public static class PeStripModes
{
    /// <summary>Extract from the entry point to the end of its containing section.</summary>
    public const string EntryPoint = "ep";

    /// <summary>Extract the full raw contents of a single named section.</summary>
    public const string Section = "section";
}
