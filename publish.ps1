<#
.SYNOPSIS
    Publishes the Washmachine GUI and CLI to a single output directory.
.DESCRIPTION
    Builds both projects in Release and publishes them to Output\Release\publish\.
    Both the GUI and CLI publish directly to the same folder — no manual copy needed.
.PARAMETER Clean
    Remove the publish output directory before building.
#>
param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$guiProj  = Join-Path $root 'washmachine.csproj'
$cliProj  = Join-Path $root 'Washmachine.Cli\Washmachine.Cli.csproj'
$publishDir = Join-Path $root 'Output\Release\publish'

if ($Clean -and (Test-Path $publishDir)) {
    Write-Host "[*] Cleaning $publishDir" -ForegroundColor Yellow
    Remove-Item $publishDir -Recurse -Force
}

Write-Host "`n[1/2] Publishing GUI..." -ForegroundColor Cyan
dotnet publish $guiProj -c Release 2>&1 | ForEach-Object { Write-Host "  $_" }
if ($LASTEXITCODE -ne 0) { Write-Error "GUI publish failed"; exit 1 }

Write-Host "`n[2/2] Publishing CLI..." -ForegroundColor Cyan
dotnet publish $cliProj -c Release 2>&1 | ForEach-Object { Write-Host "  $_" }
if ($LASTEXITCODE -ne 0) { Write-Error "CLI publish failed"; exit 1 }

Write-Host "`n[OK] Published to: $publishDir" -ForegroundColor Green
Get-ChildItem $publishDir | Format-Table Name, Length -AutoSize
