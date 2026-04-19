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
}
