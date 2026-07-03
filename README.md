# SC Overlay

Clean C#/.NET 8 rewrite of the Star Citizen input overlay, using the old Python project only as a feature reference.

## Current Phase

Early production test builds.

- WPF app shell
- Core contracts
- Input and browser-source project boundaries
- Typed profile/domain model
- KBM and HOTAS-style default profiles
- JSON profile serialization and validation
- Raw Input keyboard/mouse/HID input diagnostics
- Mixed-device binding capture and profile editing
- OBS browser source
- Desktop overlay with tray controls
- Portable self-contained Windows packaging
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

## Package

```powershell
.\scripts\publish.ps1
```

The portable zip is written to `artifacts\release`, and the executable inside the zip is `SCOverlay.exe`.
See `docs\release-checklist.md` for smoke testing notes.
