using Washmachine.Logging;

namespace Washmachine.Services;

/// <summary>
/// Strips a PE (.exe / .dll) into a flat binary (.bin) by extracting raw executable bytes.
/// Useful for analysis and for workflows where the source PE was specifically built to be
/// flattened — the output is <b>not</b> automatically guaranteed to be a generic position-independent
/// payload (use a real shellcode generator for that).
/// </summary>
public sealed class PeStripService
{
    // ── PE format constants ─────────────────────────────────────────────
    // Refs: Microsoft PE/COFF spec (https://learn.microsoft.com/en-us/windows/win32/debug/pe-format)

    private const ushort DosSignatureMz       = 0x5A4D;     // "MZ"
    private const uint   PeSignaturePe00      = 0x00004550; // "PE\0\0"
    private const ushort OptionalMagicPe32    = 0x010B;
    private const ushort OptionalMagicPe32Plus = 0x020B;

    private const int DosHeaderSize           = 64;
    private const int LfanewOffset            = 0x3C; // e_lfanew → offset of PE signature
    private const int CoffHeaderSize          = 24;   // PE sig (4) + COFF header (20)
    private const int DataDirectoriesPe32Off  = 96;   // offset of DataDirectory[0] inside the optional header
    private const int DataDirectoriesPe32PlusOff = 112;
    private const int DataDirectoryEntrySize  = 8;
    private const int ClrRuntimeHeaderIndex   = 14;   // DataDirectory[14] = COM/CLR runtime header
    private const int OptionalHeaderMinPe32       = 224; // includes all 16 data-directory entries
    private const int OptionalHeaderMinPe32Plus   = 240;

    // Section characteristics (IMAGE_SCN_*)
    private const uint SectionFlagExecutable  = 0x20000000;
    private const uint SectionFlagReadable    = 0x40000000;
    private const uint SectionFlagWritable    = 0x80000000;

    private readonly IAppLogger _logger;

