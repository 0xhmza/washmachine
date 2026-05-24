<#
.SYNOPSIS
    Washmachine build orchestrator. Build / publish / installer / LLVM passes / clean.

.DESCRIPTION
    Single entry point for every developer workflow. Run with no arguments to get
    an interactive menu (with inline help). Pass -Action to skip the menu for CI.

    Actions:
        build       Restore + build all three projects (Release).
        run         Build, then launch the GUI.
        publish     Stage a redistributable payload at Output\Release-Bundle\payload.
        installer   Publish + build the WiX MSI at Output\Release-Bundle.
        passes      Compile every Assets\llvm-passes\<id>\pass.dll plugin.
                    Auto-discovers an LLVM SDK; prompts (or exits) when absent.
        all         clean -> build -> passes -> publish -> installer.
        clean       Wipe bin\, obj\, Output\ across all projects.

    Every run is mirrored to Logs\build\build_<timestamp>.log (survives `clean`).

.PARAMETER Action
    Skip the menu and run one action.

.PARAMETER LlvmDir
    Directory containing LLVMConfig.cmake. Auto-discovery still runs first; this
    value is used as the highest-priority candidate.

.PARAMETER CleanFirst
    When used with build/publish/installer/passes, clean before running.

.PARAMETER CleanBuilds
    For the passes action: wipe each pass's build\ folder before cmake.

.PARAMETER SkipProvision
    For publish/installer: don't run washmachine-cli provision (Bin2Shell).

.PARAMETER Version
    Override the MSI product version (defaults to <Version> from washmachine.csproj).

.PARAMETER NonInteractive
    Disable any prompts. Missing prerequisites cause an immediate non-zero exit
    instead of asking the user. CI should set this.

.EXAMPLE
    .\build.ps1
        Interactive menu.

.EXAMPLE
    .\build.ps1 -Action build -CleanFirst
        Clean rebuild from CI.

.EXAMPLE
    .\build.ps1 -Action passes -LlvmDir "C:\Dev\llvm-sdk\lib\cmake\llvm" -CleanBuilds
        Rebuild every pass plugin against a specific LLVM SDK.

.EXAMPLE
    .\build.ps1 -Action installer -Version 1.0.3
        Produce a versioned MSI.
#>
[CmdletBinding()]
param(
    [ValidateSet('menu','build','run','publish','installer','passes','all','clean')]
    [string]$Action = 'menu',

    [string]$LlvmDir,
    [switch]$CleanFirst,
    [switch]$CleanBuilds,
    [switch]$SkipProvision,
    [string]$Version,
    [switch]$NonInteractive
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ── Constants ─────────────────────────────────────────────────────────────
$Script:Root        = $PSScriptRoot
$Script:Config      = 'Release'
$Script:Solution    = Join-Path $Script:Root 'washmachine.sln'
$Script:OutputDir   = Join-Path $Script:Root 'Output'
$Script:BundleDir   = Join-Path $Script:OutputDir 'Release-Bundle'
$Script:StageDir    = Join-Path $Script:BundleDir 'payload'
$Script:LogDir      = Join-Path $Script:Root 'Logs\build'
$Script:PassesRoot  = Join-Path $Script:Root 'Assets\llvm-passes'
$Script:ToolsLlvm   = Join-Path $Script:Root 'Tools\LLVM'
$Script:LogFile     = $null

# ── Logging primitives ────────────────────────────────────────────────────
function Initialize-Log {
    if (-not (Test-Path $Script:LogDir)) { New-Item -ItemType Directory -Force $Script:LogDir | Out-Null }
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $Script:LogFile = Join-Path $Script:LogDir "build_$stamp.log"
    "[$([DateTime]::Now.ToString('s'))] build.ps1 invoked (action=$Action, host=$($Host.Name), psver=$($PSVersionTable.PSVersion))" |
        Out-File -FilePath $Script:LogFile -Encoding UTF8
}

function Write-Log {
    param([string]$Message, [string]$Color = 'Gray', [switch]$NoNewline)
    if ($NoNewline) { Write-Host $Message -ForegroundColor $Color -NoNewline }
    else            { Write-Host $Message -ForegroundColor $Color }
    if ($Script:LogFile) {
        # Self-heal: an earlier `clean` may have wiped the log directory.
        $dir = Split-Path -Parent $Script:LogFile
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
        $line = if ($NoNewline) { $Message } else { $Message + [Environment]::NewLine }
        Add-Content -Path $Script:LogFile -Value $line -NoNewline -Encoding UTF8
    }
}

function Section($title)  { Write-Log ""; Write-Log "==> $title" 'Cyan' }
function Step($text)      { Write-Log "  $text" 'White' }
function Ok($text)        { Write-Log "  $text" 'Green' }
function Note($text)      { Write-Log "  $text" 'DarkGray' }
function Warn($text)      { Write-Log "  WARN: $text" 'Yellow' }
function Die($text, [int]$code = 1) { Write-Log "ERROR: $text" 'Red'; exit $code }

function Invoke-External {
    param([string]$Label, [scriptblock]$Block)
    Step $Label
    & $Block
    if ($LASTEXITCODE -ne 0) { Die "$Label failed (exit $LASTEXITCODE)." $LASTEXITCODE }
}

# ── Prerequisite checks ───────────────────────────────────────────────────
function Assert-Tool {
    param([string]$Name, [string]$InstallHint)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        Die "'$Name' not found on PATH. $InstallHint"
    }
}

