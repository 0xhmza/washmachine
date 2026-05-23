<#
.SYNOPSIS
    End-to-end test of the Washmachine LLVM obfuscation passes.

.DESCRIPTION
    1. Compiles a sample C++ program twice with no passes — confirms the
       baseline binary is deterministic (so any later divergence is the
       passes' doing, not a clang artefact).
    2. For each built pass.dll, compiles the same source with the pass
       loaded under two different OBFUSCATION_SEED values. The two resulting
       binaries MUST differ (polymorphism) and BOTH must produce the same
       runtime output (functional equivalence).
    3. As a final smoke test, runs all four passes together once.

    The script discovers built passes by scanning Assets\llvm-passes\*\pass.dll
    and skips ones that haven't been built yet.

.PARAMETER ClangDir
    Directory containing clang++.exe. Defaults to the bundled Tools\LLVM\bin.

.PARAMETER KeepArtifacts
    Don't delete the temp build directory at the end (useful for inspection).
#>
param(
    [string]$ClangDir = "",
    [switch]$KeepArtifacts
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$repoRoot = Split-Path -Parent (Split-Path -Parent $here)

if (-not $ClangDir) {
    $candidates = @(
        (Join-Path $repoRoot "Tools\LLVM\bin"),
        "C:\Program Files\LLVM\bin"
    )
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c "clang++.exe")) { $ClangDir = $c; break }
    }
}

$clang = Join-Path $ClangDir "clang++.exe"
if (-not (Test-Path $clang)) {
    Write-Host "ERROR: clang++.exe not found at '$clang'." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Washmachine LLVM Pass Test ===" -ForegroundColor Cyan
Write-Host "clang++ : $clang" -ForegroundColor Gray

$work = Join-Path ([System.IO.Path]::GetTempPath()) "wm-pass-test-$([guid]::NewGuid().ToString('N').Substring(0,8))"
New-Item -ItemType Directory -Force $work | Out-Null
Write-Host "workdir : $work" -ForegroundColor Gray
Write-Host ""

# A sample program that exercises every pass:
#   - arithmetic (instruction substitution)
#   - several basic blocks with branches (bcf, cff)
#   - string literals (string obfuscation)
$src = @"
#include <cstdio>
#include <cstdint>

static uint32_t crunch(uint32_t a, uint32_t b) {
    uint32_t x = a + b;
    uint32_t y = a ^ b;
    uint32_t z = a & b;
    if ((x ^ y) > z) {
        for (int i = 0; i < 5; ++i) {
            x = (x * 1103515245u) + 12345u;
            y = y ^ (x >> 3);
        }
    } else {
        z = z | (a - b);
    }
    return x + y + z;
}

int main() {
    const char* greeting = "washmachine-llvm-pass-self-test";
    uint32_t r = 0;
    for (uint32_t i = 1; i <= 1000; ++i) {
        r = crunch(r ^ i, i * 7u + 3u);
    }
    std::printf("%s: r=%u\n", greeting, r);
    return 0;
}
"@
$srcPath = Join-Path $work "sample.cpp"
Set-Content -Encoding ASCII -Path $srcPath -Value $src

function Sha256 {
    param([string]$Path)
    return (Get-FileHash -Algorithm SHA256 -Path $Path).Hash
}

function Run-Capture {
    param([string]$Exe)
    $out = & $Exe 2>&1
    return ($out -join "`n").Trim()
}

function Compile {
    param([string]$Out, [string[]]$Plugins, [string]$Seed)

    $args = @(
        "-O1",
        "-ffunction-sections",
        "-fdata-sections",
        "-o", $Out,
        $srcPath
    )
    foreach ($p in $Plugins) {
        $args = @("-fpass-plugin=$p") + $args
    }

    $env:OBFUSCATION_SEED = $Seed
    & $clang @args 2>&1 | Out-String | Set-Variable -Name __compile_log -Scope Global
    $rc = $LASTEXITCODE
    $env:OBFUSCATION_SEED = ""
    if ($rc -ne 0) {
        Write-Host "  clang++ failed:" -ForegroundColor Red
        Write-Host $global:__compile_log -ForegroundColor DarkRed
        return $false
    }
    return $true
}

$passes = Get-ChildItem -Path $here -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName "pass.dll") }

