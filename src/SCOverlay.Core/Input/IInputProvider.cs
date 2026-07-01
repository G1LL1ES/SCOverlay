namespace SCOverlay.Core.Input;

public interface IInputProvider
{
    string Name { get; }

    ValueTask<IReadOnlyList<InputDeviceInfo>> EnumerateDevicesAsync(CancellationToken cancellationToken = default);

    InputSnapshot Poll();

    ValueTask<InputCaptureResult> CaptureNextBindingAsync(CancellationToken cancellationToken = default);
}