function Stop-RunningInstances {
    $procs = Get-Process -Name 'washmachine*' -ErrorAction SilentlyContinue
    if ($procs) {
        Note "Stopping $($procs.Count) running washmachine process(es)..."
        $procs | ForEach-Object { try { $_ | Stop-Process -Force -ErrorAction Stop } catch { } }
        Start-Sleep -Milliseconds 250
    }
}

# ── LLVM SDK discovery ────────────────────────────────────────────────────
function Test-LlvmSdkDir {
    param([string]$Path)
    return ($Path -and (Test-Path (Join-Path $Path 'LLVMConfig.cmake')))
}

function Find-LlvmSdk {
    $candidates = @(
        $LlvmDir,
        $env:LLVM_DIR,
        'C:\Program Files\LLVM\lib\cmake\llvm',
        'C:\Program Files (x86)\LLVM\lib\cmake\llvm',
        (Join-Path $env:USERPROFILE 'scoop\apps\llvm\current\lib\cmake\llvm'),
        'C:\msys64\mingw64\lib\cmake\llvm',
        (Join-Path $Script:ToolsLlvm 'lib\cmake\llvm')
    )
    foreach ($c in $candidates) {
        if (Test-LlvmSdkDir $c) {
            return (Resolve-Path $c).Path
        }
    }
    return $null
}

function Request-LlvmSdk {
    $found = Find-LlvmSdk
    if ($found) {
        Ok "LLVM SDK : $found"
        return $found
    }

    Warn "LLVMConfig.cmake not found in any standard location."
    Note "Stock 'LLVM-x.x.x-win64.exe' ships only the runtime, not the C++ headers."
    Note "Install options:"
    Note "  1. scoop install llvm                          (recommended on Windows)"
    Note "  2. pacman -S mingw-w64-x86_64-llvm             (MSYS2)"
    Note "  3. Build LLVM from source + cmake --install    (control-freaks)"

    if ($NonInteractive) {
        Die "LLVM developer SDK is required for the 'passes' action. Re-run with -LlvmDir <path-to-lib\cmake\llvm>." 2
    }

    while ($true) {
        Write-Host ""
        Write-Host "Enter path to LLVMConfig.cmake directory (or 'q' to quit): " -ForegroundColor Yellow -NoNewline
        $input = Read-Host
        if ($input -in @('q','quit','exit','')) {
            Die "Aborted by user — LLVM SDK is required." 2
        }
        $resolved = $input.Trim('"').Trim()
        if (Test-LlvmSdkDir $resolved) {
            $abs = (Resolve-Path $resolved).Path
            Ok "LLVM SDK : $abs"
            return $abs
        }
        Warn "No LLVMConfig.cmake under '$resolved'. Try again."
    }
}

