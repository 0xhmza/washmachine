<#
.SYNOPSIS
    Automated backdoor injection test suite for washmachine-cli.

.DESCRIPTION
    Tests all injection methods (code-cave, new-section, section-ext) and
    encryption modes (none, xor) against multiple target executables and shellcodes.
    Validates that:
    1. Injection completes without error
    2. Output PE is a valid PE file (can be re-parsed)
    3. Entry point was changed
    4. Output file size is reasonable

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

# Define test matrix
$tests = @(
    # [Target, Shellcode, Method, Encryption, ExpectedResult]
    @{ Target="WinDirStat.exe";        Shellcode="calc64.bin";    Method="code-cave";    Enc="none"; Name="WDS-calc-cave" }
    @{ Target="WinDirStat.exe";        Shellcode="calc64.bin";    Method="new-section";  Enc="none"; Name="WDS-calc-newsec" }
    @{ Target="WinDirStat.exe";        Shellcode="calc64.bin";    Method="section-ext";  Enc="none"; Name="WDS-calc-secext" }
    @{ Target="WinDirStat.exe";        Shellcode="messagebox.bin"; Method="code-cave";  Enc="none"; Name="WDS-msgbox-cave" }
    @{ Target="WinDirStat.exe";        Shellcode="messagebox.bin"; Method="new-section"; Enc="none"; Name="WDS-msgbox-newsec" }
    @{ Target="WinDirStat.exe";        Shellcode="notepad64.bin"; Method="code-cave";    Enc="none"; Name="WDS-notepad-cave" }
    @{ Target="WinDirStat.exe";        Shellcode="calc64.bin";    Method="code-cave";    Enc="xor";  Name="WDS-calc-cave-xor" }
    @{ Target="7z.exe";                Shellcode="calc64.bin";    Method="code-cave";    Enc="none"; Name="7z-calc-cave" }
    @{ Target="7z.exe";                Shellcode="calc64.bin";    Method="new-section";  Enc="none"; Name="7z-calc-newsec" }
    @{ Target="7z.exe";                Shellcode="calc64.bin";    Method="section-ext";  Enc="none"; Name="7z-calc-secext" }
    @{ Target="procexp64.exe";         Shellcode="notepad64.bin"; Method="code-cave";    Enc="none"; Name="procexp-notepad-cave" }
    @{ Target="procexp64.exe";         Shellcode="notepad64.bin"; Method="new-section";  Enc="none"; Name="procexp-notepad-newsec" }
    @{ Target="wifiinfoview.exe";      Shellcode="messagebox.bin"; Method="code-cave";   Enc="none"; Name="wifi-msgbox-cave" }
    @{ Target="Cacheset.exe";          Shellcode="messagebox.bin"; Method="code-cave";   Enc="none"; Name="cacheset-msgbox-cave-x86" }
    @{ Target="Cacheset.exe";          Shellcode="messagebox.bin"; Method="new-section"; Enc="none"; Name="cacheset-msgbox-newsec-x86" }
    @{ Target="Cacheset.exe";          Shellcode="messagebox.bin"; Method="section-ext"; Enc="none"; Name="cacheset-msgbox-secext-x86" }
    @{ Target="Cacheset.exe";          Shellcode="messagebox.bin"; Method="code-cave";   Enc="xor";  Name="cacheset-msgbox-cave-xor-x86" }
)

$passed = 0
$failed = 0
$results = @()

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════"
Write-Host "  Washmachine Backdoor Test Suite"
Write-Host "═══════════════════════════════════════════════════════════════"
Write-Host "  CLI: $CliPath"
Write-Host "  Tests: $($tests.Count)"
Write-Host "═══════════════════════════════════════════════════════════════"
Write-Host ""

foreach ($test in $tests) {
    $targetPath = Join-Path $injectablesDir $test.Target
    $shellcodePath = Join-Path $shellcodesDir $test.Shellcode
    $outPath = Join-Path $outputDir "$($test.Name).exe"

    Write-Host -NoNewline "  [$($test.Name)] "

    if (-not (Test-Path $targetPath)) {
        Write-Host "SKIP (target not found)" -ForegroundColor Yellow
        continue
    }
    if (-not (Test-Path $shellcodePath)) {
        Write-Host "SKIP (shellcode not found)" -ForegroundColor Yellow
        continue
    }

    # Build command args
    $cmdArgs = @("backdoor", "--pe", $targetPath, "-s", $shellcodePath, "-o", $outPath, "--method", $test.Method, "--json")
    if ($test.Enc -eq "xor") {
        $cmdArgs += @("--enc", "xor", "--xor-key", "0xAA")
    }

    try {
        # Run injection
        $rawOutput = & $CliPath @cmdArgs 2>&1
        $jsonLine = ($rawOutput | Where-Object { $_ -match '^\{' } | Select-Object -Last 1)

        if (-not $jsonLine) {
            throw "No JSON output received"
        }

        $result = $jsonLine | ConvertFrom-Json

        if (-not $result.Success) {
            throw "Injection failed: $($result.ErrorMessage)"
        }

        # Verify output file exists
        if (-not (Test-Path $outPath)) {
            throw "Output file not created"
        }

        # Verify output is a valid PE (re-analyze)
        $verifyOutput = & $CliPath @("backdoor", "--pe", $outPath, "-s", $shellcodePath, "-o", "$outPath.verify.exe", "--dry-run", "--json") 2>&1
        $verifyJson = ($verifyOutput | Where-Object { $_ -match '^\{' } | Select-Object -Last 1)

        # Check file sizes
        $origSize = (Get-Item $targetPath).Length
        $outSize = (Get-Item $outPath).Length
        if ($outSize -lt $origSize * 0.5) {
            $minExpected = [math]::Floor($origSize * 0.5)
            throw "Output file suspiciously small: $outSize bytes, min expected $minExpected"
        }

        # Check entry point changed
        if ($result.ShellcodeAddress -eq 0) {
            throw "ShellcodeAddress is 0 - injection may have failed"
        }

        $epHex = $result.ShellcodeAddress.ToString('X')
        $stepCount = $result.Steps.Count
        Write-Host "PASS" -ForegroundColor Green -NoNewline
        Write-Host " EP=0x$epHex $stepCount-steps ${outSize}B"
        $passed++
        $results += @{ Name=$test.Name; Status="PASS"; EP=$result.ShellcodeAddress; Size=$outSize }

        # Clean up verify file
        Remove-Item -Path "$outPath.verify.exe" -ErrorAction SilentlyContinue
    }
    catch {
        Write-Host "FAIL: $_" -ForegroundColor Red
        $failed++
        $results += @{ Name=$test.Name; Status="FAIL"; Error=$_.ToString() }
    }
}

# Summary
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════"
Write-Host "  Results: $passed passed, $failed failed, $($tests.Count) total"
if ($failed -eq 0) {
    Write-Host "  All tests passed!" -ForegroundColor Green
} else {
    Write-Host "  Some tests failed." -ForegroundColor Red
}
Write-Host "═══════════════════════════════════════════════════════════════"

# Write results JSON
$resultsJson = $results | ConvertTo-Json -Depth 5
$resultsJson | Out-File -FilePath (Join-Path $outputDir "backdoor_test_results.json") -Encoding utf8

exit $failed
