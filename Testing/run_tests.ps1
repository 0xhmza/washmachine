<#
.SYNOPSIS
    Runs the washmachine end-to-end test harness against messagebox.bin and a
    synthetic big payload (~4 MB) with multiple parameter combinations.

.DESCRIPTION
    Three test groups are produced:

      A. Phase-1 encoding sweep against messagebox.bin (small, ~433 B).
         Walks every encoder x envelope pair plus a URL-fetch variant served
         by a local Python HTTP server.

      B. Phase-1 encoding sweep against big_payload.bin (~4 MB synthetic
         NOP-sled + ret). Validates that the compile + encode + execute path
         survives large inputs.

      C. Phase-2 template+snippet coverage and Phase-3 multi-shellcode runs
         using messagebox.bin as the canonical input.

    Each invocation of the test harness writes its own test_results.json
    next to the CLI; this script aggregates and prints a final summary.

.PARAMETER ShellcodeFile
    Path to the messagebox.bin used for Phase 1/2. Defaults to
    Testing\binary\shellcodes\messagebox.bin in the repo.

.PARAMETER BigPayloadFile
    Path to a >= 3 MB .bin file used for the large-input encoding sweep.
    If the file does not exist the script synthesises one (NOP sled + ret).

.PARAMETER BigPayloadSizeMB
    Size of the synthesised big payload. Default: 4 MB.

.PARAMETER Phase
    Which group to run: "small", "big", "matrix", "all" (default).

.PARAMETER Port
    Port for the local Python HTTP server used by URL-source tests.

.EXAMPLE
    .\Testing\run_tests.ps1
    .\Testing\run_tests.ps1 -Phase small
    .\Testing\run_tests.ps1 -Phase big -BigPayloadSizeMB 8
#>
param(
    [string]$ShellcodeFile,
    [string]$BigPayloadFile,
    [int]$BigPayloadSizeMB = 4,
    [ValidateSet("small", "big", "matrix", "all")]
    [string]$Phase = "all",
    [int]$Port = 18923
)

$ErrorActionPreference = "Stop"

# ── Resolve paths ─────────────────────────────────────────────────────────────
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot  = Split-Path -Parent $scriptDir
$cliOutDir = Join-Path $repoRoot "Output\Debug"
$exePath   = Join-Path $cliOutDir "washmachine-cli.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "Building CLI project..." -ForegroundColor Yellow
    Push-Location $repoRoot
    try {
        dotnet build (Join-Path $repoRoot "Washmachine.Cli\Washmachine.Cli.csproj") -c Debug 2>&1 | Out-Null
    } finally {
        Pop-Location
    }
    if (-not (Test-Path $exePath)) {
        Write-Error "Build failed or exe not found at $exePath"
        exit 1
    }
}

# ── Resolve / create test inputs ──────────────────────────────────────────────
if (-not $ShellcodeFile) {
    $candidates = @(
        (Join-Path $repoRoot "Testing\binary\shellcodes\messagebox.bin"),
        (Join-Path $cliOutDir "messagebox.bin"),
        (Join-Path $repoRoot "messagebox.bin")
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $ShellcodeFile = $c; break }
    }
}
if (-not (Test-Path $ShellcodeFile)) {
    Write-Error "messagebox.bin not found. Pass -ShellcodeFile <path>."
    exit 1
}

if (-not $BigPayloadFile) {
    $BigPayloadFile = Join-Path $cliOutDir "big_payload.bin"
}
if (-not (Test-Path $BigPayloadFile)) {
    $targetSize = $BigPayloadSizeMB * 1MB
    Write-Host "Synthesising big payload ($BigPayloadSizeMB MB NOP-sled + ret) at $BigPayloadFile" -ForegroundColor Yellow
    $parent = Split-Path -Parent $BigPayloadFile
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $bytes = New-Object byte[] $targetSize
    for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = 0x90 }
    $bytes[$bytes.Length - 1] = 0xC3   # ret
    [System.IO.File]::WriteAllBytes($BigPayloadFile, $bytes)
}

$bigSize = (Get-Item $BigPayloadFile).Length
Write-Host "Small payload: $ShellcodeFile ($((Get-Item $ShellcodeFile).Length) B)"
Write-Host "Big payload:   $BigPayloadFile ($([math]::Round($bigSize / 1MB, 2)) MB)"
if ($bigSize -lt 3MB) {
    Write-Warning "Big payload is < 3 MB; -BigPayloadSizeMB defaults to 4 - re-run with -BigPayloadSizeMB 4 (or larger)."
}

# Test harness expects the input to be reachable from the CLI working dir.
# Copy both files into the CLI output dir under stable names.
$smallLocal = Join-Path $cliOutDir "messagebox.bin"
$bigLocal   = Join-Path $cliOutDir "big_payload.bin"
Copy-Item -Force $ShellcodeFile  $smallLocal
Copy-Item -Force $BigPayloadFile $bigLocal

