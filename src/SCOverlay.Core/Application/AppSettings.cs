namespace SCOverlay.Core.Application;

public sealed record AppSettings
{
    public string ActiveProfileId { get; init; } = "kbm-default";

    public DesktopOverlaySettings DesktopOverlay { get; init; } = new();

    public bool AutomaticUpdateChecksEnabled { get; init; } = true;

    public DateTimeOffset? LastUpdateCheckUtc { get; init; }

    public string? DismissedUpdateVersion { get; init; }
}

public sealed record DesktopOverlaySettings
{
    public bool IsVisible { get; init; }

    public bool IsLocked { get; init; } = true;

    public bool IsClickThrough { get; init; } = true;

    public double Left { get; init; } = 120;

    public double Top { get; init; } = 120;

    public double Width { get; init; } = 900;

    public double Height { get; init; } = 520;
}
