# Input Model Direction

Widgets should stay mapped to semantic overlay actions such as `look-x`, `throttle`, `boost`, and `brake`.
Users should not need to remap widgets when switching between keyboard, mouse, joystick, HOTAS, HOSAS, or hybrid ship setups.

Each semantic action should support multiple physical bindings at the same time. Examples:

- `look-x`: keyboard yaw keys, right-stick X axis
- `strafe-y`: keyboard up/down keys, left-stick Y axis
- `boost`: keyboard key, mouse button, joystick button
- `brake`: keyboard key, joystick axis, joystick button

The renderer should consume only semantic action values. Physical-device conflicts should be resolved inside the input system.

Initial conflict policy:

- Buttons: active if any binding is active.
- Axes: prefer the binding with the most recent meaningful movement.
- Idle axes: fall back to zero when no binding has moved past the action deadzone.
- Keyboard button axes: act like normal axis contributors, so KBM and joystick setups can coexist.

The profile editor should therefore add/remove physical bindings for an action, not remap widgets to different actions.
