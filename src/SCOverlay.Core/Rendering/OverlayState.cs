namespace SCOverlay.Core.Rendering;

public sealed record OverlayState(
    DateTimeOffset Timestamp,
    string ProfileId,
    IReadOnlyList<WidgetState> Widgets)
{
    public static OverlayState Empty(string profileId)
    {
        return new OverlayState(DateTimeOffset.UtcNow, profileId, Array.Empty<WidgetState>());
    }
}

public sealed record WidgetState(
    string Name,
    string WidgetType,
    bool Connected);
