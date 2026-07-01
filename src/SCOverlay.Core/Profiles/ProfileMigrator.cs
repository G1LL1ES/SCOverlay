using SCOverlay.Core.Application;

namespace SCOverlay.Core.Profiles;

public static class ProfileMigrator
{
    public static OverlayProfile Migrate(OverlayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.SchemaVersion <= 0)
        {
            return profile with
            {
                SchemaVersion = AppInfo.CurrentProfileSchemaVersion
            };
        }

        return profile;
    }
}
