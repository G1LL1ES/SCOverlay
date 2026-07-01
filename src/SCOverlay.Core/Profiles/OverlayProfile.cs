using SCOverlay.Core.Application;
using SCOverlay.Core.Domain;

namespace SCOverlay.Core.Profiles;

public sealed record OverlayProfile
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public int SchemaVersion { get; init; } = AppInfo.CurrentProfileSchemaVersion;

    public RuntimeSettings Runtime { get; init; } = new();

    public IReadOnlyList<InputSource> InputSources { get; init; } = Array.Empty<InputSource>();

    public IReadOnlyList<WidgetDefinition> Widgets { get; init; } = Array.Empty<WidgetDefinition>();

    public static OverlayProfile CreateFoundationDefault()
    {
        return DefaultProfiles.CreateKbmDefault();
    }
}
