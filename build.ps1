<#
.SYNOPSIS
    Clean build washmachine (GUI + CLI) in Release mode.

.PARAMETER Launch
    If set, launch washmachine.exe after a successful build.

.PARAMETER SkipClean
    If set, skip the clean step (faster incremental builds).

.EXAMPLE
    .\build.ps1              # clean build Release
    .\build.ps1 -Launch      # clean build Release and launch GUI
    .\build.ps1 -SkipClean   # incremental build (no clean)
#>
param(
    [switch]$Launch,
    [switch]$SkipClean
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$Config = "Release"

Write-Host ""
Write-Host "=== Washmachine Build (Release) ===" -ForegroundColor Cyan

# Clean all build artifacts
if (-not $SkipClean) {
    Write-Host ""
    Write-Host "Cleaning build artifacts..." -ForegroundColor Yellow

    # Stop any running washmachine processes that would lock build files
    $procs = Get-Process -Name "washmachine*" -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "  Stopping running washmachine processes..." -ForegroundColor DarkGray
        $procs | ForEach-Object {
            Write-Host "    Stopping $($_.ProcessName) (PID $($_.Id))..." -ForegroundColor DarkGray
            $_ | Stop-Process -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Milliseconds 500
    }

    # Remove bin and obj directories from all projects
    $dirsToClean = @(
        "$root\bin",
        "$root\obj",
        "$root\Washmachine.Core\bin",
        "$root\Washmachine.Core\obj",
        "$root\Washmachine.Cli\bin",
        "$root\Washmachine.Cli\obj",
        "$root\Output"
    )

    foreach ($dir in $dirsToClean) {
        if (Test-Path $dir) {
            Write-Host "  Removing: $dir" -ForegroundColor DarkGray
            Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    
    Write-Host "  Clean complete." -ForegroundColor Green
}

Write-Host ""
Write-Host "Building washmachine (Release)..." -ForegroundColor Cyan

# Build each project separately to handle CLI's RuntimeIdentifier correctly
# First, build Core (no special requirements)
dotnet build "$root\Washmachine.Core\Washmachine.Core.csproj" -c $Config --nologo
if ($LASTEXITCODE -ne 0) { Write-Host "Core build failed." -ForegroundColor Red; exit 1 }

# Build GUI project
dotnet build "$root\washmachine.csproj" -c $Config --nologo
if ($LASTEXITCODE -ne 0) { Write-Host "GUI build failed." -ForegroundColor Red; exit 1 }

# Build CLI with runtime identifier (required for Release)
dotnet build "$root\Washmachine.Cli\Washmachine.Cli.csproj" -c $Config -r win-x64 --self-contained false --nologo
if ($LASTEXITCODE -ne 0) { Write-Host "CLI build failed." -ForegroundColor Red; exit 1 }

$guiExe = "$root\Output\$Config\washmachine.exe"
$cliExe = "$root\Output\$Config\washmachine-cli.exe"

Write-Host ""
Write-Host "Build succeeded!" -ForegroundColor Green
Write-Host "Output:" -ForegroundColor Green
if (Test-Path $guiExe) { Write-Host "  GUI : $guiExe" }
if (Test-Path $cliExe) { Write-Host "  CLI : $cliExe" }

if ($Launch -and (Test-Path $guiExe)) {
    Write-Host ""
    Write-Host "Launching GUI..." -ForegroundColor Cyan
    Start-Process $guiExe
}
