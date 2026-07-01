namespace SCOverlay.Core.Profiles;

public sealed record RuntimeSettings
{
    public int TargetHz { get; init; } = 144;

    public bool BrowserSourceEnabled { get; init; } = true;

    public string BrowserSourceHost { get; init; } = "127.0.0.1";

    public int BrowserSourcePort { get; init; } = 8765;
}
