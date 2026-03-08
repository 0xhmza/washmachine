<#
.SYNOPSIS
    Runs the washmachine end-to-end combinatorial test harness.

.DESCRIPTION
    Phase 1: All encoder × envelope × webhelper combinations using messagebox.bin.
             For URL source mode, starts a local Python HTTP server to host the payload.
    Phase 2: All template × snippet permutations with default (0) encoding.

    Results are written to test_results.json in the output directory.

.PARAMETER ShellcodeFile
    Path to the .bin shellcode file. Defaults to a known messagebox.bin location.

.PARAMETER Phase
    Which test phase to run: "1", "2", or "all" (default: "all").

.PARAMETER Port
    Port for the local Python HTTP server (default: 18923).

.EXAMPLE
    .\run_tests.ps1
    .\run_tests.ps1 -Phase 1
    .\run_tests.ps1 -ShellcodeFile C:\path\to\messagebox.bin -Phase all
#>
param(
    [string]$ShellcodeFile,
    [string]$Phase = "all",
    [int]$Port = 18923
)

$ErrorActionPreference = "Stop"

# ── Resolve paths ─────────────────────────────────────────────────────────────
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot  = Split-Path -Parent $scriptDir
$outDir    = Join-Path $repoRoot "Output\Debug\net8.0-windows10.0.19041.0"
$exePath   = Join-Path $outDir "washmachine.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "Building project..." -ForegroundColor Yellow
    Push-Location $repoRoot
    dotnet build -c Debug 2>&1 | Out-Null
    Pop-Location
    if (-not (Test-Path $exePath)) {
        Write-Error "Build failed or exe not found at $exePath"
        exit 1
    }
}

# ── Resolve shellcode file ────────────────────────────────────────────────────
if (-not $ShellcodeFile) {
    $candidates = @(
        (Join-Path $outDir "messagebox.bin"),
        "C:\Users\hamza\Desktop\Offensive\Evasion\Tools\Supermega\data\binary\shellcodes\messagebox.bin"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $ShellcodeFile = $c; break }
    }
}
if (-not $ShellcodeFile -or -not (Test-Path $ShellcodeFile)) {
    Write-Error "messagebox.bin not found. Specify -ShellcodeFile."
    exit 1
}
Write-Host "Shellcode: $ShellcodeFile" -ForegroundColor Cyan

# ── Copy shellcode to output dir if needed ────────────────────────────────────
$localBin = Join-Path $outDir "messagebox.bin"
if (-not (Test-Path $localBin)) {
    Copy-Item $ShellcodeFile $localBin
}

# ── Start Python HTTP server for URL mode ─────────────────────────────────────
$pyServer = $null
$payloadUrl = ""
if ($Phase -eq "all" -or $Phase -eq "1") {
    Write-Host "Starting Python HTTP server on port $Port..." -ForegroundColor Yellow
    $pyServer = Start-Process python -ArgumentList "-m", "http.server", $Port, "--directory", (Split-Path $localBin -Parent) `
        -PassThru -WindowStyle Hidden -RedirectStandardError "NUL"
    Start-Sleep -Seconds 1
    $payloadUrl = "http://localhost:$Port/messagebox.bin"
    Write-Host "Payload URL: $payloadUrl" -ForegroundColor Cyan
}

# ── Run the test harness ──────────────────────────────────────────────────────
try {
    $testArgs = @("--test", "--shellcode", $localBin, "--phase", $Phase)
    if ($payloadUrl) {
        $testArgs += @("--url", $payloadUrl)
    }

    Write-Host "`nRunning: washmachine.exe $($testArgs -join ' ')" -ForegroundColor Green
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
$resultsFile = Join-Path $outDir "test_results.json"
if (Test-Path $resultsFile) {
    $results = Get-Content $resultsFile | ConvertFrom-Json
    $total  = $results.Count
    $passed = ($results | Where-Object { $_.CompileOk -and $_.RunOk }).Count
    $cFail  = ($results | Where-Object { -not $_.CompileOk }).Count
    $rFail  = ($results | Where-Object { $_.CompileOk -and -not $_.RunOk }).Count

    Write-Host "`n═══════════════════════════════════════════════════════════════"
    Write-Host "TOTAL: $total  |  PASSED: $passed  |  COMPILE FAIL: $cFail  |  RUN FAIL: $rFail"
    Write-Host "Results: $resultsFile"
    Write-Host "═══════════════════════════════════════════════════════════════"

    if ($cFail -gt 0 -or $rFail -gt 0) {
        Write-Host "`nFailed tests:" -ForegroundColor Red
        $results | Where-Object { -not $_.CompileOk -or -not $_.RunOk } | ForEach-Object {
            Write-Host "  #$($_.Id) [$($_.Phase)] $($_.Description)" -ForegroundColor Red
            Write-Host "    Error: $($_.Error)" -ForegroundColor DarkRed
        }
    }
}

exit $exitCode
