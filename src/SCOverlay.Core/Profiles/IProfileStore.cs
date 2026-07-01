namespace SCOverlay.Core.Profiles;

public interface IProfileStore
{
    ValueTask<IReadOnlyList<string>> ListProfileIdsAsync(CancellationToken cancellationToken = default);

    ValueTask<OverlayProfile> LoadAsync(string profileId, CancellationToken cancellationToken = default);

    ValueTask SaveAsync(OverlayProfile profile, CancellationToken cancellationToken = default);
}
