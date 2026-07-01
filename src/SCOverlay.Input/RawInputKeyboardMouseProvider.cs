using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SCOverlay.Core.Domain;
using SCOverlay.Core.Input;

namespace SCOverlay.Input;

public sealed class RawInputKeyboardMouseProvider
{
    private readonly ConcurrentDictionary<string, bool> buttons = new(StringComparer.Ordinal);
    private readonly object captureLock = new();
    private TaskCompletionSource<InputCaptureResult>? pendingCapture;
    private CancellationTokenRegistration pendingCaptureRegistration;

    public IReadOnlyList<InputDeviceInfo> EnumerateDevices()
    {
        return new[]
        {
            new InputDeviceInfo("keyboard", "Keyboard", InputDeviceKind.Keyboard, 0, 256),
            new InputDeviceInfo("mouse", "Mouse", InputDeviceKind.Mouse, 0, 5)
        };
    }

    public void AttachWindow(IntPtr windowHandle)
    {
        var devices = new[]
        {
            new NativeMethods.RawInputDevice
            {
                UsagePage = NativeMethods.HID_USAGE_PAGE_GENERIC,
                Usage = NativeMethods.HID_USAGE_GENERIC_MOUSE,
                Flags = NativeMethods.RIDEV_INPUTSINK,
                TargetWindow = windowHandle
            },
            new NativeMethods.RawInputDevice
            {
                UsagePage = NativeMethods.HID_USAGE_PAGE_GENERIC,
                Usage = NativeMethods.HID_USAGE_GENERIC_KEYBOARD,
                Flags = NativeMethods.RIDEV_INPUTSINK,
                TargetWindow = windowHandle
            }
        };

        bool registered = NativeMethods.RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf<NativeMethods.RawInputDevice>());

        if (!registered)
        {
            throw new InvalidOperationException($"RegisterRawInputDevices failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    public void ProcessWindowMessage(IntPtr rawInputHandle)
    {
        uint size = 0;
        NativeMethods.GetRawInputData(
            rawInputHandle,
            NativeMethods.RID_INPUT,
            IntPtr.Zero,
            ref size,
            (uint)Marshal.SizeOf<NativeMethods.RawInputHeader>());

        if (size == 0)
        {
            return;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            uint bytesRead = NativeMethods.GetRawInputData(
                rawInputHandle,
                NativeMethods.RID_INPUT,
                buffer,
                ref size,
                (uint)Marshal.SizeOf<NativeMethods.RawInputHeader>());

            if (bytesRead != size)
            {
                return;
            }

            NativeMethods.RawInputHeader header = Marshal.PtrToStructure<NativeMethods.RawInputHeader>(buffer);
            IntPtr payload = IntPtr.Add(buffer, Marshal.SizeOf<NativeMethods.RawInputHeader>());

            if (header.Type == NativeMethods.RIM_TYPEKEYBOARD)
            {
                ProcessKeyboard(Marshal.PtrToStructure<NativeMethods.RawKeyboard>(payload));
            }
            else if (header.Type == NativeMethods.RIM_TYPEMOUSE)
            {
                ProcessMouse(Marshal.PtrToStructure<NativeMethods.RawMouse>(payload));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public InputSnapshot Poll(DateTimeOffset timestamp)
    {
        return new InputSnapshot(
            timestamp,
            new Dictionary<string, double>(StringComparer.Ordinal),
            buttons.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    public ValueTask<InputCaptureResult> CaptureNextBindingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<InputCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (captureLock)
        {
            pendingCaptureRegistration.Dispose();
            pendingCapture?.TrySetCanceled(CancellationToken.None);
            pendingCapture = completion;
            pendingCaptureRegistration = cancellationToken.Register(() =>
            {
                lock (captureLock)
                {
                    if (ReferenceEquals(pendingCapture, completion))
                    {
                        pendingCapture = null;
                    }
                }

                completion.TrySetCanceled(cancellationToken);
            });
        }

        return new ValueTask<InputCaptureResult>(completion.Task);
    }

    private void ProcessKeyboard(NativeMethods.RawKeyboard keyboard)
    {
        bool pressed = (keyboard.Flags & NativeMethods.RI_KEY_BREAK) == 0;
        string keyName = VirtualKeyNames.FromRawKeyboard(keyboard.VKey, keyboard.MakeCode, keyboard.Flags);
        buttons[InputSnapshotKeys.KeyboardButton(keyName)] = pressed;

        if (pressed)
        {
            TryCompleteCapture(new KeyboardKeyInputSource
            {
                Id = CreateCapturedId("keyboard", keyName),
                DisplayName = keyName,
                Key = keyName
            }, keyName);
        }
    }

    private void ProcessMouse(NativeMethods.RawMouse mouse)
    {
        ushort flags = mouse.ButtonFlags;
        ProcessMouseButton(flags, NativeMethods.RI_MOUSE_LEFT_BUTTON_DOWN, NativeMethods.RI_MOUSE_LEFT_BUTTON_UP, "Left");
        ProcessMouseButton(flags, NativeMethods.RI_MOUSE_RIGHT_BUTTON_DOWN, NativeMethods.RI_MOUSE_RIGHT_BUTTON_UP, "Right");
        ProcessMouseButton(flags, NativeMethods.RI_MOUSE_MIDDLE_BUTTON_DOWN, NativeMethods.RI_MOUSE_MIDDLE_BUTTON_UP, "Middle");
        ProcessMouseButton(flags, NativeMethods.RI_MOUSE_BUTTON_4_DOWN, NativeMethods.RI_MOUSE_BUTTON_4_UP, "X1");
        ProcessMouseButton(flags, NativeMethods.RI_MOUSE_BUTTON_5_DOWN, NativeMethods.RI_MOUSE_BUTTON_5_UP, "X2");
    }

    private void ProcessMouseButton(ushort flags, ushort downFlag, ushort upFlag, string buttonName)
    {
        if ((flags & downFlag) != 0)
        {
            buttons[InputSnapshotKeys.MouseButton(buttonName)] = true;
            TryCompleteCapture(new MouseButtonInputSource
            {
                Id = CreateCapturedId("mouse", buttonName),
                DisplayName = $"Mouse {buttonName}",
                Button = buttonName
            }, $"Mouse {buttonName}");
        }

        if ((flags & upFlag) != 0)
        {
            buttons[InputSnapshotKeys.MouseButton(buttonName)] = false;
        }
    }

    private void TryCompleteCapture(InputSource source, string displayText)
    {
        TaskCompletionSource<InputCaptureResult>? completion;

        lock (captureLock)
        {
            completion = pendingCapture;
            pendingCapture = null;
            pendingCaptureRegistration.Dispose();
        }

        completion?.TrySetResult(new InputCaptureResult(source, displayText, DateTimeOffset.UtcNow));
    }

    private static string CreateCapturedId(string kind, string name)
    {
        string normalized = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? kind : $"{kind}-{normalized}";
    }
}
