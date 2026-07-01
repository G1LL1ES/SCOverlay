using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SCOverlay.Input;

internal static partial class NativeMethods
{
    public const int WM_INPUT = 0x00FF;

    public const uint RID_INPUT = 0x10000003;
    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RIDEV_PAGEONLY = 0x00000020;

    public const uint RIM_TYPEMOUSE = 0;
    public const uint RIM_TYPEKEYBOARD = 1;
    public const uint RIM_TYPEHID = 2;

    public const uint RIDI_DEVICENAME = 0x20000007;
    public const uint RIDI_PREPARSEDDATA = 0x20000005;
    public const uint RIDI_DEVICEINFO = 0x2000000B;

    public const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    public const ushort HID_USAGE_PAGE_SIMULATION = 0x02;
    public const ushort HID_USAGE_GENERIC_MOUSE = 0x02;
    public const ushort HID_USAGE_GENERIC_JOYSTICK = 0x04;
    public const ushort HID_USAGE_GENERIC_GAMEPAD = 0x05;
    public const ushort HID_USAGE_GENERIC_KEYBOARD = 0x06;
    public const ushort HID_USAGE_GENERIC_MULTI_AXIS_CONTROLLER = 0x08;
    public const ushort HID_USAGE_GENERIC_X = 0x30;
    public const ushort HID_USAGE_GENERIC_Y = 0x31;
    public const ushort HID_USAGE_GENERIC_Z = 0x32;
    public const ushort HID_USAGE_GENERIC_RX = 0x33;
    public const ushort HID_USAGE_GENERIC_RY = 0x34;
    public const ushort HID_USAGE_GENERIC_RZ = 0x35;
    public const ushort HID_USAGE_GENERIC_SLIDER = 0x36;
    public const ushort HID_USAGE_GENERIC_DIAL = 0x37;
    public const ushort HID_USAGE_GENERIC_WHEEL = 0x38;
    public const ushort HID_USAGE_GENERIC_HAT_SWITCH = 0x39;
    public const ushort HID_USAGE_PAGE_BUTTON = 0x09;

    public const ushort RI_KEY_BREAK = 0x0001;
    public const ushort RI_KEY_E0 = 0x0002;

    public const ushort RI_MOUSE_LEFT_BUTTON_DOWN = 0x0001;
    public const ushort RI_MOUSE_LEFT_BUTTON_UP = 0x0002;
    public const ushort RI_MOUSE_RIGHT_BUTTON_DOWN = 0x0004;
    public const ushort RI_MOUSE_RIGHT_BUTTON_UP = 0x0008;
    public const ushort RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
    public const ushort RI_MOUSE_MIDDLE_BUTTON_UP = 0x0020;
    public const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040;
    public const ushort RI_MOUSE_BUTTON_4_UP = 0x0080;
    public const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100;
    public const ushort RI_MOUSE_BUTTON_5_UP = 0x0200;

    public const uint JOYERR_NOERROR = 0;
    public const uint JOY_RETURNALL = 0x000000FF;
    public const ushort JOYCAPS_HASZ = 0x0001;
    public const ushort JOYCAPS_HASR = 0x0002;
    public const ushort JOYCAPS_HASU = 0x0004;
    public const ushort JOYCAPS_HASV = 0x0008;
    public const ushort JOYCAPS_HASPOV = 0x0010;

    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputDeviceList(
        [Out] RawInputDeviceList[]? pRawInputDeviceList,
        ref uint puiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputDeviceInfoW(
        IntPtr hDevice,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize);

    [DllImport("winmm.dll")]
    public static extern uint joyGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    public static extern uint joyGetDevCapsW(uint uJoyId, out JoyCaps pjc, uint cbjc);

    [DllImport("winmm.dll")]
    public static extern uint joyGetPosEx(uint uJoyId, ref JoyInfoEx pji);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr preparsedData, out HidPCaps capabilities);

