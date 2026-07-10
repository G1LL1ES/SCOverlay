namespace SCOverlay.Core.Application;

using System.Reflection;

public static class AppInfo
{
    public const string ProductName = "SC Overlay";
    public const string AppDataFolderName = "SCOverlay";
    public const int CurrentProfileSchemaVersion = 2;

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
