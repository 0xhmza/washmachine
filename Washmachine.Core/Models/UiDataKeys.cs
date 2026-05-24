namespace Washmachine.Models;

/// <summary>
/// String keys used to exchange UI state between the GUI layer and CompilerService
/// via <see cref="UiData"/> dictionaries. The XAML control x:Name values and all
/// programmatic lookups must use these constants to stay in sync.
/// </summary>
public static class UiDataKeys
{
    // ── TextBox keys ──────────────────────────────────────────────────────────

    /// <summary>Path to the shellcode .bin file (File source).</summary>
    public const string ShellcodeFile = "shellcodeFileInput";

    /// <summary>Raw shellcode hex/bytes (Raw source).</summary>
    public const string ShellcodeRaw = "shellcodeRawInput";

    /// <summary>HTTP(S) URL for the web-delivered shellcode (URL source).</summary>
    public const string ShellcodeUrl = "shellcodeUrlValue";

    /// <summary>Path to the shellcode .bin used by the URL wizard (URL source).</summary>
    public const string ShellcodeUrlFile = "shellcodeUrlFileInput";

    /// <summary>Whether Shikata Ga Nai preprocessing is enabled.</summary>
    public const string ShikataGaNaiEnabled = "shikataGaNaiEnabledCheckBox";

    /// <summary>Shikata Ga Nai iteration count.</summary>
    public const string ShikataGaNaiEncodeCount = "shikataGaNaiEncodeCountInput";

    /// <summary>Shikata Ga Nai maximum decoder-obfuscation bytes.</summary>
    public const string ShikataGaNaiMaxBytes = "shikataGaNaiMaxBytesInput";

    /// <summary>Where SGN runs in the pipeline: "pre" (default, baked into loader shellcode)
    /// or "post" (applied to the stripped loader .bin after compile as a separate artifact).</summary>
    public const string ShikataGaNaiPlacement = "shikataGaNaiPlacement";

    // ── ComboBox keys ─────────────────────────────────────────────────────────

    /// <summary>Bin2Shell encoder selection (index string).</summary>
    public const string Encoder = "encoderCombo";

    /// <summary>Bin2Shell envelope selection (index string).</summary>
    public const string Envelope = "envelopeCombo";

    /// <summary>Loader template selection (template ID string).</summary>
    public const string Template = "templateCombo";

    /// <summary>Generic shellcode selection (for testing).</summary>
    public const string GenericShellcode = "genericShellcodeCombo";

    // ── Compilation backend keys ──────────────────────────────────────────────

    /// <summary>
    /// Compilation backend selection.
    /// Values: "Deterministic" (default) or "LlvmObfuscated".
    /// Stored in ComboBoxes.
    /// </summary>
    public const string CompilationBackend = "compilationBackend";

    /// <summary>
    /// List of enabled LLVM obfuscation pass IDs (e.g. "control-flow-flattening").
    /// Only meaningful when <see cref="CompilationBackend"/> is "LlvmObfuscated".
    /// Stored in ListBoxes.
    /// </summary>
    public const string LlvmObfuscationPasses = "llvmObfuscationPasses";

    // ── LLVM compilation-flow keys (LLVM backend only) ────────────────────────

    /// <summary>"auto", "clang-cl", or "clang++". Forces the LLVM driver used.</summary>
    public const string LlvmToolchain = "llvmToolchain";

    /// <summary>Optimization level token (O0/O1/O2/O3/Os/Oz).</summary>
    public const string LlvmOptLevel = "llvmOptLevel";

    /// <summary>Target architecture: x64 | x86.</summary>
    public const string LlvmArch = "llvmArch";

    /// <summary>Subsystem: windows | console.</summary>
    public const string LlvmSubsystem = "llvmSubsystem";

    /// <summary>C++ standard digits: 14 | 17 | 20.</summary>
    public const string LlvmCppStandard = "llvmCppStandard";

    /// <summary>Preprocessor define list (each entry "NAME" or "NAME=VAL").</summary>
    public const string LlvmDefines = "llvmDefines";

    /// <summary>Extra compiler flags passed verbatim.</summary>
    public const string LlvmExtraFlags = "llvmExtraFlags";

    /// <summary>Boolean toggle: strip symbols from the output binary.</summary>
    public const string LlvmStripSymbols = "llvmStripSymbols";

    /// <summary>Boolean toggle: enable -flto.</summary>
    public const string LlvmLto = "llvmLto";

    /// <summary>Boolean toggle: disable dead-section GC (the default is enabled).</summary>
    public const string LlvmNoGcSections = "llvmNoGcSections";

    /// <summary>Boolean toggle: emit debug info.</summary>
    public const string LlvmDebugInfo = "llvmDebugInfo";
}
