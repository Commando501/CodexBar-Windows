<#
.SYNOPSIS
  Builds a portable, self-contained Windows package of CodexBar.

.DESCRIPTION
  Produces dist\CodexBar\ (a single folder you can copy anywhere and run) and
  dist\CodexBar-win-x64.zip. The folder contains:
    - The .NET WPF tray (self-contained: bundles the .NET 8 runtime).
    - codexbar.exe (the Swift CLI, release build).
    - The Swift runtime DLLs, beside codexbar.exe (so Windows loads them
      automatically — no PATH setup, no Swift install on the target machine).

  Requires: the Swift toolchain + VS build env (via Scripts\win-build.cmd) and a
  one-time `Scripts\win-build-sqlite.cmd` to have produced ThirdParty\sqlite-win\sqlite3.lib.
#>
[CmdletBinding()]
param(
    [string] $SwiftRuntimeDir = "$env:LOCALAPPDATA\Programs\Swift\Runtimes\6.3.2\usr\bin"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo "dist"
$stage = Join-Path $dist "CodexBar"
$zip = Join-Path $dist "CodexBar-win-x64.zip"

Write-Host "==> 1/4  Building Swift CLI (release)..." -ForegroundColor Cyan
& cmd /c "`"$repo\Scripts\win-build.cmd`" build -c release --product CodexBarCLI"
if ($LASTEXITCODE -ne 0) { throw "Swift release build failed." }
$cliExe = Join-Path $repo ".build\x86_64-unknown-windows-msvc\release\CodexBarCLI.exe"
if (-not (Test-Path $cliExe)) { throw "Expected release CLI not found: $cliExe" }

Write-Host "==> 2/4  Publishing .NET tray (self-contained, win-x64)..." -ForegroundColor Cyan
$publishDir = Join-Path $repo "WindowsTray\bin\Release\net8.0-windows\win-x64\publish"
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

# Stamp the tray with the marketing version from version.env so the in-app
# update checker can compare itself against GitHub releases.
$versionArgs = @()
$versionEnv = Join-Path $repo "version.env"
if (Test-Path $versionEnv) {
    $match = Get-Content $versionEnv | Select-String '^MARKETING_VERSION=(.+)$'
    $marketing = $match.Matches.Groups[1].Value
    if ($marketing) {
        Write-Host "    version $marketing (from version.env)"
        $versionArgs = @("-p:Version=$marketing")
    }
}

& dotnet publish (Join-Path $repo "WindowsTray\CodexBarTray.csproj") `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false @versionArgs
if ($LASTEXITCODE -ne 0) { throw ".NET publish failed." }
if (-not (Test-Path $publishDir)) { throw "Expected publish dir not found: $publishDir" }

Write-Host "==> 3/4  Assembling dist\CodexBar ..." -ForegroundColor Cyan
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# .NET tray (self-contained) + its dependencies.
Copy-Item -Path (Join-Path $publishDir "*") -Destination $stage -Recurse -Force

# Swift CLI, named codexbar.exe so AppPaths finds it beside the tray.
Copy-Item -Path $cliExe -Destination (Join-Path $stage "codexbar.exe") -Force

# Swift runtime DLLs, beside codexbar.exe (auto-loaded from the same folder).
if (-not (Test-Path $SwiftRuntimeDir)) { throw "Swift runtime dir not found: $SwiftRuntimeDir" }
$dlls = Get-ChildItem -Path $SwiftRuntimeDir -Filter *.dll
Copy-Item -Path $dlls.FullName -Destination $stage -Force
Write-Host "    copied $($dlls.Count) Swift runtime DLLs"

Write-Host "==> 4/4  Zipping ..." -ForegroundColor Cyan
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $stage -DestinationPath $zip

$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Folder: $stage"
Write-Host "  Zip:    $zip ($sizeMb MB)"
Write-Host "  Run:    `"$stage\CodexBarTray.exe`""
