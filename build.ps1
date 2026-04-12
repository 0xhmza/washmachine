<#
.SYNOPSIS
    Build washmachine (GUI + CLI) and launch the GUI.

.PARAMETER Config
    Build configuration: Debug (default) or Release.

.PARAMETER Launch
    If set, launch washmachine.exe after a successful build.

.EXAMPLE
    .\build.ps1              # build Debug
    .\build.ps1 -Launch      # build Debug and launch GUI
    .\build.ps1 -Config Release -Launch
#>
param(
    [ValidateSet("Debug","Release")]
    [string]$Config = "Debug",
    [switch]$Launch
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host ""
Write-Host "Building washmachine ($Config)..." -ForegroundColor Cyan

dotnet build "$root\washmachine.sln" -c $Config --nologo
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; exit 1 }

$guiExe = "$root\Output\$Config\washmachine.exe"
$cliExe = "$root\Output\$Config\washmachine-cli.exe"

Write-Host ""
Write-Host "Output:" -ForegroundColor Green
if (Test-Path $guiExe) { Write-Host "  GUI : $guiExe" }
if (Test-Path $cliExe) { Write-Host "  CLI : $cliExe" }

if ($Launch -and (Test-Path $guiExe)) {
    Write-Host ""
    Write-Host "Launching GUI..." -ForegroundColor Cyan
    Start-Process $guiExe
}