# ── diaguids.lib patch (Windows LLVM gotcha) ──────────────────────────────
# Official LLVM Windows release bakes the build machine's absolute diaguids.lib
# path into LLVMExports.cmake. If the path doesn't exist locally the pass build
# dies with LNK1181. Repoint it at this machine's VS install before invoking cmake.
function Repair-LlvmExports {
    param([string]$SdkDir)
    $exports = Join-Path $SdkDir 'LLVMExports.cmake'
    if (-not (Test-Path $exports)) { return }

    $dia = Get-ChildItem 'C:\Program Files\Microsoft Visual Studio' -Recurse `
        -Filter 'diaguids.lib' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'amd64\\diaguids\.lib$' } |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $dia) { return }

    $normalized = $dia -replace '\\','/'
    $content = Get-Content -Raw $exports
    if ($content -notmatch 'diaguids\.lib' -or $content -match [regex]::Escape($normalized)) { return }

    $patched = [regex]::Replace($content,
        '[A-Z]:/[^"]*?DIA SDK/lib/amd64/diaguids\.lib', $normalized)
    if ($patched -ne $content) {
        Set-Content -Path $exports -Value $patched -NoNewline
        Note "Patched diaguids.lib path in LLVMExports.cmake -> $normalized"
    }
}

# ── Version resolution ────────────────────────────────────────────────────
function Resolve-ProductVersion {
    if ($Version) { return $Version }
    $csproj = Get-Content (Join-Path $Script:Root 'washmachine.csproj') -Raw
    if ($csproj -match '<Version>([^<]+)</Version>') { return $Matches[1] }
    return '1.0.0'
}

# ─────────────────────────────────────────────────────────────────────────
#  Actions
# ─────────────────────────────────────────────────────────────────────────

function Invoke-Clean {
    Section "Clean"
    Stop-RunningInstances
    $targets = @(
        (Join-Path $Script:Root 'bin'),
        (Join-Path $Script:Root 'obj'),
        (Join-Path $Script:Root 'Washmachine.Core\bin'),
        (Join-Path $Script:Root 'Washmachine.Core\obj'),
        (Join-Path $Script:Root 'Washmachine.Cli\bin'),
        (Join-Path $Script:Root 'Washmachine.Cli\obj'),
        $Script:OutputDir
    )
    foreach ($t in $targets) {
        if (Test-Path $t) { Note "rm $t"; Remove-Item -Recurse -Force $t -ErrorAction SilentlyContinue }
    }
    Ok "Clean complete."
}

function Invoke-Build {
    Section "Build ($Script:Config)"
    if ($CleanFirst) { Invoke-Clean }
    Assert-Tool 'dotnet' 'Install the .NET 8 SDK from https://dot.net/.'

    # Build each project in dependency order. Each `dotnet build` invocation
    # restores its own per-project assets — a solution-level restore can't satisfy
    # the GUI's net8.0-windows10.x/win-x64 target without an explicit RID. The CLI
    # additionally needs -r win-x64 because Washmachine.Cli.csproj sets
    # RuntimeIdentifier conditionally on Release.
    Invoke-External "dotnet build Washmachine.Core ($Script:Config)" {
        dotnet build (Join-Path $Script:Root 'Washmachine.Core\Washmachine.Core.csproj') -c $Script:Config --nologo 2>&1 |
            Tee-Object -Append -FilePath $Script:LogFile | Out-Host
    }
    Invoke-External "dotnet build washmachine (GUI, $Script:Config)" {
        dotnet build (Join-Path $Script:Root 'washmachine.csproj') -c $Script:Config --nologo 2>&1 |
            Tee-Object -Append -FilePath $Script:LogFile | Out-Host
    }
    Invoke-External "dotnet build Washmachine.Cli ($Script:Config, win-x64)" {
        dotnet build (Join-Path $Script:Root 'Washmachine.Cli\Washmachine.Cli.csproj') `
            -c $Script:Config -r win-x64 --self-contained false --nologo 2>&1 |
            Tee-Object -Append -FilePath $Script:LogFile | Out-Host
    }

    $gui = Join-Path $Script:OutputDir "$Script:Config\washmachine.exe"
    $cli = Join-Path $Script:OutputDir "$Script:Config\washmachine-cli.exe"
    Ok "Build succeeded."
    if (Test-Path $gui) { Ok "  GUI : $gui" }
    if (Test-Path $cli) { Ok "  CLI : $cli" }
    return [pscustomobject]@{ Gui = $gui; Cli = $cli }
}

