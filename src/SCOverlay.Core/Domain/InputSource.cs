using System.Text.Json.Serialization;

namespace SCOverlay.Core.Domain;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeyboardKeyInputSource), "keyboardKey")]
[JsonDerivedType(typeof(MouseButtonInputSource), "mouseButton")]
[JsonDerivedType(typeof(JoystickAxisInputSource), "joystickAxis")]
[JsonDerivedType(typeof(JoystickButtonInputSource), "joystickButton")]
[JsonDerivedType(typeof(VirtualButtonAxisInputSource), "virtualButtonAxis")]
[JsonDerivedType(typeof(CompositeAxisInputSource), "compositeAxis")]
public abstract record InputSource
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public abstract InputSourceKind Kind { get; }
}

public sealed record KeyboardKeyInputSource : InputSource
{
    public override InputSourceKind Kind => InputSourceKind.Button;

    public string Key { get; init; } = string.Empty;
}

public sealed record MouseButtonInputSource : InputSource
{
    public override InputSourceKind Kind => InputSourceKind.Button;

    public string Button { get; init; } = string.Empty;
}

public sealed record JoystickAxisInputSource : InputSource
{
    public override InputSourceKind Kind => InputSourceKind.Axis;

    public string DeviceId { get; init; } = string.Empty;

    public int AxisIndex { get; init; }

    public bool Invert { get; init; }

    public double Scale { get; init; } = 1.0;
}

public sealed record JoystickButtonInputSource : InputSource
{
    public override InputSourceKind Kind => InputSourceKind.Button;

    public string DeviceId { get; init; } = string.Empty;

    public int ButtonIndex { get; init; }

    public bool Invert { get; init; }
}

public sealed record VirtualButtonAxisInputSource : InputSource
{
    public override InputSourceKind Kind => InputSourceKind.Axis;

    public string NegativeButtonSourceId { get; init; } = string.Empty;

    public string PositiveButtonSourceId { get; init; } = string.Empty;
}

public sealed record CompositeAxisInputSource : InputSource
{
    public override InputSourceKind Kind => InputSourceKind.Axis;

    public IReadOnlyList<AxisComponent> Components { get; init; } = Array.Empty<AxisComponent>();

    public bool ClampOutput { get; init; } = true;
}
