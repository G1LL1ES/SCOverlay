using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using SCOverlay.BrowserSource;
using SCOverlay.Core.Application;
using SCOverlay.Core.Diagnostics;
using SCOverlay.Core.Domain;
using SCOverlay.Core.Input;
using SCOverlay.Core.Profiles;
using SCOverlay.Core.Rendering;
using SCOverlay.Input;

namespace SCOverlay.App;

public partial class MainWindow : Window
{
    private readonly AppPaths paths;
    private readonly AppLog log;
    private readonly IProfileStore profileStore;
    private readonly FileAppSettingsStore settingsStore;
    private readonly WindowsInputProvider inputProvider;
    private readonly BrowserSourceServer browserSourceServer;
    private readonly OverlayStateEngine stateEngine;
    private readonly DispatcherTimer inputTimer;
    private readonly JsonSerializerOptions profileJsonOptions;
    private readonly ObservableCollection<ProfileSelectionItem> profileItems = new();
    private readonly ObservableCollection<BindableSourceItem> bindableSourceItems = new();
    private AppSettings appSettings;
    private OverlayProfile profile;
    private bool isLoadingProfiles;
    private CancellationTokenSource? captureCancellation;
    private int captureSessionId;

    public MainWindow(AppPaths paths, AppLog log)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        profileStore = new FileProfileStore(paths);
        settingsStore = new FileAppSettingsStore(paths);
        profileJsonOptions = ProfileJsonSerializerOptions.Create();
        profile = LoadInitialProfile();
        appSettings = new AppSettings
        {
            ActiveProfileId = profile.Id
        };
        inputProvider = new WindowsInputProvider();
        stateEngine = new OverlayStateEngine();
        browserSourceServer = new BrowserSourceServer(profile.Runtime, OverlayState.Empty(profile.Id));
        inputTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        inputTimer.Tick += OnInputTimerTick;

        InitializeComponent();

