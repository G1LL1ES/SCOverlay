using SCOverlay.Core.Input;

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
}
