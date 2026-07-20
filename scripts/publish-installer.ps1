[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version,
    [string]$IsccPath,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$versionProps = [xml](Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props") -Raw)
$canonicalVersion = [string]$versionProps.Project.PropertyGroup.VersionPrefix
if ([string]::IsNullOrWhiteSpace($canonicalVersion)) {
    throw "Directory.Build.props does not define VersionPrefix."
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $canonicalVersion
}
elseif (-not [string]::Equals($Version, $canonicalVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Requested version '$Version' does not match canonical version '$canonicalVersion'."
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $isccCommand) {
        $IsccPath = $isccCommand.Source
    }
    else {
        $candidatePaths = @(
            (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
            (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        $IsccPath = $candidatePaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}

if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw "Inno Setup compiler was not found. Pass -IsccPath or run this script in the GitHub packaging workflow."
}

if ($SkipTests.IsPresent) {
    & (Join-Path $PSScriptRoot "publish.ps1") `
        -Configuration $Configuration `
        -Runtime $Runtime `
        -Version $Version `
        -SkipTests
}
else {
    & (Join-Path $PSScriptRoot "publish.ps1") `
        -Configuration $Configuration `
        -Runtime $Runtime `
        -Version $Version
}
if ($LASTEXITCODE -ne 0) {
    throw "Portable publishing failed with exit code $LASTEXITCODE."
}

$artifactName = "SCOverlay-$Version-$Runtime-self-contained"
$publishPath = Join-Path $repoRoot "artifacts\publish\$artifactName"
$releaseRoot = Join-Path $repoRoot "artifacts\release"
$appPath = Join-Path $publishPath "SCOverlay.exe"
$appVersion = (Get-Item -LiteralPath $appPath).VersionInfo.ProductVersion
$parsedAppVersion = [Version](($appVersion -split '[+-]')[0])
$parsedRequestedVersion = [Version]$Version
if ($parsedAppVersion -ne $parsedRequestedVersion) {
    throw "Published app version '$appVersion' does not match installer version '$Version'."
}

$installerPath = Join-Path $releaseRoot "SCOverlay-$Version-win-x64-setup.exe"
$installerChecksumPath = "$installerPath.sha256"
if (Test-Path -LiteralPath $installerPath) {
    Remove-Item -LiteralPath $installerPath -Force
}
if (Test-Path -LiteralPath $installerChecksumPath) {
    Remove-Item -LiteralPath $installerChecksumPath -Force
}

$installerScript = Join-Path $repoRoot "installer\SCOverlay.iss"
& $IsccPath "/DAppVersion=$Version" "/DSourceDir=$publishPath" "/DOutputDir=$releaseRoot" $installerScript
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installerPath)) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

$hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
Set-Content -LiteralPath $installerChecksumPath -Value "$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $installerPath)" -Encoding ASCII

Write-Host "Published installer:"
Write-Host "  Setup:  $installerPath"
Write-Host "  SHA256: $installerChecksumPath"
