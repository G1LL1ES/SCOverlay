namespace SCOverlay.Core.Domain;

public sealed record AxisComponent
{
    public string SourceId { get; init; } = string.Empty;

    public InputSourceKind SourceKind { get; init; } = InputSourceKind.Axis;

    public AxisRegion Region { get; init; } = AxisRegion.Full;

    public bool Invert { get; init; }

    public double Scale { get; init; } = 1.0;
}
