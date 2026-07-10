using System.Security.Cryptography;
using System.Text;

namespace SCOverlay.Core.Input;

public static class InputDeviceIdentity
{
    public static string CreateStableHidIdentity(
        uint vendorId,
        uint productId,
        string displayName,
        string devicePath,
        int ordinal)
    {
        string nameSlug = Slug(displayName);
        string pathHash = string.IsNullOrWhiteSpace(devicePath)
            ? $"ordinal_{Math.Max(ordinal, 0)}"
            : Hash8(devicePath);

        return $"hid:vid_{vendorId:X4}&pid_{productId:X4}:{nameSlug}:{pathHash}";
    }

    public static string CreateStableWinMmIdentity(uint ordinal, string displayName)
    {
        return $"winmm:{Slug(displayName)}:ordinal_{ordinal}";
    }

    public static string Slug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        char[] chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray();

        string slug = new(chars);
        while (slug.Contains("__", StringComparison.Ordinal))
        {
            slug = slug.Replace("__", "_", StringComparison.Ordinal);
        }

        return slug.Trim('_') is { Length: > 0 } trimmed ? trimmed : "unknown";
    }

    private static string Hash8(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }
}
