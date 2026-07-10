using System.Text.Json;

namespace SCOverlay.Core.Application;

public sealed class FileAppSettingsStore
{
    private const int BackupLimit = 10;
    private readonly string settingsPath;
    private readonly string backupDirectory;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public FileAppSettingsStore(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        AppPathProvider.EnsureCreated(paths);
        settingsPath = Path.Combine(paths.DataRoot, "settings.json");
        backupDirectory = paths.SettingsBackupsDirectory;
    }

    public string? LastRecoveryMessage { get; private set; }

    public async ValueTask<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        LastRecoveryMessage = null;
        if (!File.Exists(settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            AppSettings settings = await LoadFromFileAsync(settingsPath, cancellationToken).ConfigureAwait(false);
            return settings;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            await BackupInvalidSettingsAsync(cancellationToken).ConfigureAwait(false);

            AppSettings? recovered = await TryLoadNewestBackupAsync(cancellationToken).ConfigureAwait(false);
            if (recovered is not null)
            {
                LastRecoveryMessage = "Recovered app settings from the newest valid backup.";
                return recovered;
            }

            LastRecoveryMessage = "App settings were invalid and safe defaults were restored.";
            return new AppSettings();
        }
    }

    public async ValueTask SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath) ?? string.Empty);
        Directory.CreateDirectory(backupDirectory);
        await BackupExistingSettingsAsync("pre-save", cancellationToken).ConfigureAwait(false);

        string tempPath = Path.Combine(Path.GetDirectoryName(settingsPath) ?? string.Empty, $"settings.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (FileStream stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        PruneBackups();
    }

    private async ValueTask<AppSettings> LoadFromFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        AppSettings? settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (settings is null || string.IsNullOrWhiteSpace(settings.ActiveProfileId))
        {
            throw new InvalidDataException("Settings file is empty or missing an active profile id.");
        }

        return settings;
    }

    private async ValueTask BackupExistingSettingsAsync(string reason, CancellationToken cancellationToken)
    {
        if (!File.Exists(settingsPath))
        {
            return;
        }

        string backupPath = Path.Combine(backupDirectory, $"settings.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{reason}.json");
        await CopyFileAsync(settingsPath, backupPath, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask BackupInvalidSettingsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(settingsPath))
        {
            return;
        }

        Directory.CreateDirectory(backupDirectory);
        string backupPath = Path.Combine(backupDirectory, $"settings.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.invalid.json");
        await CopyFileAsync(settingsPath, backupPath, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<AppSettings?> TryLoadNewestBackupAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return null;
        }

        foreach (string backupPath in Directory.EnumerateFiles(backupDirectory, "settings.*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                return await LoadFromFileAsync(backupPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
            {
            }
        }

        return null;
    }

    private void PruneBackups()
    {
        if (!Directory.Exists(backupDirectory))
        {
            return;
        }

        FileInfo[] backups = Directory
            .EnumerateFiles(backupDirectory, "settings.*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        foreach (FileInfo backup in backups.Skip(BackupLimit))
        {
            backup.Delete();
        }
    }

    private static async ValueTask CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using FileStream source = File.OpenRead(sourcePath);
        await using FileStream destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }
}
