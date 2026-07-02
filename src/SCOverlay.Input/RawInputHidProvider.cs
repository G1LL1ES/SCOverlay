using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using SCOverlay.Core.Input;

namespace SCOverlay.Input;

public sealed class RawInputHidProvider : IDisposable
{
    private readonly ConcurrentDictionary<IntPtr, RawHidDevice> devicesByHandle = new();
    private readonly ConcurrentDictionary<string, double> axes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> buttons = new(StringComparer.Ordinal);

    public void AttachWindow(IntPtr windowHandle)
    {
        var registrations = new[]
        {
            CreateRegistration(NativeMethods.HID_USAGE_PAGE_GENERIC, NativeMethods.HID_USAGE_GENERIC_JOYSTICK, windowHandle),
            CreateRegistration(NativeMethods.HID_USAGE_PAGE_GENERIC, NativeMethods.HID_USAGE_GENERIC_GAMEPAD, windowHandle),
            CreateRegistration(NativeMethods.HID_USAGE_PAGE_GENERIC, NativeMethods.HID_USAGE_GENERIC_MULTI_AXIS_CONTROLLER, windowHandle),
            new NativeMethods.RawInputDevice
            {
                UsagePage = NativeMethods.HID_USAGE_PAGE_SIMULATION,
                Usage = 0,
                Flags = NativeMethods.RIDEV_INPUTSINK | NativeMethods.RIDEV_PAGEONLY,
                TargetWindow = windowHandle
            }
        };

        foreach (NativeMethods.RawInputDevice registration in registrations)
        {
            NativeMethods.RegisterRawInputDevices(
                new[] { registration },
                1,
                (uint)Marshal.SizeOf<NativeMethods.RawInputDevice>());
        }
    }

