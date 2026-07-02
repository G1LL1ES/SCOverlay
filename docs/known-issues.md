# Known Issues

- App shutdown can leave `SCOverlay.App.exe` running after the main window closes on some machines. The current workaround is to manually end the process; this should be fixed before Phase 8 hardening.
- Desktop overlay resizing changes the window bounds but does not scale/reflow the HUD layout yet, so shrinking the window can crop widgets. Later desktop-overlay polish should make resize scale the rendered overlay or expose a separate layout scale control.
