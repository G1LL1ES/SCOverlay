using System.Text.Json;
using SCOverlay.Core.Application;
using SCOverlay.Core.Input;

namespace SCOverlay.Core.Diagnostics;

public sealed record DiagnosticsReport(
    DateTimeOffset GeneratedAt,
    string AppName,
    string AppVersion,
    string DataRoot,
    string ActiveProfileId,
    string ActiveProfileName,
    string ObsUrl,
    string InputProvider,
    AppSettings Settings,
    IReadOnlyList<InputDeviceInfo> Devices,
    InputSnapshot RawSnapshot,
    EvaluatedInputState EvaluatedInput,
    IReadOnlyList<string> RecentLogLines);

public static class DiagnosticsReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string CreateJson(DiagnosticsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions);
    }
}
