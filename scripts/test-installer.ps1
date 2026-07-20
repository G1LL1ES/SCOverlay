[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath
)

$ErrorActionPreference = "Stop"

$installer = Resolve-Path -LiteralPath $InstallerPath
$testRoot = Join-Path $env:TEMP "SCOverlayInstallerTest-$([Guid]::NewGuid().ToString('N'))"
$installPath = Join-Path $testRoot "app"
$programsPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$desktopPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$startMenuShortcut = Join-Path $programsPath "SC Overlay.lnk"
$desktopShortcut = Join-Path $desktopPath "SC Overlay.lnk"
$dataRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)) "SCOverlay"
$sentinelPath = Join-Path $dataRoot "installer-preservation-test.txt"
$uninstallRegistryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{2E7E52E9-A0E7-4C4D-B082-D0A479CFA87E}_is1"

function Invoke-CheckedProcess {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList
    )

    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "'$FilePath' failed with exit code $($process.ExitCode)."
    }
}

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
    Set-Content -LiteralPath $sentinelPath -Value "preserve" -Encoding ASCII

    Invoke-CheckedProcess -FilePath $installer -ArgumentList @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/DIR=$installPath",
        "/TASKS=startmenu"
    )

    $appPath = Join-Path $installPath "SCOverlay.exe"
    $uninstallerPath = Join-Path $installPath "unins000.exe"
    foreach ($requiredPath in @($appPath, $uninstallerPath, (Join-Path $installPath "LICENSE.txt"), $startMenuShortcut)) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "Installer did not create required item '$requiredPath'."
        }
    }

    if (Test-Path -LiteralPath $desktopShortcut) {
        throw "Desktop shortcut was created even though the task was not selected."
    }
    if (Test-Path -LiteralPath (Join-Path $installPath "README-PORTABLE.txt")) {
        throw "Portable-only notes were installed by the setup package."
    }
    if (-not (Test-Path -LiteralPath $uninstallRegistryPath)) {
        throw "Installer did not create the stable per-user uninstall identity."
    }

    $firstInstallLocation = (Get-ItemProperty -LiteralPath $uninstallRegistryPath).InstallLocation.TrimEnd('\')
    if (-not [string]::Equals($firstInstallLocation, $installPath.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Uninstall registration points to '$firstInstallLocation' instead of '$installPath'."
    }

    Invoke-CheckedProcess -FilePath $installer -ArgumentList @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART",
        "/DIR=$installPath",
        "/TASKS=startmenu"
    )

    $secondInstallLocation = (Get-ItemProperty -LiteralPath $uninstallRegistryPath).InstallLocation.TrimEnd('\')
    if (-not [string]::Equals($secondInstallLocation, $firstInstallLocation, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Reinstall changed the registered installation directory."
    }

    Invoke-CheckedProcess -FilePath "dotnet" -ArgumentList @(
        (Join-Path $installPath "SCOverlay.dll"),
        "--smoke-test"
    )
    Invoke-CheckedProcess -FilePath $uninstallerPath -ArgumentList @(
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/NORESTART"
    )

    if (Test-Path -LiteralPath $appPath) {
        throw "Uninstall left the application executable behind."
    }
    if (-not (Test-Path -LiteralPath $sentinelPath)) {
        throw "Uninstall removed SC Overlay user data."
    }
    if (Test-Path -LiteralPath $startMenuShortcut) {
        throw "Uninstall left the Start Menu shortcut behind."
    }
    if (Test-Path -LiteralPath $uninstallRegistryPath) {
        throw "Uninstall left the application registration behind."
    }

    Write-Host "Installer smoke test passed."
}
finally {
    if (Test-Path -LiteralPath $sentinelPath) {
        Remove-Item -LiteralPath $sentinelPath -Force
    }

    $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
    $resolvedTempRoot = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    if ((Test-Path -LiteralPath $resolvedTestRoot) -and
        $resolvedTestRoot.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
