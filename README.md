# SC Overlay

SC Overlay is a Windows overlay for showing Star Citizen flight inputs in OBS or on your desktop. It is a clean C#/.NET rewrite of an older Python project, built around first-class keyboard, mouse button, joystick, HOTAS, HOSAS, and mixed-device setups.

The goal is simple: bind the controls you actually fly with, customize how the HUD looks, and expose the same live overlay state to OBS and the desktop overlay without editing Python files or hand-writing JSON.

## Current Status

This is an early 1.0 candidate for real-world testing. It is usable, but it is not polished like a signed commercial app.

Working today:

- Keyboard key capture.
- Mouse button capture.
- HID/joystick axis and button capture.
- Mixed-device profiles, such as keyboard plus right stick, stick plus mouse buttons, or HOTAS/HOSAS combinations.
- Default keyboard/mouse and HOTAS reference profiles.
- Profile creation, import, export, save, and device refresh from the app UI.
- OBS browser source served locally by the app.
- Transparent desktop overlay with show, lock, click-through, move, resize, reset, and tray controls.
- Appearance presets, color pickers, opacity controls, line/effect controls, per-element position, per-element scale, and per-element opacity.
- Roll indicator as either a rotating image or a classic indicator.
- Seeded roll images from the original SC Overlay reference project.
- Boost and brake state text, including optional shake when fully engaged.
- Live diagnostics for devices, raw input, and profile-evaluated values.
- Local diagnostics export and an Open Logs Folder action for support.
- Portable self-contained Windows release package.
- Rotating session logs under `%AppData%\SCOverlay\logs`.

Not done yet:

- The app is not code-signed, so Windows SmartScreen may complain.
- There is no installer yet. The current release is a portable zip.
- Some unusual HID devices may still need better diagnostics, reconnect handling, or calibration tools.
- This is not affiliated with, endorsed by, or supported by Cloud Imperium Games.

## Blunt Disclaimer

This app was built by a dumb human who needed an LLM to help write it. That does not automatically make the app bad, but it does mean you should treat early releases like early releases: test carefully, expect bugs, keep backups of profiles you care about, and do not assume the code has been blessed by a serious software company with a QA department and matching polo shirts.

Use it at your own risk. If it breaks, misreads an input, looks weird in OBS, or eats a profile, that is on the project, not on Star Citizen, CIG, Microsoft, OBS, your joystick manufacturer, or the moon.

## Requirements

