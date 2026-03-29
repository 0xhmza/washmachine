<#
.SYNOPSIS
    Publishes the Washmachine GUI and CLI to a single output directory.
.DESCRIPTION
    Builds both projects in Release, publishes them, and copies the CLI
    into the GUI publish folder so everything ships together.
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

Write-Host "`n[1/3] Publishing GUI..." -ForegroundColor Cyan
dotnet publish $guiProj -c Release --no-restore 2>&1 | ForEach-Object { Write-Host "  $_" }
if ($LASTEXITCODE -ne 0) { Write-Error "GUI publish failed"; exit 1 }

Write-Host "`n[2/3] Publishing CLI..." -ForegroundColor Cyan
dotnet publish $cliProj -c Release --no-restore 2>&1 | ForEach-Object { Write-Host "  $_" }
if ($LASTEXITCODE -ne 0) { Write-Error "CLI publish failed"; exit 1 }

Write-Host "`n[3/3] Copying CLI into GUI publish folder..." -ForegroundColor Cyan
$cliPublishDir = Join-Path $root 'Output\cli\publish'
if (Test-Path $cliPublishDir) {
    $cliFiles = Get-ChildItem $cliPublishDir -File | Where-Object { $_.Name -like 'washmachine-cli*' }
    foreach ($f in $cliFiles) {
        Copy-Item $f.FullName -Destination $publishDir -Force
        Write-Host "  Copied $($f.Name)"
    }
}

Write-Host "`n[OK] Published to: $publishDir" -ForegroundColor Green
Get-ChildItem $publishDir | Format-Table Name, Length -AutoSize