    public PeStripService(IAppLogger logger)
    {
        _logger = logger;
    }

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Strip a PE file into a flat binary, writing the result to
    /// <see cref="StripOptions.OutputPath"/> (or a sibling <c>.bin</c> when null).
    /// </summary>
    public async Task<StripResult> StripAsync(StripOptions options)
    {
        var result = new StripResult();
        try
        {
            if (!File.Exists(options.InputPath))
            {
                result.Error = $"Input file not found: {options.InputPath}";
                return result;
            }

            var data = await File.ReadAllBytesAsync(options.InputPath);
            result.OriginalSize = data.Length;

            if (!HasMzSignature(data))
            {
                result.Error = "Not a valid PE file (missing MZ signature).";
                return result;
            }

            var pe = ParsePeMinimal(data);
            if (pe == null)
            {
                result.Error = "Failed to parse PE headers.";
                return result;
            }

            result.Is64Bit       = pe.Is64Bit;
            result.EntryPoint    = pe.AddressOfEntryPoint;
            result.ImageBase     = pe.ImageBase;
            result.SectionsFound = pe.Sections.Count;

            _logger.Info($"PE: {(pe.Is64Bit ? "x64" : "x86")} | EP=0x{pe.AddressOfEntryPoint:X} | {pe.Sections.Count} sections");

            // Pick the extraction strategy based on the requested mode.
            (byte[] extracted, string description) = options.Mode switch
            {
                StripMode.Section            => ExtractSection(data, pe, options.SectionName ?? ".text"),
                StripMode.EntryPointToEnd or _ => ExtractFromEntryPoint(data, pe),
            };

            if (extracted.Length == 0)
            {
                result.Error = $"Extraction produced 0 bytes. {description}";
                return result;
            }

            result.ExtractedSize = extracted.Length;
            result.Description   = description;

            // Strip trailing zero padding when requested. Section data is page-aligned
            // on disk so the tail is almost always zeroed; keeping it bloats the .bin
            // and confuses downstream encoders.
            if (options.TrimTrailingZeros)
            {
                int trimmed = TrimTrailing(extracted, 0x00);
                if (trimmed < extracted.Length)
                {
                    int removed       = extracted.Length - trimmed;
                    extracted         = extracted[..trimmed];
                    result.ExtractedSize = extracted.Length;
                    result.TrimmedBytes  = removed;
                    _logger.Debug($"Trimmed {removed} trailing zero bytes");
                }
            }

            result.Warnings = ValidateExtracted(extracted);

            string outputPath = options.OutputPath
                ?? Path.Combine(
                    Path.GetDirectoryName(options.InputPath) ?? ".",
                    Path.GetFileNameWithoutExtension(options.InputPath) + ".bin");

            await File.WriteAllBytesAsync(outputPath, extracted);
            result.OutputPath = outputPath;
            result.Success    = true;

            _logger.Info($"Wrote {extracted.Length:N0} bytes → {outputPath}");
            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Analyze a PE without writing anything — returns section info, EP location, and an estimated
    /// flat-binary size so the caller (CLI/GUI) can decide which mode to use.
    /// </summary>
    public async Task<StripAnalysis> AnalyzeAsync(string filePath)
    {
        var analysis = new StripAnalysis();

        if (!File.Exists(filePath))
        {
            analysis.Error = "File not found.";
            return analysis;
        }

        var data = await File.ReadAllBytesAsync(filePath);

        if (!HasMzSignature(data))
        {
            analysis.Error = "Not a valid PE file.";
            return analysis;
        }

        var pe = ParsePeMinimal(data);
        if (pe == null)
        {
            analysis.Error = "Failed to parse PE.";
            return analysis;
        }

        analysis.Is64Bit    = pe.Is64Bit;
        analysis.EntryPoint = pe.AddressOfEntryPoint;
        analysis.ImageBase  = pe.ImageBase;
        analysis.IsManaged  = pe.IsManaged;

        foreach (var s in pe.Sections)
        {
            analysis.Sections.Add(new StripSectionInfo
            {
                Name               = s.Name,
                VirtualAddress     = s.VirtualAddress,
                VirtualSize        = s.VirtualSize,
                RawAddress         = s.RawAddress,
                RawSize            = s.RawSize,
                IsExecutable       = s.IsExecutable,
                IsReadable         = s.IsReadable,
                IsWritable         = s.IsWritable,
                ContainsEntryPoint = s.Contains(pe.AddressOfEntryPoint),
            });
        }

        // Estimate flat-binary size assuming the default "ep → end of section" mode.
        var epSection = pe.Sections.FirstOrDefault(s => s.Contains(pe.AddressOfEntryPoint));
        if (epSection != null)
        {
            uint epOffsetInSection      = pe.AddressOfEntryPoint - epSection.VirtualAddress;
            analysis.EstimatedBinSize   = (int)(epSection.RawSize - epOffsetInSection);
            analysis.EntryPointSection  = epSection.Name;
        }

        analysis.Success = true;
        return analysis;
    }

    /// <summary>
    /// Quickly checks whether <paramref name="data"/> represents a managed (.NET) PE
    /// by inspecting <c>DataDirectory[14]</c> (CLR Runtime Header). Returns <c>false</c>
    /// for non-PE or malformed input — never throws.
    /// </summary>
    public static bool IsManagedPe(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            return TryReadClrDirectoryRva(br, data.Length, out var rva) && rva != 0;
        }
        catch
        {
            return false;
        }
    }

    // ── Extraction modes ────────────────────────────────────────────────

    /// <summary>
    /// Default mode: extract from the entry point to the end of its containing section.
    /// This is the most common shape for shellcode-format PEs — the entry point is the start
    /// of the payload and everything after it (within the same section) is part of it.
    /// </summary>
    private (byte[] data, string desc) ExtractFromEntryPoint(byte[] peData, MinimalPe pe)
    {
        var section = pe.Sections.FirstOrDefault(s => s.Contains(pe.AddressOfEntryPoint));
        if (section == null)
            return (Array.Empty<byte>(), "Entry point does not fall within any section.");

        uint epOffsetInSection = pe.AddressOfEntryPoint - section.VirtualAddress;
        uint fileOffset        = section.RawAddress + epOffsetInSection;
        uint length            = section.RawSize - epOffsetInSection;

        if (fileOffset + length > peData.Length)
            length = (uint)(peData.Length - fileOffset);

        var buf = new byte[length];
        Array.Copy(peData, fileOffset, buf, 0, length);

        string desc = $"EP→end of {section.Name}: file 0x{fileOffset:X}..0x{fileOffset + length:X} ({length:N0} bytes)";
        _logger.Info(desc);
        return (buf, desc);
    }

    /// <summary>
    /// Extract a single named section verbatim (default name: <c>.text</c>).
    /// </summary>
    private (byte[] data, string desc) ExtractSection(byte[] peData, MinimalPe pe, string sectionName)
    {
        var section = pe.Sections.FirstOrDefault(s =>
            s.Name.Equals(sectionName, StringComparison.OrdinalIgnoreCase));

        if (section == null)
        {
            string available = string.Join(", ", pe.Sections.Select(s => s.Name));
            return (Array.Empty<byte>(), $"Section '{sectionName}' not found. Available: {available}");
        }

        uint length = section.RawSize;
        if (section.RawAddress + length > peData.Length)
            length = (uint)(peData.Length - section.RawAddress);

        var buf = new byte[length];
        Array.Copy(peData, section.RawAddress, buf, 0, length);

        string desc = $"{section.Name}: file 0x{section.RawAddress:X}..0x{section.RawAddress + length:X} ({length:N0} bytes)";
        _logger.Info(desc);
        return (buf, desc);
    }

    // ── Validation ──────────────────────────────────────────────────────

    /// <summary>
    /// Quick sanity checks on extracted bytes — warnings only, never errors.
    /// Surfaces obvious "you stripped the whole PE" or "this is mostly padding" mistakes.
    /// </summary>
    private static List<string> ValidateExtracted(byte[] data)
    {
        var warnings = new List<string>();

        if (data.Length < 4)
            warnings.Add("Extracted binary is very small (<4 bytes) — may not be valid shellcode.");

        // MZ at the start almost always means "this is a full PE", not flat code.
        if (data.Length >= 2 && data[0] == 0x4D && data[1] == 0x5A)
            warnings.Add("Extracted data starts with MZ header — you may be extracting the whole PE, not flat code.");

        // High zero ratio usually means raw section padding leaked in.
        int zeros      = data.Count(b => b == 0x00);
        double zeroPct = (double)zeros / data.Length;
        if (zeroPct > 0.7)
            warnings.Add($"High zero-byte ratio ({zeroPct:P0}) — extracted data may be mostly padding. Consider --trim.");

        return warnings;
    }

    private static int TrimTrailing(byte[] data, byte value)
    {
        int end = data.Length;
        while (end > 0 && data[end - 1] == value) end--;
        return Math.Max(end, 1); // keep at least 1 byte so callers always have something to write
    }

    // ── PE parsing helpers ──────────────────────────────────────────────

    private static bool HasMzSignature(byte[] data) =>
        data.Length >= DosHeaderSize && data[0] == 0x4D && data[1] == 0x5A;

    /// <summary>
    /// Walks DOS → PE → optional header to locate <c>DataDirectory[14]</c> (CLR runtime header)
    /// and reads its RVA. Used by both <see cref="IsManagedPe"/> and the full PE parser.
    /// On any structural problem the method returns <c>false</c> — callers treat that as
    /// "not managed".
    /// </summary>
    private static bool TryReadClrDirectoryRva(BinaryReader br, long fileLength, out uint rva)
    {
        rva = 0;
        var ms = br.BaseStream;

        if (fileLength < DosHeaderSize) return false;
        ms.Seek(0, SeekOrigin.Begin);
        if (br.ReadUInt16() != DosSignatureMz) return false;

        // Read PE-header offset from e_lfanew.
        ms.Seek(LfanewOffset, SeekOrigin.Begin);
        uint peOffset = br.ReadUInt32();
        if (peOffset + CoffHeaderSize > (uint)fileLength) return false;

        // PE signature.
        ms.Seek(peOffset, SeekOrigin.Begin);
        if (br.ReadUInt32() != PeSignaturePe00) return false;

        // Skip over machine + numberOfSections + 4 ints (12 bytes), read sizeOfOptionalHeader,
        // skip characteristics (2 bytes) → we're now at the start of the optional header.
        ms.Seek(peOffset + 20, SeekOrigin.Begin);
        ushort sizeOfOptionalHeader = br.ReadUInt16();
        ms.Seek(2, SeekOrigin.Current); // characteristics

        long optHeaderStart = ms.Position;
        ushort optMagic     = br.ReadUInt16();
        bool is64           = optMagic == OptionalMagicPe32Plus;

        long dataDirectoriesBase = optHeaderStart + (is64 ? DataDirectoriesPe32PlusOff : DataDirectoriesPe32Off);
        long clrEntryOffset      = dataDirectoriesBase + ClrRuntimeHeaderIndex * DataDirectoryEntrySize;

        int requiredOptHeader = is64 ? OptionalHeaderMinPe32Plus : OptionalHeaderMinPe32;
        if (sizeOfOptionalHeader < requiredOptHeader) return false;
        if (clrEntryOffset + 4 > fileLength) return false;

        ms.Seek(clrEntryOffset, SeekOrigin.Begin);
        rva = br.ReadUInt32();
        return true;
    }

    /// <summary>
    /// Self-contained minimal PE parser — extracts everything the strip pipeline needs without
    /// pulling in <c>System.Reflection.PortableExecutable</c> or the heavier
    /// <c>PeBackdoorService</c> path.
    /// </summary>
    private MinimalPe? ParsePeMinimal(byte[] data)
    {
        try
        {
            if (data.Length < DosHeaderSize) return null;

            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);

            if (br.ReadUInt16() != DosSignatureMz) return null;

            ms.Seek(LfanewOffset, SeekOrigin.Begin);
            uint peOffset = br.ReadUInt32();
            if (peOffset + CoffHeaderSize > data.Length) return null;

            ms.Seek(peOffset, SeekOrigin.Begin);
            if (br.ReadUInt32() != PeSignaturePe00) return null;

            // ── COFF file header (20 bytes) ──
            ushort machine          = br.ReadUInt16();
            ushort numberOfSections = br.ReadUInt16();
            br.ReadBytes(12); // TimeDateStamp(4), PointerToSymbolTable(4), NumberOfSymbols(4)
            ushort sizeOfOptionalHeader = br.ReadUInt16();
            br.ReadUInt16(); // characteristics

            // ── Optional header ──
            long optHeaderStart = ms.Position;
            ushort optMagic     = br.ReadUInt16();
            bool is64           = optMagic == OptionalMagicPe32Plus; // PE32 = 0x10B, PE32+ = 0x20B

            br.ReadBytes(2);  // MajorLinkerVersion, MinorLinkerVersion
            br.ReadBytes(12); // SizeOfCode, SizeOfInitializedData, SizeOfUninitializedData

            uint addressOfEntryPoint = br.ReadUInt32();
            br.ReadBytes(is64 ? 4 : 8); // BaseOfCode (+ BaseOfData on PE32 only)

            ulong imageBase = is64 ? br.ReadUInt64() : br.ReadUInt32();

            uint sectionAlignment = br.ReadUInt32();
            uint fileAlignment    = br.ReadUInt32();

            br.ReadBytes(16); // 6 × UInt16 version fields + Win32VersionValue (UInt32)

            uint sizeOfImage   = br.ReadUInt32();
            uint sizeOfHeaders = br.ReadUInt32();

            // ── Managed-PE flag (DataDirectory[14], CLR runtime header) ──
            bool isManaged = false;
            long dataDirectoriesBase = optHeaderStart + (is64 ? DataDirectoriesPe32PlusOff : DataDirectoriesPe32Off);
            long clrEntryOffset      = dataDirectoriesBase + ClrRuntimeHeaderIndex * DataDirectoryEntrySize;
            int  requiredOptHeader   = is64 ? OptionalHeaderMinPe32Plus : OptionalHeaderMinPe32;
            if (clrEntryOffset + 4 <= ms.Length && sizeOfOptionalHeader >= requiredOptHeader)
            {
                ms.Seek(clrEntryOffset, SeekOrigin.Begin);
                isManaged = br.ReadUInt32() != 0;
            }

            // ── Section headers ──
            long sectionHeadersOffset = optHeaderStart + sizeOfOptionalHeader;
            ms.Seek(sectionHeadersOffset, SeekOrigin.Begin);

            var sections = new List<MinimalSection>(numberOfSections);
            for (int i = 0; i < numberOfSections; i++)
            {
                var nameBytes  = br.ReadBytes(8);
                string name    = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                uint virtualSize    = br.ReadUInt32();
                uint virtualAddress = br.ReadUInt32();
                uint rawSize        = br.ReadUInt32();
                uint rawAddress     = br.ReadUInt32();
                br.ReadBytes(12); // relocations + linenumbers fields (unused by us)
                uint chars          = br.ReadUInt32();

                sections.Add(new MinimalSection
                {
                    Name            = name,
                    VirtualSize     = virtualSize,
                    VirtualAddress  = virtualAddress,
                    RawSize         = rawSize,
                    RawAddress      = rawAddress,
                    Characteristics = chars,
                });
            }

            return new MinimalPe
            {
                Is64Bit             = is64,
                IsManaged           = isManaged,
                Machine             = machine,
                AddressOfEntryPoint = addressOfEntryPoint,
                ImageBase           = imageBase,
                SectionAlignment    = sectionAlignment,
                FileAlignment       = fileAlignment,
                SizeOfImage         = sizeOfImage,
                SizeOfHeaders       = sizeOfHeaders,
                Sections            = sections,
            };
        }
        catch
        {
            return null;
        }
    }

