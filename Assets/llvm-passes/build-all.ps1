<#
.SYNOPSIS
    Build every Washmachine LLVM obfuscation pass plugin in-tree.

.DESCRIPTION
    Iterates over each subdirectory of Assets\llvm-passes that contains a
    CMakeLists.txt and produces `pass.dll` next to the source. Detects the
    LLVM 22 development SDK automatically (looks at -LlvmDir, then common
    install locations) and fails fast with actionable guidance if the SDK is
    missing — a stock LLVM Windows binary release ships only the C API
    headers, not the C++ headers needed to build pass plugins.

.PARAMETER LlvmDir
    Path to the directory containing LLVMConfig.cmake.
    Defaults to "C:\Program Files\LLVM\lib\cmake\llvm".

.PARAMETER Clean
    Wipe each pass's build directory before configuring.

.EXAMPLE
    .\build-all.ps1
    .\build-all.ps1 -LlvmDir "C:\Dev\llvm-22-sdk\lib\cmake\llvm" -Clean
#>
param(
    [string]$LlvmDir = "C:\Program Files\LLVM\lib\cmake\llvm",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot

function Resolve-Sdk {
    param([string]$Preferred)

    $candidates = @($Preferred,
        "C:\Program Files\LLVM\lib\cmake\llvm",
        "C:\Program Files (x86)\LLVM\lib\cmake\llvm",
        "$env:USERPROFILE\scoop\apps\llvm\current\lib\cmake\llvm",
        "C:\msys64\mingw64\lib\cmake\llvm")

    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c "LLVMConfig.cmake"))) {
            return (Resolve-Path $c).Path
        }
    }
    return $null
}

function Require-Tool {
    param([string]$Name, [string]$HelpUrl = "")
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        Write-Host "ERROR: '$Name' not found on PATH." -ForegroundColor Red
        if ($HelpUrl) { Write-Host "  Install: $HelpUrl" -ForegroundColor Yellow }
        exit 1
    }
}

Write-Host ""
Write-Host "=== Washmachine LLVM Pass Build ===" -ForegroundColor Cyan

Require-Tool -Name "cmake" -HelpUrl "https://cmake.org/download/"

$sdk = Resolve-Sdk -Preferred $LlvmDir
if (-not $sdk) {
    Write-Host ""
    Write-Host "ERROR: LLVMConfig.cmake not found." -ForegroundColor Red
    Write-Host ""
    Write-Host "The stock 'LLVM-22.x.x-win64.exe' binary release does NOT include the" -ForegroundColor Yellow
    Write-Host "C++ headers or LLVMConfig.cmake needed to build pass plugins. You need" -ForegroundColor Yellow
    Write-Host "a developer-flavoured LLVM install. Options:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  1. Build LLVM 22 from source with -DLLVM_INSTALL_UTILS=ON and" -ForegroundColor Gray
    Write-Host "     `cmake --install`. Then pass:" -ForegroundColor Gray
    Write-Host "        .\build-all.ps1 -LlvmDir <prefix>\lib\cmake\llvm" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  2. Install via scoop: 'scoop install llvm' (ships dev headers)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  3. Use MSYS2 mingw64 LLVM:" -ForegroundColor Gray
    Write-Host "        pacman -S mingw-w64-x86_64-llvm" -ForegroundColor Gray
    Write-Host ""
    exit 1
}

Write-Host "LLVM SDK : $sdk" -ForegroundColor Green

# Official LLVM Windows release ships LLVMExports.cmake with an absolute path
# to diaguids.lib baked in from the build machine ("C:\Program Files\Microsoft
# Visual Studio\2022\Enterprise\..."). Repoint it at whichever VS install is
# present on this box so the link step doesn't fail with LNK1181.
$exports = Join-Path $sdk "LLVMExports.cmake"
if (Test-Path $exports) {
    $found = Get-ChildItem "C:\Program Files\Microsoft Visual Studio" -Recurse `
        -Filter "diaguids.lib" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "amd64\\diaguids\.lib$" } |
        Select-Object -First 1 -ExpandProperty FullName
    if ($found) {
        $localDia = $found -replace '\\', '/'
        $content = Get-Content -Raw $exports
        if ($content -match 'diaguids\.lib' -and $content -notmatch [regex]::Escape($localDia)) {
            $patched = [regex]::Replace($content,
                '[A-Z]:/[^"]*?DIA SDK/lib/amd64/diaguids\.lib', $localDia)
            if ($patched -ne $content) {
                Set-Content -Path $exports -Value $patched -NoNewline
                Write-Host "Patched diaguids.lib path in LLVMExports.cmake -> $localDia" -ForegroundColor Yellow
            }
        }
    }
}

$passes = Get-ChildItem -Path $here -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName "CMakeLists.txt") }

if (-not $passes) {
    Write-Host "No pass directories with CMakeLists.txt under $here" -ForegroundColor Yellow
    exit 0
}

$failed = @()
foreach ($p in $passes) {
    Write-Host ""
    Write-Host "--- Building $($p.Name) ---" -ForegroundColor Cyan
    $buildDir = Join-Path $p.FullName "build"
    if ($Clean -and (Test-Path $buildDir)) {
        Remove-Item -Recurse -Force $buildDir
    }
    New-Item -ItemType Directory -Force $buildDir | Out-Null

    & cmake -S $p.FullName -B $buildDir -DLLVM_DIR="$sdk" -DCMAKE_BUILD_TYPE=Release
    if ($LASTEXITCODE -ne 0) { $failed += $p.Name; continue }

    & cmake --build $buildDir --config Release
    if ($LASTEXITCODE -ne 0) { $failed += $p.Name; continue }

    # CMake on Windows multi-config generators drops the DLL under build\Release\
    $built = @(
        (Join-Path $buildDir "Release\pass.dll"),
        (Join-Path $buildDir "pass.dll")
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $built) {
        Write-Host "  Build reported success but pass.dll not found." -ForegroundColor Red
        $failed += $p.Name
        continue
    }

    $dest = Join-Path $p.FullName "pass.dll"
    Copy-Item -Force $built $dest
    Write-Host "  -> $dest" -ForegroundColor Green
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host "FAILED: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "All passes built." -ForegroundColor Green
