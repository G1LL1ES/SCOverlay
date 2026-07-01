namespace SCOverlay.Core.Input;

public sealed record InputSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, double> Axes,
    IReadOnlyDictionary<string, bool> Buttons)
{
    public static InputSnapshot Empty(DateTimeOffset? timestamp = null)
    {
        return new InputSnapshot(
            timestamp ?? DateTimeOffset.UtcNow,
            new Dictionary<string, double>(),
            new Dictionary<string, bool>());
    }
}
