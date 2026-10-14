using System.Security.Cryptography;
using System.Text;
using Washmachine.Logging;
using Washmachine.Models;

namespace Washmachine.Services;

/// <summary>
/// Comprehensive PE file analyzer providing deep inspection and visualization.
/// </summary>
public sealed class PeAnalyzerService
{
    // PE constants
    private const ushort IMAGE_DOS_SIGNATURE = 0x5A4D;
    private const uint IMAGE_NT_SIGNATURE = 0x00004550;
    private const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664;
    private const ushort IMAGE_FILE_MACHINE_I386 = 0x014c;
    private const ushort IMAGE_FILE_MACHINE_ARM64 = 0xAA64;
    private const ushort IMAGE_FILE_MACHINE_ARMNT = 0x01c4;
    private const ushort IMAGE_NT_OPTIONAL_HDR32_MAGIC = 0x10b;
    private const ushort IMAGE_NT_OPTIONAL_HDR64_MAGIC = 0x20b;

    // File header characteristics
    private const ushort IMAGE_FILE_RELOCS_STRIPPED = 0x0001;
    private const ushort IMAGE_FILE_EXECUTABLE_IMAGE = 0x0002;
    private const ushort IMAGE_FILE_LINE_NUMS_STRIPPED = 0x0004;
    private const ushort IMAGE_FILE_LOCAL_SYMS_STRIPPED = 0x0008;
    private const ushort IMAGE_FILE_LARGE_ADDRESS_AWARE = 0x0020;
    private const ushort IMAGE_FILE_32BIT_MACHINE = 0x0100;
    private const ushort IMAGE_FILE_DEBUG_STRIPPED = 0x0200;
    private const ushort IMAGE_FILE_REMOVABLE_RUN_FROM_SWAP = 0x0400;
    private const ushort IMAGE_FILE_NET_RUN_FROM_SWAP = 0x0800;
    private const ushort IMAGE_FILE_SYSTEM = 0x1000;
    private const ushort IMAGE_FILE_DLL = 0x2000;
    private const ushort IMAGE_FILE_UP_SYSTEM_ONLY = 0x4000;

    // DLL characteristics
    private const ushort IMAGE_DLLCHARACTERISTICS_HIGH_ENTROPY_VA = 0x0020;
    private const ushort IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE = 0x0040;
    private const ushort IMAGE_DLLCHARACTERISTICS_FORCE_INTEGRITY = 0x0080;
    private const ushort IMAGE_DLLCHARACTERISTICS_NX_COMPAT = 0x0100;
    private const ushort IMAGE_DLLCHARACTERISTICS_NO_ISOLATION = 0x0200;
    private const ushort IMAGE_DLLCHARACTERISTICS_NO_SEH = 0x0400;
    private const ushort IMAGE_DLLCHARACTERISTICS_NO_BIND = 0x0800;
    private const ushort IMAGE_DLLCHARACTERISTICS_APPCONTAINER = 0x1000;
    private const ushort IMAGE_DLLCHARACTERISTICS_WDM_DRIVER = 0x2000;
    private const ushort IMAGE_DLLCHARACTERISTICS_GUARD_CF = 0x4000;
    private const ushort IMAGE_DLLCHARACTERISTICS_TERMINAL_SERVER_AWARE = 0x8000;

    // Section characteristics
    private const uint IMAGE_SCN_CNT_CODE = 0x00000020;
    private const uint IMAGE_SCN_CNT_INITIALIZED_DATA = 0x00000040;
    private const uint IMAGE_SCN_CNT_UNINITIALIZED_DATA = 0x00000080;
    private const uint IMAGE_SCN_MEM_DISCARDABLE = 0x02000000;
    private const uint IMAGE_SCN_MEM_NOT_CACHED = 0x04000000;
    private const uint IMAGE_SCN_MEM_NOT_PAGED = 0x08000000;
    private const uint IMAGE_SCN_MEM_SHARED = 0x10000000;
    private const uint IMAGE_SCN_MEM_EXECUTE = 0x20000000;
    private const uint IMAGE_SCN_MEM_READ = 0x40000000;
    private const uint IMAGE_SCN_MEM_WRITE = 0x80000000;

    // Subsystem types
    private const ushort IMAGE_SUBSYSTEM_NATIVE = 1;
    private const ushort IMAGE_SUBSYSTEM_WINDOWS_GUI = 2;
    private const ushort IMAGE_SUBSYSTEM_WINDOWS_CUI = 3;
    private const ushort IMAGE_SUBSYSTEM_OS2_CUI = 5;
    private const ushort IMAGE_SUBSYSTEM_POSIX_CUI = 7;
    private const ushort IMAGE_SUBSYSTEM_WINDOWS_CE_GUI = 9;
    private const ushort IMAGE_SUBSYSTEM_EFI_APPLICATION = 10;
    private const ushort IMAGE_SUBSYSTEM_EFI_BOOT_SERVICE_DRIVER = 11;
    private const ushort IMAGE_SUBSYSTEM_EFI_RUNTIME_DRIVER = 12;
    private const ushort IMAGE_SUBSYSTEM_EFI_ROM = 13;
    private const ushort IMAGE_SUBSYSTEM_XBOX = 14;

    // Data directory indices
    private static readonly string[] DataDirectoryNames =
    {
        "Export Table", "Import Table", "Resource Table", "Exception Table",
        "Certificate Table", "Base Relocation Table", "Debug", "Architecture",
        "Global Ptr", "TLS Table", "Load Config Table", "Bound Import",
        "IAT", "Delay Import Descriptor", "CLR Runtime Header", "Reserved"
    };

    // Suspicious imports for detection
    private static readonly HashSet<string> SuspiciousImports = new(StringComparer.OrdinalIgnoreCase)
    {
        "VirtualAlloc", "VirtualAllocEx", "VirtualProtect", "VirtualProtectEx",
        "WriteProcessMemory", "ReadProcessMemory", "CreateRemoteThread", "CreateRemoteThreadEx",
        "NtCreateThreadEx", "RtlCreateUserThread", "QueueUserAPC", "SetThreadContext",
        "NtUnmapViewOfSection", "ZwUnmapViewOfSection", "NtAllocateVirtualMemory",
        "NtWriteVirtualMemory", "NtProtectVirtualMemory", "NtQueueApcThread",
        "LoadLibraryA", "LoadLibraryW", "LoadLibraryExA", "LoadLibraryExW",
        "GetProcAddress", "LdrLoadDll", "LdrGetProcedureAddress",
        "OpenProcess", "OpenThread", "NtOpenProcess", "NtOpenThread",
        "CreateProcessA", "CreateProcessW", "CreateProcessInternalA", "CreateProcessInternalW",
        "ShellExecuteA", "ShellExecuteW", "ShellExecuteExA", "ShellExecuteExW",
        "WinExec", "system", "_wsystem"
    };

    private readonly IAppLogger _logger;

    public PeAnalyzerService(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Perform comprehensive PE analysis.
    /// </summary>
    public async Task<PeAnalysisResult> AnalyzeAsync(string filePath, int payloadSize = 0)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("PE file not found", filePath);

        return await Task.Run(() => AnalyzeInternal(filePath, payloadSize));
    }

