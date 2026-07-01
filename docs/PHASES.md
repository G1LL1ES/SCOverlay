# SC Overlay Phase Gates

## Phase 1 - Foundation

Acceptance:

- Solution builds from a clean checkout.
- Tests run without third-party packages.
- App shell starts with project boundary/status information.
- Runtime data folders are created under `%AppData%\SCOverlay`.
- Logs are written to `%AppData%\SCOverlay\logs`.

Manual verification:

- Launch `SCOverlay.App`.
- Confirm the window title is `SC Overlay`.
- Confirm the app shows Foundation status, runtime path, and OBS placeholder URL.

## Future Gates

Later phases should not start until the prior phase has been verified and any blocking defects are fixed.

## Phase 2 - Profile And Domain Model

Acceptance:

- Typed models exist for profiles, input sources, widgets, effects, tuning, and runtime settings.
- Default profiles exist for KBM-first and HOTAS-style layouts.
- Profile JSON round-trips polymorphic input sources and widgets.
- Invalid profiles return user-readable validation issues.
- File-backed profile save/list/load works.

Manual verification:

- Run `.\scripts\test.ps1`.
- Confirm the output includes profile validation, JSON round-trip, and file profile store tests.
- Optionally inspect a saved profile by running or extending the file-store test path in a debugger.