- Windows 10 or Windows 11.
- A normal x64 PC.
- OBS Studio if you want the browser source overlay.
- Star Citizen if you want to use it in-game.
- Administrator launch may be required when Star Citizen is focused. See [Running With Star Citizen Focused](#running-with-star-citizen-focused).

The normal portable build is self-contained. You should not need to install the .NET runtime.

## Installation

1. Download the latest portable zip from the GitHub Releases page.
2. Extract the zip to a normal folder, such as:

   ```text
   C:\Users\<you>\Apps\SCOverlay
   ```

3. Run `SCOverlay.exe`.
4. If Windows SmartScreen warns you, choose **More info** and then **Run anyway** if you trust the build.

Do not run the app directly from inside the zip. Extract it first.

## Updating

1. Close SC Overlay.
2. Extract the new zip over the old app folder, or extract it to a fresh folder.
3. Run `SCOverlay.exe`.

Profiles and settings are stored outside the app folder under `%AppData%\SCOverlay`, so replacing the app files should not delete your profiles.

## First Launch

On first launch, SC Overlay creates its runtime folders under:

```text
%AppData%\SCOverlay
```

It also creates default profiles:

- **Keyboard and Mouse Default**
- **HOTAS Reference**

The main window contains these tabs:

- **Setup**: profile selection, profile import/export, OBS URL, desktop overlay controls, and detected devices.
- **Bindings**: bind each action to keyboard keys, mouse buttons, joystick buttons, or joystick axes.
- **Appearance**: global colors, opacity, effects, and presets.
- **Elements**: per-widget position, scale, opacity, roll mode, rotation, throttle shape, and boost/brake shake behavior.
- **Diagnostics**: live device input and evaluated profile values.

![SC Overlay Setup tab](docs/screenshots/setup.png)

## Running With Star Citizen Focused

If SC Overlay works on the desktop but stops updating while Star Citizen is focused, run SC Overlay as administrator.

This happens because Windows blocks lower-privilege apps from observing input while a higher-privilege app is focused. If Star Citizen is running elevated, SC Overlay needs to be elevated too.

Portable build:

1. Right-click `SCOverlay.exe`.
2. Choose **Run as administrator**.

PowerShell development run:

1. Open the Start Menu.
2. Search for `PowerShell`.
3. Right-click **Windows PowerShell**.
4. Choose **Run as administrator**.
5. Run:

   ```powershell
   cd X:\CodexProjects\SCOverlay
   dotnet run --project .\src\SCOverlay.App\SCOverlay.App.csproj --configuration Release
   ```

## OBS Browser Source

SC Overlay serves a local browser source while the app is running.

1. Start SC Overlay.
2. Go to the **Setup** tab.
3. Copy the OBS browser source URL shown in the app.
4. In OBS, add a **Browser** source.
5. Paste the URL.
6. Set the browser source size to match your desired canvas area.

The OBS source updates live as the app samples inputs. The server is local by default and is intended for the same machine running OBS.

If the default OBS port is already in use, SC Overlay attempts to fall back to another available local port and shows the temporary URL in the app.

## Desktop Overlay

The desktop overlay is a transparent always-on-top overlay window.

Controls are available from the **Setup** tab and the system tray icon:

- **Show**: opens or hides the desktop overlay.
- **Locked**: prevents moving/resizing the overlay.
- **Click-through**: lets mouse clicks pass through the overlay to the window underneath. Enabling click-through also locks the overlay.
- **Reset Position**: restores the desktop overlay to its default placement.

To move or resize the overlay:

1. Turn on **Show**.
2. Turn off **Locked**.
3. Turn off **Click-through** if it is enabled.
4. Drag or resize the desktop overlay.
5. Lock it again when placed.

Resizing scales the rendered HUD to the overlay window. Use the per-element scale and position controls when you want to rearrange individual widgets instead of scaling the whole overlay.

## Profiles

Profiles define what inputs drive the overlay and how the widgets look.

From the **Setup** tab you can:

- Select the active profile.
- Create a new profile copy.
- Save the current profile.
- Import a profile JSON file.
- Export the current profile.
- Refresh detected devices.

Profiles are stored here:

```text
%AppData%\SCOverlay\profiles
```

Profile backups are stored here:

```text
%AppData%\SCOverlay\profile-backups
```

## Bindings

Use the **Bindings** tab to bind overlay actions to your real controls.

![SC Overlay Bindings tab](docs/screenshots/bindings.png)

Common actions include:

- Strafe left/right/up/down.
- Pitch up/down.
- Yaw left/right.
- Throttle forward/backward.
- Roll left/right.
- Boost.
- Brake.
- Analog strafe/look/throttle/roll/brake axes.

To bind an action:

1. Select the action in the bindings list.
2. Choose the capture type when applicable.
3. Click **Capture**.
4. Press the key, mouse button, joystick button, or move the joystick axis you want to bind.
5. Save the profile.

To remove a binding:

1. Select the binding.
2. Click **Remove**.
3. Save the profile.

Keyboard inputs and controller inputs can live in the same profile. You do not need separate profiles just because one action comes from a keyboard and another comes from a joystick.

When multiple devices can affect the same final overlay action, SC Overlay evaluates the active bindings together and uses the current input state to drive the renderer. Keyboard-style button pairs become virtual axes, so digital controls can still drive analog-looking widgets.

Controller axis inversion is available for controller inputs. Keyboard inputs do not need axis inversion because their virtual axes are defined by the positive and negative button bindings.

## Appearance

The **Appearance** tab controls the global look of the overlay.

![SC Overlay Appearance tab](docs/screenshots/appearance.png)

Available controls include:

- Appearance presets.
- Input primary color.
- Input active color.
- Frame primary color.
- Frame active color.
- Overall opacity.
- Input primary opacity.
- Input active opacity.
- Frame primary opacity.
- Frame active opacity.
- Global widget scale.
- Outline toggle and width.
- Shadow toggle and blur.
- Text backplate toggle and opacity.

Color pickers are built in, so you do not need to look up hex codes.

Input colors affect the element showing the actual input reading. Frame colors affect the surrounding widget frame, dividers, and related non-input structure. This split lets you make subtle frames with bright active input, or the other way around.

## Elements

The **Elements** tab controls individual widgets.

![SC Overlay Elements tab](docs/screenshots/elements.png)

For each widget you can adjust:

- X position.
- Y position.
- Scale.
- Opacity.
- Line thickness or component thickness where supported.
- Corner radius where supported.

Widget-specific controls include:

- **Roll**: choose image mode or indicator mode.
- **Roll**: choose the roll image asset.
- **Roll**: set max rotation.
- **Throttle**: rounded corners and center-anchored forward/reverse fill.
- **Boost/Brake**: optional shake when maxed.

Click **Apply** to write the changes into the active profile. Click **Reset** to reset the selected widget's appearance settings.

## Diagnostics

The **Diagnostics** tab is where you go when something feels wrong.

![SC Overlay Diagnostics tab](docs/screenshots/diagnostics.png)

It shows:

- Detected input devices.
- Device names and available counts where the app can discover them.
- Stable diagnostic identities for devices where available.
- Raw keyboard, mouse, joystick, and HID input snapshots.
- Evaluated profile values, such as final strafe, look, throttle, roll, boost, and brake values.

Use diagnostics to answer these questions:

- Does Windows expose the device to SC Overlay?
- Does the raw input value change when I press or move the control?
- Does the active profile convert that raw input into the overlay value I expected?

The **Setup** tab also has support buttons:

- **Open Logs Folder** opens `%AppData%\SCOverlay\logs`.
- **Export Diagnostics** writes a local JSON report under `%AppData%\SCOverlay\diagnostics`.

Diagnostics stay on your machine unless you choose to share the exported file.

## Files And Data

SC Overlay stores user data under:

```text
%AppData%\SCOverlay
```

Important folders/files:

- `profiles`: saved profile JSON files.
- `profile-backups`: automatic profile backups.
- `settings.json`: app-level settings.
- `settings-backups`: automatic settings backups.
- `logs`: local log files.
- `diagnostics`: local diagnostics exports.

The app folder can be replaced during updates. The `%AppData%\SCOverlay` folder is where your personal setup lives.

## Logs

Logs are written to:

```text
%AppData%\SCOverlay\logs
```

If something crashes, fails to start, or behaves strangely, include the latest log file when reporting the issue.

## Uninstall

SC Overlay is currently portable.

To remove the app:

1. Close SC Overlay.
2. Delete the extracted app folder.

To remove all user data too:

1. Delete:

   ```text
   %AppData%\SCOverlay
   ```

This deletes profiles, settings, backups, and logs.

## Troubleshooting

### Inputs Work Until Star Citizen Is Focused

Run SC Overlay as administrator. See [Running With Star Citizen Focused](#running-with-star-citizen-focused).

### OBS Shows Nothing Or Stops Updating

- Confirm SC Overlay is still running.
- Copy the OBS URL from the current app window again.
- Open the URL in a normal browser to confirm it renders.
- If the app reported a port fallback, use the fallback URL shown in the app.

### My Joystick Is Listed But Values Look Wrong

- Open **Diagnostics**.
- Move one axis or press one button at a time.
- Check whether raw values change.
- Rebind the action using capture instead of assuming the device's axis order.

Some HID devices report axes/buttons in surprising ways. Better calibration and reconnect tools are planned.

### The Desktop Overlay Is In The Way

- Disable **Click-through**.
- Unlock the overlay.
- Move or resize it.
- Lock it again.
- Re-enable click-through.

### Windows Warns About The App

The app is intentionally unsigned. Windows SmartScreen may warn you because this is a free/open-source portable build without a maintained code-signing certificate.

## Building From Source

Requirements:

- .NET 8 SDK.
- Windows, because the app uses WPF and Windows input APIs.

Build:

```powershell
.\scripts\build.ps1
```

Test:

```powershell
.\scripts\test.ps1
```

Run from source:

```powershell
dotnet run --project .\src\SCOverlay.App\SCOverlay.App.csproj --configuration Release
```

Smoke test:

```powershell
dotnet run --project .\src\SCOverlay.App\SCOverlay.App.csproj --configuration Release -- --smoke-test
```

## Packaging

Create a portable self-contained Windows zip:

```powershell
.\scripts\publish.ps1 -Version 1.0.0
```

Output:

```text
artifacts\release\SCOverlay-<version>-win-x64-self-contained.zip
artifacts\release\SCOverlay-<version>-win-x64-self-contained.zip.sha256
```

The executable inside the zip is:

```text
SCOverlay.exe
```

The release zip is intentionally end-user focused. It contains the published runtime files needed to run the app, the required overlay assets, and `README-PORTABLE.txt`; source files, tests, scripts, and development docs stay in the repository.

## Project Notes

This is a new C#/.NET 8 WPF implementation. The older Python repo was used as a feature and behavior reference, not as code to keep carrying forward.

Primary components:

- `SCOverlay.App`: WPF desktop app and desktop overlay.
- `SCOverlay.Core`: profile model, input evaluation, overlay state engine, settings, and logging.
- `SCOverlay.Input`: Windows input providers.
- `SCOverlay.BrowserSource`: local OBS browser source server and renderer.
- `SCOverlay.Tests`: no-dependency console test runner.

## License

No license has been selected yet.

Until a license is added, assume the code is private/all-rights-reserved and do not redistribute it unless the repository owner explicitly says otherwise.

## Credits

SC Overlay was created as a replacement for the original Python-based SC Overlay experiment by G1LL1ES.

Star Citizen is a trademark of Cloud Imperium Games. This project is unofficial and is not affiliated with or endorsed by Cloud Imperium Games.
