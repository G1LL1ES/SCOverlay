$ErrorActionPreference = "Stop"

$appDll = Join-Path $PSScriptRoot "..\src\SCOverlay.App\bin\Release\net8.0-windows\SCOverlay.dll"
if (-not (Test-Path -LiteralPath $appDll)) {
    throw "Release app output was not found. Run scripts\build.ps1 before scripts\smoke-test.ps1."
}

dotnet $appDll --smoke-test
if ($LASTEXITCODE -ne 0) {
    throw "SC Overlay smoke test failed with exit code $LASTEXITCODE."
}