    private PeAnalysisResult AnalyzeInternal(string filePath, int payloadSize)
    {
        var result = new PeAnalysisResult
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            FileSize = new FileInfo(filePath).Length,
            FileSizeFormatted = FormatFileSize(new FileInfo(filePath).Length)
        };

        try
        {
            // Calculate file hash
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var hashBytes = sha256.ComputeHash(stream);
                result.FileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            // Read and validate DOS header
            var dosResult = ReadDosHeader(br, fs, result);
            if (!dosResult) return result;

            // Read File Header
            ReadFileHeader(br, result);

            // Read Optional Header
            ReadOptionalHeader(br, fs, result);

            // Read Section Headers
            ReadSectionHeaders(br, fs, result);

            // Analyze sections for entropy and caves
            AnalyzeSections(fs, br, result);

            // Read imports
            ReadImports(fs, br, result);

            // Read exports (for DLLs)
            if (result.IsDll)
            {
                ReadExports(fs, br, result);
            }

            // Read TLS if present
            ReadTls(fs, br, result);

            // Analyze security features
            AnalyzeSecurity(result);

            // Calculate overall entropy
            result.OverallEntropy = CalculateFileEntropy(filePath);
            result.IsPossiblyPacked = result.OverallEntropy > 7.0 || 
                                      result.Sections.Any(s => s.IsPossiblyPacked);

            // Detect known packers
            result.PackerDetection = DetectPacker(result);

            // Assess injection feasibility
            result.Feasibility = AssessInjectionFeasibility(result, payloadSize);

            // Generate visualizations
            result.AsciiVisualization = GenerateAsciiVisualization(result);
            result.AsciiSectionMap = GenerateSectionMap(result);
            result.AsciiMemoryLayout = GenerateMemoryLayout(result);

            result.IsValid = true;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ValidationError = ex.Message;
        }

