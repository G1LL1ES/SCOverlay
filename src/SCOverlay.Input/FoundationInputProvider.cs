using SCOverlay.Core.Input;
using SCOverlay.Core.Domain;

namespace SCOverlay.Input;

public sealed class FoundationInputProvider : IInputProvider
{
    public string Name => "Foundation Input Provider";

    public ValueTask<IReadOnlyList<InputDeviceInfo>> EnumerateDevicesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InputDeviceInfo> devices = Array.Empty<InputDeviceInfo>();
        return ValueTask.FromResult(devices);
    }

    public InputSnapshot Poll()
    {
        return InputSnapshot.Empty();
    }

    public ValueTask<InputCaptureResult> CaptureNextBindingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = new KeyboardKeyInputSource
        {
            Id = "unbound",
            DisplayName = "Unbound",
            Key = string.Empty
        };

        return ValueTask.FromResult(new InputCaptureResult(source, "No input provider is attached.", DateTimeOffset.UtcNow));
    }
}
