namespace SCOverlay.Core.Application;

public sealed record AppSettings
{
    public string ActiveProfileId { get; init; } = "kbm-default";
}
