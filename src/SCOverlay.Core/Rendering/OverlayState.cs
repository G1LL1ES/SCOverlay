using System.Text.Json.Serialization;
using SCOverlay.Core.Domain;

namespace SCOverlay.Core.Rendering;

public sealed record OverlayState(
    DateTimeOffset Timestamp,
    string ProfileId,
    bool Connected,
    IReadOnlyList<WidgetState> Widgets)
{
    public static OverlayState Empty(string profileId)
    {
        return new OverlayState(DateTimeOffset.UtcNow, profileId, false, Array.Empty<WidgetState>());
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StickWidgetState), "stick")]
[JsonDerivedType(typeof(ThrottleWidgetState), "throttle")]
[JsonDerivedType(typeof(RollWidgetState), "roll")]
[JsonDerivedType(typeof(StateTextWidgetState), "stateText")]
public abstract record WidgetState
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public double Opacity { get; init; } = 1.0;

    public double LineThickness { get; init; } = 3.0;

    public bool Connected { get; init; }

    public double Activity { get; init; }

    public RgbaColor RingColor { get; init; }

    public RgbaColor ActiveColor { get; init; }

    public RgbaColor DisplayColor { get; init; }

    public RgbaColor FrameColor { get; init; }

    public RgbaColor FrameActiveColor { get; init; }

    public RgbaColor FrameDisplayColor { get; init; }

    public EffectSettings VisualEffects { get; init; } = new();

    public EffectSettings TextEffects { get; init; } = new();
}

public sealed record StickWidgetState : WidgetState
{
    public double Size { get; init; }

    public double RawX { get; init; }

    public double RawY { get; init; }

    public double XValue { get; init; }

    public double YValue { get; init; }

    public double Magnitude { get; init; }

    public double AngleDegrees { get; init; }

    public DirectionLabels Labels { get; init; } = new();
}

public sealed record ThrottleWidgetState : WidgetState
{
    public double Width { get; init; }

    public double Height { get; init; }

    public double CornerRadius { get; init; }

    public double RawValue { get; init; }

    public double Value { get; init; }

    public double FillRatio { get; init; }

    public VerticalLabels Labels { get; init; } = new();
}

public sealed record RollWidgetState : WidgetState
{
    public double Width { get; init; }

    public double Height { get; init; }

    public string AssetId { get; init; } = string.Empty;

    public RollRenderMode RenderMode { get; init; } = RollRenderMode.Image;

    public double RawValue { get; init; }

    public double Value { get; init; }

    public double RotationDegrees { get; init; }
}

public sealed record StateTextWidgetState : WidgetState
{
    public string Text { get; init; } = string.Empty;

    public double RawValue { get; init; }

    public bool Active { get; init; }

    public double Intensity { get; init; }

    public double ShakeIntensity { get; init; }

    public double FontSize { get; init; }
}
