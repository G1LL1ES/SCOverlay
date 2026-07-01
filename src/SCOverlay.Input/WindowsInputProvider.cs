using SCOverlay.Core.Input;

namespace SCOverlay.Input;

public sealed class WindowsInputProvider : IInputProvider
{
    public const int RawInputWindowMessage = NativeMethods.WM_INPUT;

    private readonly RawInputKeyboardMouseProvider keyboardMouse = new();
    private readonly RawInputHidProvider hid = new();
    private readonly WinMmJoystickProvider joystick = new();

    public string Name => "Windows Raw Input HID + WinMM Provider";

    public void AttachWindow(IntPtr windowHandle)
    {
        keyboardMouse.AttachWindow(windowHandle);
        hid.AttachWindow(windowHandle);
    }

    public void ProcessWindowMessage(IntPtr rawInputHandle)
    {
        keyboardMouse.ProcessWindowMessage(rawInputHandle);
        hid.ProcessWindowMessage(rawInputHandle);
    }

    public ValueTask<IReadOnlyList<InputDeviceInfo>> EnumerateDevicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<InputDeviceInfo> devices = keyboardMouse
            .EnumerateDevices()
            .Concat(hid.EnumerateDevices())
            .Concat(joystick.EnumerateDevices())
            .ToArray();

        return ValueTask.FromResult(devices);
    }

    public InputSnapshot Poll()
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        InputSnapshot keyboardMouseSnapshot = keyboardMouse.Poll(timestamp);
        InputSnapshot hidSnapshot = hid.Poll(timestamp);
        InputSnapshot joystickSnapshot = joystick.Poll(timestamp);

        Dictionary<string, double> axes = keyboardMouseSnapshot.Axes
            .Concat(hidSnapshot.Axes)
            .Concat(joystickSnapshot.Axes)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

        Dictionary<string, bool> buttons = keyboardMouseSnapshot.Buttons
            .Concat(hidSnapshot.Buttons)
            .Concat(joystickSnapshot.Buttons)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

        return new InputSnapshot(timestamp, axes, buttons);
    }

    public ValueTask<InputCaptureResult> CaptureNextBindingAsync(CancellationToken cancellationToken = default)
    {
        return keyboardMouse.CaptureNextBindingAsync(cancellationToken);
    }
}
