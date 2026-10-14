namespace Washmachine.Models;

/// <summary>
/// Comprehensive PE analysis result with deep inspection data.
/// </summary>
public sealed class PeAnalysisResult
{
    // Basic info
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileSizeFormatted { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;

    // PE Type
    public bool IsValid { get; set; }
    public string ValidationError { get; set; } = string.Empty;
    public bool Is64Bit { get; set; }
    public bool IsDll { get; set; }
    public bool IsExe => !IsDll;
    public bool IsDotNet { get; set; }
    public bool IsDriver { get; set; }
    public string PeType { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string MachineType { get; set; } = string.Empty;
    public string Subsystem { get; set; } = string.Empty;

    // Headers
    public DosHeaderInfo DosHeader { get; set; } = new();
    public FileHeaderInfo FileHeader { get; set; } = new();
    public OptionalHeaderInfo OptionalHeader { get; set; } = new();

    // Sections
    public List<SectionAnalysis> Sections { get; set; } = new();
    public int TotalSections => Sections.Count;

    // Security & Protection
    public PeSecurityInfo Security { get; set; } = new();

    // Code Caves
    public List<CodeCaveAnalysis> CodeCaves { get; set; } = new();
    public int TotalCodeCaves => CodeCaves.Count;
    public int TotalCodeCaveSpace => CodeCaves.Sum(c => c.Size);
    public int LargestCodeCave => CodeCaves.Count > 0 ? CodeCaves.Max(c => c.Size) : 0;

    // Imports & Exports
    public List<ImportedDll> Imports { get; set; } = new();
    public List<ExportedFunction> Exports { get; set; } = new();
    public int TotalImports => Imports.Sum(i => i.Functions.Count);
    public int TotalExports => Exports.Count;

    // Resources
    public List<ResourceInfo> Resources { get; set; } = new();
    public bool HasManifest { get; set; }
    public bool HasIcon { get; set; }
    public bool HasVersionInfo { get; set; }

    // TLS
    public TlsInfo? Tls { get; set; }
    public bool HasTls => Tls != null;

    // Relocation
    public RelocationInfo Relocations { get; set; } = new();

    // Entropy Analysis
    public double OverallEntropy { get; set; }
    public bool IsPossiblyPacked { get; set; }
    public string PackerDetection { get; set; } = string.Empty;

    // Injection Feasibility
    public InjectionFeasibility Feasibility { get; set; } = new();

    // ASCII Visualization
    public string AsciiVisualization { get; set; } = string.Empty;
    public string AsciiSectionMap { get; set; } = string.Empty;
    public string AsciiMemoryLayout { get; set; } = string.Empty;

    // Timestamps
    public DateTime? CompileTime { get; set; }
    public string CompileTimeFormatted { get; set; } = string.Empty;
}

/// <summary>
/// DOS header information.
/// </summary>
public sealed class DosHeaderInfo
{
    public string Signature { get; set; } = "MZ";
    public ushort Magic { get; set; }
    public uint PeHeaderOffset { get; set; }
    public int DosStubSize { get; set; }
    public bool HasRichHeader { get; set; }
    public uint RichHeaderOffset { get; set; }
    public byte[] RichHeaderData { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// COFF file header information.
/// </summary>
public sealed class FileHeaderInfo
{
    public ushort Machine { get; set; }
    public string MachineString { get; set; } = string.Empty;
    public ushort NumberOfSections { get; set; }
    public uint TimeDateStamp { get; set; }
    public DateTime TimeDateStampUtc { get; set; }
    public uint PointerToSymbolTable { get; set; }
    public uint NumberOfSymbols { get; set; }
    public ushort SizeOfOptionalHeader { get; set; }
    public ushort Characteristics { get; set; }
    public List<string> CharacteristicsList { get; set; } = new();
}

/// <summary>
/// Optional header information.
/// </summary>
public sealed class OptionalHeaderInfo
{
    public ushort Magic { get; set; }
    public string MagicString { get; set; } = string.Empty;
    public byte MajorLinkerVersion { get; set; }
    public byte MinorLinkerVersion { get; set; }
    public string LinkerVersion { get; set; } = string.Empty;
    public uint SizeOfCode { get; set; }
    public uint SizeOfInitializedData { get; set; }
    public uint SizeOfUninitializedData { get; set; }
    public uint AddressOfEntryPoint { get; set; }
    public uint BaseOfCode { get; set; }
    public uint BaseOfData { get; set; } // Only PE32
    public ulong ImageBase { get; set; }
    public uint SectionAlignment { get; set; }
    public uint FileAlignment { get; set; }
    public ushort MajorOSVersion { get; set; }
    public ushort MinorOSVersion { get; set; }
    public string OSVersion { get; set; } = string.Empty;
    public ushort MajorImageVersion { get; set; }
    public ushort MinorImageVersion { get; set; }
    public string ImageVersion { get; set; } = string.Empty;
    public ushort MajorSubsystemVersion { get; set; }
    public ushort MinorSubsystemVersion { get; set; }
    public string SubsystemVersion { get; set; } = string.Empty;
    public uint SizeOfImage { get; set; }
    public uint SizeOfHeaders { get; set; }
    public uint Checksum { get; set; }
    public ushort Subsystem { get; set; }
    public string SubsystemString { get; set; } = string.Empty;
    public ushort DllCharacteristics { get; set; }
    public List<string> DllCharacteristicsList { get; set; } = new();
    public ulong SizeOfStackReserve { get; set; }
    public ulong SizeOfStackCommit { get; set; }
    public ulong SizeOfHeapReserve { get; set; }
    public ulong SizeOfHeapCommit { get; set; }
    public uint NumberOfRvaAndSizes { get; set; }
    public List<DataDirectoryInfo> DataDirectories { get; set; } = new();
}

/// <summary>
/// Data directory entry information.
/// </summary>
public sealed class DataDirectoryInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint VirtualAddress { get; set; }
    public uint Size { get; set; }
    public bool IsPresent => Size > 0;
}

/// <summary>
/// Deep section analysis.
/// </summary>
public sealed class SectionAnalysis
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint VirtualAddress { get; set; }
    public uint VirtualSize { get; set; }
    public uint RawAddress { get; set; }
    public uint RawSize { get; set; }
    public uint PointerToRelocations { get; set; }
    public uint PointerToLinenumbers { get; set; }
    public ushort NumberOfRelocations { get; set; }
    public ushort NumberOfLinenumbers { get; set; }
    public uint Characteristics { get; set; }
    public List<string> CharacteristicsList { get; set; } = new();

    // Analysis
    public bool IsExecutable { get; set; }
    public bool IsReadable { get; set; }
    public bool IsWritable { get; set; }
    public bool ContainsCode { get; set; }
    public bool ContainsInitializedData { get; set; }
    public bool ContainsUninitializedData { get; set; }
    public bool IsDiscardable { get; set; }
    public bool NotCached { get; set; }
    public bool NotPaged { get; set; }
    public bool IsShared { get; set; }

    // Entropy
    public double Entropy { get; set; }
    public string EntropyAssessment { get; set; } = string.Empty;
    public bool IsPossiblyEncrypted { get; set; }
    public bool IsPossiblyPacked { get; set; }

    // Space analysis
    public int PaddingSize { get; set; }
    public int UsedSize { get; set; }
    public double UsagePercentage { get; set; }

    // Code caves in this section
    public List<CodeCaveAnalysis> CodeCaves { get; set; } = new();
    public int TotalCaveSpace => CodeCaves.Sum(c => c.Size);

    // Visual
    public string PermissionsString { get; set; } = string.Empty; // e.g., "RWX" or "R--"
    public string AsciiBar { get; set; } = string.Empty;
}

/// <summary>
/// Detailed code cave analysis.
/// </summary>
public sealed class CodeCaveAnalysis
{
    public string SectionName { get; set; } = string.Empty;
    public uint FileOffset { get; set; }
    public uint VirtualAddress { get; set; }
    public int Size { get; set; }
    public byte FillByte { get; set; } // Usually 0x00 or 0xCC
    public string FillByteDescription { get; set; } = string.Empty;
    public bool IsExecutable { get; set; }
    public bool SuitableForInjection { get; set; }
    public string AssessmentNote { get; set; } = string.Empty;
}

/// <summary>
/// Security features of the PE.
/// </summary>
public sealed class PeSecurityInfo
{
    // Mitigations
    public bool HasAslr { get; set; }
    public bool HasDep { get; set; }
    public bool HasSeh { get; set; }
    public bool HasSafeSeh { get; set; }
    public bool HasCfg { get; set; }
    public bool HasRfg { get; set; }
    public bool HasHighEntropyVa { get; set; }
    public bool ForceIntegrity { get; set; }
    public bool NxCompat { get; set; }
    public bool NoIsolation { get; set; }
    public bool NoBind { get; set; }
    public bool AppContainer { get; set; }
    public bool WdmDriver { get; set; }
    public bool GuardCf { get; set; }
    public bool TerminalServerAware { get; set; }

    // Signature
    public bool HasAuthenticode { get; set; }
    public uint SignatureOffset { get; set; }
    public uint SignatureSize { get; set; }
    public string SignatureInfo { get; set; } = string.Empty;

    // Assessment
    public int SecurityScore { get; set; } // 0-100
    public string SecurityAssessment { get; set; } = string.Empty;
    public List<string> EnabledProtections { get; set; } = new();
    public List<string> MissingProtections { get; set; } = new();
}

/// <summary>
/// Imported DLL and its functions.
/// </summary>
public sealed class ImportedDll
{
    public string Name { get; set; } = string.Empty;
    public List<ImportedFunction> Functions { get; set; } = new();
    public bool IsDelayLoaded { get; set; }
}

/// <summary>
/// Imported function information.
/// </summary>
public sealed class ImportedFunction
{
    public string Name { get; set; } = string.Empty;
    public ushort Ordinal { get; set; }
    public bool IsByOrdinal { get; set; }
    public ushort Hint { get; set; }
    public bool IsSuspicious { get; set; }
    public string SuspiciousReason { get; set; } = string.Empty;
}

/// <summary>
/// Exported function information.
/// </summary>
public sealed class ExportedFunction
{
    public string Name { get; set; } = string.Empty;
    public ushort Ordinal { get; set; }
    public uint Rva { get; set; }
    public bool IsForwarded { get; set; }
    public string ForwardedTo { get; set; } = string.Empty;
}

/// <summary>
/// Resource information.
/// </summary>
public sealed class ResourceInfo
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public uint Language { get; set; }
    public uint Size { get; set; }
    public uint Rva { get; set; }
}

/// <summary>
/// TLS (Thread Local Storage) information.
/// </summary>
public sealed class TlsInfo
{
    public ulong StartAddressOfRawData { get; set; }
    public ulong EndAddressOfRawData { get; set; }
    public ulong AddressOfIndex { get; set; }
    public ulong AddressOfCallbacks { get; set; }
    public uint SizeOfZeroFill { get; set; }
    public uint Characteristics { get; set; }
    public int NumberOfCallbacks { get; set; }
    public List<ulong> CallbackAddresses { get; set; } = new();
    public bool HasCallbacks => NumberOfCallbacks > 0;
}

/// <summary>
/// Relocation information.
/// </summary>
public sealed class RelocationInfo
{
    public bool HasRelocations { get; set; }
    public uint TableRva { get; set; }
    public uint TableSize { get; set; }
    public int NumberOfBlocks { get; set; }
    public int TotalRelocations { get; set; }
}

/// <summary>
/// Injection feasibility assessment.
/// </summary>
public sealed class InjectionFeasibility
{
    public bool CanInject { get; set; }
    public List<string> BlockingReasons { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();

    // Method-specific feasibility
    public MethodFeasibility CodeCave { get; set; } = new();
    public MethodFeasibility NewSection { get; set; } = new();
    public MethodFeasibility SectionExtension { get; set; } = new();
    public MethodFeasibility TlsCallback { get; set; } = new();
    public MethodFeasibility EntryPointHijack { get; set; } = new();

    // Space analysis
    public int RequiredSpace { get; set; }
    public int AvailableCodeCaveSpace { get; set; }
    public int MaxNewSectionSize { get; set; }
    public int MaxExtensionSize { get; set; }

    // Best method recommendation
    public string RecommendedMethod { get; set; } = string.Empty;
    public string RecommendedReason { get; set; } = string.Empty;
}

/// <summary>
/// Feasibility of a specific injection method.
/// </summary>
public sealed class MethodFeasibility
{
    public bool IsFeasible { get; set; }
    public bool IsRecommended { get; set; }
    public string Status { get; set; } = string.Empty; // "Available", "Limited", "Unavailable"
    public string Reason { get; set; } = string.Empty;
    public int AvailableSpace { get; set; }
    public List<string> Notes { get; set; } = new();
}
