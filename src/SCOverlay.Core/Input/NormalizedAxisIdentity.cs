namespace SCOverlay.Core.Input;

public sealed record NormalizedAxisIdentity(
    string DeviceId,
    int AxisIndex,
    string AxisName,
    uint? VendorId = null,
    uint? ProductId = null,
    AxisMatchConfidence Confidence = AxisMatchConfidence.High);

public enum AxisMatchConfidence
{
    Low,
    Medium,
    High
}

public interface IAxisTransformProvider
{
    double Transform(JoystickAxisTransformContext context, double rawValue);
}

public sealed record JoystickAxisTransformContext(
    string DeviceId,
    int AxisIndex,
    NormalizedAxisIdentity? Identity);
