using System.Runtime.InteropServices;
using SCOverlay.Core.Input;

namespace SCOverlay.Input;

public sealed class WinMmJoystickProvider
{
    private readonly object deviceCacheLock = new();
    private readonly IWinMmApi api;
    private CachedJoystick[] cachedDevices = Array.Empty<CachedJoystick>();
    private bool hasDiscoveredDevices;

    public WinMmJoystickProvider()
        : this(WinMmApi.Instance)
    {
    }

    internal WinMmJoystickProvider(IWinMmApi api)
    {
        this.api = api;
    }

    public IReadOnlyList<InputDeviceInfo> EnumerateDevices()
    {
        CachedJoystick[] discoveredDevices = DiscoverDevices();

        lock (deviceCacheLock)
        {
            cachedDevices = discoveredDevices;
            hasDiscoveredDevices = true;
        }

        return discoveredDevices.Select(device => device.Info).ToArray();
    }

    public InputSnapshot Poll(DateTimeOffset timestamp)
    {
        var axes = new Dictionary<string, double>(StringComparer.Ordinal);
        var buttons = new Dictionary<string, bool>(StringComparer.Ordinal);
        CachedJoystick[] devices = GetCachedDevices();

        foreach (CachedJoystick device in devices)
        {
            if (!api.TryGetPosition(device.Index, out NativeMethods.JoyInfoEx position))
            {
                continue;
            }

            string deviceId = device.Info.DeviceId;
            NativeMethods.JoyCaps caps = device.Capabilities;
            axes[InputSnapshotKeys.JoystickAxis(deviceId, 0)] = Normalize(position.XPosition, caps.XMin, caps.XMax);
            axes[InputSnapshotKeys.JoystickAxis(deviceId, 1)] = Normalize(position.YPosition, caps.YMin, caps.YMax);

            if ((caps.Caps & NativeMethods.JOYCAPS_HASZ) != 0)
            {
                axes[InputSnapshotKeys.JoystickAxis(deviceId, 2)] = Normalize(position.ZPosition, caps.ZMin, caps.ZMax);
            }

            if ((caps.Caps & NativeMethods.JOYCAPS_HASR) != 0)
            {
                axes[InputSnapshotKeys.JoystickAxis(deviceId, 3)] = Normalize(position.RPosition, caps.RMin, caps.RMax);
            }

            if ((caps.Caps & NativeMethods.JOYCAPS_HASU) != 0)
            {
                axes[InputSnapshotKeys.JoystickAxis(deviceId, 4)] = Normalize(position.UPosition, caps.UMin, caps.UMax);
            }

            if ((caps.Caps & NativeMethods.JOYCAPS_HASV) != 0)
            {
                axes[InputSnapshotKeys.JoystickAxis(deviceId, 5)] = Normalize(position.VPosition, caps.VMin, caps.VMax);
            }

            int buttonCount = (int)Math.Min(caps.NumButtons, 32);
            for (int button = 0; button < buttonCount; button++)
            {
                uint mask = 1u << button;
                buttons[InputSnapshotKeys.JoystickButton(deviceId, button)] = (position.Buttons & mask) != 0;
            }
        }

        return new InputSnapshot(timestamp, axes, buttons);
    }

    private CachedJoystick[] GetCachedDevices()
    {
        lock (deviceCacheLock)
        {
            if (!hasDiscoveredDevices)
            {
                cachedDevices = DiscoverDevices();
                hasDiscoveredDevices = true;
            }

            return cachedDevices;
        }
    }

    private CachedJoystick[] DiscoverDevices()
    {
        var devices = new List<CachedJoystick>();
        uint count = api.GetDeviceCount();

        for (uint index = 0; index < count; index++)
        {
            if (!api.TryGetCapabilities(index, out NativeMethods.JoyCaps caps) || !IsActiveDevice(index, caps))
            {
                continue;
            }

            string name = string.IsNullOrWhiteSpace(caps.ProductName)
                ? $"Joystick {index}"
                : caps.ProductName.Trim();
            var info = new InputDeviceInfo(
                DeviceId: $"joystick:{index}",
                DisplayName: name,
                Kind: InputDeviceKind.Joystick,
                AxisCount: CountAxes(caps),
                ButtonCount: (int)caps.NumButtons,
                Details: "Legacy WinMM",
                StableIdentity: InputDeviceIdentity.CreateStableWinMmIdentity(index, name));

            devices.Add(new CachedJoystick(index, caps, info));
        }

        return devices.ToArray();
    }

    private bool IsActiveDevice(uint index, NativeMethods.JoyCaps caps)
    {
        string productName = caps.ProductName?.Trim() ?? string.Empty;
        return !productName.Equals("Microsoft PC-joystick driver", StringComparison.OrdinalIgnoreCase)
            && api.TryGetPosition(index, out _);
    }

    private sealed record CachedJoystick(
        uint Index,
        NativeMethods.JoyCaps Capabilities,
        InputDeviceInfo Info);

    internal interface IWinMmApi
    {
        uint GetDeviceCount();

        bool TryGetCapabilities(uint index, out NativeMethods.JoyCaps capabilities);

        bool TryGetPosition(uint index, out NativeMethods.JoyInfoEx position);
    }

    private sealed class WinMmApi : IWinMmApi
    {
        public static WinMmApi Instance { get; } = new();

        public uint GetDeviceCount() => NativeMethods.joyGetNumDevs();

        public bool TryGetCapabilities(uint index, out NativeMethods.JoyCaps capabilities)
        {
            return NativeMethods.joyGetDevCapsW(
                index,
                out capabilities,
                (uint)Marshal.SizeOf<NativeMethods.JoyCaps>()) == NativeMethods.JOYERR_NOERROR;
        }

        public bool TryGetPosition(uint index, out NativeMethods.JoyInfoEx position)
        {
            position = new NativeMethods.JoyInfoEx
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.JoyInfoEx>(),
                Flags = NativeMethods.JOY_RETURNALL
            };

            return NativeMethods.joyGetPosEx(index, ref position) == NativeMethods.JOYERR_NOERROR;
        }
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
