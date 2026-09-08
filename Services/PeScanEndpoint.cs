using System.Text.Json;
using Washmachine.Logging;

namespace Washmachine.Services;

/// <summary>Read-only PE endpoint, independent of WinUI for contract tests.</summary>
internal static class PeScanEndpoint
{
    public static async Task<object?> AnalyzeAsync(JsonElement args, IAppLogger logger)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("path", out var pEl) || pEl.ValueKind != JsonValueKind.String)
            return new { ok = false, message = "A PE file path is required." };

        var path = (pEl.GetString() ?? "").Trim();
        // Windows 'Copy as path' wraps the path in double quotes.
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
            path = path[1..^1];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new { ok = false, message = "File not found." };

        try
        {
            var svc = new PeAnalyzerService(logger);
            var r = await svc.AnalyzeAsync(path);

            if (!r.IsValid)
                return new { ok = false, message = r.ValidationError };

            return new
            {
                ok = true,
                fileName = r.FileName,
                fileSize = r.FileSize,
                fileSizeFormatted = r.FileSizeFormatted,
                fileSizeText = r.FileSizeFormatted,
                is64Bit = r.Is64Bit,
                isDll = r.IsDll,
                architecture = r.Architecture,
                machineType = r.MachineType,
                subsystem = r.Subsystem,
                peType = r.PeType,
                isDotNet = r.IsDotNet,
                entryPoint = r.OptionalHeader?.AddressOfEntryPoint ?? 0,
                entryPointHex = $"0x{r.OptionalHeader?.AddressOfEntryPoint ?? 0:x8}",
                imageBase = r.OptionalHeader?.ImageBase ?? 0,
                imageBaseHex = $"0x{r.OptionalHeader?.ImageBase ?? 0:x16}",
                sections = r.Sections.Count,
                sectionCount = r.Sections.Count,
                sectionDetails = r.Sections.Select(s => new
                {
                    name = s.Name,
                    virtualAddress = s.VirtualAddress,
                    virtualAddressHex = $"0x{s.VirtualAddress:x8}",
                    virtualSize = s.VirtualSize,
                    rawSize = s.RawSize,
                    permissions = s.PermissionsString,
                    entropy = Math.Round(s.Entropy, 2),
                    isExecutable = s.IsExecutable,
                    isWritable = s.IsWritable,
                }).ToArray(),
                entropy = Math.Round(r.OverallEntropy, 2),
                isPossiblyPacked = r.IsPossiblyPacked,
                packerDetection = r.PackerDetection,
                hasSig = r.Security?.HasAuthenticode ?? false,
                hasAuthenticode = r.Security?.HasAuthenticode ?? false,
                sigInfo = r.Security?.SignatureInfo ?? "",
                securityScore = r.Security?.SecurityScore ?? 0,
                securityAssessment = r.Security?.SecurityAssessment ?? "Unavailable",
                enabledProtections = r.Security?.EnabledProtections?.ToArray() ?? Array.Empty<string>(),
                missingProtections = r.Security?.MissingProtections?.ToArray() ?? Array.Empty<string>(),
                codeCaves = r.CodeCaves.Select(c => new
                {
                    sectionName = c.SectionName,
                    fileOffset = c.FileOffset,
                    virtualAddress = c.VirtualAddress,
                    size = c.Size,
                    fillByte = c.FillByte,
                    isExecutable = c.IsExecutable,
                    suitableForInjection = c.SuitableForInjection,
                }).ToArray(),
                codeCaveCount = r.CodeCaves.Count,
                maxCaveSize = r.CodeCaves.Count == 0 ? 0 : r.CodeCaves.Max(c => c.Size),
                caves = r.CodeCaves.Select(c => new
                {
                    sectionName = c.SectionName,
                    fileOffset = c.FileOffset,
                    virtualAddress = c.VirtualAddress,
                    rvaHex = $"0x{c.VirtualAddress:x8}",
                    size = c.Size,
                    fillByte = c.FillByte,
                    isExecutable = c.IsExecutable,
                    suitableForInjection = c.SuitableForInjection,
                }).ToArray(),
                imports = r.Imports.Select(i => new
                {
                    dll = i.Name,
                    functions = i.Functions.Select(f => f.Name).ToArray(),
                }).ToArray(),
                hasVersion = r.HasVersionInfo,
                hasIcon = r.HasIcon,
                tlsCallbacks = r.Tls?.NumberOfCallbacks ?? 0,
                totalImports = r.TotalImports,
                totalExports = r.TotalExports,
                canInject = r.Feasibility?.CanInject ?? false,
                recommendedMethod = r.Feasibility?.RecommendedMethod ?? "",
                recommendedReason = r.Feasibility?.RecommendedReason ?? "",
                blockingReasons = r.Feasibility?.BlockingReasons?.ToArray() ?? Array.Empty<string>(),
                warnings = r.Feasibility?.Warnings?.ToArray() ?? Array.Empty<string>(),
            };
        }
        catch (Exception ex)
        {
            return new { ok = false, message = ex.Message };
        }
    }
}