function Invoke-Run {
    $r = Invoke-Build
    if (Test-Path $r.Gui) {
        Section "Launch"
        Step "Start-Process $($r.Gui)"
        Start-Process $r.Gui
    } else {
        Die "GUI exe not found at $($r.Gui)."
    }
}

function Invoke-Publish {
    Section "Publish"
    if ($CleanFirst) { Invoke-Clean }
    Assert-Tool 'dotnet' 'Install the .NET 8 SDK from https://dot.net/.'
    $ver = Resolve-ProductVersion
    Note "Product version: $ver"

    Invoke-External "dotnet publish washmachine (GUI)" {
        dotnet publish (Join-Path $Script:Root 'washmachine.csproj') -c $Script:Config --nologo 2>&1 |
            Tee-Object -Append -FilePath $Script:LogFile | Out-Host
    }
    $guiPub = Join-Path $Script:OutputDir "$Script:Config\publish"
    if (-not (Test-Path $guiPub)) { Die "GUI publish dir not found: $guiPub" }

    Invoke-External "dotnet publish Washmachine.Cli" {
        dotnet publish (Join-Path $Script:Root 'Washmachine.Cli\Washmachine.Cli.csproj') `
            -c $Script:Config -r win-x64 --self-contained false --nologo 2>&1 |
            Tee-Object -Append -FilePath $Script:LogFile | Out-Host
    }
    $cliExe = Join-Path $guiPub 'washmachine-cli.exe'

    if (-not $SkipProvision -and (Test-Path $cliExe)) {
        Section "Provision Bin2Shell"
        & $cliExe provision 2>&1 | Tee-Object -Append -FilePath $Script:LogFile | Out-Host
        if ($LASTEXITCODE -ne 0) { Warn "Provision failed (exit $LASTEXITCODE); installer ships without Bin2Shell." }
    }

    Section "Stage payload"
    Step "Target: $Script:StageDir"
    if (Test-Path $Script:StageDir) { Remove-Item -Recurse -Force $Script:StageDir }
    New-Item -ItemType Directory -Force $Script:StageDir | Out-Null
    Copy-Item (Join-Path $guiPub '*') -Destination $Script:StageDir -Recurse -Force

    $stagedTools = Join-Path $Script:StageDir 'Tools'
    New-Item -ItemType Directory -Force $stagedTools | Out-Null
    if (Test-Path $Script:ToolsLlvm) {
        Note "Bundling Tools\LLVM\"
        Copy-Item $Script:ToolsLlvm -Destination $stagedTools -Recurse -Force
    } else {
        Warn "Tools\LLVM not present — LLVM backend will require an external install on end-user machines."
    }

    $mb = [math]::Round((Get-ChildItem $Script:StageDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
    Ok "Staged $mb MB at $Script:StageDir"
    return [pscustomobject]@{ Stage = $Script:StageDir; Version = $ver }
}

function Invoke-Installer {
    $pub = Invoke-Publish
    Section "Installer"
    Assert-Tool 'dotnet' 'Install the .NET 8 SDK.'

    Invoke-External "dotnet tool restore (wix)" {
        dotnet tool restore 2>&1 | Tee-Object -Append -FilePath $Script:LogFile | Out-Host
    }
    $msi = Join-Path $Script:BundleDir "Washmachine-Setup-$($pub.Version).msi"
    Invoke-External "dotnet wix build -> $msi" {
        dotnet wix build `
            -arch x64 `
            -d WashmachineVersion=$($pub.Version) `
            -bindpath "payload=$($pub.Stage)" `
            -o $msi `
            (Join-Path $Script:Root 'installer\Washmachine.wxs') 2>&1 |
            Tee-Object -Append -FilePath $Script:LogFile | Out-Host
    }
    Ok "MSI: $msi"
}

