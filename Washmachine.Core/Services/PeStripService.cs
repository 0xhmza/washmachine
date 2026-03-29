using Washmachine.Logging;

namespace Washmachine.Services;

/// <summary>
/// Strips a PE (.exe / .dll) into a flat binary (.bin) by extracting raw executable bytes.
/// This is useful for analysis and for workflows where the source PE was specifically built
/// to be flattened, but the output is not automatically guaranteed to be a generic
/// position-independent backdoor payload.
/// </summary>
public sealed class PeStripService
{
    private readonly IAppLogger _logger;

    public PeStripService(IAppLogger logger)
    {
        _logger = logger;
    }

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>
    /// Strip a PE file into a flat binary.
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

            // Validate PE signature
            if (data.Length < 64 || data[0] != 0x4D || data[1] != 0x5A)
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

            result.Is64Bit = pe.Is64Bit;
            result.EntryPoint = pe.AddressOfEntryPoint;
            result.ImageBase = pe.ImageBase;
            result.SectionsFound = pe.Sections.Count;

            _logger.Info($"PE: {(pe.Is64Bit ? "x64" : "x86")} | EP=0x{pe.AddressOfEntryPoint:X} | {pe.Sections.Count} sections");

            // Determine what to extract
            byte[] extracted;
            string description;

            switch (options.Mode)
            {
                case StripMode.EntryPointToEnd:
                    (extracted, description) = ExtractFromEntryPoint(data, pe);
                    break;

                case StripMode.Section:
                    (extracted, description) = ExtractSection(data, pe, options.SectionName ?? ".text");
                    break;

                case StripMode.AllExecutable:
                    (extracted, description) = ExtractAllExecutable(data, pe);
                    break;

                case StripMode.RawRange:
                    if (options.RawOffset < 0 || options.RawLength <= 0)
                    {
                        result.Error = "Raw range mode requires valid --offset and --length.";
                        return result;
                    }
                    (extracted, description) = ExtractRawRange(data, options.RawOffset, options.RawLength);
                    break;

                default:
                    (extracted, description) = ExtractFromEntryPoint(data, pe);
                    break;
            }

            if (extracted.Length == 0)
            {
                result.Error = $"Extraction produced 0 bytes. {description}";
                return result;
            }

            result.ExtractedSize = extracted.Length;
            result.Description = description;

            // Trim trailing zero padding if requested
            if (options.TrimTrailingZeros)
            {
                int trimmed = TrimTrailing(extracted, 0x00);
                if (trimmed < extracted.Length)
                {
                    int removed = extracted.Length - trimmed;
                    extracted = extracted[..trimmed];
                    result.ExtractedSize = extracted.Length;
                    result.TrimmedBytes = removed;
                    _logger.Debug($"Trimmed {removed} trailing zero bytes");
                }
            }

            // Quick sanity checks on extracted bytes
            result.Warnings = ValidateExtracted(extracted, pe.Is64Bit);

            // Write output
            string outputPath = options.OutputPath
                ?? Path.Combine(
                    Path.GetDirectoryName(options.InputPath) ?? ".",
                    Path.GetFileNameWithoutExtension(options.InputPath) + ".bin");

            await File.WriteAllBytesAsync(outputPath, extracted);
            result.OutputPath = outputPath;
            result.Success = true;

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
    /// Analyze a PE without extracting — returns section info for the user to decide.
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

        if (data.Length < 64 || data[0] != 0x4D || data[1] != 0x5A)
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

        analysis.Is64Bit = pe.Is64Bit;
        analysis.EntryPoint = pe.AddressOfEntryPoint;
        analysis.ImageBase = pe.ImageBase;

        foreach (var s in pe.Sections)
        {
            analysis.Sections.Add(new StripSectionInfo
            {
                Name = s.Name,
                VirtualAddress = s.VirtualAddress,
                VirtualSize = s.VirtualSize,
                RawAddress = s.RawAddress,
                RawSize = s.RawSize,
                IsExecutable = s.IsExecutable,
                IsReadable = s.IsReadable,
                IsWritable = s.IsWritable,
                ContainsEntryPoint = pe.AddressOfEntryPoint >= s.VirtualAddress
                    && pe.AddressOfEntryPoint < s.VirtualAddress + s.VirtualSize,
            });
        }

        // Estimate flat binary size from EP to end of .text
        var textSect = pe.Sections.FirstOrDefault(s =>
            pe.AddressOfEntryPoint >= s.VirtualAddress
            && pe.AddressOfEntryPoint < s.VirtualAddress + s.VirtualSize);

        if (textSect != null)
        {
            uint epOffsetInSection = pe.AddressOfEntryPoint - textSect.VirtualAddress;
            analysis.EstimatedBinSize = (int)(textSect.RawSize - epOffsetInSection);
            analysis.EntryPointSection = textSect.Name;
        }

