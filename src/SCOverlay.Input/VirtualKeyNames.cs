namespace SCOverlay.Input;

internal static class VirtualKeyNames
{
    public static string FromRawKeyboard(ushort virtualKey, ushort makeCode, ushort flags)
    {
        bool extended = (flags & NativeMethods.RI_KEY_E0) != 0;

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
}
