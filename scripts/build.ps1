$ErrorActionPreference = "Stop"

dotnet build "$PSScriptRoot\..\SCOverlay.sln" --configuration Release