        analysis.Success = true;
        return analysis;
    }

    // ── Extraction modes ────────────────────────────────────────────────

    /// <summary>
    /// Default mode: extract from entry point to the end of its containing section.
    /// This is the most common approach for shellcode — the entry point is the start
    /// of the payload, and everything after it (in the same section) is part of it.
    /// </summary>
    private (byte[] data, string desc) ExtractFromEntryPoint(byte[] peData, MinimalPe pe)
    {
        var section = pe.Sections.FirstOrDefault(s =>
            pe.AddressOfEntryPoint >= s.VirtualAddress
            && pe.AddressOfEntryPoint < s.VirtualAddress + s.VirtualSize);

        if (section == null)
            return (Array.Empty<byte>(), "Entry point does not fall within any section.");

        uint epOffsetInSection = pe.AddressOfEntryPoint - section.VirtualAddress;
        uint fileOffset = section.RawAddress + epOffsetInSection;
        uint length = section.RawSize - epOffsetInSection;

        if (fileOffset + length > peData.Length)
            length = (uint)(peData.Length - fileOffset);

        var buf = new byte[length];
        Array.Copy(peData, fileOffset, buf, 0, length);

        string desc = $"EP→end of {section.Name}: file 0x{fileOffset:X}..0x{fileOffset + length:X} ({length:N0} bytes)";
        _logger.Info(desc);
        return (buf, desc);
    }

    /// <summary>
    /// Extract a specific section by name (default: .text).
    /// Returns the entire raw section data.
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

    /// <summary>
    /// Extract and concatenate all executable sections in order.
    /// Useful for PEs where code spans multiple sections.
    /// </summary>
    private (byte[] data, string desc) ExtractAllExecutable(byte[] peData, MinimalPe pe)
    {
        var execSections = pe.Sections.Where(s => s.IsExecutable).OrderBy(s => s.VirtualAddress).ToList();

        if (execSections.Count == 0)
            return (Array.Empty<byte>(), "No executable sections found.");

        using var ms = new MemoryStream();
        var names = new List<string>();

        foreach (var section in execSections)
        {
            uint length = section.RawSize;
            if (section.RawAddress + length > peData.Length)
                length = (uint)(peData.Length - section.RawAddress);

            ms.Write(peData, (int)section.RawAddress, (int)length);
            names.Add($"{section.Name}({length:N0}B)");
        }

        var buf = ms.ToArray();
        string desc = $"Executable sections: {string.Join(" + ", names)} = {buf.Length:N0} bytes total";
        _logger.Info(desc);
        return (buf, desc);
    }

    /// <summary>
    /// Extract a raw byte range from the file. For advanced users who know
    /// exactly what offset and length they need.
    /// </summary>
    private (byte[] data, string desc) ExtractRawRange(byte[] peData, long offset, int length)
    {
        if (offset + length > peData.Length)
            length = (int)(peData.Length - offset);

        if (length <= 0)
            return (Array.Empty<byte>(), $"Raw range 0x{offset:X}+{length} is out of bounds (file is {peData.Length:N0} bytes).");

        var buf = new byte[length];
        Array.Copy(peData, offset, buf, 0, length);

        string desc = $"Raw range: 0x{offset:X}..0x{offset + length:X} ({length:N0} bytes)";
        _logger.Info(desc);
        return (buf, desc);
    }

    // ── Validation ──────────────────────────────────────────────────────

    private static List<string> ValidateExtracted(byte[] data, bool is64Bit)
    {
        var warnings = new List<string>();

        if (data.Length < 4)
            warnings.Add("Extracted binary is very small (<4 bytes) — may not be valid shellcode.");

        // Check if it starts with common non-code patterns
        if (data.Length >= 2 && data[0] == 0x4D && data[1] == 0x5A)
            warnings.Add("Extracted data starts with MZ header — you may be extracting the whole PE, not flat code.");

        // Check for high proportion of zeros (might be padding)
        int zeros = data.Count(b => b == 0x00);
        double zeroPct = (double)zeros / data.Length;
        if (zeroPct > 0.7)
            warnings.Add($"High zero-byte ratio ({zeroPct:P0}) — extracted data may be mostly padding. Consider --trim.");

        // Check for RET at end (common for well-formed shellcode)
        if (data.Length > 0 && data[^1] != 0xC3 && data[^1] != 0x00)
        {
            // Not necessarily wrong, but informational
        }

        return warnings;
    }

    private static int TrimTrailing(byte[] data, byte value)
    {
        int end = data.Length;
        while (end > 0 && data[end - 1] == value) end--;
        return Math.Max(end, 1); // keep at least 1 byte
    }

    // ── Minimal PE parser (self-contained, no dependency on PeBackdoorService) ──

    private MinimalPe? ParsePeMinimal(byte[] data)
    {
        try
        {
            if (data.Length < 64) return null;

            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);

            // DOS header
            ushort magic = br.ReadUInt16();
            if (magic != 0x5A4D) return null;

            ms.Seek(0x3C, SeekOrigin.Begin);
            uint peOffset = br.ReadUInt32();

            if (peOffset + 24 > data.Length) return null;
            ms.Seek(peOffset, SeekOrigin.Begin);

            // PE signature
            uint peSig = br.ReadUInt32();
            if (peSig != 0x00004550) return null;

            // COFF file header (20 bytes)
            ushort machine = br.ReadUInt16();
            ushort numberOfSections = br.ReadUInt16();
            br.ReadBytes(12); // TimeDateStamp(4), PointerToSymbolTable(4), NumberOfSymbols(4)
            ushort sizeOfOptionalHeader = br.ReadUInt16();
            ushort characteristics = br.ReadUInt16();

            // Optional header
            long optHeaderStart = ms.Position;
            ushort optMagic = br.ReadUInt16();
            bool is64 = optMagic == 0x20B; // PE32+ = 0x20B, PE32 = 0x10B

            br.ReadBytes(2); // MajorLinkerVersion, MinorLinkerVersion
            br.ReadBytes(12); // SizeOfCode(4), SizeOfInitializedData(4), SizeOfUninitializedData(4)

            uint addressOfEntryPoint = br.ReadUInt32();
            br.ReadBytes(is64 ? 4 : 8); // BaseOfCode(4), [BaseOfData(4) for PE32 only]

            ulong imageBase = is64 ? br.ReadUInt64() : br.ReadUInt32();

            uint sectionAlignment = br.ReadUInt32();
            uint fileAlignment = br.ReadUInt32();

            // Skip version fields: 6 × UInt16 + 1 × UInt32 = 16 bytes
            br.ReadBytes(16);

            uint sizeOfImage = br.ReadUInt32();
            uint sizeOfHeaders = br.ReadUInt32();

            // Read section headers
            long sectionHeadersOffset = optHeaderStart + sizeOfOptionalHeader;
            ms.Seek(sectionHeadersOffset, SeekOrigin.Begin);

            var sections = new List<MinimalSection>();
            for (int i = 0; i < numberOfSections; i++)
            {
                var nameBytes = br.ReadBytes(8);
                string name = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                uint virtualSize = br.ReadUInt32();
                uint virtualAddress = br.ReadUInt32();
                uint rawSize = br.ReadUInt32();
                uint rawAddress = br.ReadUInt32();
                br.ReadBytes(12); // PointerToRelocations(4), PointerToLinenumbers(4), NumberOfRelocations(2), NumberOfLinenumbers(2)
                uint chars = br.ReadUInt32();

                sections.Add(new MinimalSection
                {
                    Name = name,
                    VirtualSize = virtualSize,
                    VirtualAddress = virtualAddress,
                    RawSize = rawSize,
                    RawAddress = rawAddress,
                    Characteristics = chars,
                });
            }

            return new MinimalPe
            {
                Is64Bit = is64,
                Machine = machine,
                AddressOfEntryPoint = addressOfEntryPoint,
                ImageBase = imageBase,
                SectionAlignment = sectionAlignment,
                FileAlignment = fileAlignment,
                SizeOfImage = sizeOfImage,
                SizeOfHeaders = sizeOfHeaders,
                Sections = sections,
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

        public bool IsExecutable => (Characteristics & 0x20000000) != 0;
        public bool IsReadable   => (Characteristics & 0x40000000) != 0;
        public bool IsWritable   => (Characteristics & 0x80000000) != 0;
    }
}

// ── Models ──────────────────────────────────────────────────────────────

public sealed class StripOptions
{
    /// <summary>Path to the input PE (.exe/.dll).</summary>
    public string InputPath { get; set; } = "";

    /// <summary>Path for the output .bin file.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Extraction mode.</summary>
    public StripMode Mode { get; set; } = StripMode.EntryPointToEnd;

    /// <summary>Section name for Section mode (default: .text).</summary>
    public string? SectionName { get; set; }

    /// <summary>Raw file offset for RawRange mode.</summary>
    public long RawOffset { get; set; }

    /// <summary>Byte count for RawRange mode.</summary>
    public int RawLength { get; set; }

    /// <summary>Remove trailing zero-byte padding from extracted data.</summary>
    public bool TrimTrailingZeros { get; set; } = true;
}

public enum StripMode
{
    /// <summary>Extract from entry point to end of its containing section (default).</summary>
    EntryPointToEnd,

    /// <summary>Extract an entire named section.</summary>
    Section,

    /// <summary>Extract and concatenate all executable sections.</summary>
    AllExecutable,

    /// <summary>Extract a raw byte range at a specific file offset.</summary>
    RawRange,
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
