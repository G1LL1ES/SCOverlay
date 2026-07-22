using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SCOverlay.Core.Input;

public sealed record StarCitizenAxisSettings(
    double Deadzone,
    double Saturation,
    string SourcePath,
    AxisMatchConfidence MatchConfidence);

public sealed record StarCitizenAxisTranslationStatus(
    bool Enabled,
    bool Loaded,
    string Message,
    string? ProfileName,
    DateTimeOffset? LoadedAt,
    int MatchedAxes,
    int RawFallbackAxes);

public sealed class StarCitizenAxisTranslationService : IAxisTransformProvider
{
    private static readonly Regex DeviceGuidPattern = new(
        @"\{(?<pid>[0-9A-Fa-f]{4})(?<vid>[0-9A-Fa-f]{4})-[0-9A-Fa-f-]+\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly object sync = new();
    private string actionMapsPath = string.Empty;
    private bool enabled;
    private DateTime lastWriteUtc;
    private IReadOnlyList<DeviceSettings> devices = Array.Empty<DeviceSettings>();
    private readonly Dictionary<string, string> axisDiagnostics = new(StringComparer.Ordinal);
    private StarCitizenAxisTranslationStatus status = new(false, false, "Disabled; raw input is active.", null, null, 0, 0);

    public StarCitizenAxisTranslationStatus Status
    {
        get { lock (sync) return status; }
    }

    public IReadOnlyList<string> AxisDiagnostics
    {
        get { lock (sync) return axisDiagnostics.Values.OrderBy(value => value, StringComparer.Ordinal).ToArray(); }
    }

    public static string FindActionMapsPath()
    {
        var candidates = new List<string>();
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(item => item.IsReady))
        {
            try
            {
                string root = Path.Combine(drive.RootDirectory.FullName, "RSI", "StarCitizen");
                if (!Directory.Exists(root)) continue;
                foreach (string build in Directory.EnumerateDirectories(root))
                {
                    string profiles = Path.Combine(build, "user", "client", "0", "Profiles");
                    if (!Directory.Exists(profiles)) continue;
                    candidates.AddRange(Directory.EnumerateFiles(profiles, "actionmaps.xml", SearchOption.AllDirectories));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }

        return candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? string.Empty;
    }

    public void Configure(bool isEnabled, string? path)
    {
        lock (sync)
        {
            enabled = isEnabled;
            actionMapsPath = path?.Trim() ?? string.Empty;
            lastWriteUtc = DateTime.MinValue;
            devices = Array.Empty<DeviceSettings>();
            axisDiagnostics.Clear();
            status = isEnabled
                ? new(true, false, "Waiting for a valid Star Citizen actionmaps.xml; raw fallback is active.", null, null, 0, 0)
                : new(false, false, "Disabled; raw input is active.", null, null, 0, 0);
        }

        RefreshIfChanged(force: true);
    }

    public double Transform(JoystickAxisTransformContext context, double rawValue)
    {
        if (!double.IsFinite(rawValue)) return rawValue;
        RefreshIfChanged(force: false);

        lock (sync)
        {
            if (!enabled || devices.Count == 0 || context.Identity is null ||
                string.IsNullOrWhiteSpace(context.Identity.AxisName))
            {
                NoteFallback(context, "identity or loaded configuration unavailable");
                return rawValue;
            }

            DeviceSettings[] matches = devices.Where(device => DeviceMatches(device, context.Identity)).ToArray();
            if (matches.Length != 1 || !matches[0].Axes.TryGetValue(context.Identity.AxisName, out ParsedAxisSettings? parsed))
            {
                NoteFallback(context, matches.Length > 1 ? "ambiguous device match" : "no matching configured axis");
                return rawValue;
            }

            double translated = Apply(rawValue, parsed.Deadzone, parsed.Saturation);
            string diagnosticKey = DiagnosticKey(context);
            bool wasMatched = axisDiagnostics.TryGetValue(diagnosticKey, out string? existing) && existing.Contains("matched", StringComparison.Ordinal);
            axisDiagnostics[diagnosticKey] = $"{diagnosticKey}: matched {context.Identity.AxisName} (deadzone {parsed.Deadzone:0.####}, saturation {parsed.Saturation:0.####})";
            if (!wasMatched) status = status with { MatchedAxes = status.MatchedAxes + 1 };
            return translated;
        }
    }

    public static double Apply(double rawValue, double deadzone, double saturation)
    {
        if (!double.IsFinite(rawValue) || !double.IsFinite(deadzone) || !double.IsFinite(saturation) ||
            deadzone < 0 || saturation <= deadzone || saturation > 1)
        {
            return rawValue;
        }

        double magnitude = Math.Abs(rawValue);
        if (magnitude <= deadzone) return 0;
        double translated = Math.Clamp((magnitude - deadzone) / (saturation - deadzone), 0, 1);
        return Math.CopySign(translated, rawValue);
    }

    public static IReadOnlyList<StarCitizenAxisSettings> Parse(string path)
    {
        return ParseDevices(path).SelectMany(device => device.Axes.Values.Select(axis =>
            new StarCitizenAxisSettings(axis.Deadzone, axis.Saturation, path, AxisMatchConfidence.High))).ToArray();
    }

    private void RefreshIfChanged(bool force)
    {
        string path;
        lock (sync)
        {
            if (!enabled) return;
            path = actionMapsPath;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                lock (sync) status = status with { Loaded = false, Message = "Config file not found; raw fallback is active." };
                return;
            }

            DateTime writeUtc = File.GetLastWriteTimeUtc(path);
            lock (sync) if (!force && writeUtc == lastWriteUtc) return;
            IReadOnlyList<DeviceSettings> parsed = ParseDevices(path);
            string? profileName = XDocument.Load(path).Descendants("ActionProfiles").FirstOrDefault()?.Attribute("profileName")?.Value;
            lock (sync)
            {
                devices = parsed;
                lastWriteUtc = writeUtc;
                status = new(true, true, $"Loaded {parsed.Count} device configuration(s).", profileName,
                    DateTimeOffset.UtcNow, 0, 0);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidDataException)
        {
            lock (sync) status = status with { Loaded = devices.Count > 0, Message = $"Reload failed ({exception.Message}); {(devices.Count > 0 ? "last valid settings retained" : "raw fallback is active")}." };
        }
    }

    private static IReadOnlyList<DeviceSettings> ParseDevices(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        XDocument document = XDocument.Load(stream, LoadOptions.None);
        var result = new List<DeviceSettings>();
        foreach (XElement element in document.Descendants("deviceoptions"))
        {
            string name = element.Attribute("name")?.Value?.Trim() ?? string.Empty;
            Match guid = DeviceGuidPattern.Match(name);
            uint? vid = guid.Success ? uint.Parse(guid.Groups["vid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null;
            uint? pid = guid.Success ? uint.Parse(guid.Groups["pid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null;
            string displayName = DeviceGuidPattern.Replace(name, string.Empty).Trim();
            var axes = new Dictionary<string, MutableAxis>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement option in element.Elements("option"))
            {
                string axisName = option.Attribute("input")?.Value?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(axisName)) continue;
                if (!axes.TryGetValue(axisName, out MutableAxis? axis)) axes[axisName] = axis = new MutableAxis();
                if (TryRead(option, "deadzone", out double deadzone)) axis.Deadzone = deadzone;
                if (TryRead(option, "saturation", out double saturation)) axis.Saturation = saturation;
            }

            result.Add(new DeviceSettings(displayName, vid, pid, axes.ToDictionary(
                pair => pair.Key,
                pair => new ParsedAxisSettings(pair.Value.Deadzone ?? 0, pair.Value.Saturation ?? 1),
                StringComparer.OrdinalIgnoreCase)));
        }
        return result;
    }

    private static bool TryRead(XElement element, string attribute, out double value) =>
        double.TryParse(element.Attribute(attribute)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool DeviceMatches(DeviceSettings device, NormalizedAxisIdentity identity)
    {
        if (device.VendorId.HasValue && device.ProductId.HasValue && identity.VendorId.HasValue && identity.ProductId.HasValue)
            return device.VendorId == identity.VendorId && device.ProductId == identity.ProductId;

        if (identity.Confidence == AxisMatchConfidence.Low) return false;
        HashSet<string> ignored = new(StringComparer.OrdinalIgnoreCase) { "winmm", "ordinal", "device", "virtual", "joystick", "controller" };
        string[] identityTokens = InputDeviceIdentity.Slug(identity.DeviceId).Split('_', ':')
            .Where(token => token.Length >= 3 && !ignored.Contains(token) && !int.TryParse(token, out _)).ToArray();
        string[] nameTokens = InputDeviceIdentity.Slug(device.DisplayName).Split('_')
            .Where(token => token.Length >= 3 && !ignored.Contains(token)).ToArray();
        return identityTokens.Intersect(nameTokens, StringComparer.OrdinalIgnoreCase).Any();
    }

    private void NoteFallback(JoystickAxisTransformContext context, string reason)
    {
        string key = DiagnosticKey(context);
        bool wasFallback = axisDiagnostics.TryGetValue(key, out string? existing) && existing.Contains("raw fallback", StringComparison.Ordinal);
        axisDiagnostics[key] = $"{key}: raw fallback ({reason})";
        if (!wasFallback) status = status with { RawFallbackAxes = status.RawFallbackAxes + 1 };
    }

    private static string DiagnosticKey(JoystickAxisTransformContext context) =>
        $"{context.Identity?.DeviceId ?? context.DeviceId} axis {context.Identity?.AxisName ?? context.AxisIndex.ToString(CultureInfo.InvariantCulture)}";

    private sealed class MutableAxis { public double? Deadzone { get; set; } public double? Saturation { get; set; } }
    private sealed record ParsedAxisSettings(double Deadzone, double Saturation);
    private sealed record DeviceSettings(string DisplayName, uint? VendorId, uint? ProductId, IReadOnlyDictionary<string, ParsedAxisSettings> Axes);
}
