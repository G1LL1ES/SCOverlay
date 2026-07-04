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
- Customizable HUD appearance with presets, color pickers, separate primary/active opacity, per-element layout/scale/opacity, and effects
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

## Running With Star Citizen

If inputs stop updating when Star Citizen is focused, run SC Overlay as administrator. Star Citizen may run elevated, and Windows blocks lower-privilege apps from observing its focused input.

For development runs:

1. Open Start Menu.
2. Search for `PowerShell`.
3. Right-click `Windows PowerShell` and choose `Run as administrator`.
4. Run:

```powershell
cd <path-to-SCOverlay>
dotnet run --project .\src\SCOverlay.App\SCOverlay.App.csproj --configuration Release
```

For the portable build, right-click `SCOverlay.exe` and choose `Run as administrator`, or launch it from an elevated PowerShell window.

## Package

```powershell
.\scripts\publish.ps1
```

The portable zip is written to `artifacts\release`, and the executable inside the zip is `SCOverlay.exe`.
See `docs\release-checklist.md` for smoke testing notes.
