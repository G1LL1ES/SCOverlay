using SCOverlay.Core.Application;

namespace SCOverlay.Core.Diagnostics;

public sealed class AppLog
{
    private readonly object gate = new();
    private readonly string logPath;

    public AppLog(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        AppPathProvider.EnsureCreated(paths);
        logPath = Path.Combine(paths.LogsDirectory, "sc-overlay.log");
    }

    public string LogPath => logPath;

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception exception) => Write("ERROR", $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        string line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
        lock (gate)
        {
            File.AppendAllText(logPath, line);
        }
    }
}
