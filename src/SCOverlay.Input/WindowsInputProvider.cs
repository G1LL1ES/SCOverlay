using SCOverlay.Core.Domain;
using SCOverlay.Core.Input;

namespace SCOverlay.Input;

public sealed class WindowsInputProvider : IInputProvider, IDisposable
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

    public async ValueTask<InputCaptureResult> CaptureNextBindingAsync(CancellationToken cancellationToken = default)
    {
        return await CaptureNextBindingCoreAsync(expectedKind: null, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<InputCaptureResult> CaptureNextBindingAsync(InputSourceKind expectedKind, CancellationToken cancellationToken = default)
    {
        return await CaptureNextBindingCoreAsync(expectedKind, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<InputCaptureResult> CaptureNextBindingAsync(InputSourceKind? expectedKind, CancellationToken cancellationToken = default)
    {
        return await CaptureNextBindingCoreAsync(expectedKind, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<InputCaptureResult> CaptureNextBindingCoreAsync(InputSourceKind? expectedKind, CancellationToken cancellationToken)
    {
        using var captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<InputCaptureResult> snapshotCapture = CaptureSnapshotChangeAsync(expectedKind, captureCancellation.Token);
        try
        {
            if (expectedKind is null or InputSourceKind.Button)
            {
                Task<InputCaptureResult> keyboardMouseCapture = keyboardMouse.CaptureNextBindingAsync(captureCancellation.Token).AsTask();
                return await AwaitFirstCompletedCaptureAsync(
                    new[] { keyboardMouseCapture, snapshotCapture },
                    captureCancellation.Token).ConfigureAwait(false);
            }

            return await snapshotCapture.ConfigureAwait(false);
        }
        finally
        {
            captureCancellation.Cancel();
        }
    }

    private static async Task<InputCaptureResult> AwaitFirstCompletedCaptureAsync(
        IReadOnlyList<Task<InputCaptureResult>> captureTasks,
        CancellationToken cancellationToken)
    {
        var pending = captureTasks.ToList();
        while (pending.Count > 0)
        {
            Task<InputCaptureResult> completed = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(completed);

            try
            {
                return await completed.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && pending.Count > 0)
            {
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new OperationCanceledException(cancellationToken);
    }

    private async Task<InputCaptureResult> CaptureSnapshotChangeAsync(InputSourceKind? expectedKind, CancellationToken cancellationToken)
    {
        InputSnapshot baseline = Poll();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            InputSnapshot current = Poll();

            if (expectedKind is null or InputSourceKind.Button)
            {
                InputCaptureResult? button = TryCaptureButton(baseline, current);
                if (button is not null)
                {
                    return button;
                }
            }

            if (expectedKind is null or InputSourceKind.Axis)
            {
                InputCaptureResult? axis = TryCaptureAxis(baseline, current);
                if (axis is not null)
                {
                    return axis;
                }
            }
        }
    }

    public void Dispose()
    {
        hid.Dispose();
    }

    private static InputCaptureResult? TryCaptureButton(InputSnapshot baseline, InputSnapshot current)
    {
        foreach (KeyValuePair<string, bool> pair in current.Buttons.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            bool wasPressed = baseline.Buttons.TryGetValue(pair.Key, out bool baselineValue) && baselineValue;
            if (!wasPressed && pair.Value && TryCreateButtonSource(pair.Key, out InputSource? source, out string displayText))
            {
                return new InputCaptureResult(source, displayText, current.Timestamp);
            }
        }

        return null;
    }

    private static InputCaptureResult? TryCaptureAxis(InputSnapshot baseline, InputSnapshot current)
    {
        foreach (KeyValuePair<string, double> pair in current.Axes.OrderByDescending(pair => Math.Abs(pair.Value - GetBaselineAxisValue(baseline, pair.Key))))
        {
            double baselineValue = GetBaselineAxisValue(baseline, pair.Key);
            double delta = Math.Abs(pair.Value - baselineValue);
            if (delta >= 0.35 && Math.Abs(pair.Value) >= 0.25 && TryCreateAxisSource(pair.Key, out JoystickAxisInputSource? source, out string displayText))
            {
                return new InputCaptureResult(source, displayText, current.Timestamp);
            }
        }

        return null;
    }

    private static double GetBaselineAxisValue(InputSnapshot baseline, string key)
    {
        return baseline.Axes.TryGetValue(key, out double value) ? value : 0.0;
    }

    private static bool TryCreateButtonSource(string snapshotKey, out InputSource source, out string displayText)
    {
        source = new KeyboardKeyInputSource();
        displayText = snapshotKey;

        if (snapshotKey.StartsWith("keyboard:", StringComparison.Ordinal))
        {
            string key = snapshotKey["keyboard:".Length..];
            source = new KeyboardKeyInputSource
            {
                Id = CreateCapturedId("keyboard", key),
                DisplayName = key,
                Key = key
            };
            displayText = key;
            return true;
        }

        if (snapshotKey.StartsWith("mouse:", StringComparison.Ordinal))
        {
            string button = snapshotKey["mouse:".Length..];
            source = new MouseButtonInputSource
            {
                Id = CreateCapturedId("mouse", button),
                DisplayName = $"Mouse {button}",
                Button = button
            };
            displayText = $"Mouse {button}";
            return true;
        }

        int marker = snapshotKey.LastIndexOf(":button:", StringComparison.Ordinal);
        if (marker > 0 && int.TryParse(snapshotKey[(marker + ":button:".Length)..], out int buttonIndex))
        {
            string deviceId = snapshotKey[..marker];
            source = new JoystickButtonInputSource
            {
                Id = CreateCapturedId("joystick-button", $"{deviceId}-{buttonIndex}"),
                DisplayName = $"Button {buttonIndex}",
                DeviceId = deviceId,
                ButtonIndex = buttonIndex
            };
            displayText = $"{deviceId} button {buttonIndex}";
            return true;
        }

        return false;
    }

    private static bool TryCreateAxisSource(string snapshotKey, out JoystickAxisInputSource source, out string displayText)
    {
        source = new JoystickAxisInputSource();
        displayText = snapshotKey;

        int marker = snapshotKey.LastIndexOf(":axis:", StringComparison.Ordinal);
        if (marker <= 0 || !int.TryParse(snapshotKey[(marker + ":axis:".Length)..], out int axisIndex))
        {
            return false;
        }

        string deviceId = snapshotKey[..marker];
        source = new JoystickAxisInputSource
        {
            Id = CreateCapturedId("joystick-axis", $"{deviceId}-{axisIndex}"),
            DisplayName = $"Axis {axisIndex}",
            DeviceId = deviceId,
            AxisIndex = axisIndex
        };
        displayText = $"{deviceId} axis {axisIndex}";
        return true;
    }

    private static string CreateCapturedId(string kind, string name)
    {
        string normalized = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? kind : $"{kind}-{normalized}";
    }
}
