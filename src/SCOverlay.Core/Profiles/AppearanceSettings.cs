using SCOverlay.Core.Domain;

namespace SCOverlay.Core.Profiles;

public sealed record AppearanceSettings
{
    public string PresetId { get; init; } = "clean-hud";

    public RgbaColor RingColor { get; init; } = new(228, 241, 255, 235);

    public RgbaColor ActiveColor { get; init; } = new(255, 84, 84, 255);

    public double Opacity { get; init; } = 1.0;

    public double WidgetScale { get; init; } = 1.0;
}
