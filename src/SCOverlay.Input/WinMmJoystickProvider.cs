using System.Runtime.InteropServices;
using SCOverlay.Core.Input;

namespace SCOverlay.Input;

public sealed class WinMmJoystickProvider
{
    public IReadOnlyList<InputDeviceInfo> EnumerateDevices()
    {
        var devices = new List<InputDeviceInfo>();
        uint count = NativeMethods.joyGetNumDevs();

        for (uint index = 0; index < count; index++)
        {
            if (!TryGetCaps(index, out NativeMethods.JoyCaps caps) || !IsActiveDevice(index, caps))
            {
                continue;
            }

            string name = string.IsNullOrWhiteSpace(caps.ProductName)
                ? $"Joystick {index}"
                : caps.ProductName.Trim();

            devices.Add(new InputDeviceInfo(
                DeviceId: $"joystick:{index}",
                DisplayName: name,
                Kind: InputDeviceKind.Joystick,
                AxisCount: CountAxes(caps),
                ButtonCount: (int)caps.NumButtons,
                Details: "Legacy WinMM",
                StableIdentity: InputDeviceIdentity.CreateStableWinMmIdentity(index, name)));
        }

        return devices;
    }

    public InputSnapshot Poll(DateTimeOffset timestamp)
    {
        var axes = new Dictionary<string, double>(StringComparer.Ordinal);
        var buttons = new Dictionary<string, bool>(StringComparer.Ordinal);
        uint count = NativeMethods.joyGetNumDevs();

        for (uint index = 0; index < count; index++)
        {
            if (!TryGetCaps(index, out NativeMethods.JoyCaps caps) || !IsActiveDevice(index, caps))
            {
                continue;
            }

            var info = new NativeMethods.JoyInfoEx
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.JoyInfoEx>(),
                Flags = NativeMethods.JOY_RETURNALL
            };

            if (NativeMethods.joyGetPosEx(index, ref info) != NativeMethods.JOYERR_NOERROR)
            {
                continue;
            }

            string deviceId = $"joystick:{index}";
            axes[InputSnapshotKeys.JoystickAxis(deviceId, 0)] = Normalize(info.XPosition, caps.XMin, caps.XMax);
            axes[InputSnapshotKeys.JoystickAxis(deviceId, 1)] = Normalize(info.YPosition, caps.YMin, caps.YMax);

            if ((caps.Caps & NativeMethods.JOYCAPS_HASZ) != 0)
            {
                axes[InputSnapshotKeys.JoystickAxis(deviceId, 2)] = Normalize(info.ZPosition, caps.ZMin, caps.ZMax);
            }

            if ((caps.Caps & NativeMethods.JOYCAPS_HASR) != 0)
            {
                axes[InputSnapshotKeys.JoystickAxis(deviceId, 3)] = Normalize(info.RPosition, caps.RMin, caps.RMax);
            }

            if ((caps.Caps & NativeMethods.JOYCAPS_HASU) != 0)
            {
                axes[InputSnapshotKeys.JoystickAxis(deviceId, 4)] = Normalize(info.UPosition, caps.UMin, caps.UMax);
            }

            if ((caps.Caps & NativeMethods.JOYCAPS_HASV) != 0)
            {
                axes[InputSnapshotKeys.JoystickAxis(deviceId, 5)] = Normalize(info.VPosition, caps.VMin, caps.VMax);
            }

            int buttonCount = (int)Math.Min(caps.NumButtons, 32);
            for (int button = 0; button < buttonCount; button++)
            {
                uint mask = 1u << button;
                buttons[InputSnapshotKeys.JoystickButton(deviceId, button)] = (info.Buttons & mask) != 0;
            }
        }

        return new InputSnapshot(timestamp, axes, buttons);
    }

    private static bool TryGetCaps(uint index, out NativeMethods.JoyCaps caps)
    {
        return NativeMethods.joyGetDevCapsW(index, out caps, (uint)Marshal.SizeOf<NativeMethods.JoyCaps>()) == NativeMethods.JOYERR_NOERROR;
    }

    private static bool IsActiveDevice(uint index, NativeMethods.JoyCaps caps)
    {
        string productName = caps.ProductName?.Trim() ?? string.Empty;
        if (productName.Equals("Microsoft PC-joystick driver", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var info = new NativeMethods.JoyInfoEx
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.JoyInfoEx>(),
            Flags = NativeMethods.JOY_RETURNALL
        };

        return NativeMethods.joyGetPosEx(index, ref info) == NativeMethods.JOYERR_NOERROR;
    }

    private static int CountAxes(NativeMethods.JoyCaps caps)
    {
        int count = 2;

        if ((caps.Caps & NativeMethods.JOYCAPS_HASZ) != 0)
        {
            count++;
        }

        if ((caps.Caps & NativeMethods.JOYCAPS_HASR) != 0)
        {
            count++;
        }

        if ((caps.Caps & NativeMethods.JOYCAPS_HASU) != 0)
        {
            count++;
        }

        if ((caps.Caps & NativeMethods.JOYCAPS_HASV) != 0)
        {
            count++;
        }

        return count;
    }

    private static double Normalize(uint value, uint minimum, uint maximum)
    {
        if (maximum <= minimum)
        {
            return 0.0;
        }

        double normalized = ((double)value - minimum) / (maximum - minimum);
        return Math.Clamp((normalized * 2.0) - 1.0, -1.0, 1.0);
    }
}
