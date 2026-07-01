$ErrorActionPreference = "Stop"

dotnet run --project "$PSScriptRoot\..\tests\SCOverlay.Tests\SCOverlay.Tests.csproj" --configuration Release