    // ── Internal types ──────────────────────────────────────────────────

    private sealed class MinimalPe
    {
        public bool Is64Bit;
        public bool IsManaged;
        public ushort Machine;
        public uint AddressOfEntryPoint;
        public ulong ImageBase;
        public uint SectionAlignment;
        public uint FileAlignment;
        public uint SizeOfImage;
        public uint SizeOfHeaders;
        public List<MinimalSection> Sections = new();
    }

    private sealed class MinimalSection
    {
        public string Name = "";
        public uint VirtualSize;
        public uint VirtualAddress;
        public uint RawSize;
        public uint RawAddress;
        public uint Characteristics;

        public bool IsExecutable => (Characteristics & SectionFlagExecutable) != 0;
        public bool IsReadable   => (Characteristics & SectionFlagReadable)   != 0;
        public bool IsWritable   => (Characteristics & SectionFlagWritable)   != 0;

        /// <summary>True when the given RVA falls within this section's virtual range.</summary>
        public bool Contains(uint rva) => rva >= VirtualAddress && rva < VirtualAddress + VirtualSize;
    }
}

// ── Models ──────────────────────────────────────────────────────────────

public sealed class StripOptions
{
    /// <summary>Path to the input PE (.exe / .dll).</summary>
    public string InputPath { get; set; } = "";

