namespace SCOverlay.Core.Input;

public sealed record InputDeviceInfo(
    string DeviceId,
    string DisplayName,
    InputDeviceKind Kind,
    int AxisCount,
    int ButtonCount);

public enum InputDeviceKind
{
    Keyboard,
    Mouse,
    Joystick
}
