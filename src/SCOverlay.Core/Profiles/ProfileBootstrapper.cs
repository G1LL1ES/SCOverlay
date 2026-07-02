namespace SCOverlay.Core.Profiles;

public static class ProfileBootstrapper
{
    public static async ValueTask EnsureDefaultProfilesAsync(IProfileStore store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        IReadOnlyList<string> existingIds = await store.ListProfileIdsAsync(cancellationToken).ConfigureAwait(false);
        var existing = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);

        foreach (OverlayProfile profile in DefaultProfiles.CreateAll())
        {
            if (!existing.Contains(profile.Id))
            {
                await store.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!await CanLoadProfileAsync(store, profile.Id, cancellationToken).ConfigureAwait(false))
            {
                if (store is FileProfileStore fileStore)
                {
                    await fileStore.BackupExistingProfileAsync(profile.Id, "invalid", cancellationToken).ConfigureAwait(false);
                }

                await store.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask<bool> CanLoadProfileAsync(IProfileStore store, string profileId, CancellationToken cancellationToken)
    {
        try
        {
            await store.LoadAsync(profileId, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or ProfileValidationException or System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
