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

    public static bool TryParseJoystickAxis(string key, out string deviceId, out int axisIndex)
    {
        return TryParseJoystickKey(key, ":axis:", out deviceId, out axisIndex);
    }

    public static bool TryParseJoystickButton(string key, out string deviceId, out int buttonIndex)
    {
        return TryParseJoystickKey(key, ":button:", out deviceId, out buttonIndex);
    }

    public static bool DeviceIdsMatch(string configuredDeviceId, string snapshotDeviceId)
    {
        string configured = NormalizeToken(configuredDeviceId);
        string snapshot = NormalizeToken(snapshotDeviceId);
        if (string.Equals(configured, snapshot, StringComparison.Ordinal))
        {
            return true;
        }

        string? configuredHidPrefix = TryGetHidVidPidPrefix(configured);
        string? snapshotHidPrefix = TryGetHidVidPidPrefix(snapshot);
        return configuredHidPrefix is not null &&
               snapshotHidPrefix is not null &&
               string.Equals(configuredHidPrefix, snapshotHidPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeToken(string value)
    {
        return value.Trim();
    }

    private static bool TryParseJoystickKey(string key, string marker, out string deviceId, out int index)
    {
        deviceId = string.Empty;
        index = 0;

        int markerIndex = key.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex <= 0 || !int.TryParse(key[(markerIndex + marker.Length)..], out index))
        {
            return false;
        }

        deviceId = key[..markerIndex];
        return true;
    }

    private static string? TryGetHidVidPidPrefix(string deviceId)
    {
        const string prefix = "hid:vid_";
        if (!deviceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int separator = deviceId.IndexOf(':', prefix.Length);
        if (separator <= prefix.Length)
        {
            return null;
        }

        string vidPid = deviceId[..separator];
        return vidPid.Contains("&pid_", StringComparison.OrdinalIgnoreCase)
            ? $"{vidPid}:"
            : null;
    }
}
