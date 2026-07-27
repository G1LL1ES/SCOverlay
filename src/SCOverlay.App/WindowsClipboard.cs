using System.Runtime.InteropServices;

namespace SCOverlay.App;

internal static class WindowsClipboard
{
    private const uint ClipboardFormatUnicodeText = 13;
    private const uint GlobalMemoryMoveable = 0x0002;
    private const uint GlobalMemoryZeroInitialize = 0x0040;
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200)
    ];

    public static async Task<bool> TrySetTextAsync(
        IntPtr ownerWindow,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (int attempt = 0; ; attempt++)
        {
            if (TrySetText(ownerWindow, text))
            {
                return true;
            }

            if (attempt >= RetryDelays.Length)
            {
                return false;
            }

            await Task.Delay(RetryDelays[attempt], cancellationToken);
        }
    }

    private static bool TrySetText(IntPtr ownerWindow, string text)
    {
        nuint byteCount = checked((nuint)(text.Length + 1) * sizeof(char));
        IntPtr memory = GlobalAlloc(GlobalMemoryMoveable | GlobalMemoryZeroInitialize, byteCount);
        if (memory == IntPtr.Zero)
        {
            return false;
        }

        IntPtr destination = GlobalLock(memory);
        if (destination == IntPtr.Zero)
        {
            GlobalFree(memory);
            return false;
        }

        try
        {
            Marshal.Copy(text.ToCharArray(), 0, destination, text.Length);
            Marshal.WriteInt16(destination, text.Length * sizeof(char), 0);
        }
        finally
        {
            GlobalUnlock(memory);
        }

        if (!OpenClipboard(ownerWindow))
        {
            GlobalFree(memory);
            return false;
        }

        try
        {
            if (!EmptyClipboard() || SetClipboardData(ClipboardFormatUnicodeText, memory) == IntPtr.Zero)
            {
                return false;
            }

            // Windows owns the allocation after SetClipboardData succeeds.
            memory = IntPtr.Zero;
            return true;
        }
        finally
        {
            CloseClipboard();
            if (memory != IntPtr.Zero)
            {
                GlobalFree(memory);
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr newOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
