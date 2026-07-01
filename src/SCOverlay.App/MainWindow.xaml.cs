using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SCOverlay.BrowserSource;
using SCOverlay.Core.Application;
using SCOverlay.Core.Diagnostics;
using SCOverlay.Core.Input;
using SCOverlay.Core.Profiles;
using SCOverlay.Input;

namespace SCOverlay.App;

public partial class MainWindow : Window
{
    private readonly AppLog log;
    private readonly OverlayProfile profile;
    private readonly WindowsInputProvider inputProvider;
    private readonly BrowserSourceServer browserSourceServer;
    private readonly DispatcherTimer inputTimer;
    private CancellationTokenSource? captureCancellation;

    public MainWindow(AppPaths paths, AppLog log)
    {
        ArgumentNullException.ThrowIfNull(paths);
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        profile = OverlayProfile.CreateFoundationDefault();
        inputProvider = new WindowsInputProvider();
        browserSourceServer = new BrowserSourceServer(profile.Runtime);
        inputTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        inputTimer.Tick += OnInputTimerTick;

        InitializeComponent();

        StatusText.Text = "Raw Input is attached for keyboard, mouse, and HID flight devices. HID reports are parsed into declared axes, buttons, and hats; WinMM remains as a legacy fallback.";
        DataRootText.Text = $"Runtime data: {paths.DataRoot}";
        CaptureText.Text = "Click capture, then press a key or mouse button.";
        this.log.Info($"Input diagnostics initialized with {inputProvider.Name}. OBS placeholder URL: {browserSourceServer.Url}");

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private async void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            inputProvider.AttachWindow(source.Handle);
            source.AddHook(WindowMessageHook);
            await RefreshDevicesAsync();
            inputTimer.Start();
        }
        catch (Exception exception)
        {
            log.Error("Failed to initialize Windows input diagnostics.", exception);
            StatusText.Text = $"Input initialization failed: {exception.Message}";
        }
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WindowsInputProvider.RawInputWindowMessage)
        {
            try
            {
                inputProvider.ProcessWindowMessage(lParam);
            }
            catch (Exception exception)
            {
                log.Error("Raw input message processing failed.", exception);
                StatusText.Text = $"Raw input processing failed: {exception.Message}";
            }
        }

        return IntPtr.Zero;
    }

    private async Task RefreshDevicesAsync()
    {
        IReadOnlyList<InputDeviceInfo> devices = await inputProvider.EnumerateDevicesAsync();
        DevicesText.Text = string.Join(
            Environment.NewLine,
            devices.Select(FormatDevice));
    }

    private void OnInputTimerTick(object? sender, EventArgs e)
    {
        try
        {
            InputSnapshot snapshot = inputProvider.Poll();
            EvaluatedInputState evaluated = InputSourceEvaluator.Evaluate(profile.InputSources, snapshot);

            RawSnapshotText.Text = FormatRawSnapshot(snapshot);
            ProfileValuesText.Text = FormatProfileValues(evaluated);
        }
        catch (Exception exception)
        {
            inputTimer.Stop();
            log.Error("Input diagnostics update failed.", exception);
            StatusText.Text = $"Input diagnostics update failed: {exception.Message}";
        }
    }

    private async void CaptureButton_OnClick(object sender, RoutedEventArgs e)
    {
        captureCancellation?.Cancel();
        captureCancellation?.Dispose();
        captureCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CaptureText.Text = "Listening...";

        try
        {
            InputCaptureResult result = await inputProvider.CaptureNextBindingAsync(captureCancellation.Token);
            CaptureText.Text = result.DisplayText;
        }
        catch (OperationCanceledException)
        {
            CaptureText.Text = "Capture timed out.";
        }
        catch (Exception exception)
        {
            log.Error("Binding capture failed.", exception);
            CaptureText.Text = $"Capture failed: {exception.Message}";
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        inputTimer.Stop();
        captureCancellation?.Cancel();
        captureCancellation?.Dispose();
    }

    private static string FormatRawSnapshot(InputSnapshot snapshot)
    {
        string[] pressed = snapshot.Buttons
            .Where(pair => pair.Value)
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .Take(12)
            .ToArray();

        string[] axes = snapshot.Axes
            .Where(pair => Math.Abs(pair.Value) > 0.02)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(12)
            .Select(pair => $"{pair.Key}={pair.Value:0.00}")
            .ToArray();

        return $"pressed: {(pressed.Length == 0 ? "(none)" : string.Join(", ", pressed))}{Environment.NewLine}" +
               $"axes: {(axes.Length == 0 ? "(idle)" : string.Join(", ", axes))}";
    }

    private static string FormatDevice(InputDeviceInfo device)
    {
        string counts = $"axes:{device.AxisCount} buttons:{device.ButtonCount} hats:{device.HatCount}";

        string details = string.IsNullOrWhiteSpace(device.Details)
            ? string.Empty
            : $"  {device.Details}";

        return $"{device.DeviceId}  {device.DisplayName}  {counts}{details}";
    }

    private static string FormatProfileValues(EvaluatedInputState evaluated)
    {
        string axisText = string.Join(
            Environment.NewLine,
            evaluated.Axes
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key,-14} {pair.Value,5:0.00}"));

        string buttonText = string.Join(
            Environment.NewLine,
            evaluated.Buttons
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key,-14} {(pair.Value ? "ON" : "off")}"));

        return $"axes{Environment.NewLine}{axisText}{Environment.NewLine}{Environment.NewLine}buttons{Environment.NewLine}{buttonText}";
    }
}
