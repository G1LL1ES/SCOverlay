namespace SCOverlay.Core.Input;

public static class InputSnapshotKeys
{
    public static string KeyboardButton(string key)
    {
        return $"keyboard:{NormalizeToken(key)}";
    }

    public static string MouseButton(string button)
    {
        return $"mouse:{NormalizeToken(button)}";
    }

    public static string JoystickAxis(string deviceId, int axisIndex)
    {
        return $"{NormalizeToken(deviceId)}:axis:{axisIndex}";
    }

    public static string JoystickButton(string deviceId, int buttonIndex)
    {
        return $"{NormalizeToken(deviceId)}:button:{buttonIndex}";
    }

    public static string JoystickHat(string deviceId, int hatIndex)
    {
        return $"{NormalizeToken(deviceId)}:hat:{hatIndex}";
    }

    private static string NormalizeToken(string value)
    {
        return value.Trim();
    }
}
