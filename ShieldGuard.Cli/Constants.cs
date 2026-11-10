namespace ShieldGuard.Cli;

/// <summary>
/// Shared constants for CLI operations.
/// </summary>
public static class CliConstants
{
    // Entropy thresholds for security scoring
    public const double LowEntropyThreshold = 6.0;
    public const double HighEntropyThreshold = 7.0;

    // LicenseCave injection methods
    public static readonly string[] LicenseCaveMethods = { "code-cave", "new-section", "section-ext", "text-pad", "tls-callback" };

    // Strip extraction modes
    public static readonly string[] StripModes = { "all", "section", "range", "overlay" };

    // List subcommands
    public static readonly string[] ListSubcommands = { "templates", "snippets", "compilers", "encoders", "envelopes" };
}