        return result;
    }

    private bool ReadDosHeader(BinaryReader br, FileStream fs, PeAnalysisResult result)
    {
        var dosSignature = br.ReadUInt16();
        if (dosSignature != IMAGE_DOS_SIGNATURE)
        {
            result.IsValid = false;
            result.ValidationError = "Invalid DOS signature (not a valid PE file)";
            return false;
        }

        result.DosHeader.Magic = dosSignature;
        result.DosHeader.Signature = "MZ";

        // Get PE header offset
        fs.Seek(0x3C, SeekOrigin.Begin);
        result.DosHeader.PeHeaderOffset = br.ReadUInt32();
        result.DosHeader.DosStubSize = (int)(result.DosHeader.PeHeaderOffset - 64);

        // Check for Rich header
        fs.Seek(0x80, SeekOrigin.Begin);
        var richCheck = new byte[result.DosHeader.PeHeaderOffset - 0x80];
        if (richCheck.Length > 0)
        {
            br.Read(richCheck, 0, richCheck.Length);
            var richIndex = FindPattern(richCheck, Encoding.ASCII.GetBytes("Rich"));
            if (richIndex >= 0)
            {
                result.DosHeader.HasRichHeader = true;
                result.DosHeader.RichHeaderOffset = (uint)(0x80 + richIndex - 4);
            }
        }

        // Verify PE signature
        fs.Seek(result.DosHeader.PeHeaderOffset, SeekOrigin.Begin);
        var peSignature = br.ReadUInt32();
        if (peSignature != IMAGE_NT_SIGNATURE)
        {
            result.IsValid = false;
            result.ValidationError = "Invalid PE signature";
            return false;
        }

        return true;
    }

    private void ReadFileHeader(BinaryReader br, PeAnalysisResult result)
    {
        result.FileHeader.Machine = br.ReadUInt16();
        result.FileHeader.MachineString = GetMachineString(result.FileHeader.Machine);
        result.Architecture = result.FileHeader.Machine == IMAGE_FILE_MACHINE_AMD64 ? "x64" :
                             result.FileHeader.Machine == IMAGE_FILE_MACHINE_I386 ? "x86" :
                             result.FileHeader.Machine == IMAGE_FILE_MACHINE_ARM64 ? "ARM64" :
                             result.FileHeader.Machine == IMAGE_FILE_MACHINE_ARMNT ? "ARM" : "Unknown";
        result.Is64Bit = result.FileHeader.Machine == IMAGE_FILE_MACHINE_AMD64 || 
                         result.FileHeader.Machine == IMAGE_FILE_MACHINE_ARM64;

        result.FileHeader.NumberOfSections = br.ReadUInt16();
        result.FileHeader.TimeDateStamp = br.ReadUInt32();
        
        // Convert timestamp to DateTime
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        result.FileHeader.TimeDateStampUtc = epoch.AddSeconds(result.FileHeader.TimeDateStamp);
        result.CompileTime = result.FileHeader.TimeDateStampUtc;
        result.CompileTimeFormatted = result.FileHeader.TimeDateStampUtc.ToString("yyyy-MM-dd HH:mm:ss UTC");

        result.FileHeader.PointerToSymbolTable = br.ReadUInt32();
        result.FileHeader.NumberOfSymbols = br.ReadUInt32();
        result.FileHeader.SizeOfOptionalHeader = br.ReadUInt16();
        result.FileHeader.Characteristics = br.ReadUInt16();

        // Parse characteristics
        result.IsDll = (result.FileHeader.Characteristics & IMAGE_FILE_DLL) != 0;
        result.IsDriver = (result.FileHeader.Characteristics & IMAGE_FILE_SYSTEM) != 0;
        result.PeType = result.IsDll ? "DLL" : result.IsDriver ? "Driver" : "EXE";

        result.FileHeader.CharacteristicsList = GetFileCharacteristics(result.FileHeader.Characteristics);
    }

    private void ReadOptionalHeader(BinaryReader br, FileStream fs, PeAnalysisResult result)
    {
        var optHeaderStart = fs.Position;

        result.OptionalHeader.Magic = br.ReadUInt16();
        result.OptionalHeader.MagicString = result.OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR64_MAGIC 
            ? "PE32+ (64-bit)" 
            : "PE32 (32-bit)";

        result.OptionalHeader.MajorLinkerVersion = br.ReadByte();
        result.OptionalHeader.MinorLinkerVersion = br.ReadByte();
        result.OptionalHeader.LinkerVersion = $"{result.OptionalHeader.MajorLinkerVersion}.{result.OptionalHeader.MinorLinkerVersion}";

        result.OptionalHeader.SizeOfCode = br.ReadUInt32();
        result.OptionalHeader.SizeOfInitializedData = br.ReadUInt32();
        result.OptionalHeader.SizeOfUninitializedData = br.ReadUInt32();
        result.OptionalHeader.AddressOfEntryPoint = br.ReadUInt32();
        result.OptionalHeader.BaseOfCode = br.ReadUInt32();

        if (result.Is64Bit)
        {
            // PE32+
            result.OptionalHeader.ImageBase = br.ReadUInt64();
        }
        else
        {
            // PE32
            result.OptionalHeader.BaseOfData = br.ReadUInt32();
            result.OptionalHeader.ImageBase = br.ReadUInt32();
        }

        result.OptionalHeader.SectionAlignment = br.ReadUInt32();
        result.OptionalHeader.FileAlignment = br.ReadUInt32();

        result.OptionalHeader.MajorOSVersion = br.ReadUInt16();
        result.OptionalHeader.MinorOSVersion = br.ReadUInt16();
        result.OptionalHeader.OSVersion = $"{result.OptionalHeader.MajorOSVersion}.{result.OptionalHeader.MinorOSVersion}";

        result.OptionalHeader.MajorImageVersion = br.ReadUInt16();
        result.OptionalHeader.MinorImageVersion = br.ReadUInt16();
        result.OptionalHeader.ImageVersion = $"{result.OptionalHeader.MajorImageVersion}.{result.OptionalHeader.MinorImageVersion}";

        result.OptionalHeader.MajorSubsystemVersion = br.ReadUInt16();
        result.OptionalHeader.MinorSubsystemVersion = br.ReadUInt16();
        result.OptionalHeader.SubsystemVersion = $"{result.OptionalHeader.MajorSubsystemVersion}.{result.OptionalHeader.MinorSubsystemVersion}";

        br.ReadUInt32(); // Win32VersionValue (reserved)

        result.OptionalHeader.SizeOfImage = br.ReadUInt32();
        result.OptionalHeader.SizeOfHeaders = br.ReadUInt32();
        result.OptionalHeader.Checksum = br.ReadUInt32();

        result.OptionalHeader.Subsystem = br.ReadUInt16();
        result.OptionalHeader.SubsystemString = GetSubsystemString(result.OptionalHeader.Subsystem);
        result.Subsystem = result.OptionalHeader.SubsystemString;

        result.OptionalHeader.DllCharacteristics = br.ReadUInt16();
        result.OptionalHeader.DllCharacteristicsList = GetDllCharacteristics(result.OptionalHeader.DllCharacteristics);

        if (result.Is64Bit)
        {
            result.OptionalHeader.SizeOfStackReserve = br.ReadUInt64();
            result.OptionalHeader.SizeOfStackCommit = br.ReadUInt64();
            result.OptionalHeader.SizeOfHeapReserve = br.ReadUInt64();
            result.OptionalHeader.SizeOfHeapCommit = br.ReadUInt64();
        }
        else
        {
            result.OptionalHeader.SizeOfStackReserve = br.ReadUInt32();
            result.OptionalHeader.SizeOfStackCommit = br.ReadUInt32();
            result.OptionalHeader.SizeOfHeapReserve = br.ReadUInt32();
            result.OptionalHeader.SizeOfHeapCommit = br.ReadUInt32();
        }

        br.ReadUInt32(); // LoaderFlags (reserved)
        result.OptionalHeader.NumberOfRvaAndSizes = br.ReadUInt32();

        // Read data directories
        var numDirs = Math.Min((int)result.OptionalHeader.NumberOfRvaAndSizes, 16);
        for (int i = 0; i < numDirs; i++)
        {
            var dir = new DataDirectoryInfo
            {
                Index = i,
                Name = i < DataDirectoryNames.Length ? DataDirectoryNames[i] : $"Unknown ({i})",
                VirtualAddress = br.ReadUInt32(),
                Size = br.ReadUInt32()
            };
            result.OptionalHeader.DataDirectories.Add(dir);
        }

        // Check for .NET
        var comDescriptor = result.OptionalHeader.DataDirectories.ElementAtOrDefault(14);
        result.IsDotNet = comDescriptor?.IsPresent == true;

        result.MachineType = result.FileHeader.MachineString;
    }

    private void ReadSectionHeaders(BinaryReader br, FileStream fs, PeAnalysisResult result)
    {
        for (int i = 0; i < result.FileHeader.NumberOfSections; i++)
        {
            var nameBytes = br.ReadBytes(8);
            var name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

            var section = new SectionAnalysis
            {
                Index = i,
                Name = name,
                VirtualSize = br.ReadUInt32(),
                VirtualAddress = br.ReadUInt32(),
                RawSize = br.ReadUInt32(),
                RawAddress = br.ReadUInt32(),
                PointerToRelocations = br.ReadUInt32(),
                PointerToLinenumbers = br.ReadUInt32(),
                NumberOfRelocations = br.ReadUInt16(),
                NumberOfLinenumbers = br.ReadUInt16(),
                Characteristics = br.ReadUInt32()
            };

            // Parse characteristics
            section.IsExecutable = (section.Characteristics & IMAGE_SCN_MEM_EXECUTE) != 0;
            section.IsReadable = (section.Characteristics & IMAGE_SCN_MEM_READ) != 0;
            section.IsWritable = (section.Characteristics & IMAGE_SCN_MEM_WRITE) != 0;
            section.ContainsCode = (section.Characteristics & IMAGE_SCN_CNT_CODE) != 0;
            section.ContainsInitializedData = (section.Characteristics & IMAGE_SCN_CNT_INITIALIZED_DATA) != 0;
            section.ContainsUninitializedData = (section.Characteristics & IMAGE_SCN_CNT_UNINITIALIZED_DATA) != 0;
            section.IsDiscardable = (section.Characteristics & IMAGE_SCN_MEM_DISCARDABLE) != 0;
            section.NotCached = (section.Characteristics & IMAGE_SCN_MEM_NOT_CACHED) != 0;
            section.NotPaged = (section.Characteristics & IMAGE_SCN_MEM_NOT_PAGED) != 0;
            section.IsShared = (section.Characteristics & IMAGE_SCN_MEM_SHARED) != 0;

            section.CharacteristicsList = GetSectionCharacteristics(section.Characteristics);
            section.PermissionsString = $"{(section.IsReadable ? "R" : "-")}{(section.IsWritable ? "W" : "-")}{(section.IsExecutable ? "X" : "-")}";

            // Calculate padding/usage
            section.UsedSize = (int)Math.Min(section.VirtualSize, section.RawSize);
            section.PaddingSize = (int)(section.RawSize > section.VirtualSize ? section.RawSize - section.VirtualSize : 0);
            section.UsagePercentage = section.RawSize > 0 ? (double)section.VirtualSize / section.RawSize * 100 : 0;

            result.Sections.Add(section);
        }
    }

    private void AnalyzeSections(FileStream fs, BinaryReader br, PeAnalysisResult result)
    {
        foreach (var section in result.Sections)
        {
            if (section.RawSize == 0 || section.RawAddress == 0)
                continue;

            // Read section data
            fs.Seek(section.RawAddress, SeekOrigin.Begin);
            var sectionData = new byte[Math.Min(section.RawSize, (uint)(fs.Length - section.RawAddress))];
            br.Read(sectionData, 0, sectionData.Length);

            // Calculate entropy
            section.Entropy = CalculateEntropy(sectionData);
            section.EntropyAssessment = section.Entropy switch
            {
                < 1.0 => "Very low (mostly zeros/padding)",
                < 4.0 => "Low (text/ASCII data)",
                < 6.0 => "Normal (typical code/data)",
                < 7.0 => "High (compressed data)",
                < 7.5 => "Very high (encrypted/packed)",
                _ => "Extremely high (encrypted)"
            };
            section.IsPossiblyEncrypted = section.Entropy > 7.5;
            section.IsPossiblyPacked = section.Entropy > 7.0;

            // Generate ASCII usage bar
            var barLength = 20;
            var usedBlocks = Math.Clamp((int)(section.UsagePercentage / 100 * barLength), 0, barLength);
            section.AsciiBar = $"[{new string('█', usedBlocks)}{new string('░', barLength - usedBlocks)}]";

            // Find code caves in this section
            if (section.IsExecutable || section.Name == ".text")
            {
                var caves = FindCodeCavesInSection(sectionData, section);
                section.CodeCaves.AddRange(caves);
                result.CodeCaves.AddRange(caves);
            }
        }
    }

    private List<CodeCaveAnalysis> FindCodeCavesInSection(byte[] data, SectionAnalysis section)
    {
        var caves = new List<CodeCaveAnalysis>();
        int minCaveSize = 32; // Minimum useful cave size
        int currentCaveStart = -1;
        byte currentFillByte = 0;

        for (int i = 0; i < data.Length; i++)
        {
            bool isNullOrInt3 = data[i] == 0x00 || data[i] == 0xCC;

            if (isNullOrInt3)
            {
                if (currentCaveStart == -1)
                {
                    currentCaveStart = i;
                    currentFillByte = data[i];
                }
            }
            else
            {
                if (currentCaveStart != -1)
                {
                    int caveSize = i - currentCaveStart;
                    if (caveSize >= minCaveSize)
                    {
                        caves.Add(new CodeCaveAnalysis
                        {
                            SectionName = section.Name,
                            FileOffset = (uint)(section.RawAddress + currentCaveStart),
                            VirtualAddress = (uint)(section.VirtualAddress + currentCaveStart),
                            Size = caveSize,
                            FillByte = currentFillByte,
                            FillByteDescription = currentFillByte == 0x00 ? "NULL (0x00)" : "INT3 breakpoint (0xCC)",
                            IsExecutable = section.IsExecutable,
                            SuitableForInjection = section.IsExecutable && caveSize >= 64,
                            AssessmentNote = GetCaveAssessment(caveSize, section.IsExecutable)
                        });
                    }
                    currentCaveStart = -1;
                }
            }
        }

        // Check for cave at end of section
        if (currentCaveStart != -1)
        {
            int caveSize = data.Length - currentCaveStart;
            if (caveSize >= minCaveSize)
            {
                caves.Add(new CodeCaveAnalysis
                {
                    SectionName = section.Name,
                    FileOffset = (uint)(section.RawAddress + currentCaveStart),
                    VirtualAddress = (uint)(section.VirtualAddress + currentCaveStart),
                    Size = caveSize,
                    FillByte = currentFillByte,
                    FillByteDescription = currentFillByte == 0x00 ? "NULL (0x00)" : "INT3 breakpoint (0xCC)",
                    IsExecutable = section.IsExecutable,
                    SuitableForInjection = section.IsExecutable && caveSize >= 64,
                    AssessmentNote = GetCaveAssessment(caveSize, section.IsExecutable)
                });
            }
        }

        return caves.OrderByDescending(c => c.Size).ToList();
    }

    private string GetCaveAssessment(int size, bool isExecutable)
    {
        if (!isExecutable) return "Not executable - requires VirtualProtect";
        if (size < 64) return "Too small for most shellcode";
        if (size < 256) return "Suitable for small stubs only";
        if (size < 1024) return "Good for medium payloads";
        if (size < 4096) return "Excellent for most payloads";
        return "Large cave - ideal for complex payloads";
    }

    private void ReadImports(FileStream fs, BinaryReader br, PeAnalysisResult result)
    {
        var importDir = result.OptionalHeader.DataDirectories.ElementAtOrDefault(1);
        if (importDir == null || !importDir.IsPresent)
            return;

        try
        {
            var importRva = importDir.VirtualAddress;
            var importOffset = RvaToOffset(importRva, result.Sections);
            if (importOffset == 0) return;

            fs.Seek(importOffset, SeekOrigin.Begin);

            while (true)
            {
                var originalFirstThunk = br.ReadUInt32();
                var timeDateStamp = br.ReadUInt32();
                var forwarderChain = br.ReadUInt32();
                var nameRva = br.ReadUInt32();
                var firstThunk = br.ReadUInt32();

                if (nameRva == 0) break;

                var dll = new ImportedDll
                {
                    Name = ReadAsciiString(fs, br, RvaToOffset(nameRva, result.Sections)),
                    IsDelayLoaded = false
                };

                // Read imported functions
                var thunkRva = originalFirstThunk != 0 ? originalFirstThunk : firstThunk;
                var thunkOffset = RvaToOffset(thunkRva, result.Sections);
                if (thunkOffset > 0)
                {
                    var savedPos = fs.Position;
                    fs.Seek(thunkOffset, SeekOrigin.Begin);

                    while (true)
                    {
                        ulong thunkData = result.Is64Bit ? br.ReadUInt64() : br.ReadUInt32();
                        if (thunkData == 0) break;

                        var func = new ImportedFunction();

                        // Check if import by ordinal
                        ulong ordinalFlag = result.Is64Bit ? 0x8000000000000000UL : 0x80000000UL;
                        if ((thunkData & ordinalFlag) != 0)
                        {
                            func.IsByOrdinal = true;
                            func.Ordinal = (ushort)(thunkData & 0xFFFF);
                            func.Name = $"Ordinal #{func.Ordinal}";
                        }
                        else
                        {
                            var hintNameRva = (uint)(thunkData & (result.Is64Bit ? 0x7FFFFFFFUL : 0x7FFFFFFFUL));
                            var hintNameOffset = RvaToOffset(hintNameRva, result.Sections);
                            if (hintNameOffset > 0)
                            {
                                var savedPos2 = fs.Position;
                                fs.Seek(hintNameOffset, SeekOrigin.Begin);
                                func.Hint = br.ReadUInt16();
                                func.Name = ReadAsciiStringInline(br);
                                fs.Seek(savedPos2, SeekOrigin.Begin);
                            }
                        }

                        // Check if suspicious
                        if (SuspiciousImports.Contains(func.Name))
                        {
                            func.IsSuspicious = true;
                            func.SuspiciousReason = "Commonly used in shellcode/malware";
                        }

                        dll.Functions.Add(func);
                    }

                    fs.Seek(savedPos, SeekOrigin.Begin);
                }

                result.Imports.Add(dll);
            }
        }
        catch
        {
            // Import parsing is best-effort
        }
    }

    private void ReadExports(FileStream fs, BinaryReader br, PeAnalysisResult result)
    {
        var exportDir = result.OptionalHeader.DataDirectories.ElementAtOrDefault(0);
        if (exportDir == null || !exportDir.IsPresent)
            return;

        try
        {
            var exportOffset = RvaToOffset(exportDir.VirtualAddress, result.Sections);
            if (exportOffset == 0) return;

            fs.Seek(exportOffset, SeekOrigin.Begin);

            br.ReadUInt32(); // Characteristics
            br.ReadUInt32(); // TimeDateStamp
            br.ReadUInt16(); // MajorVersion
            br.ReadUInt16(); // MinorVersion
            var nameRva = br.ReadUInt32();
            var ordinalBase = br.ReadUInt32();
            var numberOfFunctions = br.ReadUInt32();
            var numberOfNames = br.ReadUInt32();
            var addressOfFunctions = br.ReadUInt32();
            var addressOfNames = br.ReadUInt32();
            var addressOfNameOrdinals = br.ReadUInt32();

            // Read function RVAs
            var functionsOffset = RvaToOffset(addressOfFunctions, result.Sections);
            var namesOffset = RvaToOffset(addressOfNames, result.Sections);
            var ordinalsOffset = RvaToOffset(addressOfNameOrdinals, result.Sections);

            if (functionsOffset == 0) return;

            // Read names and ordinals
            for (uint i = 0; i < numberOfNames && i < 1000; i++)
            {
                fs.Seek(namesOffset + i * 4, SeekOrigin.Begin);
                var nameRvaEntry = br.ReadUInt32();
                var funcName = ReadAsciiString(fs, br, RvaToOffset(nameRvaEntry, result.Sections));

                fs.Seek(ordinalsOffset + i * 2, SeekOrigin.Begin);
                var ordinalIndex = br.ReadUInt16();

                fs.Seek(functionsOffset + ordinalIndex * 4, SeekOrigin.Begin);
                var funcRva = br.ReadUInt32();

                var export = new ExportedFunction
                {
                    Name = funcName,
                    Ordinal = (ushort)(ordinalBase + ordinalIndex),
                    Rva = funcRva
                };

                // Check if forwarded
                if (funcRva >= exportDir.VirtualAddress && 
                    funcRva < exportDir.VirtualAddress + exportDir.Size)
                {
                    export.IsForwarded = true;
                    export.ForwardedTo = ReadAsciiString(fs, br, RvaToOffset(funcRva, result.Sections));
                }

                result.Exports.Add(export);
            }
        }
        catch
        {
            // Export parsing is best-effort
        }
    }

    private void ReadTls(FileStream fs, BinaryReader br, PeAnalysisResult result)
    {
        var tlsDir = result.OptionalHeader.DataDirectories.ElementAtOrDefault(9);
        if (tlsDir == null || !tlsDir.IsPresent)
            return;

        try
        {
            var tlsOffset = RvaToOffset(tlsDir.VirtualAddress, result.Sections);
            if (tlsOffset == 0) return;

            fs.Seek(tlsOffset, SeekOrigin.Begin);

            var tls = new TlsInfo();

            if (result.Is64Bit)
            {
                tls.StartAddressOfRawData = br.ReadUInt64();
                tls.EndAddressOfRawData = br.ReadUInt64();
                tls.AddressOfIndex = br.ReadUInt64();
                tls.AddressOfCallbacks = br.ReadUInt64();
                tls.SizeOfZeroFill = br.ReadUInt32();
                tls.Characteristics = br.ReadUInt32();

                // Read callbacks
                if (tls.AddressOfCallbacks != 0)
                {
                    var callbacksRva = (uint)(tls.AddressOfCallbacks - result.OptionalHeader.ImageBase);
                    var callbacksOffset = RvaToOffset(callbacksRva, result.Sections);
                    if (callbacksOffset > 0)
                    {
                        fs.Seek(callbacksOffset, SeekOrigin.Begin);
                        while (true)
                        {
                            var callback = br.ReadUInt64();
                            if (callback == 0) break;
                            tls.CallbackAddresses.Add(callback);
                            tls.NumberOfCallbacks++;
                            if (tls.NumberOfCallbacks > 100) break;
                        }
                    }
                }
            }
            else
            {
                tls.StartAddressOfRawData = br.ReadUInt32();
                tls.EndAddressOfRawData = br.ReadUInt32();
                tls.AddressOfIndex = br.ReadUInt32();
                tls.AddressOfCallbacks = br.ReadUInt32();
                tls.SizeOfZeroFill = br.ReadUInt32();
                tls.Characteristics = br.ReadUInt32();

                // Read callbacks
                if (tls.AddressOfCallbacks != 0)
                {
                    var callbacksRva = (uint)(tls.AddressOfCallbacks - result.OptionalHeader.ImageBase);
                    var callbacksOffset = RvaToOffset(callbacksRva, result.Sections);
                    if (callbacksOffset > 0)
                    {
                        fs.Seek(callbacksOffset, SeekOrigin.Begin);
                        while (true)
                        {
                            var callback = br.ReadUInt32();
                            if (callback == 0) break;
                            tls.CallbackAddresses.Add(callback);
                            tls.NumberOfCallbacks++;
                            if (tls.NumberOfCallbacks > 100) break;
                        }
                    }
                }
            }

            result.Tls = tls;
        }
        catch
        {
            // TLS parsing is best-effort
        }
    }

    private void AnalyzeSecurity(PeAnalysisResult result)
    {
        var security = result.Security;
        var dllChars = result.OptionalHeader.DllCharacteristics;

        security.HasAslr = (dllChars & IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE) != 0;
        security.HasHighEntropyVa = (dllChars & IMAGE_DLLCHARACTERISTICS_HIGH_ENTROPY_VA) != 0;
        security.NxCompat = (dllChars & IMAGE_DLLCHARACTERISTICS_NX_COMPAT) != 0;
        security.HasDep = security.NxCompat;
        security.HasSeh = (dllChars & IMAGE_DLLCHARACTERISTICS_NO_SEH) == 0;
        security.ForceIntegrity = (dllChars & IMAGE_DLLCHARACTERISTICS_FORCE_INTEGRITY) != 0;
        security.NoIsolation = (dllChars & IMAGE_DLLCHARACTERISTICS_NO_ISOLATION) != 0;
        security.NoBind = (dllChars & IMAGE_DLLCHARACTERISTICS_NO_BIND) != 0;
        security.AppContainer = (dllChars & IMAGE_DLLCHARACTERISTICS_APPCONTAINER) != 0;
        security.WdmDriver = (dllChars & IMAGE_DLLCHARACTERISTICS_WDM_DRIVER) != 0;
        security.GuardCf = (dllChars & IMAGE_DLLCHARACTERISTICS_GUARD_CF) != 0;
        security.HasCfg = security.GuardCf;
        security.TerminalServerAware = (dllChars & IMAGE_DLLCHARACTERISTICS_TERMINAL_SERVER_AWARE) != 0;

        // Check for Authenticode signature
        var securityDir = result.OptionalHeader.DataDirectories.ElementAtOrDefault(4);
        if (securityDir?.IsPresent == true)
        {
            security.HasAuthenticode = true;
            security.SignatureOffset = securityDir.VirtualAddress;
            security.SignatureSize = securityDir.Size;
            security.SignatureInfo = $"Authenticode signature present ({securityDir.Size} bytes)";
        }

        // Build protection lists
        if (security.HasAslr) security.EnabledProtections.Add("ASLR");
        if (security.HasHighEntropyVa) security.EnabledProtections.Add("High Entropy VA");
        if (security.HasDep) security.EnabledProtections.Add("DEP/NX");
        if (security.HasCfg) security.EnabledProtections.Add("Control Flow Guard");
        if (security.HasSeh) security.EnabledProtections.Add("SEH");
        if (security.ForceIntegrity) security.EnabledProtections.Add("Force Integrity");
        if (security.HasAuthenticode) security.EnabledProtections.Add("Code Signing");

        if (!security.HasAslr) security.MissingProtections.Add("ASLR");
        if (!security.HasDep) security.MissingProtections.Add("DEP/NX");
        if (!security.HasCfg) security.MissingProtections.Add("Control Flow Guard");
        if (!security.HasAuthenticode) security.MissingProtections.Add("Code Signing");

        // Calculate security score
        security.SecurityScore = 0;
        if (security.HasAslr) security.SecurityScore += 20;
        if (security.HasHighEntropyVa) security.SecurityScore += 10;
        if (security.HasDep) security.SecurityScore += 20;
        if (security.HasCfg) security.SecurityScore += 25;
        if (security.HasAuthenticode) security.SecurityScore += 25;

        security.SecurityAssessment = security.SecurityScore switch
        {
            >= 90 => "Excellent - Strong security posture",
            >= 70 => "Good - Most protections enabled",
            >= 50 => "Moderate - Some protections missing",
            >= 30 => "Weak - Limited protections",
            _ => "Poor - Minimal security features"
        };
    }

    private InjectionFeasibility AssessInjectionFeasibility(PeAnalysisResult result, int payloadSize)
    {
        var feasibility = new InjectionFeasibility
        {
            RequiredSpace = payloadSize > 0 ? payloadSize + 200 : 500, // Add carrier overhead
            AvailableCodeCaveSpace = result.TotalCodeCaveSpace,
            CanInject = true
        };

        // Check blocking conditions
        if (result.IsDotNet)
        {
            feasibility.CanInject = false;
            feasibility.BlockingReasons.Add(".NET assemblies cannot be backdoored using PE injection");
        }

        if (result.Security.HasCfg)
        {
            feasibility.Warnings.Add("Control Flow Guard enabled - may detect injected code");
        }

        if (result.Security.HasAuthenticode)
        {
            feasibility.Warnings.Add("Authenticode signature will be invalidated");
            feasibility.Recommendations.Add("Enable 'Remove Signature' option");
        }

        if (result.IsPossiblyPacked)
        {
            feasibility.Warnings.Add("PE appears to be packed - injection may fail or be detected at runtime");
        }

        // Code Cave feasibility
        var largestCave = result.CodeCaves.FirstOrDefault();
        feasibility.CodeCave.AvailableSpace = result.LargestCodeCave;
        if (largestCave != null && largestCave.Size >= feasibility.RequiredSpace)
        {
            feasibility.CodeCave.IsFeasible = true;
            feasibility.CodeCave.Status = "Available";
            feasibility.CodeCave.Reason = $"Largest cave: {largestCave.Size} bytes in {largestCave.SectionName}";
        }
        else if (largestCave != null)
        {
            feasibility.CodeCave.IsFeasible = false;
            feasibility.CodeCave.Status = "Limited";
            feasibility.CodeCave.Reason = $"Largest cave ({largestCave.Size} bytes) < required ({feasibility.RequiredSpace} bytes)";
        }
        else
        {
            feasibility.CodeCave.IsFeasible = false;
            feasibility.CodeCave.Status = "Unavailable";
            feasibility.CodeCave.Reason = "No code caves found";
        }

        // New Section feasibility
        feasibility.NewSection.IsFeasible = true;
        feasibility.NewSection.Status = "Available";
        feasibility.NewSection.Reason = "Can always add new section";
        feasibility.NewSection.AvailableSpace = 0x10000; // 64KB typical max
        feasibility.NewSection.Notes.Add("Adds new .extra section");
        feasibility.NewSection.Notes.Add("Most reliable method");
        if (result.Security.HasAuthenticode)
        {
            feasibility.NewSection.Notes.Add("Will invalidate signature");
        }

        // Section Extension feasibility
        var textSection = result.Sections.FirstOrDefault(s => s.Name == ".text" || s.IsExecutable);
        if (textSection != null)
        {
            feasibility.SectionExtension.IsFeasible = true;
            feasibility.SectionExtension.Status = "Available";
            feasibility.SectionExtension.Reason = $"Can extend {textSection.Name} section";
            feasibility.SectionExtension.AvailableSpace = (int)(result.OptionalHeader.SectionAlignment - textSection.RawSize % result.OptionalHeader.SectionAlignment);
            feasibility.SectionExtension.Notes.Add("Less detectable than new section");
        }
        else
        {
            feasibility.SectionExtension.IsFeasible = false;
            feasibility.SectionExtension.Status = "Unavailable";
            feasibility.SectionExtension.Reason = "No executable section found";
        }

        // TLS callback is theoretically feasible for many PEs, but the current
        // backdoor injector does not implement a TLS carrier path yet.
        feasibility.TlsCallback.IsFeasible = false;
        feasibility.TlsCallback.Status = "Not implemented";
        feasibility.TlsCallback.Reason = result.HasTls
            ? $"Target has TLS ({result.Tls?.NumberOfCallbacks ?? 0} callbacks), but the injector cannot use it yet"
            : "Injector only supports entry-point hijack today";
        feasibility.TlsCallback.Notes.Add("The current backdoor path supports entry-point hijack only.");

        // Entry Point Hijack
        feasibility.EntryPointHijack.IsFeasible = true;
        feasibility.EntryPointHijack.Status = "Available";
        feasibility.EntryPointHijack.Reason = $"Entry point at 0x{result.OptionalHeader.AddressOfEntryPoint:X}";
        if (!result.IsDll)
        {
            feasibility.EntryPointHijack.IsRecommended = true;
        }

        // Determine recommended method
        if (feasibility.CodeCave.IsFeasible)
        {
            feasibility.RecommendedMethod = "Code Cave";
            feasibility.RecommendedReason = "Uses existing space, harder to detect";
        }
        else
        {
            feasibility.RecommendedMethod = "New Section";
            feasibility.RecommendedReason = "Most reliable, works with any payload size";
        }

        return feasibility;
    }

    private string GenerateAsciiVisualization(PeAnalysisResult result)
    {
        var sb = new StringBuilder();
        var width = 60;

        sb.AppendLine("╔" + new string('═', width - 2) + "╗");
        sb.AppendLine($"║ {"PE FILE ANALYSIS",-56} ║");
        sb.AppendLine("╠" + new string('═', width - 2) + "╣");
        sb.AppendLine($"║ File: {TruncateString(result.FileName, 50),-50} ║");
        sb.AppendLine($"║ Type: {result.PeType,-10} Arch: {result.Architecture,-6} Size: {result.FileSizeFormatted,-14} ║");
        sb.AppendLine($"║ Compile: {result.CompileTimeFormatted,-47} ║");
        sb.AppendLine("╠" + new string('═', width - 2) + "╣");

        // Security
        var secBar = GenerateProgressBar(result.Security.SecurityScore, 100, 20);
        sb.AppendLine($"║ Security Score: {secBar} {result.Security.SecurityScore,3}% ║");
        
        var protections = string.Join(", ", result.Security.EnabledProtections.Take(4));
        sb.AppendLine($"║ Protections: {TruncateString(protections, 43),-43} ║");

        sb.AppendLine("╠" + new string('═', width - 2) + "╣");

        // Sections summary
        sb.AppendLine($"║ SECTIONS ({result.TotalSections})                                          ║");
        sb.AppendLine("║" + new string('─', width - 2) + "║");
        sb.AppendLine("║  Name     VirtAddr   Size      Perm  Entropy           ║");
        sb.AppendLine("║" + new string('─', width - 2) + "║");

        foreach (var section in result.Sections.Take(8))
        {
            var entropyBar = GenerateProgressBar((int)(section.Entropy * 12.5), 100, 8);
            sb.AppendLine($"║  {section.Name,-8} 0x{section.VirtualAddress:X6} {section.RawSize,8}  {section.PermissionsString}  {entropyBar} {section.Entropy:F1} ║");
        }

        if (result.Sections.Count > 8)
        {
            sb.AppendLine($"║  ... and {result.Sections.Count - 8} more sections                           ║");
        }

        sb.AppendLine("╠" + new string('═', width - 2) + "╣");

        // Code Caves
        sb.AppendLine($"║ CODE CAVES ({result.TotalCodeCaves})                                       ║");
        if (result.CodeCaves.Count > 0)
        {
            sb.AppendLine("║" + new string('─', width - 2) + "║");
            foreach (var cave in result.CodeCaves.Take(5))
            {
                var status = cave.SuitableForInjection ? "✓" : "○";
                sb.AppendLine($"║  {status} {cave.SectionName,-8} @ 0x{cave.VirtualAddress:X6}  {cave.Size,6} bytes       ║");
            }
            if (result.CodeCaves.Count > 5)
            {
                sb.AppendLine($"║  ... and {result.CodeCaves.Count - 5} more caves ({result.TotalCodeCaveSpace - result.CodeCaves.Take(5).Sum(c => c.Size)} bytes)              ║");
            }
        }
        else
        {
            sb.AppendLine("║  No code caves found                                    ║");
        }

        sb.AppendLine("╠" + new string('═', width - 2) + "╣");

        // Injection Feasibility
        sb.AppendLine("║ INJECTION FEASIBILITY                                    ║");
        sb.AppendLine("║" + new string('─', width - 2) + "║");
        sb.AppendLine($"║  Code Cave:     {GetStatusIcon(feasibility: result.Feasibility.CodeCave)} {result.Feasibility.CodeCave.Status,-40} ║");
        sb.AppendLine($"║  New Section:   {GetStatusIcon(feasibility: result.Feasibility.NewSection)} {result.Feasibility.NewSection.Status,-40} ║");
        sb.AppendLine($"║  Section Ext:   {GetStatusIcon(feasibility: result.Feasibility.SectionExtension)} {result.Feasibility.SectionExtension.Status,-40} ║");
        sb.AppendLine($"║  TLS Callback:  {GetStatusIcon(feasibility: result.Feasibility.TlsCallback)} {result.Feasibility.TlsCallback.Status,-40} ║");
        sb.AppendLine("║" + new string('─', width - 2) + "║");
        sb.AppendLine($"║  Recommended: {result.Feasibility.RecommendedMethod,-43} ║");

        sb.AppendLine("╚" + new string('═', width - 2) + "╝");

        return sb.ToString();
    }

    private string GenerateSectionMap(PeAnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("┌─────────────────────────────────────────────────────────────┐");
        sb.AppendLine("│                    SECTION MEMORY MAP                        │");
        sb.AppendLine("├─────────────────────────────────────────────────────────────┤");

        var totalSize = result.Sections.Sum(s => s.RawSize);
        if (totalSize == 0) totalSize = 1;

        foreach (var section in result.Sections)
        {
            var percentage = (double)section.RawSize / totalSize * 100;
            var barLength = (int)(percentage / 100 * 40);
            barLength = Math.Max(1, Math.Min(40, barLength));

            var bar = new string('█', barLength) + new string('░', 40 - barLength);
            var sizeStr = FormatFileSize(section.RawSize);
            
            sb.AppendLine($"│ {section.Name,-8} │{bar}│ {sizeStr,8} {section.PermissionsString} │");
        }

        sb.AppendLine("└─────────────────────────────────────────────────────────────┘");
        return sb.ToString();
    }

    private string GenerateMemoryLayout(PeAnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("┌─────────────────────────────────────────┐");
        sb.AppendLine("│           VIRTUAL ADDRESS SPACE         │");
        sb.AppendLine("├─────────────────────────────────────────┤");
        sb.AppendLine($"│  Image Base: 0x{result.OptionalHeader.ImageBase:X16}  │");
        sb.AppendLine("├─────────────────────────────────────────┤");
        sb.AppendLine($"│  0x{0:X8}  ┌───────────────────────┐  │");
        sb.AppendLine("│            │   PE Headers          │  │");
        sb.AppendLine($"│  0x{result.OptionalHeader.SizeOfHeaders:X8}  ├───────────────────────┤  │");

        uint currentRva = result.OptionalHeader.SizeOfHeaders;
        foreach (var section in result.Sections)
        {
            var sectionChar = section.IsExecutable ? '█' : section.IsWritable ? '▓' : '░';
            sb.AppendLine($"│            │ {section.Name,-8} {new string(sectionChar, 10)}   │  │");
            currentRva = section.VirtualAddress + section.VirtualSize;
            sb.AppendLine($"│  0x{currentRva:X8}  ├───────────────────────┤  │");
        }

        sb.AppendLine("│            │   (Unmapped)          │  │");
        sb.AppendLine($"│  0x{result.OptionalHeader.SizeOfImage:X8}  └───────────────────────┘  │");
        sb.AppendLine("├─────────────────────────────────────────┤");
        sb.AppendLine($"│  Entry Point: 0x{result.OptionalHeader.AddressOfEntryPoint:X8}              │");
        sb.AppendLine("└─────────────────────────────────────────┘");

        return sb.ToString();
    }

    // Helper methods
    private static string GetMachineString(ushort machine) => machine switch
    {
        IMAGE_FILE_MACHINE_AMD64 => "AMD64 (x64)",
        IMAGE_FILE_MACHINE_I386 => "i386 (x86)",
        IMAGE_FILE_MACHINE_ARM64 => "ARM64",
        IMAGE_FILE_MACHINE_ARMNT => "ARM",
        0x01c0 => "ARM",
        0x0200 => "IA64",
        _ => $"Unknown (0x{machine:X4})"
    };

    private static string GetSubsystemString(ushort subsystem) => subsystem switch
    {
        IMAGE_SUBSYSTEM_NATIVE => "Native",
        IMAGE_SUBSYSTEM_WINDOWS_GUI => "Windows GUI",
        IMAGE_SUBSYSTEM_WINDOWS_CUI => "Windows Console",
        IMAGE_SUBSYSTEM_OS2_CUI => "OS/2 Console",
        IMAGE_SUBSYSTEM_POSIX_CUI => "POSIX Console",
        IMAGE_SUBSYSTEM_WINDOWS_CE_GUI => "Windows CE GUI",
        IMAGE_SUBSYSTEM_EFI_APPLICATION => "EFI Application",
        IMAGE_SUBSYSTEM_EFI_BOOT_SERVICE_DRIVER => "EFI Boot Service Driver",
        IMAGE_SUBSYSTEM_EFI_RUNTIME_DRIVER => "EFI Runtime Driver",
        IMAGE_SUBSYSTEM_EFI_ROM => "EFI ROM",
        IMAGE_SUBSYSTEM_XBOX => "Xbox",
        _ => $"Unknown ({subsystem})"
    };

    private static List<string> GetFileCharacteristics(ushort chars)
    {
        var list = new List<string>();
        if ((chars & IMAGE_FILE_RELOCS_STRIPPED) != 0) list.Add("RELOCS_STRIPPED");
        if ((chars & IMAGE_FILE_EXECUTABLE_IMAGE) != 0) list.Add("EXECUTABLE_IMAGE");
        if ((chars & IMAGE_FILE_LARGE_ADDRESS_AWARE) != 0) list.Add("LARGE_ADDRESS_AWARE");
        if ((chars & IMAGE_FILE_32BIT_MACHINE) != 0) list.Add("32BIT_MACHINE");
        if ((chars & IMAGE_FILE_DEBUG_STRIPPED) != 0) list.Add("DEBUG_STRIPPED");
        if ((chars & IMAGE_FILE_SYSTEM) != 0) list.Add("SYSTEM");
        if ((chars & IMAGE_FILE_DLL) != 0) list.Add("DLL");
        return list;
    }

    private static List<string> GetDllCharacteristics(ushort chars)
    {
        var list = new List<string>();
        if ((chars & IMAGE_DLLCHARACTERISTICS_HIGH_ENTROPY_VA) != 0) list.Add("HIGH_ENTROPY_VA");
        if ((chars & IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE) != 0) list.Add("DYNAMIC_BASE (ASLR)");
        if ((chars & IMAGE_DLLCHARACTERISTICS_FORCE_INTEGRITY) != 0) list.Add("FORCE_INTEGRITY");
        if ((chars & IMAGE_DLLCHARACTERISTICS_NX_COMPAT) != 0) list.Add("NX_COMPAT (DEP)");
        if ((chars & IMAGE_DLLCHARACTERISTICS_NO_ISOLATION) != 0) list.Add("NO_ISOLATION");
        if ((chars & IMAGE_DLLCHARACTERISTICS_NO_SEH) != 0) list.Add("NO_SEH");
        if ((chars & IMAGE_DLLCHARACTERISTICS_NO_BIND) != 0) list.Add("NO_BIND");
        if ((chars & IMAGE_DLLCHARACTERISTICS_APPCONTAINER) != 0) list.Add("APPCONTAINER");
        if ((chars & IMAGE_DLLCHARACTERISTICS_WDM_DRIVER) != 0) list.Add("WDM_DRIVER");
        if ((chars & IMAGE_DLLCHARACTERISTICS_GUARD_CF) != 0) list.Add("GUARD_CF");
        if ((chars & IMAGE_DLLCHARACTERISTICS_TERMINAL_SERVER_AWARE) != 0) list.Add("TERMINAL_SERVER_AWARE");
        return list;
    }

    private static List<string> GetSectionCharacteristics(uint chars)
    {
        var list = new List<string>();
        if ((chars & IMAGE_SCN_CNT_CODE) != 0) list.Add("CODE");
        if ((chars & IMAGE_SCN_CNT_INITIALIZED_DATA) != 0) list.Add("INITIALIZED_DATA");
        if ((chars & IMAGE_SCN_CNT_UNINITIALIZED_DATA) != 0) list.Add("UNINITIALIZED_DATA");
        if ((chars & IMAGE_SCN_MEM_EXECUTE) != 0) list.Add("EXECUTE");
        if ((chars & IMAGE_SCN_MEM_READ) != 0) list.Add("READ");
        if ((chars & IMAGE_SCN_MEM_WRITE) != 0) list.Add("WRITE");
        if ((chars & IMAGE_SCN_MEM_DISCARDABLE) != 0) list.Add("DISCARDABLE");
        if ((chars & IMAGE_SCN_MEM_NOT_CACHED) != 0) list.Add("NOT_CACHED");
        if ((chars & IMAGE_SCN_MEM_NOT_PAGED) != 0) list.Add("NOT_PAGED");
        if ((chars & IMAGE_SCN_MEM_SHARED) != 0) list.Add("SHARED");
        return list;
    }

    private static double CalculateEntropy(byte[] data)
    {
        if (data.Length == 0) return 0;

        var frequency = new int[256];
        foreach (var b in data)
            frequency[b]++;

        double entropy = 0;
        foreach (var count in frequency)
        {
            if (count == 0) continue;
            double probability = (double)count / data.Length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }

    private static double CalculateFileEntropy(string filePath)
    {
        var data = File.ReadAllBytes(filePath);
        return CalculateEntropy(data);
    }

    private string DetectPacker(PeAnalysisResult result)
    {
        // Check for known packer signatures
        var sectionNames = result.Sections.Select(s => s.Name.ToUpperInvariant()).ToList();

        if (sectionNames.Contains("UPX0") || sectionNames.Contains("UPX1"))
            return "UPX";
        if (sectionNames.Any(s => s.StartsWith(".ASPACK")))
            return "ASPack";
        if (sectionNames.Contains(".PETITE"))
            return "Petite";
        if (sectionNames.Contains(".MPRESS1") || sectionNames.Contains(".MPRESS2"))
            return "MPRESS";
        if (sectionNames.Contains(".ENIGMA1") || sectionNames.Contains(".ENIGMA2"))
            return "Enigma Protector";
        if (sectionNames.Contains(".THEMIDA"))
            return "Themida";
        if (sectionNames.Contains(".VMPROTECT"))
            return "VMProtect";
        if (sectionNames.Contains("PEBUNDLE"))
            return "PEBundle";
        if (sectionNames.Any(s => s.Contains("PACKER")))
            return "Generic Packer";

        if (result.OverallEntropy > 7.5)
            return "Unknown (High entropy suggests packing/encryption)";

        return "None detected";
    }

    private static uint RvaToOffset(uint rva, List<SectionAnalysis> sections)
    {
        foreach (var section in sections)
        {
            if (rva >= section.VirtualAddress && 
                rva < section.VirtualAddress + section.VirtualSize)
            {
                return rva - section.VirtualAddress + section.RawAddress;
            }
        }
        return 0;
    }

    private static string ReadAsciiString(FileStream fs, BinaryReader br, uint offset)
    {
        if (offset == 0 || offset >= fs.Length) return string.Empty;
        
        var savedPos = fs.Position;
        fs.Seek(offset, SeekOrigin.Begin);
        var result = ReadAsciiStringInline(br);
        fs.Seek(savedPos, SeekOrigin.Begin);
        return result;
    }

    private static string ReadAsciiStringInline(BinaryReader br)
    {
        var sb = new StringBuilder();
        byte b;
        int count = 0;
        while ((b = br.ReadByte()) != 0 && count < 256)
        {
            sb.Append((char)b);
            count++;
        }
        return sb.ToString();
    }

    private static int FindPattern(byte[] data, byte[] pattern)
    {
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    found = false;
                    break;
                }
            }
            if (found) return i;
        }
        return -1;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    private static string TruncateString(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str)) return str;
        return str.Length <= maxLength ? str : str.Substring(0, maxLength - 3) + "...";
    }

    private static string GenerateProgressBar(int value, int max, int length)
    {
        var filled = (int)((double)value / max * length);
        filled = Math.Max(0, Math.Min(length, filled));
        return "[" + new string('█', filled) + new string('░', length - filled) + "]";
    }

    private static string GetStatusIcon(MethodFeasibility feasibility)
    {
        if (feasibility.IsFeasible && feasibility.IsRecommended) return "★";
        if (feasibility.IsFeasible) return "✓";
        return "✗";
    }
}