# ── Optional Python HTTP server for URL-source tests ──────────────────────────
$pyServer  = $null
$payloadUrl = ""
function Start-PayloadServer {
    param([string]$RootDir, [int]$ListenPort)
    Write-Host "Starting Python HTTP server on :$ListenPort ($RootDir)..." -ForegroundColor Yellow
    return Start-Process python `
        -ArgumentList "-m", "http.server", $ListenPort, "--directory", $RootDir `
        -PassThru -WindowStyle Hidden -RedirectStandardError "NUL"
}

$aggregate = @{ Total = 0; Passed = 0; CompileFail = 0; RunFail = 0; SecBlocked = 0; Failures = @() }

function Invoke-Harness {
    param(
        [string]$Label,
        [string[]]$TestArgs
    )
    Write-Host ""
    Write-Host "─────────────────────────────────────────────────────────"
    Write-Host "  [$Label]" -ForegroundColor Cyan
    Write-Host "  cmd: washmachine-cli.exe $($TestArgs -join ' ')"
    Write-Host "─────────────────────────────────────────────────────────"
    & $exePath @TestArgs
    $code = $LASTEXITCODE
    $resultsFile = Join-Path $cliOutDir "test_results.json"
    if (Test-Path $resultsFile) {
        $results = Get-Content $resultsFile | ConvertFrom-Json
        $aggregate.Total      += $results.Count
        $aggregate.Passed     += ($results | Where-Object { $_.CompileOk -and $_.RunOk }).Count
        $aggregate.CompileFail+= ($results | Where-Object { -not $_.CompileOk }).Count
        $aggregate.RunFail    += ($results | Where-Object { $_.CompileOk -and -not $_.RunOk }).Count
        $aggregate.SecBlocked += ($results | Where-Object { $_.CompileBlockedBySecurity }).Count
        $aggregate.Failures   += ($results | Where-Object { (-not $_.CompileOk -or -not $_.RunOk) -and -not $_.CompileBlockedBySecurity } | ForEach-Object {
            "[$Label #$($_.Id)] $($_.Description) — $($_.Error)"
        })
        # Snapshot the results next to the script with a per-label name so they
        # are not overwritten by the next Invoke-Harness call.
        $snapshot = Join-Path $scriptDir ("test_results.{0}.json" -f ($Label -replace '[^\w\-]','_'))
        Copy-Item -Force $resultsFile $snapshot
    }
    return $code
}

try {
    # ── Group A: small payload, all encoder/envelope combos ───────────────────
    if ($Phase -in @("small", "all")) {
        $pyServer = Start-PayloadServer -RootDir $cliOutDir -ListenPort $Port
        Start-Sleep -Seconds 1
        $payloadUrl = "http://localhost:$Port/messagebox.bin"

        Invoke-Harness -Label "A.small-encoding-matrix" -TestArgs @(
            "test", "--phase", "1",
            "--shellcode", $smallLocal,
            "--url", $payloadUrl
        ) | Out-Null

        if ($pyServer -and -not $pyServer.HasExited) {
            Stop-Process -Id $pyServer.Id -Force -ErrorAction SilentlyContinue
            $pyServer = $null
        }
    }

    # ── Group B: big payload, all encoder/envelope combos (file source only) ──
    if ($Phase -in @("big", "all")) {
        Invoke-Harness -Label "B.big-encoding-matrix" -TestArgs @(
            "test", "--phase", "1",
            "--shellcode", $bigLocal
        ) | Out-Null
    }

    # ── Group C: template + multi-shellcode matrix (smaller, slower runs) ─────
    if ($Phase -in @("matrix", "all")) {
        Invoke-Harness -Label "C.template-and-snippet-matrix" -TestArgs @(
            "test", "--phase", "2",
            "--shellcode", $smallLocal
        ) | Out-Null

        Invoke-Harness -Label "C.multi-shellcode" -TestArgs @(
            "test", "--phase", "3",
            "--test-assets", (Join-Path $repoRoot "Testing\binary\shellcodes")
        ) | Out-Null
    }
}
finally {
    if ($pyServer -and -not $pyServer.HasExited) {
        Stop-Process -Id $pyServer.Id -Force -ErrorAction SilentlyContinue
    }
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════"
Write-Host "AGGREGATE: $($aggregate.Total) tests | $($aggregate.Passed) passed | $($aggregate.CompileFail) compile fails | $($aggregate.RunFail) run fails | $($aggregate.SecBlocked) security-blocked"
Write-Host "Per-group results: $scriptDir\test_results.*.json"
Write-Host "═══════════════════════════════════════════════════════════════"

if ($aggregate.Failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Failures (excluding security-blocked):" -ForegroundColor Red
    $aggregate.Failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
}

$effectiveCompileFail = $aggregate.CompileFail - $aggregate.SecBlocked
if ($effectiveCompileFail -gt 0 -or $aggregate.RunFail -gt 0) {
    exit 1
}
exit 0
