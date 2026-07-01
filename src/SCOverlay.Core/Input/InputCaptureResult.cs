using SCOverlay.Core.Domain;

namespace SCOverlay.Core.Input;

public sealed record InputCaptureResult(
    InputSource CapturedSource,
    string DisplayText,
    DateTimeOffset Timestamp);
