using System.Text.Json.Serialization;

namespace SCOverlay.Core.Domain;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StickWidgetDefinition), "stick")]
[JsonDerivedType(typeof(ThrottleWidgetDefinition), "throttle")]
[JsonDerivedType(typeof(RollWidgetDefinition), "roll")]
[JsonDerivedType(typeof(StateTextWidgetDefinition), "stateText")]
public abstract record WidgetDefinition
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public double Scale { get; init; } = 1.0;

    public double Opacity { get; init; } = 1.0;

    public double LineThickness { get; init; } = 3.0;

    public RgbaColor RingColor { get; init; } = new(255, 255, 255, 255);

    public RgbaColor ActiveColor { get; init; } = new(255, 0, 0, 255);

    public EffectSettings VisualEffects { get; init; } = new();

    public EffectSettings TextEffects { get; init; } = new();
}

public sealed record StickWidgetDefinition : WidgetDefinition
{
    public double Size { get; init; } = 220.0;

    public string XSourceId { get; init; } = string.Empty;

    public string YSourceId { get; init; } = string.Empty;

    public DirectionLabels Labels { get; init; } = new();

    public StickTuning Tuning { get; init; } = new();
}

public sealed record ThrottleWidgetDefinition : WidgetDefinition
{
    public double Width { get; init; } = 45.0;

    public double Height { get; init; } = 130.0;

    public double CornerRadius { get; init; } = 8.0;

    public string SourceId { get; init; } = string.Empty;

    public VerticalLabels Labels { get; init; } = new();

    public AxisTuning Tuning { get; init; } = new();
}

public sealed record RollWidgetDefinition : WidgetDefinition
{
    public double Width { get; init; } = 162.5;

    public double Height { get; init; } = 112.5;

    public string SourceId { get; init; } = string.Empty;

    public string AssetId { get; init; } = RollAssets.Gladius;

    public RollRenderMode RenderMode { get; init; } = RollRenderMode.Image;

    public double MaxRotationDegrees { get; init; } = 60.0;

    public AxisTuning Tuning { get; init; } = new()
    {
        ValueSmoothingSpeed = 98.0
    };
}

public enum RollRenderMode
{
    Image,
    Indicator
}

public static class RollAssets
{
    public const string Indicator = "roll-indicator-default";

    public const string Gladius = "roll-indicator-gladius";

    public const string Arrow = "roll-indicator-arrow";

    public static bool IsKnown(string assetId)
    {
        return string.Equals(assetId, Indicator, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assetId, Gladius, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assetId, Arrow, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record StateTextWidgetDefinition : WidgetDefinition
{
    public string Text { get; init; } = string.Empty;

    public string SourceId { get; init; } = string.Empty;

    public InputSourceKind SourceKind { get; init; } = InputSourceKind.Button;

    public int FontSizeOff { get; init; } = 34;

    public int FontSizeOn { get; init; } = 48;

    public StateTextTuning Tuning { get; init; } = new();
}

public sealed record DirectionLabels
{
    public bool Enabled { get; init; } = true;

    public string Up { get; init; } = string.Empty;

    public string Down { get; init; } = string.Empty;

    public string Left { get; init; } = string.Empty;

    public string Right { get; init; } = string.Empty;
}

public sealed record VerticalLabels
{
    public bool Enabled { get; init; } = true;

    public string Top { get; init; } = string.Empty;

    public string Bottom { get; init; } = string.Empty;
}
