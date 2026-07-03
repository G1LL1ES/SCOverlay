namespace SCOverlay.Input;

internal static class VirtualKeyNames
{
    private static readonly IReadOnlyDictionary<string, int> NameToVirtualKey = CreateNameMap();

    public static string FromRawKeyboard(ushort virtualKey, ushort makeCode, ushort flags)
    {
        bool extended = (flags & NativeMethods.RI_KEY_E0) != 0;
        return FromVirtualKey(virtualKey, makeCode, extended);
    }

    public static string FromLowLevelKeyboard(uint virtualKey, uint scanCode, uint flags)
    {
        bool extended = (flags & NativeMethods.LLKHF_EXTENDED) != 0;
        return FromVirtualKey((ushort)virtualKey, (ushort)scanCode, extended);
    }

    private static string FromVirtualKey(ushort virtualKey, ushort makeCode, bool extended)
    {
        return virtualKey switch
        {
            >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
            >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
            0x10 => makeCode == 0x36 ? "RightShift" : "LeftShift",
            0x11 => extended ? "RightCtrl" : "LeftCtrl",
            0x12 => extended ? "RightAlt" : "LeftAlt",
            0x20 => "Space",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Escape",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x2D => "Insert",
            0x2E => "Delete",
            0xBA => "Semicolon",
            0xBB => "Equals",
            0xBC => "Comma",
            0xBD => "Minus",
            0xBE => "Period",
            0xBF => "Slash",
            0xC0 => "Backtick",
            0xDB => "LeftBracket",
            0xDC => "Backslash",
            0xDD => "RightBracket",
            0xDE => "Quote",
            >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
            >= 0x60 and <= 0x69 => $"NumPad{virtualKey - 0x60}",
            0x6A => "NumPadMultiply",
            0x6B => "NumPadAdd",
            0x6D => "NumPadSubtract",
            0x6E => "NumPadDecimal",
            0x6F => "NumPadDivide",
            _ => $"VK_{virtualKey:X2}"
        };
    }

    public static IReadOnlyDictionary<string, int> PollableKeys => NameToVirtualKey;

    private static IReadOnlyDictionary<string, int> CreateNameMap()
    {
        var keys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["LeftShift"] = 0xA0,
            ["RightShift"] = 0xA1,
            ["LeftCtrl"] = 0xA2,
            ["RightCtrl"] = 0xA3,
            ["LeftAlt"] = 0xA4,
            ["RightAlt"] = 0xA5,
            ["Space"] = 0x20,
            ["Left"] = 0x25,
            ["Up"] = 0x26,
            ["Right"] = 0x27,
            ["Down"] = 0x28,
            ["Backspace"] = 0x08,
            ["Tab"] = 0x09,
            ["Enter"] = 0x0D,
            ["Escape"] = 0x1B,
            ["PageUp"] = 0x21,
            ["PageDown"] = 0x22,
            ["End"] = 0x23,
            ["Home"] = 0x24,
            ["Insert"] = 0x2D,
            ["Delete"] = 0x2E,
            ["Semicolon"] = 0xBA,
            ["Equals"] = 0xBB,
            ["Comma"] = 0xBC,
            ["Minus"] = 0xBD,
            ["Period"] = 0xBE,
            ["Slash"] = 0xBF,
            ["Backtick"] = 0xC0,
            ["LeftBracket"] = 0xDB,
            ["Backslash"] = 0xDC,
            ["RightBracket"] = 0xDD,
            ["Quote"] = 0xDE
        };

        for (int key = 0x41; key <= 0x5A; key++)
        {
            keys[((char)key).ToString()] = key;
        }

        for (int key = 0x30; key <= 0x39; key++)
        {
            keys[((char)key).ToString()] = key;
        }

        for (int key = 0x70; key <= 0x87; key++)
        {
            keys[$"F{key - 0x6F}"] = key;
        }

        for (int key = 0x60; key <= 0x69; key++)
        {
            keys[$"NumPad{key - 0x60}"] = key;
        }

        keys["NumPadMultiply"] = 0x6A;
        keys["NumPadAdd"] = 0x6B;
        keys["NumPadSubtract"] = 0x6D;
        keys["NumPadDecimal"] = 0x6E;
        keys["NumPadDivide"] = 0x6F;

        return keys;
    }
}
