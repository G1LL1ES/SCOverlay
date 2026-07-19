# Windows Release Checklist

Use this checklist for installer candidates and public releases. GitHub Actions is the canonical installer build environment, so Inno Setup does not need to be installed on a development machine.

## Build A Release

Run the **Windows Package** workflow with `workflow_dispatch`. The workflow runs build, tests, application smoke testing, portable publishing, installer compilation, a silent install/uninstall smoke test, and package-content validation.

For local portable-only validation:

```powershell
.\scripts\publish.ps1
```

Default output:

- Installer: `artifacts\release\SCOverlay-1.1.0-win-x64-setup.exe`
- Installer checksum: `artifacts\release\SCOverlay-1.1.0-win-x64-setup.exe.sha256`
- Portable zip: `artifacts\release\SCOverlay-1.1.0-win-x64-self-contained.zip`
- Portable checksum: `artifacts\release\SCOverlay-1.1.0-win-x64-self-contained.zip.sha256`

Both packages are self-contained for `win-x64`, so testers should not need the .NET runtime. Neither package may contain source, tests, scripts, symbols, or development documentation. The installer includes the MIT license; the portable zip includes the license and concise portable notes.

## Local Installer Compilation

`scripts\publish-installer.ps1` can use a local `ISCC.exe`, but this is optional and is not the release path. Pass `-IsccPath` when Inno Setup is not discoverable automatically.

The version always comes from `Directory.Build.props`. Packaging rejects an explicit version that disagrees with it.

## Tester Smoke Checklist

- Run the installer and confirm the Start Menu option is selected by default.
- Confirm the desktop shortcut option is not selected by default.
- Confirm installation uses `%LocalAppData%\Programs\SCOverlay`.
- Launch from Start and approve the UAC prompt.
- Confirm the main window opens.
- Confirm Setup lists keyboard, mouse, and HID devices.
- Confirm Bindings can capture a keyboard key, mouse button, and available joystick/HID input.
- Confirm OBS URL opens in a browser and updates live.
- Confirm desktop overlay can be shown, moved while unlocked, locked, and set click-through.
- Confirm Appearance preset/effect changes update OBS and desktop overlay after Apply.
- Close the app with `X` and confirm the process exits.
- Run the installer again and confirm it upgrades the same directory.
- Run the installer while SC Overlay is open and confirm it asks for a clean app exit.
- Uninstall from Windows Installed apps.
- Confirm application files and shortcuts are removed.
- Reinstall and confirm prior profiles/settings return.
- Confirm a manual update check is non-blocking and opens the GitHub release page when an update exists.

## Support Notes

- Runtime data is stored under `%AppData%\SCOverlay`.
- Logs are stored under `%AppData%\SCOverlay\logs`.
- Profiles are stored under `%AppData%\SCOverlay\profiles`.
- Profile backups are stored under `%AppData%\SCOverlay\profile-backups`.
- The installer and app are intentionally unsigned, so SmartScreen and UAC identify an unknown publisher.
- The app always requests administrator access for focused Star Citizen input.
- Verify both `.sha256` files before publishing or sharing a release.
- Push a matching `v<VersionPrefix>` tag only after the workflow artifact passes manual acceptance; the tag workflow creates the GitHub release and uploads both distribution formats.
