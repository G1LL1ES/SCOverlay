using SCOverlay.Core.Application;

namespace SCOverlay.Core.Diagnostics;

public sealed class AppLog
{
    private const int SessionLogLimit = 20;
    private readonly object gate = new();
    private readonly string logPath;
    private readonly string logDirectory;

    public AppLog(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        AppPathProvider.EnsureCreated(paths);
        logDirectory = paths.LogsDirectory;
        logPath = Path.Combine(logDirectory, $"sc-overlay-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
        File.AppendAllText(logPath, string.Empty);
        PruneLogs();
    }

    public string LogPath => logPath;

    public string LogDirectory => logDirectory;

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception exception) => Write("ERROR", $"{message}{Environment.NewLine}{exception}");

    public void WriteSessionHeader(AppPaths paths, string activeProfileId, string obsUrl, string inputProviderName)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Info(
            $"Session start: {AppInfo.ProductName} {AppInfo.Version}; " +
            $"OS={Environment.OSVersion}; Runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; " +
            $"ProcessId={Environment.ProcessId}; DataRoot={paths.DataRoot}; ActiveProfile={activeProfileId}; " +
            $"OBS={obsUrl}; InputProvider={inputProviderName}");
    }

    public IReadOnlyList<string> RecentLines(int maxLines)
    {
        if (maxLines <= 0 || !File.Exists(logPath))
        {
            return Array.Empty<string>();
        }

        lock (gate)
        {
            return File.ReadLines(logPath).TakeLast(maxLines).ToArray();
        }
    }

    private void Write(string level, string message)
    {
        string line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
        lock (gate)
        {
            File.AppendAllText(logPath, line);
        }
    }

    private void PruneLogs()
    {
        FileInfo[] logs = Directory
            .EnumerateFiles(logDirectory, "sc-overlay-*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        foreach (FileInfo log in logs.Skip(SessionLogLimit))
        {
            log.Delete();
        }
    }
}