function Invoke-Passes {
    Section "LLVM passes"

    $buildAllScript = Join-Path $Script:PassesRoot 'build-all.ps1'
    if (-not (Test-Path $buildAllScript)) {
        Die "build-all.ps1 not found at $buildAllScript" 3
    }

    # build-all.ps1 resolution order: bundled Tools\msys64 -> system C:\msys64 -> error.
    # -SkipDownload enforces that: no auto-download, just error if toolchain is absent.
    $psArgs = @{ SkipDownload = $true }
    if ($CleanBuilds) { $psArgs['Force'] = $true }

    & $buildAllScript @psArgs 2>&1 | Tee-Object -Append -FilePath $Script:LogFile | Out-Host

    if ($LASTEXITCODE -ne 0) {
        Die "One or more LLVM pass builds failed." 3
    }
}

function Invoke-All {
    Invoke-Clean
    Invoke-Build | Out-Null
    Invoke-Passes
    Invoke-Installer
}

# ─────────────────────────────────────────────────────────────────────────
#  Interactive menu
# ─────────────────────────────────────────────────────────────────────────

function Show-Menu {
    Write-Log ""
    Write-Log "================================================" 'Cyan'
    Write-Log "  Washmachine builder" 'Cyan'
    Write-Log "================================================" 'Cyan'
    Write-Log @'

  1   build       compile Release artifacts
  2   run         build, then launch the GUI
  3   publish     stage a redistributable payload
  4   installer   publish + build MSI
  5   passes      compile LLVM obfuscation plugins
  6   all         clean -> build -> passes -> installer
  7   clean       wipe bin / obj / Output
  q   quit

  Hints:
    * Get-Help .\build.ps1 -Full         full parameter reference
    * .\build.ps1 -Action <name>         skip menu, run one action
    * -CleanFirst -CleanBuilds -SkipProvision -NonInteractive -Version <x>
    * Log : Logs\build\build_<timestamp>.log

'@ 'Gray'
}

function Invoke-Menu {
    while ($true) {
        Show-Menu
        Write-Host "Choose: " -ForegroundColor Yellow -NoNewline
        $choice = (Read-Host).Trim().ToLower()
        switch ($choice) {
            { $_ -in '1','build'     } { Invoke-Build     | Out-Null; return }
            { $_ -in '2','run'       } { Invoke-Run;                  return }
            { $_ -in '3','publish'   } { Invoke-Publish   | Out-Null; return }
            { $_ -in '4','installer' } { Invoke-Installer;            return }
            { $_ -in '5','passes'    } { Invoke-Passes;               return }
            { $_ -in '6','all'       } { Invoke-All;                  return }
            { $_ -in '7','clean'     } { Invoke-Clean;                return }
            { $_ -in 'q','quit','exit' } { return }
            default                  { Warn "Unknown choice: '$choice'" }
        }
    }
}

# ─────────────────────────────────────────────────────────────────────────
#  Dispatch
# ─────────────────────────────────────────────────────────────────────────

Initialize-Log
try {
    switch ($Action) {
        'menu'      { Invoke-Menu }
        'build'     { Invoke-Build     | Out-Null }
        'run'       { Invoke-Run }
        'publish'   { Invoke-Publish   | Out-Null }
        'installer' { Invoke-Installer }
        'passes'    { Invoke-Passes }
        'all'       { Invoke-All }
        'clean'     { Invoke-Clean }
    }
    Write-Log ""
    Ok "Done. Log: $Script:LogFile"
}
catch {
    Write-Log ""
    Write-Log "ERROR: $($_.Exception.Message)" 'Red'
    if ($_.ScriptStackTrace) { Write-Log $_.ScriptStackTrace 'DarkGray' }
    Write-Log "Log: $Script:LogFile" 'Yellow'
    exit 1
}