    [DllImport("hid.dll")]
    public static extern int HidP_GetValueCaps(
        HidPReportType reportType,
        [Out] HidPValueCaps[] valueCaps,
        ref ushort valueCapsLength,
        IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern int HidP_GetButtonCaps(
        HidPReportType reportType,
        [Out] HidPButtonCaps[] buttonCaps,
        ref ushort buttonCapsLength,
        IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern int HidP_GetUsageValue(
        HidPReportType reportType,
        ushort usagePage,
        ushort linkCollection,
        ushort usage,
        out uint usageValue,
        IntPtr preparsedData,
        byte[] report,
        uint reportLength);

    [DllImport("hid.dll")]
    public static extern int HidP_GetUsages(
        HidPReportType reportType,
        ushort usagePage,
        ushort linkCollection,
        [Out] ushort[] usageList,
        ref uint usageLength,
        IntPtr preparsedData,
        byte[] report,
        uint reportLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool HidD_GetProductString(
        SafeFileHandle hidDeviceObject,
        byte[] buffer,
        uint bufferLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr TargetWindow;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputDeviceList
    {
        public IntPtr Device;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawMouse
    {
        public ushort Flags;
        public uint Buttons;
        public uint RawButtons;
        public int LastX;
        public int LastY;
        public uint ExtraInformation;

        public readonly ushort ButtonFlags => unchecked((ushort)(Buttons & 0xFFFF));
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputDeviceInfo
    {
        public uint Size;
        public uint Type;
        public RawInputDeviceInfoUnion Info;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct RawInputDeviceInfoUnion
    {
        [FieldOffset(0)]
        public RawInputDeviceInfoMouse Mouse;

        [FieldOffset(0)]
        public RawInputDeviceInfoKeyboard Keyboard;

        [FieldOffset(0)]
        public RawInputDeviceInfoHid Hid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputDeviceInfoMouse
    {
        public uint Id;
        public uint NumberOfButtons;
        public uint SampleRate;
        public bool HasHorizontalWheel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputDeviceInfoKeyboard
    {
        public uint Type;
        public uint SubType;
        public uint KeyboardMode;
        public uint NumberOfFunctionKeys;
        public uint NumberOfIndicators;
        public uint NumberOfKeysTotal;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputDeviceInfoHid
    {
        public uint VendorId;
        public uint ProductId;
        public uint VersionNumber;
        public ushort UsagePage;
        public ushort Usage;
    }

    public enum HidPReportType
    {
        Input = 0,
        Output = 1,
        Feature = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HidPCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HidPButtonCaps
    {
        public ushort UsagePage;
        public byte ReportId;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public uint Reserved0;
        public uint Reserved1;
        public uint Reserved2;
        public uint Reserved3;
        public uint Reserved4;
        public uint Reserved5;
        public uint Reserved6;
        public uint Reserved7;
        public uint Reserved8;
        public uint Reserved9;
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HidPValueCaps
    {
        public ushort UsagePage;
        public byte ReportId;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public byte HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;
        public ushort Reserved2a;
        public ushort Reserved2b;
        public ushort Reserved2c;
        public ushort Reserved2d;
        public ushort Reserved2e;
        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;
        public ushort UsageMin;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct JoyCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ProductName;

        public uint XMin;
        public uint XMax;
        public uint YMin;
        public uint YMax;
        public uint ZMin;
        public uint ZMax;
        public uint NumButtons;
        public uint PeriodMin;
        public uint PeriodMax;
        public uint RMin;
        public uint RMax;
        public uint UMin;
        public uint UMax;
        public uint VMin;
        public uint VMax;
        public ushort Caps;
        public uint MaxAxes;
        public uint NumAxes;
        public uint MaxButtons;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string RegistryKey;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string OemVxD;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JoyInfoEx
    {
        public uint Size;
        public uint Flags;
        public uint XPosition;
        public uint YPosition;
        public uint ZPosition;
        public uint RPosition;
        public uint UPosition;
        public uint VPosition;
        public uint Buttons;
        public uint ButtonNumber;
        public uint Pov;
        public uint Reserved1;
        public uint Reserved2;
    }
}
