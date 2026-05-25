<#
.SYNOPSIS
    Build all four Washmachine LLVM obfuscation pass plug-ins.

.DESCRIPTION
    Compiles bogus-control-flow, control-flow-flattening, instruction-substitution,
    and string-obfuscation into pass.dll files that clang can load with -fpass-plugin.

    Toolchain resolution order (first match wins):
      1. Tools\msys64\mingw64  – bundled minimal MSYS2/MinGW-w64 + LLVM SDK
      2. C:\msys64\mingw64     – system-wide MSYS2 installation
      3. Auto-download          – fetches msys2-base + installs required packages

    Required packages (installed automatically if needed):
      mingw-w64-x86_64-gcc  mingw-w64-x86_64-cmake  mingw-w64-x86_64-ninja
      mingw-w64-x86_64-llvm  mingw-w64-x86_64-libffi

.PARAMETER Force
    Re-build even if pass.dll is already present.

.PARAMETER SkipDownload
    Fail instead of downloading msys2 when the toolchain is absent.

.EXAMPLE
    .\build-all.ps1
    .\build-all.ps1 -Force
#>
param(
    [switch]$Force,
    [switch]$SkipDownload
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$here     = $PSScriptRoot                             # Assets\llvm-passes\
$repoRoot = (Resolve-Path (Join-Path $here '..\..')).Path  # repo root

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Write-Step  { param($msg) Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok    { param($msg) Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Warn  { param($msg) Write-Host "  WARN: $msg" -ForegroundColor Yellow }
function Write-Fail  { param($msg) Write-Host " ERROR: $msg" -ForegroundColor Red }

# ---------------------------------------------------------------------------
# Locate or provision the toolchain
# ---------------------------------------------------------------------------
Write-Step "LLVM passes"

$MINGW = $null

# 1. Bundled toolchain (trimmed, lives in Tools\msys64)
$bundled = Join-Path $repoRoot 'Tools\msys64\mingw64'
if (Test-Path (Join-Path $bundled 'bin\g++.exe')) {
    $llvmHeader = Join-Path $bundled 'include\llvm\Plugins\PassPlugin.h'
    $altHeader  = Join-Path $bundled 'include\llvm\Passes\PassPlugin.h'
    if ((Test-Path $llvmHeader) -or (Test-Path $altHeader)) {
        $MINGW = $bundled
        Write-Ok "Bundled toolchain: $MINGW"
    } else {
        Write-Warn "Bundled g++ found but LLVM headers missing — will install packages."
    }
}

# 2. System MSYS2 at C:\msys64
if (-not $MINGW) {
    $sys = 'C:\msys64\mingw64'
    if (Test-Path (Join-Path $sys 'bin\g++.exe')) {
        $llvmHeader = Join-Path $sys 'include\llvm\Plugins\PassPlugin.h'
        $altHeader  = Join-Path $sys 'include\llvm\Passes\PassPlugin.h'
        if ((Test-Path $llvmHeader) -or (Test-Path $altHeader)) {
            $MINGW = $sys
            Write-Ok "System MSYS2 toolchain: $MINGW"
        }
    }
}

# 3. Try to provision the bundled toolchain via pacman (if usr/bin/pacman exists)
if (-not $MINGW) {
    $pacman = Join-Path $repoRoot 'Tools\msys64\usr\bin\pacman.exe'
    if (-not (Test-Path $pacman)) { $pacman = 'C:\msys64\usr\bin\pacman.exe' }

    if (Test-Path $pacman) {
        Write-Step "Installing required packages via pacman..."
        $pkgs = @(
            'mingw-w64-x86_64-gcc',
            'mingw-w64-x86_64-cmake',
            'mingw-w64-x86_64-ninja',
            'mingw-w64-x86_64-llvm',
            'mingw-w64-x86_64-libffi'
        )
        & $pacman -S --noconfirm --needed @pkgs 2>&1 | Write-Host
        if ($LASTEXITCODE -ne 0) { Write-Fail "pacman install failed."; exit 1 }

        # Re-check after install
        $msys64Root = Split-Path (Split-Path $pacman)
        $mg = Join-Path $msys64Root 'mingw64'
        if (Test-Path (Join-Path $mg 'bin\g++.exe')) { $MINGW = $mg }
    }
}

# 4. Auto-download msys2-base SFX and bootstrap
if (-not $MINGW) {
    if ($SkipDownload) {
        Write-Fail "Toolchain not found and -SkipDownload was specified."
        Write-Host "  Install options:"
        Write-Host "    scoop install llvm                   (recommended)"
        Write-Host "    pacman -S mingw-w64-x86_64-llvm      (MSYS2)"
        exit 1
    }

    Write-Step "Downloading MSYS2 base installer..."

    # Pinned to a known-good release; update the date if you need a newer base.
    $msys2Url  = 'https://github.com/msys2/msys2-installer/releases/download/2024-11-19/msys2-base-x86_64-20241119.sfx.exe'
    $toolsDir  = Join-Path $repoRoot 'Tools'
    $sfxPath   = Join-Path $env:TEMP 'msys2-base.sfx.exe'

    if (-not (Test-Path $toolsDir)) { New-Item -ItemType Directory -Force $toolsDir | Out-Null }

    Write-Host "  URL : $msys2Url"
    Write-Host "  Dest: $sfxPath"
    $wc = New-Object System.Net.WebClient
    $wc.DownloadFile($msys2Url, $sfxPath)

    Write-Step "Extracting to $toolsDir ..."
    & $sfxPath -y "-o$toolsDir" 2>&1 | Out-Null
    Remove-Item $sfxPath -Force -EA SilentlyContinue

    $downloadedMsys = Join-Path $toolsDir 'msys64'
    if (-not (Test-Path "$downloadedMsys\usr\bin\pacman.exe")) {
        Write-Fail "Extraction failed — msys64\usr\bin\pacman.exe not found."
        exit 1
    }

    Write-Step "Bootstrapping pacman database..."
    $bash = "$downloadedMsys\usr\bin\bash.exe"
    & $bash -lc 'pacman -Syu --noconfirm' 2>&1 | Out-Null

    Write-Step "Installing required packages..."
    $pkgs = 'mingw-w64-x86_64-gcc mingw-w64-x86_64-cmake mingw-w64-x86_64-ninja mingw-w64-x86_64-llvm mingw-w64-x86_64-libffi'
    & $bash -lc "pacman -S --noconfirm --needed $pkgs" 2>&1 | Write-Host

    $MINGW = Join-Path $downloadedMsys 'mingw64'
    if (-not (Test-Path "$MINGW\bin\g++.exe")) {
        Write-Fail "After download+install g++ still missing. Aborting."
        exit 1
    }
    Write-Ok "Toolchain ready: $MINGW"
}

# Determine LLVM cmake dir
$llvmDir = Join-Path $MINGW 'lib\cmake\llvm'
if (-not (Test-Path "$llvmDir\LLVMConfig.cmake")) {
    Write-Fail "LLVMConfig.cmake not found at: $llvmDir"
    Write-Warn "The LLVM package may not be installed."
    Write-Host "  Run: pacman -S --noconfirm --needed mingw-w64-x86_64-llvm"
    exit 1
}

# Ensure llvm/Plugins/PassPlugin.h is reachable (older LLVM used Passes/)
$pluginsHeader = Join-Path $MINGW 'include\llvm\Plugins\PassPlugin.h'
$passesHeader  = Join-Path $MINGW 'include\llvm\Passes\PassPlugin.h'
if (-not (Test-Path $pluginsHeader) -and -not (Test-Path $passesHeader)) {
    Write-Fail "PassPlugin.h not found under include\llvm\Plugins\ or include\llvm\Passes\"
    exit 1
}

# Patch LLVMExports.cmake if it still uses FATAL_ERROR for missing imported files.
# This is needed when the toolchain has been trimmed (non-x86 backend .a files removed)
# but LLVMExports.cmake still references them.  Downgrading to WARNING is safe because
# we only link against x86 targets, all of which are present.
$llvmExports = Join-Path $llvmDir 'LLVMExports.cmake'
if (Test-Path $llvmExports) {
    $raw = Get-Content $llvmExports -Raw
    if ($raw -match 'message\(FATAL_ERROR "The imported target') {
        $patched = $raw -replace 'message\(FATAL_ERROR "The imported target', 'message(WARNING "Skipping missing import:'
        Set-Content -Path $llvmExports -Value $patched -NoNewline
        Write-Warn "Patched LLVMExports.cmake: FATAL_ERROR -> WARNING for missing imported files (trimmed toolchain)."
    }
}

# Prepend toolchain bin to PATH so cmake/ninja/g++ are found
$env:PATH = "$MINGW\bin;$env:PATH"

# ---------------------------------------------------------------------------
# Build each pass
# ---------------------------------------------------------------------------
$passes = @(
    @{ Dir = 'bogus-control-flow';        Proj = 'BogusControlFlowPass'        }
    @{ Dir = 'control-flow-flattening';   Proj = 'ControlFlowFlatteningPass'   }
    @{ Dir = 'instruction-substitution';  Proj = 'InstructionSubstitutionPass' }
    @{ Dir = 'string-obfuscation';        Proj = 'StringObfuscationPass'       }
    @{ Dir = 'time-stretch';              Proj = 'TimeStretchPass'             }
)
$ok   = [System.Collections.Generic.List[string]]::new()
$fail = [System.Collections.Generic.List[string]]::new()

foreach ($pass in $passes) {
    $passDir  = Join-Path $here $pass.Dir
    $outDll   = Join-Path $passDir 'pass.dll'
    $buildDir = Join-Path $passDir 'build'

    Write-Step "pass: $($pass.Dir)"

    if ((Test-Path $outDll) -and -not $Force) {
        Write-Ok "already built ($outDll). Use -Force to rebuild."
        $ok.Add($pass.Dir)
        continue
    }

    # Clean stale build dir
    if (Test-Path $buildDir) {
        Remove-Item -Recurse -Force $buildDir -EA SilentlyContinue
    }
    New-Item -ItemType Directory -Force $buildDir | Out-Null

    # --- cmake configure ---
    Write-Host "  cmake configure"
    $cmakeArgs = @(
        $passDir,
        '-G', 'Ninja',
        "-DCMAKE_C_COMPILER=$MINGW\bin\gcc.exe",
        "-DCMAKE_CXX_COMPILER=$MINGW\bin\g++.exe",
        "-DLLVM_DIR=$llvmDir",
        '-DCMAKE_BUILD_TYPE=Release'
    )
    Push-Location $buildDir
    $cmakeOut = & "$MINGW\bin\cmake.exe" @cmakeArgs 2>&1
    $cmakeRC  = $LASTEXITCODE
    Pop-Location

    if ($cmakeRC -ne 0) {
        Write-Fail "configure failed for $($pass.Dir)"
        Write-Host ($cmakeOut | Where-Object { $_ -match 'Error|error|WARN' }) -ForegroundColor DarkRed
        $fail.Add($pass.Dir); continue
    }

    # --- ninja build ---
    Write-Host "  ninja build"
    Push-Location $buildDir
    $ninjaOut = & "$MINGW\bin\ninja.exe" 2>&1
    $ninjaRC  = $LASTEXITCODE
    Pop-Location

    if ($ninjaRC -ne 0) {
        Write-Fail "build failed for $($pass.Dir)"
        Write-Host ($ninjaOut | Select-Object -Last 30) -ForegroundColor DarkRed
        $fail.Add($pass.Dir); continue
    }

    # Copy dll out of build dir
    $built = Join-Path $buildDir 'pass.dll'
    if (Test-Path $built) {
        Copy-Item $built $outDll -Force
        $mb = [math]::Round((Get-Item $outDll).Length / 1MB, 1)
        Write-Ok "$outDll  ($mb MB)"
        $ok.Add($pass.Dir)
    } else {
        Write-Fail "pass.dll not found in build dir after successful ninja run"
        $fail.Add($pass.Dir)
    }

    # Clean build dir (not needed at runtime)
    Remove-Item -Recurse -Force $buildDir -EA SilentlyContinue
}

# ---------------------------------------------------------------------------
# Build pass-runner.exe (ABI bridge: MinGW-built, no runtime DLL deps)
# ---------------------------------------------------------------------------
Write-Step "pass-runner"

$runnerSrcDir = Join-Path $here 'pass-runner'
$runnerExe    = Join-Path $here 'pass-runner.exe'
$runnerBuild  = Join-Path $runnerSrcDir 'build'

if ((Test-Path $runnerExe) -and -not $Force) {
    $mb = [math]::Round((Get-Item $runnerExe).Length / 1MB, 1)
    Write-Ok "already built ($runnerExe, $mb MB). Use -Force to rebuild."
    $ok.Add('pass-runner')
} else {
    if (Test-Path $runnerBuild) {
        Remove-Item -Recurse -Force $runnerBuild -EA SilentlyContinue
    }
    New-Item -ItemType Directory -Force $runnerBuild | Out-Null

    Write-Host "  cmake configure"
    $cmakeArgs = @(
        $runnerSrcDir,
        '-G', 'Ninja',
        "-DCMAKE_C_COMPILER=$MINGW\bin\gcc.exe",
        "-DCMAKE_CXX_COMPILER=$MINGW\bin\g++.exe",
        "-DLLVM_DIR=$llvmDir",
        '-DCMAKE_BUILD_TYPE=Release'
    )
    Push-Location $runnerBuild
    $cmakeOut = & "$MINGW\bin\cmake.exe" @cmakeArgs 2>&1
    $cmakeRC  = $LASTEXITCODE
    Pop-Location

    if ($cmakeRC -ne 0) {
        Write-Fail "configure failed for pass-runner"
        Write-Host ($cmakeOut | Where-Object { $_ -match 'Error|error|WARN' }) -ForegroundColor DarkRed
        $fail.Add('pass-runner')
    } else {
        Write-Host "  ninja build"
        Push-Location $runnerBuild
        $ninjaOut = & "$MINGW\bin\ninja.exe" 2>&1
        $ninjaRC  = $LASTEXITCODE
        Pop-Location

        if ($ninjaRC -ne 0) {
            Write-Fail "build failed for pass-runner"
            Write-Host ($ninjaOut | Select-Object -Last 30) -ForegroundColor DarkRed
            $fail.Add('pass-runner')
        } else {
            $built = Join-Path $runnerBuild 'pass-runner.exe'
            if (Test-Path $built) {
                Copy-Item $built $runnerExe -Force
                $mb = [math]::Round((Get-Item $runnerExe).Length / 1MB, 1)
                Write-Ok "$runnerExe  ($mb MB)"
                $ok.Add('pass-runner')
            } else {
                Write-Fail "pass-runner.exe not found in build dir after successful ninja run"
                $fail.Add('pass-runner')
            }
        }

        Remove-Item -Recurse -Force $runnerBuild -EA SilentlyContinue
    }
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Step "Summary"
foreach ($name in $ok)   { Write-Host "    OK : $name" -ForegroundColor Green }
foreach ($name in $fail) { Write-Fail "Failed: $name" }

if ($fail.Count -gt 0) {
    Write-Fail "$($fail.Count) pass build(s) failed."
    exit 1
} else {
    Write-Host ""
    Write-Ok "All $($ok.Count) passes built successfully."
    Write-Host ""
    Write-Host "  Load a pass in clang:  clang -fpass-plugin=<path>\pass.dll ..." -ForegroundColor Gray
}
