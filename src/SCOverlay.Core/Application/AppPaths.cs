namespace SCOverlay.Core.Application;

public sealed record AppPaths(
    string DataRoot,
    string ProfilesDirectory,
    string LogsDirectory,
    string AssetsDirectory,
    string ProfileBackupsDirectory);

public static class AppPathProvider
{
    public static AppPaths Create()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dataRoot = Path.Combine(appData, AppInfo.AppDataFolderName);

        return new AppPaths(
            DataRoot: dataRoot,
            ProfilesDirectory: Path.Combine(dataRoot, "profiles"),
            LogsDirectory: Path.Combine(dataRoot, "logs"),
            AssetsDirectory: Path.Combine(dataRoot, "assets"),
            ProfileBackupsDirectory: Path.Combine(dataRoot, "profile-backups"));
    }

    public static void EnsureCreated(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        Directory.CreateDirectory(paths.DataRoot);
        Directory.CreateDirectory(paths.ProfilesDirectory);
        Directory.CreateDirectory(paths.LogsDirectory);
        Directory.CreateDirectory(paths.AssetsDirectory);
        Directory.CreateDirectory(paths.ProfileBackupsDirectory);
    }
}
