# Portable Release Checklist

Use this checklist for early friend/tester builds. The release is a portable Windows zip, not an installer.

## Build A Release

```powershell
.\scripts\publish.ps1
```

Default output:

- Published folder: `artifacts\publish\SCOverlay-1.0.0-win-x64-self-contained`
- Zip file: `artifacts\release\SCOverlay-1.0.0-win-x64-self-contained.zip`
- Checksum file: `artifacts\release\SCOverlay-1.0.0-win-x64-self-contained.zip.sha256`
- Executable inside the zip: `SCOverlay.exe`

The default build is self-contained for `win-x64`, so testers should not need to install the .NET runtime. The release zip is intentionally end-user focused: it contains the published runtime files, required overlay assets, and `README-PORTABLE.txt`, not source, tests, scripts, or development docs.

## Optional Arguments

```powershell
.\scripts\publish.ps1 -Version 1.0.0-test1
.\scripts\publish.ps1 -Runtime win-x64 -Version 1.0.0-test1
.\scripts\publish.ps1 -FrameworkDependent
.\scripts\publish.ps1 -SkipTests
```

Use `-SkipTests` only when build and test were already run for the exact changes being packaged.

## Tester Smoke Checklist

- Extract the zip to a normal user-writable folder.
- Run `SCOverlay.exe`.
- Confirm the main window opens.
- Confirm Setup lists keyboard, mouse, and HID devices.
- Confirm Bindings can capture a keyboard key, mouse button, and available joystick/HID input.
- Confirm OBS URL opens in a browser and updates live.
- Confirm desktop overlay can be shown, moved while unlocked, locked, and set click-through.
- Confirm Appearance preset/effect changes update OBS and desktop overlay after Apply.
- Close the app with `X` and confirm the process exits.

## Support Notes

- Runtime data is stored under `%AppData%\SCOverlay`.
- Logs are stored under `%AppData%\SCOverlay\logs`.
- Profiles are stored under `%AppData%\SCOverlay\profiles`.
- Profile backups are stored under `%AppData%\SCOverlay\profile-backups`.
- The package is intentionally unsigned, so Windows SmartScreen may warn testers.
- Verify the `.sha256` file before publishing or sharing a release.