    /// <summary>Path for the output .bin. Falls back to <c>&lt;input&gt;.bin</c> when null.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Extraction mode.</summary>
    public StripMode Mode { get; set; } = StripMode.EntryPointToEnd;

    /// <summary>Section name used by <see cref="StripMode.Section"/>. Defaults to <c>.text</c>.</summary>
    public string? SectionName { get; set; }

    /// <summary>Remove trailing zero-byte padding from the extracted result.</summary>
    public bool TrimTrailingZeros { get; set; } = true;
}

public enum StripMode
{
    /// <summary>Extract from entry point to end of its containing section (default).</summary>
    EntryPointToEnd,

    /// <summary>Extract an entire named section.</summary>
    Section,
}

public sealed class StripResult
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? Error { get; set; }
    public string? Description { get; set; }
    public int OriginalSize { get; set; }
    public int ExtractedSize { get; set; }
    public int TrimmedBytes { get; set; }
    public bool Is64Bit { get; set; }
    public uint EntryPoint { get; set; }
    public ulong ImageBase { get; set; }
    public int SectionsFound { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public sealed class StripAnalysis
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool Is64Bit { get; set; }
    public bool IsManaged { get; set; }
    public uint EntryPoint { get; set; }
    public ulong ImageBase { get; set; }
    public string? EntryPointSection { get; set; }
    public int EstimatedBinSize { get; set; }
    public List<StripSectionInfo> Sections { get; set; } = new();
}

public sealed class StripSectionInfo
{
    public string Name { get; set; } = "";
    public uint VirtualAddress { get; set; }
    public uint VirtualSize { get; set; }
    public uint RawAddress { get; set; }
    public uint RawSize { get; set; }
    public bool IsExecutable { get; set; }
    public bool IsReadable { get; set; }
    public bool IsWritable { get; set; }
    public bool ContainsEntryPoint { get; set; }
}
