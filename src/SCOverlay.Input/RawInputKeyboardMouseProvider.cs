using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SCOverlay.Core.Domain;
using SCOverlay.Core.Input;

namespace SCOverlay.Input;

public sealed class RawInputKeyboardMouseProvider : IDisposable
{
    private readonly ConcurrentDictionary<string, bool> buttons = new(StringComparer.Ordinal);
    private readonly object captureLock = new();
    private readonly NativeMethods.LowLevelHookProc keyboardHookProc;
    private readonly NativeMethods.LowLevelHookProc mouseHookProc;
    private IntPtr keyboardHookHandle;
    private IntPtr mouseHookHandle;
    private TaskCompletionSource<InputCaptureResult>? pendingCapture;
    private CancellationTokenRegistration pendingCaptureRegistration;

    public RawInputKeyboardMouseProvider()
    {
        keyboardHookProc = KeyboardHookCallback;
        mouseHookProc = MouseHookCallback;
    }

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

        InstallHooks();
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
        PollKeyboardAndMouseState();
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

    public void Dispose()
    {
        pendingCaptureRegistration.Dispose();
        if (keyboardHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(keyboardHookHandle);
            keyboardHookHandle = IntPtr.Zero;
        }

        if (mouseHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(mouseHookHandle);
            mouseHookHandle = IntPtr.Zero;
        }
    }

    private void InstallHooks()
    {
        if (keyboardHookHandle == IntPtr.Zero)
        {
            keyboardHookHandle = NativeMethods.SetWindowsHookExW(
                NativeMethods.WH_KEYBOARD_LL,
                keyboardHookProc,
                IntPtr.Zero,
                0);
        }

        if (mouseHookHandle == IntPtr.Zero)
        {
            mouseHookHandle = NativeMethods.SetWindowsHookExW(
                NativeMethods.WH_MOUSE_LL,
                mouseHookProc,
                IntPtr.Zero,
                0);
        }
    }

    private void ProcessKeyboard(NativeMethods.RawKeyboard keyboard)
    {
        bool pressed = (keyboard.Flags & NativeMethods.RI_KEY_BREAK) == 0;
        string keyName = VirtualKeyNames.FromRawKeyboard(keyboard.VKey, keyboard.MakeCode, keyboard.Flags);
        ProcessKeyboardButton(keyName, pressed);
    }

    private void ProcessKeyboardButton(string keyName, bool pressed)
    {
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

    private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int message = wParam.ToInt32();
            if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
            {
                NativeMethods.KeyboardHookStruct keyboard = Marshal.PtrToStructure<NativeMethods.KeyboardHookStruct>(lParam);
                string keyName = VirtualKeyNames.FromLowLevelKeyboard(keyboard.VirtualKey, keyboard.ScanCode, keyboard.Flags);
                bool pressed = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                ProcessKeyboardButton(keyName, pressed);
            }
        }

        return NativeMethods.CallNextHookEx(keyboardHookHandle, code, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int message = wParam.ToInt32();
            if (message is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_LBUTTONUP)
            {
                ProcessHookMouseButton("Left", message == NativeMethods.WM_LBUTTONDOWN);
            }
            else if (message is NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_RBUTTONUP)
            {
                ProcessHookMouseButton("Right", message == NativeMethods.WM_RBUTTONDOWN);
            }
            else if (message is NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_MBUTTONUP)
            {
                ProcessHookMouseButton("Middle", message == NativeMethods.WM_MBUTTONDOWN);
            }
            else if (message is NativeMethods.WM_XBUTTONDOWN or NativeMethods.WM_XBUTTONUP)
            {
                NativeMethods.MouseHookStruct mouse = Marshal.PtrToStructure<NativeMethods.MouseHookStruct>(lParam);
                string buttonName = ((mouse.MouseData >> 16) & 0xFFFF) == 2 ? "X2" : "X1";
                ProcessHookMouseButton(buttonName, message == NativeMethods.WM_XBUTTONDOWN);
            }
        }

        return NativeMethods.CallNextHookEx(mouseHookHandle, code, wParam, lParam);
    }

    private void ProcessHookMouseButton(string buttonName, bool pressed)
    {
        buttons[InputSnapshotKeys.MouseButton(buttonName)] = pressed;
        if (pressed)
        {
            TryCompleteCapture(new MouseButtonInputSource
            {
                Id = CreateCapturedId("mouse", buttonName),
                DisplayName = $"Mouse {buttonName}",
                Button = buttonName
            }, $"Mouse {buttonName}");
        }
    }

    private void PollKeyboardAndMouseState()
    {
        foreach (KeyValuePair<string, int> key in VirtualKeyNames.PollableKeys)
        {
            MergePolledButton(InputSnapshotKeys.KeyboardButton(key.Key), IsPressed(key.Value), keyboardHookHandle != IntPtr.Zero);
        }

        bool mouseHookInstalled = mouseHookHandle != IntPtr.Zero;
        MergePolledButton(InputSnapshotKeys.MouseButton("Left"), IsPressed(0x01), mouseHookInstalled);
        MergePolledButton(InputSnapshotKeys.MouseButton("Right"), IsPressed(0x02), mouseHookInstalled);
        MergePolledButton(InputSnapshotKeys.MouseButton("Middle"), IsPressed(0x04), mouseHookInstalled);
        MergePolledButton(InputSnapshotKeys.MouseButton("X1"), IsPressed(0x05), mouseHookInstalled);
        MergePolledButton(InputSnapshotKeys.MouseButton("X2"), IsPressed(0x06), mouseHookInstalled);
    }

    private static bool IsPressed(int virtualKey)
    {
        return (NativeMethods.GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
    }

    private void MergePolledButton(string key, bool pressed, bool hookInstalled)
    {
        if (!hookInstalled || pressed)
        {
            buttons[key] = pressed;
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