    public IReadOnlyList<InputDeviceInfo> EnumerateDevices()
    {
        var discovered = new List<RawHidDevice>();
        uint count = 0;
        uint result = NativeMethods.GetRawInputDeviceList(null, ref count, (uint)Marshal.SizeOf<NativeMethods.RawInputDeviceList>());
        if (result == uint.MaxValue || count == 0)
        {
            return Array.Empty<InputDeviceInfo>();
        }

        var rawDevices = new NativeMethods.RawInputDeviceList[count];
        result = NativeMethods.GetRawInputDeviceList(rawDevices, ref count, (uint)Marshal.SizeOf<NativeMethods.RawInputDeviceList>());
        if (result == uint.MaxValue)
        {
            return Array.Empty<InputDeviceInfo>();
        }

        int ordinal = 0;
        foreach (NativeMethods.RawInputDeviceList rawDevice in rawDevices)
        {
            if (rawDevice.Type != NativeMethods.RIM_TYPEHID)
            {
                continue;
            }

            if (!TryGetDeviceInfo(rawDevice.Device, ordinal, out RawHidDevice? device) || device is null)
            {
                continue;
            }

            if (!IsFlightRelevant(device.UsagePage, device.Usage))
            {
                device.Dispose();
                continue;
            }

            devicesByHandle.AddOrUpdate(
                rawDevice.Device,
                device,
                (_, existing) =>
                {
                    device.Dispose();
                    return existing;
                });

            discovered.Add(devicesByHandle[rawDevice.Device]);
            ordinal++;
        }

        return discovered
            .Select(device => new InputDeviceInfo(
                DeviceId: device.DeviceId,
                DisplayName: device.DisplayName,
                Kind: InputDeviceKind.Joystick,
                AxisCount: device.AxisItems.Count,
                ButtonCount: device.ButtonItems.Count,
                HatCount: device.HatItems.Count,
                Details: $"Raw HID usage {device.UsagePage:X2}:{device.Usage:X2} VID:{device.VendorId:X4} PID:{device.ProductId:X4}"))
            .ToArray();
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
            if (header.Type != NativeMethods.RIM_TYPEHID)
            {
                return;
            }

            RawHidDevice device = devicesByHandle.GetOrAdd(header.Device, handle => CreateFallbackDevice(handle, devicesByHandle.Count));
            IntPtr payload = IntPtr.Add(buffer, Marshal.SizeOf<NativeMethods.RawInputHeader>());
            int reportSize = Marshal.ReadInt32(payload);
            int reportCount = Marshal.ReadInt32(payload, sizeof(int));

            if (reportSize <= 0 || reportCount <= 0)
            {
                return;
            }

            IntPtr reportStart = IntPtr.Add(payload, sizeof(int) * 2);
            for (int reportIndex = 0; reportIndex < reportCount; reportIndex++)
            {
                byte[] report = new byte[reportSize];
                Marshal.Copy(IntPtr.Add(reportStart, reportIndex * reportSize), report, 0, reportSize);
                StoreReport(device, report);
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
            axes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            buttons.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    public void Dispose()
    {
        foreach (RawHidDevice device in devicesByHandle.Values)
        {
            device.Dispose();
        }

        devicesByHandle.Clear();
        axes.Clear();
        buttons.Clear();
    }

    private static NativeMethods.RawInputDevice CreateRegistration(ushort usagePage, ushort usage, IntPtr windowHandle)
    {
        return new NativeMethods.RawInputDevice
        {
            UsagePage = usagePage,
            Usage = usage,
            Flags = NativeMethods.RIDEV_INPUTSINK,
            TargetWindow = windowHandle
        };
    }

    private static bool TryGetDeviceInfo(IntPtr handle, int ordinal, out RawHidDevice? device)
    {
        device = null;
        uint size = (uint)Marshal.SizeOf<NativeMethods.RawInputDeviceInfo>();
        IntPtr infoBuffer = Marshal.AllocHGlobal((int)size);

        try
        {
            Marshal.WriteInt32(infoBuffer, (int)size);
            uint result = NativeMethods.GetRawInputDeviceInfoW(handle, NativeMethods.RIDI_DEVICEINFO, infoBuffer, ref size);
            if (result == uint.MaxValue)
            {
                return false;
            }

            NativeMethods.RawInputDeviceInfo info = Marshal.PtrToStructure<NativeMethods.RawInputDeviceInfo>(infoBuffer);
            if (info.Type != NativeMethods.RIM_TYPEHID)
            {
                return false;
            }

            NativeMethods.RawInputDeviceInfoHid hid = info.Info.Hid;
            string rawName = GetDeviceName(handle);
            if (!TryCreateParsedDevice(handle, ordinal, hid, rawName, out device))
            {
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(infoBuffer);
        }
    }

    private static bool TryCreateParsedDevice(
        IntPtr handle,
        int ordinal,
        NativeMethods.RawInputDeviceInfoHid hid,
        string rawName,
        out RawHidDevice? device)
    {
        device = null;
        if (!TryGetPreparsedData(handle, out IntPtr preparsedData))
        {
            return false;
        }

        if (NativeMethods.HidP_GetCaps(preparsedData, out NativeMethods.HidPCaps caps) < 0)
        {
            Marshal.FreeHGlobal(preparsedData);
            return false;
        }

        IReadOnlyList<HidValueItem> values = GetValueItems(preparsedData, caps);
        IReadOnlyList<HidButtonItem> parsedButtons = GetButtonItems(preparsedData, caps);
        string deviceId = $"hid:vid_{hid.VendorId:X4}&pid_{hid.ProductId:X4}:{ordinal}";
        string displayName = CreateDisplayName(hid, rawName);

        device = new RawHidDevice(
            DeviceId: deviceId,
            DisplayName: displayName,
            VendorId: hid.VendorId,
            ProductId: hid.ProductId,
            UsagePage: hid.UsagePage,
            Usage: hid.Usage,
            PreparsedData: preparsedData,
            ReportByteLength: caps.InputReportByteLength,
            AxisItems: values.Where(item => item.Kind == HidValueKind.Axis).ToArray(),
            HatItems: values.Where(item => item.Kind == HidValueKind.Hat).ToArray(),
            ButtonItems: parsedButtons);

        return true;
    }

    private static bool TryGetPreparsedData(IntPtr handle, out IntPtr preparsedData)
    {
        preparsedData = IntPtr.Zero;
        uint size = 0;
        NativeMethods.GetRawInputDeviceInfoW(handle, NativeMethods.RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
        if (size == 0)
        {
            return false;
        }

        preparsedData = Marshal.AllocHGlobal((int)size);
        uint result = NativeMethods.GetRawInputDeviceInfoW(handle, NativeMethods.RIDI_PREPARSEDDATA, preparsedData, ref size);
        if (result == uint.MaxValue)
        {
            Marshal.FreeHGlobal(preparsedData);
            preparsedData = IntPtr.Zero;
            return false;
        }

        return true;
    }

    private static IReadOnlyList<HidValueItem> GetValueItems(IntPtr preparsedData, NativeMethods.HidPCaps caps)
    {
        if (caps.NumberInputValueCaps == 0)
        {
            return Array.Empty<HidValueItem>();
        }

        ushort valueCapsLength = caps.NumberInputValueCaps;
        var valueCaps = new NativeMethods.HidPValueCaps[valueCapsLength];
        if (NativeMethods.HidP_GetValueCaps(NativeMethods.HidPReportType.Input, valueCaps, ref valueCapsLength, preparsedData) < 0)
        {
            return Array.Empty<HidValueItem>();
        }

        var values = new List<HidValueItem>();
        int axisIndex = 0;
        int hatIndex = 0;
        foreach (NativeMethods.HidPValueCaps cap in valueCaps.Take(valueCapsLength))
        {
            foreach (ushort usage in EnumerateUsages(cap.IsRange != 0, cap.UsageMin, cap.UsageMax))
            {
                if (cap.UsagePage != NativeMethods.HID_USAGE_PAGE_GENERIC)
                {
                    continue;
                }

                if (usage == NativeMethods.HID_USAGE_GENERIC_HAT_SWITCH)
                {
                    values.Add(new HidValueItem(HidValueKind.Hat, usage, cap.UsagePage, cap.LinkCollection, hatIndex++, cap.LogicalMin, cap.LogicalMax));
                }
                else if (IsAxisUsage(usage))
                {
                    values.Add(new HidValueItem(HidValueKind.Axis, usage, cap.UsagePage, cap.LinkCollection, axisIndex++, cap.LogicalMin, cap.LogicalMax));
                }
            }
        }

        return values;
    }

    private static IReadOnlyList<HidButtonItem> GetButtonItems(IntPtr preparsedData, NativeMethods.HidPCaps caps)
    {
        if (caps.NumberInputButtonCaps == 0)
        {
            return Array.Empty<HidButtonItem>();
        }

        ushort buttonCapsLength = caps.NumberInputButtonCaps;
        var buttonCaps = new NativeMethods.HidPButtonCaps[buttonCapsLength];
        if (NativeMethods.HidP_GetButtonCaps(NativeMethods.HidPReportType.Input, buttonCaps, ref buttonCapsLength, preparsedData) < 0)
        {
            return Array.Empty<HidButtonItem>();
        }

        var buttonsByUsage = new List<HidButtonItem>();
        int buttonIndex = 0;
        foreach (NativeMethods.HidPButtonCaps cap in buttonCaps.Take(buttonCapsLength))
        {
            if (cap.UsagePage != NativeMethods.HID_USAGE_PAGE_BUTTON)
            {
                continue;
            }

            foreach (ushort usage in EnumerateUsages(cap.IsRange != 0, cap.UsageMin, cap.UsageMax))
            {
                buttonsByUsage.Add(new HidButtonItem(usage, cap.UsagePage, cap.LinkCollection, buttonIndex++));
            }
        }

        return buttonsByUsage;
    }

    private static IEnumerable<ushort> EnumerateUsages(bool isRange, ushort usageMin, ushort usageMax)
    {
        if (!isRange)
        {
            yield return usageMin;
            yield break;
        }

        int count = Math.Min(Math.Max(usageMax - usageMin + 1, 0), 256);
        for (int offset = 0; offset < count; offset++)
        {
            yield return (ushort)(usageMin + offset);
        }
    }

    private static RawHidDevice CreateFallbackDevice(IntPtr handle, int ordinal)
    {
        if (TryGetDeviceInfo(handle, ordinal, out RawHidDevice? device) && device is not null)
        {
            return device;
        }

        return new RawHidDevice(
            DeviceId: $"hid:unknown:{ordinal}",
            DisplayName: $"Raw HID Device {ordinal}",
            VendorId: 0,
            ProductId: 0,
            UsagePage: 0,
            Usage: 0,
            PreparsedData: IntPtr.Zero,
            ReportByteLength: 0,
            AxisItems: Array.Empty<HidValueItem>(),
            HatItems: Array.Empty<HidValueItem>(),
            ButtonItems: Array.Empty<HidButtonItem>());
    }

    private static string GetDeviceName(IntPtr handle)
    {
        uint characterCount = 0;
        NativeMethods.GetRawInputDeviceInfoW(handle, NativeMethods.RIDI_DEVICENAME, IntPtr.Zero, ref characterCount);
        if (characterCount == 0)
        {
            return string.Empty;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)characterCount * sizeof(char));
        try
        {
            uint result = NativeMethods.GetRawInputDeviceInfoW(handle, NativeMethods.RIDI_DEVICENAME, buffer, ref characterCount);
            if (result == uint.MaxValue)
            {
                return string.Empty;
            }

            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string CreateDisplayName(NativeMethods.RawInputDeviceInfoHid hid, string rawName)
    {
        string productName = GetHidProductString(rawName);
        if (!string.IsNullOrWhiteSpace(productName))
        {
            return productName;
        }

        string kind = hid.Usage switch
        {
            NativeMethods.HID_USAGE_GENERIC_JOYSTICK => "HID Joystick",
            NativeMethods.HID_USAGE_GENERIC_GAMEPAD => "HID Gamepad",
            NativeMethods.HID_USAGE_GENERIC_MULTI_AXIS_CONTROLLER => "HID Multi-axis Controller",
            _ when hid.UsagePage == NativeMethods.HID_USAGE_PAGE_SIMULATION => "HID Simulation Control",
            _ => "HID Control"
        };

        return $"{kind} {hid.VendorId:X4}:{hid.ProductId:X4}";
    }

    private static string GetHidProductString(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return string.Empty;
        }

        using Microsoft.Win32.SafeHandles.SafeFileHandle handle = NativeMethods.CreateFileW(
            devicePath,
            0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return string.Empty;
        }

        byte[] buffer = new byte[512];
        if (!NativeMethods.HidD_GetProductString(handle, buffer, (uint)buffer.Length))
        {
            return string.Empty;
        }

        return Encoding.Unicode.GetString(buffer).TrimEnd('\0', ' ');
    }

    private static bool IsFlightRelevant(ushort usagePage, ushort usage)
    {
        if (usagePage == NativeMethods.HID_USAGE_PAGE_SIMULATION)
        {
            return true;
        }

        return usagePage == NativeMethods.HID_USAGE_PAGE_GENERIC &&
               (usage == NativeMethods.HID_USAGE_GENERIC_JOYSTICK ||
                usage == NativeMethods.HID_USAGE_GENERIC_GAMEPAD ||
                usage == NativeMethods.HID_USAGE_GENERIC_MULTI_AXIS_CONTROLLER);
    }

    private void StoreReport(RawHidDevice device, byte[] report)
    {
        if (device.PreparsedData == IntPtr.Zero)
        {
            return;
        }

        foreach (HidValueItem item in device.AxisItems)
        {
            if (TryReadUsageValue(device, item, report, out uint value))
            {
                axes[InputSnapshotKeys.JoystickAxis(device.DeviceId, item.Index)] = NormalizeUsageValue(value, item.LogicalMinimum, item.LogicalMaximum);
            }
        }

        foreach (HidValueItem item in device.HatItems)
        {
            if (TryReadUsageValue(device, item, report, out uint value))
            {
                axes[InputSnapshotKeys.JoystickHat(device.DeviceId, item.Index)] = NormalizeHat(value, item.LogicalMinimum, item.LogicalMaximum);
            }
        }

        IReadOnlySet<ushort> activeUsages = ReadActiveButtonUsages(device, report);
        foreach (HidButtonItem button in device.ButtonItems)
        {
            buttons[InputSnapshotKeys.JoystickButton(device.DeviceId, button.Index)] = activeUsages.Contains(button.Usage);
        }
    }

    private static bool TryReadUsageValue(RawHidDevice device, HidValueItem item, byte[] report, out uint value)
    {
        return NativeMethods.HidP_GetUsageValue(
            NativeMethods.HidPReportType.Input,
            item.UsagePage,
            item.LinkCollection,
            item.Usage,
            out value,
            device.PreparsedData,
            report,
            (uint)report.Length) >= 0;
    }

    private static IReadOnlySet<ushort> ReadActiveButtonUsages(RawHidDevice device, byte[] report)
    {
        if (device.ButtonItems.Count == 0)
        {
            return new HashSet<ushort>();
        }

        var active = new HashSet<ushort>();
        foreach (IGrouping<ushort, HidButtonItem> group in device.ButtonItems.GroupBy(item => item.LinkCollection))
        {
            uint usageLength = (uint)Math.Max(group.Count(), 1);
            ushort[] usages = new ushort[usageLength];
            int status = NativeMethods.HidP_GetUsages(
                NativeMethods.HidPReportType.Input,
                NativeMethods.HID_USAGE_PAGE_BUTTON,
                group.Key,
                usages,
                ref usageLength,
                device.PreparsedData,
                report,
                (uint)report.Length);

            if (status >= 0)
            {
                active.UnionWith(usages.Take((int)usageLength));
            }
        }

        return active;
    }

    private static bool IsAxisUsage(ushort usage)
    {
        return usage is NativeMethods.HID_USAGE_GENERIC_X or
            NativeMethods.HID_USAGE_GENERIC_Y or
            NativeMethods.HID_USAGE_GENERIC_Z or
            NativeMethods.HID_USAGE_GENERIC_RX or
            NativeMethods.HID_USAGE_GENERIC_RY or
            NativeMethods.HID_USAGE_GENERIC_RZ or
            NativeMethods.HID_USAGE_GENERIC_SLIDER or
            NativeMethods.HID_USAGE_GENERIC_DIAL or
            NativeMethods.HID_USAGE_GENERIC_WHEEL;
    }

    private static double NormalizeUsageValue(uint value, int logicalMinimum, int logicalMaximum)
    {
        if (logicalMaximum <= logicalMinimum)
        {
            return 0.0;
        }

        double normalized = ((double)value - logicalMinimum) / (logicalMaximum - logicalMinimum);
        return Math.Clamp((normalized * 2.0) - 1.0, -1.0, 1.0);
    }

    private static double NormalizeHat(uint value, int logicalMinimum, int logicalMaximum)
    {
        if (logicalMaximum <= logicalMinimum || value < logicalMinimum || value > logicalMaximum)
        {
            return -1.0;
        }

        double range = logicalMaximum - logicalMinimum;
        return Math.Clamp((value - logicalMinimum) / range, 0.0, 1.0);
    }

    private enum HidValueKind
    {
        Axis,
        Hat
    }

    private sealed record HidValueItem(
        HidValueKind Kind,
        ushort Usage,
        ushort UsagePage,
        ushort LinkCollection,
        int Index,
        int LogicalMinimum,
        int LogicalMaximum);

    private sealed record HidButtonItem(
        ushort Usage,
        ushort UsagePage,
        ushort LinkCollection,
        int Index);

    private sealed record RawHidDevice(
        string DeviceId,
        string DisplayName,
        uint VendorId,
        uint ProductId,
        ushort UsagePage,
        ushort Usage,
        IntPtr PreparsedData,
        ushort ReportByteLength,
        IReadOnlyList<HidValueItem> AxisItems,
        IReadOnlyList<HidValueItem> HatItems,
        IReadOnlyList<HidButtonItem> ButtonItems)
        : IDisposable
    {
        public void Dispose()
        {
            if (PreparsedData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(PreparsedData);
            }
        }
    }
}
