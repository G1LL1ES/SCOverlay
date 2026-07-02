using System.Text.Json;
using SCOverlay.Core.Application;

namespace SCOverlay.Core.Profiles;

public sealed class FileProfileStore : IProfileStore
{
    private readonly string profilesDirectory;
    private readonly JsonSerializerOptions jsonOptions;

    public FileProfileStore(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        AppPathProvider.EnsureCreated(paths);
        profilesDirectory = paths.ProfilesDirectory;
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
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, profile, jsonOptions, cancellationToken)
            .ConfigureAwait(false);
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
}
