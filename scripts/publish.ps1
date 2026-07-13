[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.1",
    [switch]$FrameworkDependent,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "src\SCOverlay.App\SCOverlay.App.csproj"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$publishRoot = Join-Path $artifactsRoot "publish"
$releaseRoot = Join-Path $artifactsRoot "release"
$packageKind = if ($FrameworkDependent.IsPresent) { "framework-dependent" } else { "self-contained" }
$artifactName = "SCOverlay-$Version-$Runtime-$packageKind"
$publishPath = Join-Path $publishRoot $artifactName
$zipPath = Join-Path $releaseRoot "$artifactName.zip"
$checksumPath = "$zipPath.sha256"

function Assert-UnderRepo {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $rootPath = [System.IO.Path]::GetFullPath($repoRoot)
    if (-not $fullPath.StartsWith($rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside the repository: $fullPath"
    }
}

Assert-UnderRepo $publishPath
Assert-UnderRepo $zipPath

if (-not $SkipTests.IsPresent) {
    & (Join-Path $PSScriptRoot "build.ps1")
    & (Join-Path $PSScriptRoot "test.ps1")
}

New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

if (Test-Path $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

if (Test-Path $checksumPath) {
    Remove-Item -LiteralPath $checksumPath -Force
}

$selfContained = if ($FrameworkDependent.IsPresent) { "false" } else { "true" }

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained $selfContained `
    --output $publishPath `
    -p:Version=$Version `
    -p:InformationalVersion=$Version `
    -p:DebugType=None `
    -p:DebugSymbols=false

$runGuide = @"
SC Overlay Portable Build
=========================

This folder contains only the files needed to run SC Overlay. Source code,
tests, build scripts, and development documentation stay in the GitHub repo.

Run:
  SCOverlay.exe

Unsigned app notice:
  This free/open-source build is not code-signed. Windows SmartScreen may warn
  you until the app has enough local reputation. Choose "More info" and "Run
  anyway" only if you trust where this zip came from.

OBS browser source:
  Launch the app, then copy the OBS browser source URL shown in the top-right of the main window.

User data:
  Profiles, settings, logs, and backups are stored under %AppData%\SCOverlay.

First-use smoke check:
  1. Start SCOverlay.exe.
  2. Confirm the main window opens.
  3. Open Setup and confirm keyboard, mouse, and HID devices are listed.
  4. Open Bindings and capture at least one keyboard or mouse binding.
  5. Open Appearance, change a preset/effect, click Apply, and confirm OBS/desktop overlay update.
  6. Toggle the desktop overlay on, move it while unlocked, then lock/click-through it.
  7. Close the app and confirm the process exits.
"@

Set-Content -LiteralPath (Join-Path $publishPath "README-PORTABLE.txt") -Value $runGuide -Encoding UTF8

$publishItems = Get-ChildItem -LiteralPath $publishPath
Compress-Archive -LiteralPath $publishItems.FullName -DestinationPath $zipPath -Force

$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
Set-Content -LiteralPath $checksumPath -Value "$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $zipPath)" -Encoding ASCII

Write-Host "Published portable build:"
Write-Host "  Folder: $publishPath"
Write-Host "  Zip:    $zipPath"
Write-Host "  SHA256: $checksumPath"
