namespace SCOverlay.Core.Domain;

public sealed record StickTuning
{
    public double Deadzone { get; init; }

    public double InputNoiseGate { get; init; } = 0.022;

    public double ZeroSnapThreshold { get; init; } = 0.001;

    public double AngleSmoothingSpeed { get; init; } = 64.0;

    public double MagnitudeSmoothingSpeed { get; init; } = 90.0;

    public double MaxThrowRatio { get; init; } = 0.90;

    public double ColorRampExponent { get; init; } = 1.15;
}

public sealed record AxisTuning
{
    public double Deadzone { get; init; }

    public double InputNoiseGate { get; init; } = 0.018;

    public double ZeroSnapThreshold { get; init; } = 0.001;

    public double ValueSmoothingSpeed { get; init; } = 36.0;

    public double MaxThrowRatio { get; init; } = 0.79;

    public double ColorRampExponent { get; init; } = 1.15;
}

public sealed record StateTextTuning
{
    public double ActivationDeadzone { get; init; }

    public double RiseSpeed { get; init; } = 22.0;

    public double FallSpeed { get; init; } = 16.0;

    public double ColorRampExponent { get; init; } = 0.6;

    public bool MaxedShakeEnabled { get; init; } = true;
}
