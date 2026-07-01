namespace SCOverlay.Core.Domain;

public sealed record EffectSettings
{
    public bool OutlineEnabled { get; init; } = true;

    public RgbaColor OutlineColor { get; init; } = new(0, 0, 0, 220);

    public double OutlineWidth { get; init; } = 2.0;

    public bool ShadowEnabled { get; init; } = true;

    public RgbaColor ShadowColor { get; init; } = new(0, 0, 0, 120);

    public double ShadowWidth { get; init; } = 3.0;

    public double ShadowOffsetX { get; init; }

    public double ShadowOffsetY { get; init; }

    public bool BackplateEnabled { get; init; }

    public RgbaColor BackplateColor { get; init; } = new(0, 0, 0, 84);

    public double BackplatePadding { get; init; } = 10.0;

    public double BackplateRadius { get; init; } = 8.0;
}
