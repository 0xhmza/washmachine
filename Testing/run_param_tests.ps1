<#
.SYNOPSIS
    Exercises washmachine-cli `encode` with a hand-picked matrix of CLI
    parameters against messagebox.bin and a big (>= 3 MB) synthetic payload.

.DESCRIPTION
    Unlike run_tests.ps1 (which drives the headless test harness), this
    script issues real `encode` commands with varied parameter combinations
    so you can verify each CLI flag end-to-end on the shipping binary.

    Each row writes its build artefact into Output\Debug\param-tests\<row>\
    so they can be diffed / re-run individually.

.PARAMETER ShellcodeFile
    Path to messagebox.bin. Defaults to Testing\binary\shellcodes\messagebox.bin.

.PARAMETER BigPayloadFile
    Path to the >= 3 MB binary. Synthesised on demand (NOP-sled + ret).

.PARAMETER BigPayloadSizeMB
    Size of the synthesised big payload. Default: 4 MB.

.EXAMPLE
    .\Testing\run_param_tests.ps1
#>
param(
    [string]$ShellcodeFile,
    [string]$BigPayloadFile,
    [int]$BigPayloadSizeMB = 4
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot  = Split-Path -Parent $scriptDir
$cliOutDir = Join-Path $repoRoot "Output\Debug"
$exePath   = Join-Path $cliOutDir "washmachine-cli.exe"
$outRoot   = Join-Path $cliOutDir "param-tests"

if (-not (Test-Path $exePath)) {
    Write-Host "Building CLI project..." -ForegroundColor Yellow
    Push-Location $repoRoot
    try {
        dotnet build (Join-Path $repoRoot "Washmachine.Cli\Washmachine.Cli.csproj") -c Debug 2>&1 | Out-Null
    } finally { Pop-Location }
    if (-not (Test-Path $exePath)) { Write-Error "Build failed."; exit 1 }
}

if (-not $ShellcodeFile) {
    $ShellcodeFile = Join-Path $repoRoot "Testing\binary\shellcodes\messagebox.bin"
}
if (-not (Test-Path $ShellcodeFile)) { Write-Error "messagebox.bin not found at $ShellcodeFile"; exit 1 }

if (-not $BigPayloadFile) {
    $BigPayloadFile = Join-Path $cliOutDir "big_payload.bin"
}
if (-not (Test-Path $BigPayloadFile)) {
    $targetSize = $BigPayloadSizeMB * 1MB
    Write-Host "Synthesising big payload ($BigPayloadSizeMB MB) at $BigPayloadFile" -ForegroundColor Yellow
    $bytes = New-Object byte[] $targetSize
    for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = 0x90 }
    $bytes[$bytes.Length - 1] = 0xC3
    [System.IO.File]::WriteAllBytes($BigPayloadFile, $bytes)
}

if (Test-Path $outRoot) { Remove-Item -Recurse -Force $outRoot }
New-Item -ItemType Directory -Path $outRoot | Out-Null

# Donor used by --clone-from. The Cacheset binary is a small signed Sysinternals
# helper that is already part of the test corpus.
$donor = Join-Path $repoRoot "Testing\binary\injectables\Cacheset.exe"

# ── Parameter matrix ──────────────────────────────────────────────────────────
#
#   Name            : run identifier (also output subdir)
#   Source          : path to .bin
#   Args            : extra flags after `encode -s <Source>`
#
$rows = @(
    @{ Name = "small-default";          Source = $ShellcodeFile;  Args = @() }
    @{ Name = "small-encoder-1";        Source = $ShellcodeFile;  Args = @("-e", "1") }
    @{ Name = "small-encoder-2";        Source = $ShellcodeFile;  Args = @("-e", "2") }
    @{ Name = "small-envelope-1";       Source = $ShellcodeFile;  Args = @("-v", "1") }
    @{ Name = "small-enc1-env1";        Source = $ShellcodeFile;  Args = @("-e", "1", "-v", "1") }
    @{ Name = "small-sgn";              Source = $ShellcodeFile;  Args = @("--sgn", "--shikata-enc", "1", "--shikata-max", "32") }
    @{ Name = "small-clone-donor";      Source = $ShellcodeFile;  Args = @("--clone-from", $donor, "--clone-icon", "--clone-metadata") }
    @{ Name = "small-pad-nops";         Source = $ShellcodeFile;  Args = @("--pad-nops", "65536") }
    @{ Name = "small-clone-and-pad";    Source = $ShellcodeFile;  Args = @("--clone-from", $donor, "--pad-nops", "131072") }

    @{ Name = "big-default";            Source = $BigPayloadFile; Args = @() }
    @{ Name = "big-encoder-1";          Source = $BigPayloadFile; Args = @("-e", "1") }
    @{ Name = "big-enc1-env1";          Source = $BigPayloadFile; Args = @("-e", "1", "-v", "1") }
    @{ Name = "big-pad-nops";           Source = $BigPayloadFile; Args = @("--pad-nops", "1048576") }
    @{ Name = "big-clone-donor";        Source = $BigPayloadFile; Args = @("--clone-from", $donor, "--clone-icon", "--clone-metadata") }
)

$passed  = 0
$failed  = 0
$results = @()
$start   = Get-Date

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════"
Write-Host "  washmachine-cli encode parameter sweep"
Write-Host "  CLI:  $exePath"
Write-Host "  Rows: $($rows.Count)"
Write-Host "═══════════════════════════════════════════════════════════════"

foreach ($row in $rows) {
    $rowDir = Join-Path $outRoot $row.Name
    New-Item -ItemType Directory -Path $rowDir -Force | Out-Null
    $argList = @("encode", "-s", $row.Source, "--json") + $row.Args

    Write-Host -NoNewline "  [$($row.Name)] "
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        Push-Location $rowDir
        try {
            $output = & $exePath @argList 2>&1
            $exit = $LASTEXITCODE
        } finally { Pop-Location }
        $sw.Stop()

        if ($exit -ne 0) {
            $failed++
            Write-Host "FAIL (exit $exit) $([math]::Round($sw.Elapsed.TotalSeconds, 1))s" -ForegroundColor Red
            $results += @{ Name = $row.Name; Status = "FAIL"; Exit = $exit; DurationMs = $sw.Elapsed.TotalMilliseconds; Snippet = ($output | Select-Object -Last 5 | Out-String).Trim() }
        } else {
            $passed++
            Write-Host "PASS $([math]::Round($sw.Elapsed.TotalSeconds, 1))s" -ForegroundColor Green
            $results += @{ Name = $row.Name; Status = "PASS"; Exit = 0; DurationMs = $sw.Elapsed.TotalMilliseconds }
        }
    } catch {
        $sw.Stop()
        $failed++
        Write-Host "EXCEPTION: $_" -ForegroundColor Red
        $results += @{ Name = $row.Name; Status = "EXCEPTION"; Exit = -1; DurationMs = $sw.Elapsed.TotalMilliseconds; Snippet = $_.Exception.Message }
    }
}

$elapsed = (Get-Date) - $start
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════"
Write-Host "  $passed/$($rows.Count) passed, $failed failed in $([math]::Round($elapsed.TotalSeconds, 1))s"
Write-Host "  Artefacts: $outRoot"
Write-Host "═══════════════════════════════════════════════════════════════"

$resultsJson = $results | ConvertTo-Json -Depth 5
$resultsJson | Out-File -FilePath (Join-Path $scriptDir "param_test_results.json") -Encoding utf8

if ($failed -gt 0) { exit 1 }
exit 0