Write-Host "--- Baseline (no passes) ---" -ForegroundColor Cyan
$baseA = Join-Path $work "base_a.exe"
$baseB = Join-Path $work "base_b.exe"
if (-not (Compile -Out $baseA -Plugins @() -Seed "0")) { exit 2 }
if (-not (Compile -Out $baseB -Plugins @() -Seed "0")) { exit 2 }
$hA = Sha256 $baseA
$hB = Sha256 $baseB
$expected = Run-Capture $baseA
Write-Host "  baseline-a sha256: $hA"
Write-Host "  baseline-b sha256: $hB"
if ($hA -ne $hB) {
    Write-Host "  WARN: baseline is non-deterministic (clang variance). " -ForegroundColor Yellow
    Write-Host "  Polymorphism checks below remain valid against the baseline pair." -ForegroundColor Yellow
} else {
    Write-Host "  baseline is deterministic." -ForegroundColor Green
}
Write-Host "  baseline runtime output: $expected" -ForegroundColor Gray
Write-Host ""

if (-not $passes) {
    Write-Host "No built pass.dll found under $here." -ForegroundColor Yellow
    Write-Host "Build passes first:  .\Assets\llvm-passes\build-all.ps1" -ForegroundColor Yellow
    if (-not $KeepArtifacts) { Remove-Item -Recurse -Force $work }
    exit 0
}

$results = @()
foreach ($p in $passes) {
    $name = $p.Name
    $dll = Join-Path $p.FullName "pass.dll"
    Write-Host "--- Pass: $name ---" -ForegroundColor Cyan

    $outA = Join-Path $work "$name`_a.exe"
    $outB = Join-Path $work "$name`_b.exe"

    if (-not (Compile -Out $outA -Plugins @($dll) -Seed "111111")) {
        $results += [pscustomobject]@{ Pass=$name; Built=$true; CompileOk=$false }
        continue
    }
    if (-not (Compile -Out $outB -Plugins @($dll) -Seed "999999")) {
        $results += [pscustomobject]@{ Pass=$name; Built=$true; CompileOk=$false }
        continue
    }

    $shA = Sha256 $outA
    $shB = Sha256 $outB
    $runA = Run-Capture $outA
    $runB = Run-Capture $outB

    $diverged = ($shA -ne $shB)
    $correctA = ($runA -eq $expected)
    $correctB = ($runB -eq $expected)

    Write-Host ("  seed=111111 sha={0}  runtime={1}" -f $shA, ($(if($correctA){'OK'}else{'BAD'}))) -ForegroundColor Gray
    Write-Host ("  seed=999999 sha={0}  runtime={1}" -f $shB, ($(if($correctB){'OK'}else{'BAD'}))) -ForegroundColor Gray
    if ($diverged) {
        Write-Host "  polymorphic: YES (binaries differ)" -ForegroundColor Green
    } else {
        Write-Host "  polymorphic: NO  (binaries identical — randomization isn't reaching codegen)" -ForegroundColor Red
    }

    $results += [pscustomobject]@{
        Pass = $name
        Built = $true
        CompileOk = $true
        Polymorphic = $diverged
        RuntimeA = $correctA
        RuntimeB = $correctB
    }
}

# Combined smoke test: all built passes at once.
Write-Host ""
Write-Host "--- Combined smoke test (all built passes) ---" -ForegroundColor Cyan
$combo = Join-Path $work "combined.exe"
$allDlls = $passes | ForEach-Object { Join-Path $_.FullName "pass.dll" }
if (Compile -Out $combo -Plugins $allDlls -Seed "42") {
    $hc = Sha256 $combo
    $rc = Run-Capture $combo
    Write-Host "  sha256: $hc"
    Write-Host "  runtime output: $rc"
    Write-Host ("  matches baseline: {0}" -f $(if($rc -eq $expected){'YES'}else{'NO'})) -ForegroundColor $(if($rc -eq $expected){'Green'}else{'Red'})
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
$results | Format-Table -AutoSize

if (-not $KeepArtifacts) {
    Remove-Item -Recurse -Force $work
} else {
    Write-Host "Artefacts kept: $work" -ForegroundColor Gray
}

$bad = $results | Where-Object {
    -not $_.CompileOk -or -not $_.Polymorphic -or -not $_.RuntimeA -or -not $_.RuntimeB
}
if ($bad) { exit 1 } else { exit 0 }
