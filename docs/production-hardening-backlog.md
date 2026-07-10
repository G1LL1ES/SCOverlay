# Production Hardening Backlog

This backlog captures production-readiness work that should not block higher-impact feature work unless it becomes a normal-use blocker. Each item should be implemented behind its own verification gate and checkpoint commit.

## Current Defaults

- The app remains Windows-first with .NET 8 and WPF.
- Local user data lives under `%AppData%\SCOverlay`.
- OBS browser source hosting should stay loopback-only by default.
- `scripts/build.ps1` and `scripts/test.ps1` remain the canonical local gates.
- Desktop overlay resize scaling is implemented for 1.0, but monitor/DPI edge cases should continue to be validated.

## Priority Order

1. Validate 1.0 release artifacts from the curated portable zip.
2. Improve HID/device robustness with reconnect handling and calibration/deadzone diagnostics.
3. Polish desktop overlay resilience across DPI and multi-monitor changes.
4. Add optional installer support after the portable release path is proven.

## Release And Packaging

- Keep the portable zip publish script producing a self-contained or framework-dependent release from a clean checkout.
- Keep version metadata, app icon metadata, checksum output, and release artifact naming current.
- Keep the clean-machine smoke checklist covering first launch, profile creation, OBS URL, desktop overlay, tray exit, and log creation.
- Keep installer work optional until the portable release path is stable.

## CI And Quality Gates

- Keep GitHub Actions on Windows for restore/build/test.
- Keep build/test commands aligned with `scripts/build.ps1` and `scripts/test.ps1`.
- Add publish verification only after the portable package script exists.
- Keep dependency additions documented in `docs/DEPENDENCIES.md`.

## Config Resilience

- Keep profile-style backup and recovery behavior for `settings.json`.
- Keep settings writes atomic using a temporary file and replace/move semantics.
- Keep backup pruning bounded.
- Keep recovery events visible in the app footer and logs so users know when defaults were restored.

## Runtime Reliability

- Preserve the verified forced-exit fallback for shutdown.
- Document single-instance behavior, including focus existing, start another, and quit old/start new.
- Keep OBS port conflicts nonfatal by falling back to a temporary available port.
- Show clear, user-facing status when input initialization or browser-source startup fails.

## Logging And Crash Diagnostics

- Rotate local logs so a long-running install cannot grow one unbounded file forever.
- Keep per-session headers with app version, OS/runtime version, process id, data root, and active profile.
- Keep dispatcher, unhandled, and unobserved task exception logging.
- Keep "open logs folder" affordances in the app UI and tray menu.

## Input Robustness

- Keep local diagnostics export for app/settings/profile/device/input/log-tail context.
- Add deeper HID diagnostics for value caps, button caps, hats, and recent raw reports.
- Handle device reconnects without requiring app restart where possible.
- Define a stable device identity strategy for duplicate devices and devices whose Windows paths change.
- Add manual calibration/deadzone diagnostics for devices with unusual ranges or noisy centers.

## Overlay Robustness

- Keep desktop overlay resizing scaling the HUD instead of cropping it.
- Continue preserving overlay placement across monitor count, resolution, and DPI changes.
- Validate click-through, lock/unlock, drag, resize, reset, and tray controls over desktop and game-like fullscreen scenarios.
- Keep OBS and desktop overlay rendering on the same `OverlayState` contract.

## Security And Local Server Boundaries

- Keep `127.0.0.1` as the default browser source host.
- Validate browser source host and port settings before server startup.
- Warn clearly if a profile changes the host away from localhost.
- Do not serve arbitrary files from the local filesystem.

## Docs And Supportability

- Update `README.md` and `docs/PHASES.md` to match the real phase progress.
- Add troubleshooting for stuck processes, port conflicts, HID capture, and corrupt profiles/settings.
- Document profile import/export and mixed-device binding behavior.
- Keep manual verification steps for every phase and release checklist in source control.

## Verification Standard

Before any hardening checkpoint commit, run:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
dotnet run --project .\src\SCOverlay.App\SCOverlay.App.csproj --configuration Release -- --smoke-test
```

Add focused unit or integration tests for any hardening item that changes runtime behavior.
