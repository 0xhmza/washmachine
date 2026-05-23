<#
.SYNOPSIS
    Publish Washmachine for release and (optionally) build a bundled MSI installer.

.DESCRIPTION
    Two-phase script:

      Phase A (always runs):
        1. dotnet publish washmachine.csproj  (Release, framework-dependent WinUI 3 app)
        2. dotnet publish Washmachine.Cli/    (Release, single-file CLI)
        3. Stages the published payload + Tools\LLVM + Tools\Bin2Shell + Assets
           into Output\Release-Bundle\payload\

      Phase B (when -Installer is passed):
        4. dotnet wix build  installer\Washmachine.wxs against the payload
           → Output\Release-Bundle\Washmachine-Setup-<version>.msi

    MSVC is NOT bundled. The doctor preflight tells users to install
    Visual Studio Build Tools manually when cl.exe is missing.

.PARAMETER Installer
    Also build the WiX MSI installer (requires the WiX dotnet tool;
    `dotnet tool restore` populates it from .config\dotnet-tools.json).

.PARAMETER SkipBin2ShellProvision
    Skip the runtime provisioning step that downloads Bin2Shell into
    Tools\Bin2Shell\ before staging. Use when offline; the installer will
    ship without Bin2Shell and the first-run preflight will trigger
    provisioning on the end-user's machine.

.PARAMETER Version
    Override the product version embedded in the MSI (default: read from
    washmachine.csproj <Version>).

.EXAMPLE
    .\publish.ps1                     # Stage payload, no MSI
    .\publish.ps1 -Installer          # Stage + build MSI
    .\publish.ps1 -Installer -Version 1.0.1
#>
param(
    [switch]$Installer,
    [switch]$SkipBin2ShellProvision,
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root      = $PSScriptRoot
$config    = "Release"
$bundleDir = Join-Path $root "Output\Release-Bundle"
$stageDir  = Join-Path $bundleDir "payload"

Write-Host ""
Write-Host "=== Washmachine Release Publish ===" -ForegroundColor Cyan

# ── Resolve version ──────────────────────────────────────────────────
if (-not $Version) {
    $csproj = Get-Content (Join-Path $root "washmachine.csproj") -Raw
    if ($csproj -match '<Version>([^<]+)</Version>') { $Version = $matches[1] }
    else { $Version = "1.0.0" }
}
Write-Host "Product version: $Version" -ForegroundColor DarkGray

# ── Phase A.1: publish GUI ───────────────────────────────────────────
Write-Host ""
Write-Host "Publishing GUI..." -ForegroundColor Yellow
dotnet publish "$root\washmachine.csproj" -c $config --nologo
if ($LASTEXITCODE -ne 0) { Write-Host "GUI publish failed." -ForegroundColor Red; exit 1 }

$guiPublishDir = Join-Path $root "Output\Release\publish"
if (-not (Test-Path $guiPublishDir)) {
    Write-Host "Expected GUI publish dir not found: $guiPublishDir" -ForegroundColor Red
    exit 1
}

# ── Phase A.2: publish CLI ───────────────────────────────────────────
Write-Host ""
Write-Host "Publishing CLI..." -ForegroundColor Yellow
dotnet publish "$root\Washmachine.Cli\Washmachine.Cli.csproj" -c $config -r win-x64 --self-contained false --nologo
if ($LASTEXITCODE -ne 0) { Write-Host "CLI publish failed." -ForegroundColor Red; exit 1 }

# CLI's csproj sets PublishDir to Output\Release\publish — it lands next to the GUI.
$cliPublishDir = $guiPublishDir
$cliExe = Join-Path $cliPublishDir "washmachine-cli.exe"

# ── Phase A.3: provision Bin2Shell ───────────────────────────────────
if (-not $SkipBin2ShellProvision) {
    Write-Host ""
    Write-Host "Provisioning Bin2Shell into Tools\Bin2Shell..." -ForegroundColor Yellow
    if (Test-Path $cliExe) {
        & $cliExe provision 2>&1 | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Provisioning failed; continuing without Bin2Shell." -ForegroundColor DarkYellow
        }
    } else {
        Write-Host "Skipping provisioning — washmachine-cli.exe not found." -ForegroundColor DarkYellow
    }
}

# ── Phase A.4: stage payload ─────────────────────────────────────────
Write-Host ""
Write-Host "Staging payload at $stageDir..." -ForegroundColor Yellow
if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir }
New-Item -ItemType Directory -Force $stageDir | Out-Null

Copy-Item -Path "$guiPublishDir\*" -Destination $stageDir -Recurse -Force

# Bundle LLVM tools (must be populated under .\Tools\LLVM\ already — see
# Assets\llvm-passes\build-all.ps1 for how to build the pass DLLs locally).
$llvmSrc = Join-Path $root "Tools\LLVM"
$stagedTools = Join-Path $stageDir "Tools"
New-Item -ItemType Directory -Force $stagedTools | Out-Null
if (Test-Path $llvmSrc) {
    Write-Host "  + Tools\LLVM\"
    Copy-Item -Path $llvmSrc -Destination $stagedTools -Recurse -Force
} else {
    Write-Host "  ! Tools\LLVM not found — LLVM obfuscation backend will require local install." -ForegroundColor DarkYellow
}

$totalMb = [math]::Round((Get-ChildItem $stageDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
Write-Host ""
Write-Host "Payload staged ($totalMb MB total)." -ForegroundColor Green
Write-Host "  Location: $stageDir" -ForegroundColor Green

# ── Phase B: MSI ─────────────────────────────────────────────────────
if (-not $Installer) {
    Write-Host ""
    Write-Host "Skipping installer (re-run with -Installer to build the MSI)." -ForegroundColor DarkGray
    exit 0
}

Write-Host ""
Write-Host "Restoring WiX tool..." -ForegroundColor Yellow
dotnet tool restore 2>&1 | Out-Host
if ($LASTEXITCODE -ne 0) { Write-Host "dotnet tool restore failed." -ForegroundColor Red; exit 1 }

$msiOut = Join-Path $bundleDir "Washmachine-Setup-$Version.msi"
Write-Host ""
Write-Host "Building MSI..." -ForegroundColor Yellow

# -bindpath maps the !(bindpath.payload) token used inside Washmachine.wxs.
dotnet wix build `
    -arch x64 `
    -d WashmachineVersion=$Version `
    -bindpath "payload=$stageDir" `
    -o $msiOut `
    "$root\installer\Washmachine.wxs"
if ($LASTEXITCODE -ne 0) { Write-Host "wix build failed." -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "Installer built." -ForegroundColor Green
Write-Host "  $msiOut" -ForegroundColor Green
