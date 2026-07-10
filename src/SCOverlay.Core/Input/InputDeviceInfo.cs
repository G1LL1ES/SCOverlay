namespace SCOverlay.Core.Input;

public sealed record InputDeviceInfo(
    string DeviceId,
    string DisplayName,
    InputDeviceKind Kind,
    int AxisCount,
    int ButtonCount,
    int HatCount = 0,
    string? Details = null,
    string? StableIdentity = null);

public enum InputDeviceKind
{
    Keyboard,
    Mouse,
    Joystick
}
