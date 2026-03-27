<#
.SYNOPSIS
    Runs the washmachine end-to-end combinatorial test harness.

.DESCRIPTION
    Phase 1: All encoder × envelope × webhelper combinations using messagebox.bin.
             For URL source mode, starts a local Python HTTP server to host the payload.
    Phase 2: All template × snippet permutations with default (0) encoding.
    Phase 3: Multiple shellcode inputs from testing assets directory.

    Results are written to test_results.json in the output directory.

.PARAMETER ShellcodeFile
    Path to the .bin shellcode file. Defaults to a known messagebox.bin location.

.PARAMETER Phase
    Which test phase to run: "1", "2", "3", or "all" (default: "all").

.PARAMETER Port
    Port for the local Python HTTP server (default: 18923).

.PARAMETER TestAssetsDir
    Path to the testing assets directory containing shellcodes for Phase 3.
    Defaults to 'testing assets/binary/shellcodes' in the repo root.

.EXAMPLE
    .\run_tests.ps1
    .\run_tests.ps1 -Phase 1
    .\run_tests.ps1 -Phase 3
    .\run_tests.ps1 -ShellcodeFile C:\path\to\messagebox.bin -Phase all
#>
param(
    [string]$ShellcodeFile,
    [string]$Phase = "all",
    [int]$Port = 18923,
    [string]$TestAssetsDir
)

$ErrorActionPreference = "Stop"

# ── Resolve paths ─────────────────────────────────────────────────────────────
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot  = Split-Path -Parent $scriptDir
$cliOutDir = Join-Path $repoRoot "Output\Debug\net8.0"
$exePath   = Join-Path $cliOutDir "washmachine-cli.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "Building CLI project..." -ForegroundColor Yellow
    Push-Location $repoRoot
    dotnet build Washmachine.Cli\Washmachine.Cli.csproj -c Debug 2>&1 | Out-Null
    Pop-Location
    if (-not (Test-Path $exePath)) {
        Write-Error "Build failed or exe not found at $exePath"
        exit 1
    }
}

# ── Resolve shellcode file ────────────────────────────────────────────────────
# For Phase 3 only, shellcode is optional (uses test assets)
$shellcodeRequired = $Phase -ne "3"

if (-not $ShellcodeFile) {
    $candidates = @(
        (Join-Path $cliOutDir "messagebox.bin"),
        (Join-Path $repoRoot "messagebox.bin"),
        (Join-Path $repoRoot "testing assets" "binary" "shellcodes" "messagebox.bin")
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $ShellcodeFile = $c; break }
    }
}
if ($shellcodeRequired -and (-not $ShellcodeFile -or -not (Test-Path $ShellcodeFile))) {
    Write-Error "messagebox.bin not found. Specify -ShellcodeFile or run Phase 3 with -Phase 3."
    exit 1
}

if ($ShellcodeFile) {
    Write-Host "Shellcode: $ShellcodeFile" -ForegroundColor Cyan
}

# ── Resolve test assets directory for Phase 3 ─────────────────────────────────
if (-not $TestAssetsDir) {
    $TestAssetsDir = Join-Path $repoRoot "testing assets" "binary" "shellcodes"
}
if ($Phase -eq "all" -or $Phase -eq "3") {
    if (Test-Path $TestAssetsDir) {
        Write-Host "Test assets: $TestAssetsDir" -ForegroundColor Cyan
    } else {
        Write-Host "Test assets directory not found: $TestAssetsDir" -ForegroundColor Yellow
    }
}

# ── Copy shellcode to output dir if needed ────────────────────────────────────
$localBin = ""
if ($ShellcodeFile) {
    $localBin = Join-Path $cliOutDir "messagebox.bin"
    if (-not (Test-Path $localBin)) {
        Copy-Item $ShellcodeFile $localBin
    }
}

# ── Start Python HTTP server for URL mode ─────────────────────────────────────
$pyServer = $null
$payloadUrl = ""
if (($Phase -eq "all" -or $Phase -eq "1") -and $localBin) {
    Write-Host "Starting Python HTTP server on port $Port..." -ForegroundColor Yellow
    $pyServer = Start-Process python -ArgumentList "-m", "http.server", $Port, "--directory", (Split-Path $localBin -Parent) `
        -PassThru -WindowStyle Hidden -RedirectStandardError "NUL"
    Start-Sleep -Seconds 1
    $payloadUrl = "http://localhost:$Port/messagebox.bin"
    Write-Host "Payload URL: $payloadUrl" -ForegroundColor Cyan
}

# ── Run the test harness ──────────────────────────────────────────────────────
try {
    $testArgs = @("test", "--phase", $Phase)
    
    if ($localBin) {
        $testArgs += @("--shellcode", $localBin)
    }
    if ($payloadUrl) {
        $testArgs += @("--url", $payloadUrl)
    }
    if ($TestAssetsDir -and (Test-Path $TestAssetsDir)) {
        $testArgs += @("--test-assets", $TestAssetsDir)
    }

    Write-Host "`nRunning: washmachine-cli.exe $($testArgs -join ' ')" -ForegroundColor Green
    Write-Host "═══════════════════════════════════════════════════════════════`n"

    & $exePath @testArgs
    $exitCode = $LASTEXITCODE
}
finally {
    # ── Stop HTTP server ──────────────────────────────────────────────────
    if ($pyServer -and -not $pyServer.HasExited) {
        Write-Host "`nStopping HTTP server..." -ForegroundColor Yellow
        Stop-Process -Id $pyServer.Id -Force -ErrorAction SilentlyContinue
    }
}

# ── Report ────────────────────────────────────────────────────────────────────
$resultsFile = Join-Path $cliOutDir "test_results.json"
if (Test-Path $resultsFile) {
    $results = Get-Content $resultsFile | ConvertFrom-Json
    $total  = $results.Count
    $passed = ($results | Where-Object { $_.CompileOk -and $_.RunOk }).Count
    $cFail  = ($results | Where-Object { -not $_.CompileOk }).Count
    $rFail  = ($results | Where-Object { $_.CompileOk -and -not $_.RunOk }).Count
    $secBlocked = ($results | Where-Object { $_.CompileBlockedBySecurity }).Count

    Write-Host "`n═══════════════════════════════════════════════════════════════"
    Write-Host "TOTAL: $total  |  PASSED: $passed  |  COMPILE FAIL: $cFail  |  RUN FAIL: $rFail  |  SECURITY BLOCKED: $secBlocked"
    Write-Host "Results: $resultsFile"
    Write-Host "═══════════════════════════════════════════════════════════════"

    $effectiveCompileFail = $cFail - $secBlocked
    if ($effectiveCompileFail -gt 0 -or $rFail -gt 0) {
        Write-Host "`nFailed tests:" -ForegroundColor Red
        $results | Where-Object { (-not $_.CompileOk -or -not $_.RunOk) -and -not $_.CompileBlockedBySecurity } | ForEach-Object {
            Write-Host "  #$($_.Id) [$($_.Phase)] $($_.Description)" -ForegroundColor Red
            Write-Host "    Error: $($_.Error)" -ForegroundColor DarkRed
        }
    }

    if ($secBlocked -gt 0) {
        Write-Host "`nSecurity-blocked tests (environment issue):" -ForegroundColor Yellow
        $results | Where-Object { $_.CompileBlockedBySecurity } | ForEach-Object {
            Write-Host "  #$($_.Id) [$($_.Phase)] $($_.Description)" -ForegroundColor Yellow
            Write-Host "    Error: $($_.Error)" -ForegroundColor DarkYellow
        }
    }
}

exit $exitCode
