<#
.SYNOPSIS
  One-shot dev build for CodexBar on Windows: Swift CLI engine + .NET WPF tray.

.DESCRIPTION
  Builds everything needed to run CodexBar from the repo while developing:
    1. ThirdParty\sqlite-win\sqlite3.lib   (built once, automatically, if missing)
    2. The Swift CLI (CodexBarCLI)         via Scripts\win-build.cmd
    3. The .NET WPF tray                   (WindowsTray\CodexBarTray.csproj)

  After a debug build the tray finds the CLI automatically through its dev
  fallback (AppPaths -> .build\x86_64-unknown-windows-msvc\debug\CodexBarCLI.exe)
  and picks up the Swift runtime DLLs from your installed toolchain, so -Run
  works straight from the repo with no staging.

  For a shippable, self-contained folder + zip, use -Package (delegates to
  Scripts\win-package.ps1).

.PARAMETER Release
  Build both components in release configuration instead of debug.

.PARAMETER Run
  Launch the tray after a successful build.

.PARAMETER Clean
  Remove build outputs (.build, WindowsTray\bin, WindowsTray\obj) before building.

.PARAMETER CliOnly
  Build only the Swift CLI.

.PARAMETER TrayOnly
  Build only the .NET tray (assumes the CLI is already built).

.PARAMETER Package
  Build a full self-contained distributable via Scripts\win-package.ps1 and exit.

.EXAMPLE
  .\build.ps1                 # debug build of CLI + tray
  .\build.ps1 -Run           # build, then launch the tray
  .\build.ps1 -Release       # release build of both
  .\build.ps1 -Clean -Run    # clean rebuild, then launch
  .\build.ps1 -Package       # full self-contained package + zip
#>
[CmdletBinding()]
param(
    [switch] $Release,
    [switch] $Run,
    [switch] $Clean,
    [switch] $CliOnly,
    [switch] $TrayOnly,
    [switch] $Package
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot

function Write-Step($n, $total, $msg) {
    Write-Host "==> $n/$total  $msg" -ForegroundColor Cyan
}

# Full package path: delegate and stop.
if ($Package) {
    & (Join-Path $repo "Scripts\win-package.ps1")
    exit $LASTEXITCODE
}

$config = if ($Release) { "release" } else { "debug" }
$dotnetConfig = if ($Release) { "Release" } else { "Debug" }
$buildCli = -not $TrayOnly
$buildTray = -not $CliOnly

# Count steps for nicer progress headers.
$total = 0
if ($Clean) { $total++ }
if ($buildCli) { $total += 2 }   # sqlite check + swift build
if ($buildTray) { $total++ }
$step = 0

if ($Clean) {
    $step++; Write-Step $step $total "Cleaning build outputs ..."
    foreach ($p in @(
            (Join-Path $repo ".build"),
            (Join-Path $repo "WindowsTray\bin"),
            (Join-Path $repo "WindowsTray\obj"))) {
        if (Test-Path $p) { Remove-Item -Recurse -Force $p }
    }
}

if ($buildCli) {
    # 1. sqlite3.lib is a one-time prerequisite for linking the Swift CLI.
    $step++; Write-Step $step $total "Checking ThirdParty\sqlite-win\sqlite3.lib ..."
    $sqliteLib = Join-Path $repo "ThirdParty\sqlite-win\sqlite3.lib"
    if (-not (Test-Path $sqliteLib)) {
        Write-Host "    not found - building it (one-time) ..." -ForegroundColor Yellow
        & cmd /c "`"$repo\Scripts\win-build-sqlite.cmd`""
        if ($LASTEXITCODE -ne 0) { throw "Failed to build sqlite3.lib." }
        if (-not (Test-Path $sqliteLib)) { throw "sqlite3.lib still missing after build." }
    }
    else {
        Write-Host "    present." -ForegroundColor DarkGray
    }

    # 2. Swift CLI engine.
    $step++; Write-Step $step $total "Building Swift CLI (CodexBarCLI, $config) ..."
    & cmd /c "`"$repo\Scripts\win-build.cmd`" build -c $config --product CodexBarCLI"
    if ($LASTEXITCODE -ne 0) { throw "Swift CLI build failed." }
    $cliExe = Join-Path $repo ".build\x86_64-unknown-windows-msvc\$config\CodexBarCLI.exe"
    if (-not (Test-Path $cliExe)) { throw "Expected CLI not found: $cliExe" }
    Write-Host "    -> $cliExe" -ForegroundColor DarkGray
}

if ($buildTray) {
    # 3. .NET WPF tray.
    $step++; Write-Step $step $total "Building .NET tray ($dotnetConfig) ..."
    & dotnet build (Join-Path $repo "WindowsTray\CodexBarTray.csproj") -c $dotnetConfig --nologo
    if ($LASTEXITCODE -ne 0) { throw ".NET tray build failed." }
}

Write-Host ""
Write-Host "Build complete." -ForegroundColor Green

$trayExe = Join-Path $repo "WindowsTray\bin\$dotnetConfig\net8.0-windows\CodexBarTray.exe"
if ($buildTray) { Write-Host "  Tray: $trayExe" }

if ($Run) {
    if (-not $buildTray) { throw "-Run requires the tray to be built (don't combine with -CliOnly)." }
    if (-not (Test-Path $trayExe)) { throw "Tray executable not found: $trayExe" }
    Write-Host ""
    Write-Host "Launching tray ..." -ForegroundColor Cyan
    Start-Process -FilePath $trayExe
}