        ProfileComboBox.ItemsSource = profileItems;
        BindingSourceComboBox.ItemsSource = bindableSourceItems;
        InputSourcesGrid.ItemsSource = bindableSourceItems;
        HeaderText.Text = "Profile setup, binding capture, OBS source, and live input diagnostics.";
        StatusText.Text = "Raw Input is attached for keyboard, mouse, and HID flight devices. HID reports are parsed into declared axes, buttons, and hats; WinMM remains as a legacy fallback.";
        ObsUrlText.Text = $"OBS browser source:{Environment.NewLine}{browserSourceServer.Url}";
        NewProfileNameTextBox.Text = $"{profile.Name} Copy";
        FooterStatusText.Text = $"Runtime data: {paths.DataRoot}";
        this.log.Info($"SC Overlay initialized with {inputProvider.Name}. Active profile: {profile.Id}. OBS URL: {browserSourceServer.Url}");

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        Loaded += OnLoaded;
    }

    private OverlayProfile LoadInitialProfile()
    {
        try
        {
            ProfileBootstrapper.EnsureDefaultProfilesAsync(profileStore).AsTask().GetAwaiter().GetResult();
            AppSettings loadedSettings = settingsStore.LoadAsync().AsTask().GetAwaiter().GetResult();
            IReadOnlyList<string> ids = profileStore.ListProfileIdsAsync().AsTask().GetAwaiter().GetResult();
            string profileId = ids.Contains(loadedSettings.ActiveProfileId, StringComparer.OrdinalIgnoreCase)
                ? loadedSettings.ActiveProfileId
                : ids.FirstOrDefault() ?? DefaultProfiles.CreateKbmDefault().Id;
            return profileStore.LoadAsync(profileId).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            log.Error("Failed to load persisted profile. Falling back to KBM default.", exception);
            return DefaultProfiles.CreateKbmDefault();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshProfilesAsync(profile.Id);
        RefreshBindingUi();
        ProfileStatusText.Text = $"Active: {profile.Name}";
    }

    private async void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            inputProvider.AttachWindow(source.Handle);
            source.AddHook(WindowMessageHook);
            if (profile.Runtime.BrowserSourceEnabled)
            {
                browserSourceServer.Start();
            }

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

    private async Task RefreshProfilesAsync(string? selectedProfileId = null)
    {
        isLoadingProfiles = true;
        try
        {
            profileItems.Clear();
            IReadOnlyList<string> ids = await profileStore.ListProfileIdsAsync();
            foreach (string id in ids)
            {
                OverlayProfile listedProfile = await profileStore.LoadAsync(id);
                profileItems.Add(new ProfileSelectionItem(listedProfile.Id, listedProfile.Name));
            }

            string idToSelect = selectedProfileId ?? profile.Id;
            ProfileSelectionItem? selected = profileItems.FirstOrDefault(item =>
                string.Equals(item.Id, idToSelect, StringComparison.OrdinalIgnoreCase));
            ProfileComboBox.SelectedItem = selected ?? profileItems.FirstOrDefault();
        }
        finally
        {
            isLoadingProfiles = false;
        }
    }

    private async Task RefreshDevicesAsync()
    {
        IReadOnlyList<InputDeviceInfo> devices = await inputProvider.EnumerateDevicesAsync();
        DevicesText.Text = string.Join(
            Environment.NewLine,
            devices.Select(FormatDevice));
    }

    private void RefreshBindingUi(string? selectedSourceId = null)
    {
        bindableSourceItems.Clear();
        foreach (InputSource source in profile.InputSources.Where(IsBindableActionSource))
        {
            bindableSourceItems.Add(new BindableSourceItem(
                source.Id,
                source.DisplayName,
                source.Kind,
                DetermineCaptureKind(profile, source.Id, source.Kind),
                FormatInputSource(source)));
        }

        BindableSourceItem? selected = bindableSourceItems.FirstOrDefault(item =>
            string.Equals(item.Id, selectedSourceId, StringComparison.OrdinalIgnoreCase));
        BindingSourceComboBox.SelectedItem = selected ?? bindableSourceItems.FirstOrDefault();
        UpdateSelectedBindingText();
    }

    private void OnInputTimerTick(object? sender, EventArgs e)
    {
        try
        {
            InputSnapshot snapshot = inputProvider.Poll();
            EvaluatedInputState evaluated = InputSourceEvaluator.Evaluate(profile.InputSources, snapshot);
            OverlayState overlayState = stateEngine.BuildState(profile, snapshot);
            browserSourceServer.UpdateState(overlayState);

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

    private async void ProfileComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoadingProfiles || ProfileComboBox.SelectedItem is not ProfileSelectionItem item)
        {
            return;
        }

        try
        {
            CancelActiveCapture();
            profile = await profileStore.LoadAsync(item.Id);
            appSettings = appSettings with
            {
                ActiveProfileId = profile.Id
            };
            await settingsStore.SaveAsync(appSettings);
            stateEngine.Reset();
            RefreshBindingUi();
            NewProfileNameTextBox.Text = $"{profile.Name} Copy";
            ProfileStatusText.Text = $"Active: {profile.Name}";
            FooterStatusText.Text = $"Applied profile '{profile.Name}'.";
            log.Info($"Active profile changed to {profile.Id}.");
        }
        catch (Exception exception)
        {
            log.Error("Failed to apply selected profile.", exception);
            FooterStatusText.Text = $"Could not apply profile: {exception.Message}";
        }
    }

    private async void NewProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            CancelActiveCapture();
            IReadOnlyList<string> ids = await profileStore.ListProfileIdsAsync();
            string name = string.IsNullOrWhiteSpace(NewProfileNameTextBox.Text)
                ? $"{profile.Name} Copy"
                : NewProfileNameTextBox.Text.Trim();
            string id = ProfileEditor.CreateSafeProfileId(name, ids);
            profile = ProfileEditor.CreateCopy(profile, id, name);
            await SaveAndActivateProfileAsync();
            await RefreshProfilesAsync(profile.Id);
            RefreshBindingUi();
            FooterStatusText.Text = $"Created profile '{profile.Name}'.";
        }
        catch (Exception exception)
        {
            log.Error("Failed to create profile copy.", exception);
            FooterStatusText.Text = $"Could not create profile: {exception.Message}";
        }
    }

    private async void SaveProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await SaveAndActivateProfileAsync();
            await RefreshProfilesAsync(profile.Id);
            FooterStatusText.Text = $"Saved profile '{profile.Name}'.";
        }
        catch (Exception exception)
        {
            log.Error("Failed to save profile.", exception);
            FooterStatusText.Text = $"Could not save profile: {exception.Message}";
        }
    }

    private async void ImportProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "SC Overlay profile (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import SC Overlay Profile"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            CancelActiveCapture();
            await using FileStream stream = File.OpenRead(dialog.FileName);
            OverlayProfile? imported = await JsonSerializer.DeserializeAsync<OverlayProfile>(stream, profileJsonOptions);
            if (imported is null)
            {
                throw new InvalidOperationException("Profile file is empty or invalid JSON.");
            }

            imported = ProfileMigrator.Migrate(imported);
            ProfileValidator.ThrowIfInvalid(imported);
            IReadOnlyList<string> ids = await profileStore.ListProfileIdsAsync();
            if (ids.Contains(imported.Id, StringComparer.OrdinalIgnoreCase))
            {
                imported = ProfileEditor.CreateCopy(
                    imported,
                    ProfileEditor.CreateSafeProfileId(imported.Name, ids),
                    imported.Name);
            }

            profile = imported;
            await SaveAndActivateProfileAsync();
            await RefreshProfilesAsync(profile.Id);
            RefreshBindingUi();
            FooterStatusText.Text = $"Imported profile '{profile.Name}'.";
        }
        catch (Exception exception)
        {
            log.Error("Failed to import profile.", exception);
            FooterStatusText.Text = $"Could not import profile: {exception.Message}";
        }
    }

    private async void ExportProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "SC Overlay profile (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"{profile.Id}.json",
            Title = "Export SC Overlay Profile"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await using FileStream stream = File.Create(dialog.FileName);
            await JsonSerializer.SerializeAsync(stream, profile, profileJsonOptions);
            FooterStatusText.Text = $"Exported profile '{profile.Name}'.";
        }
        catch (Exception exception)
        {
            log.Error("Failed to export profile.", exception);
            FooterStatusText.Text = $"Could not export profile: {exception.Message}";
        }
    }

    private async void RefreshDevicesButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshDevicesAsync();
            FooterStatusText.Text = "Device list refreshed.";
        }
        catch (Exception exception)
        {
            log.Error("Failed to refresh devices.", exception);
            FooterStatusText.Text = $"Could not refresh devices: {exception.Message}";
        }
    }

    private void BindingSourceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedBindingText();
    }

    private async void CaptureSelectedBindingButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (BindingSourceComboBox.SelectedItem is not BindableSourceItem item)
        {
            FooterStatusText.Text = "Select an input source before capturing.";
            return;
        }

        captureCancellation?.Cancel();
        captureCancellation?.Dispose();
        int sessionId = Interlocked.Increment(ref captureSessionId);
        captureCancellation = new CancellationTokenSource();
        captureCancellation.CancelAfter(TimeSpan.FromSeconds(10));
        CaptureSelectedBindingButton.IsEnabled = false;
        FooterStatusText.Text = item.CaptureKind is null
            ? $"Listening for button input or axis movement for '{item.DisplayName}'..."
            : item.CaptureKind == InputSourceKind.Axis
            ? $"Listening for axis movement for '{item.DisplayName}'..."
            : $"Listening for button input for '{item.DisplayName}'...";

        try
        {
            InputCaptureResult result = await inputProvider.CaptureNextBindingAsync(item.CaptureKind, captureCancellation.Token);
            if (!IsCurrentCaptureSession(sessionId))
            {
                return;
            }

            profile = ProfileEditor.ReplaceInputSource(profile, item.Id, result.CapturedSource);
            await SaveAndActivateProfileAsync();
            if (!IsCurrentCaptureSession(sessionId))
            {
                return;
            }

            RefreshBindingUi(item.Id);
            FooterStatusText.Text = $"Bound '{item.DisplayName}' to {result.DisplayText}.";
            log.Info($"Bound '{item.Id}' to {result.DisplayText} in profile {profile.Id}.");
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentCaptureSession(sessionId))
            {
                FooterStatusText.Text = "Capture timed out.";
                log.Info($"Binding capture timed out for '{item.Id}' in profile {profile.Id}.");
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentCaptureSession(sessionId))
            {
                log.Error("Binding capture failed.", exception);
                FooterStatusText.Text = $"Capture failed: {exception.Message}";
            }
        }
        finally
        {
            if (IsCurrentCaptureSession(sessionId))
            {
                CaptureSelectedBindingButton.IsEnabled = true;
                captureCancellation?.Dispose();
                captureCancellation = null;
            }
        }
    }

    private async Task SaveAndActivateProfileAsync()
    {
        ProfileValidator.ThrowIfInvalid(profile);
        await profileStore.SaveAsync(profile);
        appSettings = appSettings with
        {
            ActiveProfileId = profile.Id
        };
        await settingsStore.SaveAsync(appSettings);
        ProfileStatusText.Text = $"Active: {profile.Name}";
    }

    private void UpdateSelectedBindingText()
    {
        if (BindingSourceComboBox.SelectedItem is BindableSourceItem item)
        {
            SelectedBindingText.Text = item.BindingText;
        }
        else
        {
            SelectedBindingText.Text = "(none)";
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref captureSessionId);
        inputTimer.Stop();
        browserSourceServer.Dispose();
        captureCancellation?.Cancel();
        captureCancellation?.Dispose();
        Application.Current.Shutdown();
    }

    private bool IsCurrentCaptureSession(int sessionId)
    {
        return Volatile.Read(ref captureSessionId) == sessionId;
    }

    private void CancelActiveCapture()
    {
        Interlocked.Increment(ref captureSessionId);
        captureCancellation?.Cancel();
        captureCancellation?.Dispose();
        captureCancellation = null;
        CaptureSelectedBindingButton.IsEnabled = true;
    }

    private static bool IsBindableActionSource(InputSource source)
    {
        return source is KeyboardKeyInputSource or MouseButtonInputSource or JoystickAxisInputSource or JoystickButtonInputSource or VirtualButtonAxisInputSource or CompositeAxisInputSource;
    }

    private static InputSourceKind? DetermineCaptureKind(OverlayProfile profile, string sourceId, InputSourceKind fallback)
    {
        foreach (InputSource source in profile.InputSources)
        {
            if (source is VirtualButtonAxisInputSource virtualAxis &&
                (SourceIdEquals(virtualAxis.NegativeButtonSourceId, sourceId) || SourceIdEquals(virtualAxis.PositiveButtonSourceId, sourceId)))
            {
                return InputSourceKind.Button;
            }

            if (source is CompositeAxisInputSource compositeAxis)
            {
                foreach (AxisComponent component in compositeAxis.Components)
                {
                    if (SourceIdEquals(component.SourceId, sourceId))
                    {
                        return component.SourceKind;
                    }
                }
            }
        }

        foreach (WidgetDefinition widget in profile.Widgets)
        {
            switch (widget)
            {
                case StickWidgetDefinition stick when SourceIdEquals(stick.XSourceId, sourceId) || SourceIdEquals(stick.YSourceId, sourceId):
                case ThrottleWidgetDefinition throttle when SourceIdEquals(throttle.SourceId, sourceId):
                case RollWidgetDefinition roll when SourceIdEquals(roll.SourceId, sourceId):
                    return InputSourceKind.Axis;
                case StateTextWidgetDefinition stateText when SourceIdEquals(stateText.SourceId, sourceId):
                    return null;
            }
        }

        return fallback;
    }

    private static bool SourceIdEquals(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
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

    private static string FormatInputSource(InputSource source)
    {
        return source switch
        {
            KeyboardKeyInputSource keyboard => $"Keyboard {keyboard.Key}",
            MouseButtonInputSource mouse => $"Mouse {mouse.Button}",
            JoystickAxisInputSource axis => $"{axis.DeviceId} axis {axis.AxisIndex}{(axis.Invert ? " inverted" : string.Empty)}",
            JoystickButtonInputSource button => $"{button.DeviceId} button {button.ButtonIndex}{(button.Invert ? " inverted" : string.Empty)}",
            VirtualButtonAxisInputSource buttonAxis => $"{buttonAxis.NegativeButtonSourceId} / {buttonAxis.PositiveButtonSourceId}",
            CompositeAxisInputSource composite => $"{composite.Components.Count} components",
            _ => source.GetType().Name
        };
    }

    private sealed record ProfileSelectionItem(string Id, string Name);

    private sealed record BindableSourceItem(string Id, string DisplayName, InputSourceKind SourceKind, InputSourceKind? CaptureKind, string BindingText)
    {
        public string Kind => CaptureKind is null ? "Button or Axis" : CaptureKind.Value.ToString();
    }
}
