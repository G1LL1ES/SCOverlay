using System.Text.Json;
using SCOverlay.Core.Application;

namespace SCOverlay.Core.Profiles;

public sealed class FileProfileStore : IProfileStore
{
    private const int BackupsPerProfileLimit = 10;
    private readonly string profilesDirectory;
    private readonly string profileBackupsDirectory;
    private readonly JsonSerializerOptions jsonOptions;

    public FileProfileStore(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        AppPathProvider.EnsureCreated(paths);
        profilesDirectory = paths.ProfilesDirectory;
        profileBackupsDirectory = paths.ProfileBackupsDirectory;
        jsonOptions = ProfileJsonSerializerOptions.Create();
    }

    public ValueTask<IReadOnlyList<string>> ListProfileIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> ids = Directory
            .EnumerateFiles(profilesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ValueTask.FromResult(ids);
    }

    public async ValueTask<OverlayProfile> LoadAsync(string profileId, CancellationToken cancellationToken = default)
    {
        string path = GetProfilePath(profileId);
        await using FileStream stream = File.OpenRead(path);
        OverlayProfile? profile = await JsonSerializer.DeserializeAsync<OverlayProfile>(stream, jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null)
        {
            throw new ProfileValidationException(new[]
            {
                new ProfileValidationIssue("profile", "Profile file is empty or invalid JSON.")
            });
        }

        profile = ProfileMigrator.Migrate(profile);
        ProfileValidator.ThrowIfInvalid(profile);
        return profile;
    }

    public async ValueTask SaveAsync(OverlayProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile = ProfileMigrator.Migrate(profile);
        ProfileValidator.ThrowIfInvalid(profile);

        string path = GetProfilePath(profile.Id);
        await BackupExistingProfileAsync(profile.Id, "pre-save", cancellationToken).ConfigureAwait(false);

        string tempPath = Path.Combine(profilesDirectory, $"{profile.Id}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (FileStream stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, profile, jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public async ValueTask<string?> BackupExistingProfileAsync(
        string profileId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string sourcePath = GetProfilePath(profileId);
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        Directory.CreateDirectory(profileBackupsDirectory);
        string safeReason = SanitizeBackupReason(reason);
        string backupPath = Path.Combine(
            profileBackupsDirectory,
            $"{profileId}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{safeReason}.json");

        await using (FileStream source = File.OpenRead(sourcePath))
        await using (FileStream destination = File.Create(backupPath))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        PruneBackups(profileId);
        return backupPath;
    }

    private string GetProfilePath(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile id is required.", nameof(profileId));
        }

        string fileName = $"{profileId}.json";
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"Profile id '{profileId}' is not safe for a file name.", nameof(profileId));
        }

        return Path.Combine(profilesDirectory, fileName);
    }

    private void PruneBackups(string profileId)
    {
        FileInfo[] backups = Directory
            .EnumerateFiles(profileBackupsDirectory, $"{profileId}.*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        foreach (FileInfo backup in backups.Skip(BackupsPerProfileLimit))
        {
            backup.Delete();
        }
    }

    private static string SanitizeBackupReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "backup";
        }

        char[] safe = reason
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .ToArray();

        string sanitized = new(safe);
        return string.IsNullOrWhiteSpace(sanitized) ? "backup" : sanitized;
    }
}
