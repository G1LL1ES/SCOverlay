# SC Overlay

Clean C#/.NET 8 rewrite of the Star Citizen input overlay, using the old Python project only as a feature reference.

## Current Phase

Phase 2: profile and domain model.

- WPF app shell
- Core contracts
- Input and browser-source project boundaries
- Typed profile/domain model
- KBM and HOTAS-style default profiles
- JSON profile serialization and validation
- No third-party runtime dependencies
- Local file logging under `%AppData%\SCOverlay\logs`
- No-dependency console test runner

## Build

```powershell
.\scripts\build.ps1
```

## Test

```powershell
.\scripts\test.ps1
```

## Run

```powershell
dotnet run --project .\src\SCOverlay.App\SCOverlay.App.csproj
```
