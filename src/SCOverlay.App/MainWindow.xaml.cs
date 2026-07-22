using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
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
    private readonly OverlayStateEngine stateEngine;
    private readonly StarCitizenAxisTranslationService starCitizenAxisTranslationService;
    private readonly GitHubReleaseUpdateService releaseUpdateService;
    private readonly CancellationTokenSource updateCheckCancellation = new();
    private readonly DispatcherTimer inputTimer;
    private readonly JsonSerializerOptions profileJsonOptions;
    private readonly ObservableCollection<ProfileSelectionItem> profileItems = new();
    private readonly ObservableCollection<BindableSourceItem> bindableSourceItems = new();
    private readonly ObservableCollection<BindingDetailItem> bindingDetailItems = new();
    private readonly ObservableCollection<AppearancePresetItem> appearancePresetItems = new();
    private readonly ObservableCollection<WidgetAppearanceItem> widgetAppearanceItems = new();
    private readonly ObservableCollection<RollAssetItem> rollAssetItems = new();
    private AppSettings appSettings;
    private OverlayProfile profile;
    private BrowserSourceServer browserSourceServer;
    private DesktopOverlayWindow? desktopOverlayWindow;
    private Forms.NotifyIcon? trayIcon;
    private OverlayState latestOverlayState;
    private IReadOnlyList<InputDeviceInfo> latestDevices = Array.Empty<InputDeviceInfo>();
    private InputSnapshot latestInputSnapshot = InputSnapshot.Empty();
    private EvaluatedInputState latestEvaluatedInputState = new(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>());
    private bool isLoadingProfiles;
    private bool isRefreshingBindingUi;
    private bool isLoadingAppearance = true;
    private bool isLoadingElementAppearance = true;
    private bool isRefreshingDesktopOverlayUi;
    private bool isClosing;
    private CancellationTokenSource? captureCancellation;
    private int captureSessionId;
    private bool isLoadingUpdateUi = true;
    private bool isLoadingStarCitizenAxisUi = true;
    private Uri? availableReleaseUri;
    private string? availableReleaseVersion;

    public MainWindow(AppPaths paths, AppLog log)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        profileStore = new FileProfileStore(paths);
        settingsStore = new FileAppSettingsStore(paths);
        profileJsonOptions = ProfileJsonSerializerOptions.Create();
        appSettings = LoadInitialSettings();
        profile = LoadInitialProfile(appSettings);
        appSettings = appSettings with
        {
            ActiveProfileId = profile.Id
        };
        latestOverlayState = OverlayState.Empty(profile.Id);
        inputProvider = new WindowsInputProvider();
        starCitizenAxisTranslationService = new StarCitizenAxisTranslationService();
        string configuredActionMapsPath = appSettings.StarCitizenAxisTranslation.ActionMapsPath;
        if (string.IsNullOrWhiteSpace(configuredActionMapsPath))
        {
            configuredActionMapsPath = StarCitizenAxisTranslationService.FindActionMapsPath();
        }
        starCitizenAxisTranslationService.Configure(appSettings.StarCitizenAxisTranslation.Enabled, configuredActionMapsPath);
        stateEngine = new OverlayStateEngine(starCitizenAxisTranslationService);
        releaseUpdateService = new GitHubReleaseUpdateService();
        browserSourceServer = new BrowserSourceServer(profile.Runtime, OverlayState.Empty(profile.Id));
        inputTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        inputTimer.Tick += OnInputTimerTick;

        InitializeComponent();

        AutomaticUpdateChecksCheckBox.IsChecked = appSettings.AutomaticUpdateChecksEnabled;
        isLoadingUpdateUi = false;
        StarCitizenAxisTranslationCheckBox.IsChecked = appSettings.StarCitizenAxisTranslation.Enabled;
        StarCitizenActionMapsPathTextBox.Text = configuredActionMapsPath;
        isLoadingStarCitizenAxisUi = false;
        RefreshStarCitizenAxisTranslationStatus();

        ProfileComboBox.ItemsSource = profileItems;
        BindingSourceComboBox.ItemsSource = bindableSourceItems;
        InputSourcesGrid.ItemsSource = bindableSourceItems;
        BindingDetailComboBox.ItemsSource = bindingDetailItems;
        AppearancePresetComboBox.ItemsSource = appearancePresetItems;
        ElementWidgetComboBox.ItemsSource = widgetAppearanceItems;
        ElementRollAssetComboBox.ItemsSource = rollAssetItems;
        foreach (AppearancePresetItem preset in CreateAppearancePresets())
        {
            appearancePresetItems.Add(preset);
        }
        foreach (RollAssetItem item in CreateRollAssetItems())
        {
            rollAssetItems.Add(item);
        }

        HeaderText.Text = "Profile setup, binding capture, OBS source, and live input diagnostics.";
        StatusText.Text = "Raw Input is attached for keyboard, mouse, and HID flight devices. HID reports are parsed into declared axes, buttons, and hats; WinMM remains as a legacy fallback.";
        UpdateObsUrlText();
        NewProfileNameTextBox.Text = $"{profile.Name} Copy";
        RefreshAppearanceUi();
        RefreshElementAppearanceUi();
        RefreshDesktopOverlayUi();
        InitializeTrayIcon();
        FooterStatusText.Text = settingsStore.LastRecoveryMessage is null
            ? $"Runtime data: {paths.DataRoot}"
            : $"{settingsStore.LastRecoveryMessage} Runtime data: {paths.DataRoot}";
        if (settingsStore.LastRecoveryMessage is not null)
        {
            this.log.Info(settingsStore.LastRecoveryMessage);
        }

        this.log.WriteSessionHeader(paths, profile.Id, browserSourceServer.Url, inputProvider.Name);

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        Loaded += OnLoaded;
    }

    private AppSettings LoadInitialSettings()
    {
        try
        {
            return settingsStore.LoadAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            log.Error("Failed to load app settings. Falling back to defaults.", exception);
            return new AppSettings();
        }
    }

    private OverlayProfile LoadInitialProfile(AppSettings loadedSettings)
    {
        try
        {
            ProfileBootstrapper.EnsureDefaultProfilesAsync(profileStore).AsTask().GetAwaiter().GetResult();
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
        ShowDesktopOverlayFromSettings();
        if (ReleaseUpdatePolicy.ShouldCheckAutomatically(appSettings, DateTimeOffset.UtcNow))
        {
            await CheckForUpdatesAsync(isManual: false);
        }
    }

    private async void CheckForUpdatesButton_OnClick(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(isManual: true);
    }

    private async void AutomaticUpdateChecksCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (isLoadingUpdateUi)
        {
            return;
        }

        appSettings = appSettings with
        {
            AutomaticUpdateChecksEnabled = AutomaticUpdateChecksCheckBox.IsChecked == true
        };

        try
        {
            await settingsStore.SaveAsync(appSettings);
            UpdateStatusText.Text = appSettings.AutomaticUpdateChecksEnabled
                ? "Automatic update checks are enabled."
                : "Automatic update checks are disabled.";
        }
        catch (Exception exception)
        {
            log.Error("Failed to save the automatic update preference.", exception);
            UpdateStatusText.Text = $"Could not save the update preference: {exception.Message}";
        }
    }

    private void ViewReleaseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (availableReleaseUri is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = availableReleaseUri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            log.Error("Failed to open the SC Overlay release page.", exception);
            FooterStatusText.Text = $"Could not open the release page: {exception.Message}";
        }
    }

    private async void DismissUpdateButton_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
        if (availableReleaseVersion is null)
        {
            return;
        }

        appSettings = appSettings with
        {
            DismissedUpdateVersion = availableReleaseVersion
        };
        try
        {
            await settingsStore.SaveAsync(appSettings);
        }
        catch (Exception exception)
        {
            log.Error("Failed to save the dismissed update version.", exception);
        }
    }

    private async Task CheckForUpdatesAsync(bool isManual)
    {
        CheckForUpdatesButton.IsEnabled = false;
        if (isManual)
        {
            UpdateStatusText.Text = "Checking for updates...";
        }

        try
        {
            ReleaseUpdateInfo update = await releaseUpdateService.CheckAsync(
                AppInfo.Version,
                updateCheckCancellation.Token);
            appSettings = appSettings with
            {
                LastUpdateCheckUtc = DateTimeOffset.UtcNow,
                DismissedUpdateVersion = isManual && update.IsUpdateAvailable
                    ? null
                    : appSettings.DismissedUpdateVersion
            };
            await settingsStore.SaveAsync(appSettings, updateCheckCancellation.Token);

            if (update.IsUpdateAvailable)
            {
                availableReleaseUri = update.ReleaseUri;
                availableReleaseVersion = update.LatestVersion;
                if (isManual || !string.Equals(
                    appSettings.DismissedUpdateVersion,
                    update.LatestVersion,
                    StringComparison.OrdinalIgnoreCase))
                {
                    UpdateBannerText.Text = $"SC Overlay {update.LatestVersion} is available.";
                    UpdateBanner.Visibility = Visibility.Visible;
                }

                UpdateStatusText.Text = $"Version {update.LatestVersion} is available.";
                log.Info($"Update check found SC Overlay {update.LatestVersion}.");
            }
            else
            {
                UpdateStatusText.Text = $"SC Overlay {update.CurrentVersion} is up to date.";
                if (isManual)
                {
                    UpdateBanner.Visibility = Visibility.Collapsed;
                }
                log.Info($"Update check completed; SC Overlay {update.CurrentVersion} is current.");
            }
        }
        catch (OperationCanceledException) when (updateCheckCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException or FormatException or TaskCanceledException)
        {
            log.Info($"Update check could not complete: {exception.Message}");
            if (isManual)
            {
                UpdateStatusText.Text = $"Could not check for updates: {exception.Message}";
            }
        }
        finally
        {
            if (!isClosing)
            {
                CheckForUpdatesButton.IsEnabled = true;
            }
        }
    }

    private async void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            inputProvider.AttachWindow(source.Handle);
            source.AddHook(WindowMessageHook);
            StartBrowserSourceServer();

            await RefreshDevicesAsync();
            inputTimer.Start();
        }
        catch (Exception exception)
        {
            log.Error("Failed to initialize Windows input diagnostics.", exception);
            StatusText.Text = $"Input initialization failed: {exception.Message}";
        }
    }

    private void StartBrowserSourceServer()
    {
        if (!profile.Runtime.BrowserSourceEnabled)
        {
            ObsUrlText.Text = "OBS browser source: disabled";
            return;
        }

        try
        {
            browserSourceServer.Start();
            UpdateObsUrlText();
        }
        catch (HttpListenerException exception)
        {
            int originalPort = browserSourceServer.Port;
            log.Error($"OBS browser source could not start on port {originalPort}. Trying an available fallback port.", exception);
            TryStartBrowserSourceFallback(originalPort);
        }
    }

    private void TryStartBrowserSourceFallback(int originalPort)
    {
        try
        {
            int fallbackPort = BrowserSourceServer.FindAvailablePort();
            RuntimeSettings fallbackRuntime = profile.Runtime with
            {
                BrowserSourcePort = fallbackPort
            };
            browserSourceServer.Dispose();
            browserSourceServer = new BrowserSourceServer(fallbackRuntime, latestOverlayState);
            browserSourceServer.Start();
            UpdateObsUrlText($"Configured port {originalPort} was unavailable; using {fallbackPort} for this session.");
            FooterStatusText.Text = $"OBS port {originalPort} was unavailable. Use the temporary URL shown above.";
            log.Info($"OBS browser source fallback started on port {fallbackPort} after port {originalPort} failed.");
        }
        catch (Exception exception) when (exception is HttpListenerException or IOException or InvalidOperationException)
        {
            log.Error("OBS browser source fallback startup failed.", exception);
            ObsUrlText.Text = $"OBS browser source unavailable:{Environment.NewLine}{exception.Message}";
            FooterStatusText.Text = "OBS browser source could not start. Input diagnostics and desktop overlay are still running.";
        }
    }

    private void UpdateObsUrlText(string? note = null)
    {
        ObsUrlText.Text = string.IsNullOrWhiteSpace(note)
            ? $"OBS browser source:{Environment.NewLine}{browserSourceServer.Url}"
            : $"OBS browser source:{Environment.NewLine}{browserSourceServer.Url}{Environment.NewLine}{note}";
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
        latestDevices = devices;
        DevicesText.Text = string.Join(
            Environment.NewLine,
            devices.Select(FormatDevice));
    }

    private void RefreshBindingUi(string? selectedSourceId = null)
    {
        isRefreshingBindingUi = true;
        try
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
        finally
        {
            isRefreshingBindingUi = false;
            RefreshBindingInvertUi();
        }
    }

    private void OnInputTimerTick(object? sender, EventArgs e)
    {
        try
        {
            InputSnapshot snapshot = inputProvider.Poll();
            EvaluatedInputState evaluated = InputSourceEvaluator.Evaluate(profile.InputSources, snapshot, starCitizenAxisTranslationService);
            OverlayState overlayState = stateEngine.BuildState(profile, snapshot);
            latestInputSnapshot = snapshot;
            latestEvaluatedInputState = evaluated;
            latestOverlayState = overlayState;
            browserSourceServer.UpdateState(overlayState);
            desktopOverlayWindow?.UpdateState(overlayState);

            RawSnapshotText.Text = FormatRawSnapshot(snapshot);
            ProfileValuesText.Text = FormatProfileValues(evaluated);
            RefreshStarCitizenAxisTranslationStatus();
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
            latestOverlayState = OverlayState.Empty(profile.Id);
            desktopOverlayWindow?.UpdateState(latestOverlayState);
            RefreshBindingUi();
            RefreshAppearanceUi();
            RefreshElementAppearanceUi();
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
            RefreshAppearanceUi();
            RefreshElementAppearanceUi();
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
        var dialog = new Microsoft.Win32.OpenFileDialog
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
            RefreshAppearanceUi();
            RefreshElementAppearanceUi();
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
        var dialog = new Microsoft.Win32.SaveFileDialog
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

    private async void StarCitizenAxisTranslationCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (isLoadingStarCitizenAxisUi) return;
        await SaveStarCitizenAxisTranslationSettingsAsync();
    }

    private async void BrowseStarCitizenActionMapsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select the active Star Citizen actionmaps.xml",
            Filter = "Star Citizen action maps (actionmaps.xml)|actionmaps.xml|XML files (*.xml)|*.xml",
            FileName = "actionmaps.xml",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        StarCitizenActionMapsPathTextBox.Text = dialog.FileName;
        await SaveStarCitizenAxisTranslationSettingsAsync();
    }

    private async void StarCitizenActionMapsPathTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (!isLoadingStarCitizenAxisUi &&
            !string.Equals(StarCitizenActionMapsPathTextBox.Text.Trim(), appSettings.StarCitizenAxisTranslation.ActionMapsPath, StringComparison.OrdinalIgnoreCase))
        {
            await SaveStarCitizenAxisTranslationSettingsAsync();
        }
    }

    private async Task SaveStarCitizenAxisTranslationSettingsAsync()
    {
        var settings = new StarCitizenAxisTranslationSettings
        {
            Enabled = StarCitizenAxisTranslationCheckBox.IsChecked == true,
            ActionMapsPath = StarCitizenActionMapsPathTextBox.Text.Trim()
        };
        appSettings = appSettings with { StarCitizenAxisTranslation = settings };
        starCitizenAxisTranslationService.Configure(settings.Enabled, settings.ActionMapsPath);
        stateEngine.Reset();
        try
        {
            await settingsStore.SaveAsync(appSettings);
            RefreshStarCitizenAxisTranslationStatus();
        }
        catch (Exception exception)
        {
            log.Error("Failed to save Star Citizen axis translation settings.", exception);
            StarCitizenAxisTranslationStatusText.Text = $"Could not save setting: {exception.Message}";
        }
    }

    private void RefreshStarCitizenAxisTranslationStatus()
    {
        StarCitizenAxisTranslationStatus status = starCitizenAxisTranslationService.Status;
        string profileText = string.IsNullOrWhiteSpace(status.ProfileName) ? string.Empty : $" Profile: {status.ProfileName}.";
        string diagnostics = string.Join(Environment.NewLine, starCitizenAxisTranslationService.AxisDiagnostics.Take(8));
        StarCitizenAxisTranslationStatusText.Text = $"{status.Message}{profileText} Matched axes: {status.MatchedAxes}; raw fallbacks: {status.RawFallbackAxes}." +
            (string.IsNullOrWhiteSpace(diagnostics) ? string.Empty : Environment.NewLine + diagnostics);
    }

    private void OpenLogsFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenLogsFolder();
    }

    private async void ExportDiagnosticsButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ExportDiagnosticsAsync();
    }

    private async void DesktopOverlayVisibleCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (isRefreshingDesktopOverlayUi)
        {
            return;
        }

        await SetDesktopOverlayVisibleAsync(DesktopOverlayVisibleCheckBox.IsChecked == true);
    }

    private async void DesktopOverlayLockedCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (isRefreshingDesktopOverlayUi)
        {
            return;
        }

        bool locked = DesktopOverlayLockedCheckBox.IsChecked == true;
        DesktopOverlaySettings current = CaptureCurrentDesktopOverlaySettings();
        DesktopOverlaySettings settings = current with
        {
            IsLocked = locked,
            IsClickThrough = locked ? current.IsClickThrough : false
        };
        await ApplyDesktopOverlaySettingsAsync(settings);
    }

    private async void DesktopOverlayClickThroughCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (isRefreshingDesktopOverlayUi)
        {
            return;
        }

        bool clickThrough = DesktopOverlayClickThroughCheckBox.IsChecked == true;
        DesktopOverlaySettings current = CaptureCurrentDesktopOverlaySettings();
        DesktopOverlaySettings settings = current with
        {
            IsClickThrough = clickThrough,
            IsLocked = clickThrough || current.IsLocked
        };
        await ApplyDesktopOverlaySettingsAsync(settings);
    }

    private async void ResetDesktopOverlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        EnsureDesktopOverlayWindow();
        desktopOverlayWindow!.ResetPlacement();
        DesktopOverlaySettings settings = desktopOverlayWindow.CaptureSettings(appSettings.DesktopOverlay.IsVisible);
        await ApplyDesktopOverlaySettingsAsync(settings);
        FooterStatusText.Text = "Desktop overlay position reset.";
    }

    private void BindingSourceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedBindingText();
    }

    private void BindingDetailComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshBindingInvertUi();
    }

    private async void BindingInvertAxisCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (isRefreshingBindingUi)
        {
            return;
        }

        if (BindingDetailComboBox.SelectedItem is not BindingDetailItem binding)
        {
            return;
        }

        try
        {
            profile = ProfileEditor.SetJoystickAxisInverted(profile, binding.SourceId, BindingInvertAxisCheckBox.IsChecked == true);
            await SaveAndActivateProfileAsync();
            stateEngine.Reset();
            RefreshBindingUi((BindingSourceComboBox.SelectedItem as BindableSourceItem)?.Id);
            FooterStatusText.Text = BindingInvertAxisCheckBox.IsChecked == true
                ? $"Inverted controller axis '{binding.DisplayName}'."
                : $"Restored controller axis '{binding.DisplayName}'.";
        }
        catch (Exception exception)
        {
            log.Error("Failed to update controller axis inversion.", exception);
            FooterStatusText.Text = $"Could not update axis inversion: {exception.Message}";
            RefreshBindingInvertUi();
        }
    }

    private async void RemoveSelectedBindingButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (BindingSourceComboBox.SelectedItem is not BindableSourceItem action ||
            BindingDetailComboBox.SelectedItem is not BindingDetailItem binding)
        {
            FooterStatusText.Text = "Select an action binding before removing.";
            return;
        }

        try
        {
            profile = ProfileEditor.RemoveInputBinding(profile, action.Id, binding.SourceId);
            await SaveAndActivateProfileAsync();
            stateEngine.Reset();
            RefreshBindingUi(action.Id);
            FooterStatusText.Text = $"Removed binding '{binding.DisplayName}' from '{action.DisplayName}'.";
            log.Info($"Removed binding '{binding.SourceId}' from '{action.Id}' in profile {profile.Id}.");
        }
        catch (Exception exception)
        {
            log.Error("Failed to remove binding.", exception);
            FooterStatusText.Text = $"Could not remove binding: {exception.Message}";
        }
    }

    private void AppearancePresetComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoadingAppearance)
        {
            return;
        }

        ApplyPresetToAppearanceUi(SelectedAppearancePreset());
        UpdateAppearanceValueText();
        UpdateAppearancePreview();
    }

    private void AppearanceSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isLoadingAppearance)
        {
            return;
        }

        UpdateAppearanceValueText();
        UpdateAppearancePreview();
    }

    private void AppearanceEffect_OnChanged(object sender, RoutedEventArgs e)
    {
        if (isLoadingAppearance)
        {
            return;
        }

        UpdateAppearancePreview();
    }

    private void AppearanceColorTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (isLoadingAppearance)
        {
            return;
        }

        UpdateAppearancePreview();
    }

    private void PickPrimaryColorButton_OnClick(object sender, RoutedEventArgs e)
    {
        PickColorInto(AppearanceRingColorTextBox, profile.Appearance.RingColor);
    }

    private void PickActiveColorButton_OnClick(object sender, RoutedEventArgs e)
    {
        PickColorInto(AppearanceActiveColorTextBox, profile.Appearance.ActiveColor);
    }

    private void PickFramePrimaryColorButton_OnClick(object sender, RoutedEventArgs e)
    {
        PickColorInto(AppearanceFrameColorTextBox, profile.Appearance.FrameColor);
    }

    private void PickFrameActiveColorButton_OnClick(object sender, RoutedEventArgs e)
    {
        PickColorInto(AppearanceFrameActiveColorTextBox, profile.Appearance.FrameActiveColor);
    }

    private void ElementWidgetComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoadingElementAppearance)
        {
            return;
        }

        LoadSelectedElementAppearance();
    }

    private void ElementSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isLoadingElementAppearance)
        {
            return;
        }

        UpdateElementAppearanceValueText();
    }

    private void ElementRollAssetComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoadingElementAppearance)
        {
            return;
        }

        UpdateElementAppearanceValueText();
    }

    private void ElementStateTextShakeCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (isLoadingElementAppearance)
        {
            return;
        }

        UpdateElementAppearanceValueText();
    }

    private async void ApplyElementAppearanceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ElementWidgetComboBox.SelectedItem is not WidgetAppearanceItem item)
        {
            FooterStatusText.Text = "Select an element before applying.";
            return;
        }

        try
        {
            profile = ProfileEditor.ApplyWidgetAppearance(
                profile,
                item.Id,
                ElementXSlider.Value,
                ElementYSlider.Value,
                ElementScaleSlider.Value,
                ElementOpacitySlider.Value,
                ElementLineThicknessSlider.Value,
                ElementThrottleCornerSlider.Value,
                SelectedRollAssetId(),
                ElementRollMaxRotationSlider.Value,
                ElementStateTextShakeCheckBox.IsChecked == true);
            await SaveAndActivateProfileAsync();
            RefreshElementAppearanceUi(item.Id);
            stateEngine.Reset();
            FooterStatusText.Text = $"Applied element appearance for '{item.DisplayName}'.";
        }
        catch (Exception exception)
        {
            log.Error("Failed to apply element appearance.", exception);
            FooterStatusText.Text = $"Could not apply element appearance: {exception.Message}";
        }
    }

    private async void ResetElementAppearanceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ElementWidgetComboBox.SelectedItem is not WidgetAppearanceItem item)
        {
            FooterStatusText.Text = "Select an element before resetting.";
            return;
        }

        try
        {
            profile = ProfileEditor.ResetWidgetAppearance(profile, item.Id);
            await SaveAndActivateProfileAsync();
            RefreshElementAppearanceUi(item.Id);
            stateEngine.Reset();
            FooterStatusText.Text = $"Reset element appearance for '{item.DisplayName}'.";
        }
        catch (Exception exception)
        {
            log.Error("Failed to reset element appearance.", exception);
            FooterStatusText.Text = $"Could not reset element appearance: {exception.Message}";
        }
    }

    private async void ApplyAppearanceButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            AppearanceSettings appearance = BuildAppearanceFromUi();
            profile = ProfileEditor.ApplyAppearance(profile, appearance);
            profile = ProfileEditor.ApplyWidgetEffects(profile, BuildVisualEffectsFromUi(), BuildTextEffectsFromUi());
            await SaveAndActivateProfileAsync();
            RefreshAppearanceUi();
            RefreshElementAppearanceUi();
            FooterStatusText.Text = $"Applied appearance preset '{SelectedAppearancePreset().Name}'.";
        }
        catch (Exception exception)
        {
            log.Error("Failed to apply appearance.", exception);
            FooterStatusText.Text = $"Could not apply appearance: {exception.Message}";
        }
    }

    private async void ResetAppearanceButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            profile = ProfileEditor.ApplyAppearance(profile, new AppearanceSettings());
            profile = ProfileEditor.ApplyWidgetEffects(profile, new EffectSettings(), new EffectSettings());
            await SaveAndActivateProfileAsync();
            RefreshAppearanceUi();
            RefreshElementAppearanceUi();
            FooterStatusText.Text = "Appearance reset.";
        }
        catch (Exception exception)
        {
            log.Error("Failed to reset appearance.", exception);
            FooterStatusText.Text = $"Could not reset appearance: {exception.Message}";
        }
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
            RefreshBindingDetailUi(item.Id);
        }
        else
        {
            SelectedBindingText.Text = "(none)";
            bindingDetailItems.Clear();
            RefreshBindingInvertUi();
        }
    }

    private void RefreshBindingDetailUi(string actionSourceId)
    {
        bindingDetailItems.Clear();
        InputSource? action = profile.InputSources.FirstOrDefault(source =>
            SourceIdEquals(source.Id, actionSourceId));
        if (action is null)
        {
            RemoveSelectedBindingButton.IsEnabled = false;
            return;
        }

        foreach (BindingDetailItem item in CreateBindingDetails(action))
        {
            bindingDetailItems.Add(item);
        }

        BindingDetailComboBox.SelectedItem = bindingDetailItems.FirstOrDefault();
        RemoveSelectedBindingButton.IsEnabled = bindingDetailItems.Count > 1;
        RefreshBindingInvertUi();
    }

    private void RefreshBindingInvertUi()
    {
        bool wasRefreshing = isRefreshingBindingUi;
        isRefreshingBindingUi = true;
        try
        {
            if (BindingDetailComboBox.SelectedItem is BindingDetailItem binding &&
                profile.InputSources.FirstOrDefault(source => SourceIdEquals(source.Id, binding.SourceId)) is JoystickAxisInputSource axis)
            {
                BindingInvertAxisCheckBox.Visibility = Visibility.Visible;
                BindingInvertAxisCheckBox.IsEnabled = true;
                BindingInvertAxisCheckBox.IsChecked = axis.Invert;
            }
            else
            {
                BindingInvertAxisCheckBox.Visibility = Visibility.Collapsed;
                BindingInvertAxisCheckBox.IsEnabled = false;
                BindingInvertAxisCheckBox.IsChecked = false;
            }
        }
        finally
        {
            isRefreshingBindingUi = wasRefreshing;
        }
    }

    private void RefreshAppearanceUi()
    {
        isLoadingAppearance = true;
        try
        {
            AppearancePresetItem selected = appearancePresetItems.FirstOrDefault(item =>
                string.Equals(item.Id, profile.Appearance.PresetId, StringComparison.OrdinalIgnoreCase)) ??
                appearancePresetItems.First();
            AppearancePresetComboBox.SelectedItem = selected;
            AppearanceRingColorTextBox.Text = FormatColor(profile.Appearance.RingColor);
            AppearanceActiveColorTextBox.Text = FormatColor(profile.Appearance.ActiveColor);
            AppearanceFrameColorTextBox.Text = FormatColor(profile.Appearance.FrameColor);
            AppearanceFrameActiveColorTextBox.Text = FormatColor(profile.Appearance.FrameActiveColor);
            AppearanceScaleSlider.Value = profile.Appearance.WidgetScale;
            AppearanceOpacitySlider.Value = profile.Appearance.Opacity;
            AppearancePrimaryOpacitySlider.Value = profile.Appearance.PrimaryOpacity;
            AppearanceActiveOpacitySlider.Value = profile.Appearance.ActiveOpacity;
            AppearanceFramePrimaryOpacitySlider.Value = profile.Appearance.FramePrimaryOpacity;
            AppearanceFrameActiveOpacitySlider.Value = profile.Appearance.FrameActiveOpacity;
            EffectSettings visualEffects = CurrentVisualEffects();
            EffectSettings textEffects = CurrentTextEffects();
            AppearanceOutlineCheckBox.IsChecked = visualEffects.OutlineEnabled || textEffects.OutlineEnabled;
            AppearanceShadowCheckBox.IsChecked = visualEffects.ShadowEnabled || textEffects.ShadowEnabled;
            AppearanceBackplateCheckBox.IsChecked = textEffects.BackplateEnabled;
            AppearanceOutlineWidthSlider.Value = visualEffects.OutlineWidth;
            AppearanceShadowBlurSlider.Value = visualEffects.ShadowWidth;
            AppearanceBackplateOpacitySlider.Value = textEffects.BackplateColor.A / 255.0;
            UpdateAppearanceValueText();
            UpdateAppearancePreview();
        }
        finally
        {
            isLoadingAppearance = false;
        }
    }

    private void RefreshElementAppearanceUi(string? selectedWidgetId = null)
    {
        isLoadingElementAppearance = true;
        try
        {
            widgetAppearanceItems.Clear();
            foreach (WidgetDefinition widget in profile.Widgets)
            {
                widgetAppearanceItems.Add(new WidgetAppearanceItem(widget.Id, widget.DisplayName));
            }

            string idToSelect = selectedWidgetId ?? (ElementWidgetComboBox.SelectedItem as WidgetAppearanceItem)?.Id ?? profile.Widgets.FirstOrDefault()?.Id ?? string.Empty;
            ElementWidgetComboBox.SelectedItem = widgetAppearanceItems.FirstOrDefault(item =>
                SourceIdEquals(item.Id, idToSelect)) ?? widgetAppearanceItems.FirstOrDefault();
            LoadSelectedElementAppearance();
        }
        finally
        {
            isLoadingElementAppearance = false;
        }
    }

    private void LoadSelectedElementAppearance()
    {
        if (ElementWidgetComboBox.SelectedItem is not WidgetAppearanceItem item)
        {
            return;
        }

        WidgetDefinition? widget = profile.Widgets.FirstOrDefault(candidate => SourceIdEquals(candidate.Id, item.Id));
        if (widget is null)
        {
            return;
        }

        isLoadingElementAppearance = true;
        try
        {
            ElementXSlider.Value = ClampToSlider(widget.X, ElementXSlider);
            ElementYSlider.Value = ClampToSlider(widget.Y, ElementYSlider);
            ElementScaleSlider.Value = ClampToSlider(widget.Scale, ElementScaleSlider);
            ElementOpacitySlider.Value = ClampToSlider(widget.Opacity, ElementOpacitySlider);
            ElementLineThicknessSlider.Value = ClampToSlider(widget.LineThickness, ElementLineThicknessSlider);
            ElementThrottleCornerSlider.Value = widget is ThrottleWidgetDefinition throttle
                ? ClampToSlider(throttle.CornerRadius, ElementThrottleCornerSlider)
                : ClampToSlider(8.0, ElementThrottleCornerSlider);
            ElementThrottleCornerRow.Visibility = widget is ThrottleWidgetDefinition ? Visibility.Visible : Visibility.Collapsed;
            ElementRollAssetPanel.Visibility = widget is RollWidgetDefinition ? Visibility.Visible : Visibility.Collapsed;
            ElementRollRotationRow.Visibility = widget is RollWidgetDefinition ? Visibility.Visible : Visibility.Collapsed;
            ElementStateTextShakeCheckBox.Visibility = widget is StateTextWidgetDefinition ? Visibility.Visible : Visibility.Collapsed;
            ElementStateTextShakeCheckBox.IsChecked = widget is StateTextWidgetDefinition stateText && stateText.Tuning.MaxedShakeEnabled;
            ElementRollAssetComboBox.SelectedItem = widget is RollWidgetDefinition roll
                ? rollAssetItems.FirstOrDefault(asset => SourceIdEquals(asset.AssetId, RollAssetSelectionId(roll))) ?? rollAssetItems.FirstOrDefault()
                : rollAssetItems.FirstOrDefault();
            ElementRollMaxRotationSlider.Value = widget is RollWidgetDefinition rollWidget
                ? ClampToSlider(rollWidget.MaxRotationDegrees, ElementRollMaxRotationSlider)
                : ClampToSlider(60.0, ElementRollMaxRotationSlider);
            UpdateElementAppearanceValueText();
        }
        finally
        {
            isLoadingElementAppearance = false;
        }
    }

    private void RefreshDesktopOverlayUi()
    {
        isRefreshingDesktopOverlayUi = true;
        try
        {
            DesktopOverlaySettings settings = appSettings.DesktopOverlay;
            DesktopOverlayVisibleCheckBox.IsChecked = settings.IsVisible;
            DesktopOverlayLockedCheckBox.IsChecked = settings.IsLocked;
            DesktopOverlayClickThroughCheckBox.IsChecked = settings.IsClickThrough;
            DesktopOverlayStatusText.Text = settings.IsVisible
                ? settings.IsClickThrough
                    ? "Visible and click-through."
                    : settings.IsLocked
                    ? "Visible and locked."
                    : "Visible and editable."
                : "Hidden.";
        }
        finally
        {
            isRefreshingDesktopOverlayUi = false;
        }
    }

    private void ShowDesktopOverlayFromSettings()
    {
        if (!appSettings.DesktopOverlay.IsVisible)
        {
            return;
        }

        EnsureDesktopOverlayWindow();
        desktopOverlayWindow!.ApplySettings(appSettings.DesktopOverlay);
        desktopOverlayWindow.UpdateState(latestOverlayState);
        desktopOverlayWindow.Show();
        desktopOverlayWindow.Topmost = true;
        RebuildTrayMenu();
    }

    private async Task SetDesktopOverlayVisibleAsync(bool isVisible)
    {
        if (isVisible)
        {
            EnsureDesktopOverlayWindow();
            desktopOverlayWindow!.ApplySettings(appSettings.DesktopOverlay with
            {
                IsVisible = true
            });
            desktopOverlayWindow.UpdateState(latestOverlayState);
            desktopOverlayWindow.Show();
            desktopOverlayWindow.Topmost = true;
        }
        else
        {
            desktopOverlayWindow?.Hide();
        }

        DesktopOverlaySettings settings = desktopOverlayWindow?.CaptureSettings(isVisible) ??
            appSettings.DesktopOverlay with
            {
                IsVisible = isVisible
            };
        await ApplyDesktopOverlaySettingsAsync(settings);
        FooterStatusText.Text = isVisible ? "Desktop overlay shown." : "Desktop overlay hidden.";
    }

    private async Task ApplyDesktopOverlaySettingsAsync(DesktopOverlaySettings settings)
    {
        appSettings = appSettings with
        {
            DesktopOverlay = settings
        };

        if (desktopOverlayWindow is not null)
        {
            desktopOverlayWindow.ApplySettings(settings);
        }

        await settingsStore.SaveAsync(appSettings);
        RefreshDesktopOverlayUi();
        RebuildTrayMenu();
    }

    private DesktopOverlaySettings CaptureCurrentDesktopOverlaySettings()
    {
        return desktopOverlayWindow?.CaptureSettings(appSettings.DesktopOverlay.IsVisible) ??
            appSettings.DesktopOverlay;
    }

    private void EnsureDesktopOverlayWindow()
    {
        if (desktopOverlayWindow is not null)
        {
            return;
        }

        desktopOverlayWindow = new DesktopOverlayWindow();
        desktopOverlayWindow.ApplySettings(appSettings.DesktopOverlay);
        desktopOverlayWindow.UpdateState(latestOverlayState);
        desktopOverlayWindow.Closed += (_, _) =>
        {
            desktopOverlayWindow = null;
            if (!isClosing)
            {
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await ApplyDesktopOverlaySettingsAsync(appSettings.DesktopOverlay with
                    {
                        IsVisible = false
                    });
                });
            }
        };
    }

    private void InitializeTrayIcon()
    {
        trayIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Text = "SC Overlay",
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(BringToFront);
        RebuildTrayMenu();
    }

    private void RebuildTrayMenu()
    {
        if (trayIcon is null)
        {
            return;
        }

        Forms.ContextMenuStrip menu = new();
        menu.Items.Add("Show SC Overlay", null, (_, _) => Dispatcher.Invoke(BringToFront));
        menu.Items.Add(
            appSettings.DesktopOverlay.IsVisible ? "Hide Desktop Overlay" : "Show Desktop Overlay",
            null,
            (_, _) => Dispatcher.Invoke(() => _ = SetDesktopOverlayVisibleAsync(!appSettings.DesktopOverlay.IsVisible)));
        menu.Items.Add(
            appSettings.DesktopOverlay.IsLocked ? "Unlock Desktop Overlay" : "Lock Desktop Overlay",
            null,
            (_, _) => Dispatcher.Invoke(() =>
            {
                DesktopOverlaySettings current = CaptureCurrentDesktopOverlaySettings();
                DesktopOverlaySettings settings = current with
                {
                    IsLocked = !current.IsLocked,
                    IsClickThrough = !current.IsLocked ? current.IsClickThrough : false
                };
                _ = ApplyDesktopOverlaySettingsAsync(settings);
            }));
        menu.Items.Add(
            appSettings.DesktopOverlay.IsClickThrough ? "Disable Click-through" : "Enable Click-through",
            null,
            (_, _) => Dispatcher.Invoke(() =>
            {
                DesktopOverlaySettings current = CaptureCurrentDesktopOverlaySettings();
                bool clickThrough = !current.IsClickThrough;
                DesktopOverlaySettings settings = current with
                {
                    IsClickThrough = clickThrough,
                    IsLocked = clickThrough || current.IsLocked
                };
                _ = ApplyDesktopOverlaySettingsAsync(settings);
            }));
        menu.Items.Add("Reset Desktop Overlay", null, (_, _) => Dispatcher.Invoke(() =>
        {
            EnsureDesktopOverlayWindow();
            desktopOverlayWindow!.ResetPlacement();
            _ = ApplyDesktopOverlaySettingsAsync(desktopOverlayWindow.CaptureSettings(appSettings.DesktopOverlay.IsVisible));
        }));
        menu.Items.Add("Open Logs Folder", null, (_, _) => Dispatcher.Invoke(OpenLogsFolder));
        menu.Items.Add("Export Diagnostics", null, (_, _) => Dispatcher.Invoke(() => _ = ExportDiagnosticsAsync()));
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(RequestAppShutdown));

        Forms.ContextMenuStrip? oldMenu = trayIcon.ContextMenuStrip;
        trayIcon.ContextMenuStrip = menu;
        oldMenu?.Dispose();
    }

    public void BringToFront()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void RequestAppShutdown()
    {
        if (System.Windows.Application.Current is App app)
        {
            app.RequestShutdown();
        }
        else
        {
            Close();
        }
    }

    private AppearanceSettings BuildAppearanceFromUi()
    {
        AppearancePresetItem preset = SelectedAppearancePreset();
        RgbaColor ringColor = TryParseColor(AppearanceRingColorTextBox.Text, preset.RingColor, out _);
        RgbaColor activeColor = TryParseColor(AppearanceActiveColorTextBox.Text, preset.ActiveColor, out _);
        RgbaColor frameColor = TryParseColor(AppearanceFrameColorTextBox.Text, preset.FrameColor, out _);
        RgbaColor frameActiveColor = TryParseColor(AppearanceFrameActiveColorTextBox.Text, preset.FrameActiveColor, out _);
        return new AppearanceSettings
        {
            PresetId = preset.Id,
            RingColor = ringColor,
            ActiveColor = activeColor,
            FrameColor = frameColor,
            FrameActiveColor = frameActiveColor,
            WidgetScale = AppearanceScaleSlider.Value,
            Opacity = AppearanceOpacitySlider.Value,
            PrimaryOpacity = AppearancePrimaryOpacitySlider.Value,
            ActiveOpacity = AppearanceActiveOpacitySlider.Value,
            FramePrimaryOpacity = AppearanceFramePrimaryOpacitySlider.Value,
            FrameActiveOpacity = AppearanceFrameActiveOpacitySlider.Value
        };
    }

    private EffectSettings BuildVisualEffectsFromUi()
    {
        EffectSettings current = CurrentVisualEffects();
        bool outline = AppearanceOutlineCheckBox.IsChecked == true;
        bool shadow = AppearanceShadowCheckBox.IsChecked == true;
        return current with
        {
            OutlineEnabled = outline,
            OutlineWidth = AppearanceOutlineWidthSlider.Value,
            ShadowEnabled = shadow,
            ShadowWidth = AppearanceShadowBlurSlider.Value,
            BackplateEnabled = false
        };
    }

    private EffectSettings BuildTextEffectsFromUi()
    {
        EffectSettings current = CurrentTextEffects();
        bool outline = AppearanceOutlineCheckBox.IsChecked == true;
        bool shadow = AppearanceShadowCheckBox.IsChecked == true;
        bool backplate = AppearanceBackplateCheckBox.IsChecked == true;
        return current with
        {
            OutlineEnabled = outline,
            OutlineWidth = AppearanceOutlineWidthSlider.Value,
            ShadowEnabled = shadow,
            ShadowWidth = AppearanceShadowBlurSlider.Value,
            BackplateEnabled = backplate,
            BackplateColor = WithAlpha(current.BackplateColor, AppearanceBackplateOpacitySlider.Value)
        };
    }

    private EffectSettings CurrentVisualEffects()
    {
        return profile.Widgets.FirstOrDefault()?.VisualEffects ?? new EffectSettings();
    }

    private EffectSettings CurrentTextEffects()
    {
        return profile.Widgets.FirstOrDefault()?.TextEffects ?? new EffectSettings();
    }

    private AppearancePresetItem SelectedAppearancePreset()
    {
        return AppearancePresetComboBox.SelectedItem is AppearancePresetItem preset
            ? preset
            : appearancePresetItems.FirstOrDefault() ?? new AppearancePresetItem(
                "clean-hud",
                "Clean HUD",
                new RgbaColor(228, 241, 255, 235),
                new RgbaColor(255, 84, 84, 255),
                new RgbaColor(228, 241, 255, 235),
                new RgbaColor(255, 84, 84, 255),
                new EffectSettings(),
                new EffectSettings());
    }

    private void UpdateAppearanceValueText()
    {
        AppearanceScaleValueText.Text = $"{AppearanceScaleSlider.Value:0.00}x";
        AppearanceOpacityValueText.Text = $"{AppearanceOpacitySlider.Value:P0}";
        AppearancePrimaryOpacityValueText.Text = $"{AppearancePrimaryOpacitySlider.Value:P0}";
        AppearanceActiveOpacityValueText.Text = $"{AppearanceActiveOpacitySlider.Value:P0}";
        AppearanceFramePrimaryOpacityValueText.Text = $"{AppearanceFramePrimaryOpacitySlider.Value:P0}";
        AppearanceFrameActiveOpacityValueText.Text = $"{AppearanceFrameActiveOpacitySlider.Value:P0}";
        AppearanceOutlineWidthValueText.Text = $"{AppearanceOutlineWidthSlider.Value:0.0}px";
        AppearanceShadowBlurValueText.Text = $"{AppearanceShadowBlurSlider.Value:0}px";
        AppearanceBackplateOpacityValueText.Text = $"{AppearanceBackplateOpacitySlider.Value:P0}";
    }

    private void UpdateElementAppearanceValueText()
    {
        ElementXValueText.Text = $"{ElementXSlider.Value:0}";
        ElementYValueText.Text = $"{ElementYSlider.Value:0}";
        ElementScaleValueText.Text = $"{ElementScaleSlider.Value:0.00}x";
        ElementOpacityValueText.Text = $"{ElementOpacitySlider.Value:P0}";
        ElementLineThicknessValueText.Text = $"{ElementLineThicknessSlider.Value:0.0}px";
        ElementThrottleCornerValueText.Text = $"{ElementThrottleCornerSlider.Value:0}px";
        ElementRollMaxRotationValueText.Text = $"{ElementRollMaxRotationSlider.Value:0} deg";
    }

    private string SelectedRollAssetId()
    {
        return ElementRollAssetComboBox.SelectedItem is RollAssetItem item
            ? item.AssetId
            : RollAssets.Gladius;
    }

    private static string RollAssetSelectionId(RollWidgetDefinition roll)
    {
        return roll.RenderMode == RollRenderMode.Indicator
            ? RollAssets.Indicator
            : RollAssets.IsKnown(roll.AssetId) && !SourceIdEquals(roll.AssetId, RollAssets.Indicator)
            ? roll.AssetId
            : RollAssets.Gladius;
    }

    private void UpdateAppearancePreview()
    {
        AppearancePresetItem preset = SelectedAppearancePreset();
        RgbaColor ringColor = TryParseColor(AppearanceRingColorTextBox.Text, preset.RingColor, out bool ringValid);
        RgbaColor activeColor = TryParseColor(AppearanceActiveColorTextBox.Text, preset.ActiveColor, out bool activeValid);
        RgbaColor frameColor = TryParseColor(AppearanceFrameColorTextBox.Text, preset.FrameColor, out bool frameValid);
        RgbaColor frameActiveColor = TryParseColor(AppearanceFrameActiveColorTextBox.Text, preset.FrameActiveColor, out bool frameActiveValid);
        AppearanceRingColorSwatch.Background = Brush(ringColor, 1.0);
        AppearanceActiveColorSwatch.Background = Brush(activeColor, 1.0);
        AppearanceFrameColorSwatch.Background = Brush(frameColor, 1.0);
        AppearanceFrameActiveColorSwatch.Background = Brush(frameActiveColor, 1.0);
        AppearanceRingColorTextBox.BorderBrush = ringValid ? System.Windows.SystemColors.ControlDarkBrush : System.Windows.Media.Brushes.IndianRed;
        AppearanceActiveColorTextBox.BorderBrush = activeValid ? System.Windows.SystemColors.ControlDarkBrush : System.Windows.Media.Brushes.IndianRed;
        AppearanceFrameColorTextBox.BorderBrush = frameValid ? System.Windows.SystemColors.ControlDarkBrush : System.Windows.Media.Brushes.IndianRed;
        AppearanceFrameActiveColorTextBox.BorderBrush = frameActiveValid ? System.Windows.SystemColors.ControlDarkBrush : System.Windows.Media.Brushes.IndianRed;

        byte alpha = (byte)Math.Round(activeColor.A *
            Math.Clamp(AppearanceOpacitySlider.Value, 0.0, 1.0) *
            Math.Clamp(AppearanceActiveOpacitySlider.Value, 0.0, 1.0));
        AppearancePreviewText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, activeColor.R, activeColor.G, activeColor.B));
        AppearancePreviewText.FontSize = 22 * Math.Clamp(AppearanceScaleSlider.Value, 0.5, 1.75);
        byte backplateAlpha = (byte)Math.Round(255.0 * Math.Clamp(AppearanceBackplateOpacitySlider.Value, 0.0, 0.8));
        AppearancePreviewBackplate.Background = AppearanceBackplateCheckBox.IsChecked == true
            ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(backplateAlpha, 0, 0, 0))
            : System.Windows.Media.Brushes.Transparent;
        AppearancePreviewBackplate.BorderBrush = Brush(frameColor, 0.45 * AppearanceOpacitySlider.Value * AppearanceFramePrimaryOpacitySlider.Value);
        AppearancePreviewBackplate.BorderThickness = AppearanceOutlineCheckBox.IsChecked == true && AppearanceOutlineWidthSlider.Value > 0.0
            ? new Thickness(Math.Max(1.0, Math.Min(2.0, AppearanceOutlineWidthSlider.Value / 2.0)))
            : new Thickness(0);
        AppearancePreviewText.Padding = AppearanceBackplateCheckBox.IsChecked == true
            ? new Thickness(8, 3, 8, 3)
            : new Thickness(0);
        AppearancePreviewText.Effect = AppearanceShadowCheckBox.IsChecked == true
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = AppearanceShadowBlurSlider.Value,
                ShadowDepth = 2,
                Opacity = 0.65
            }
            : null;
    }

    private void ApplyPresetToAppearanceUi(AppearancePresetItem preset)
    {
        AppearanceRingColorTextBox.Text = FormatColor(preset.RingColor);
        AppearanceActiveColorTextBox.Text = FormatColor(preset.ActiveColor);
        AppearanceFrameColorTextBox.Text = FormatColor(preset.FrameColor);
        AppearanceFrameActiveColorTextBox.Text = FormatColor(preset.FrameActiveColor);
        AppearanceOutlineCheckBox.IsChecked = preset.VisualEffects.OutlineEnabled || preset.TextEffects.OutlineEnabled;
        AppearanceShadowCheckBox.IsChecked = preset.VisualEffects.ShadowEnabled || preset.TextEffects.ShadowEnabled;
        AppearanceBackplateCheckBox.IsChecked = preset.TextEffects.BackplateEnabled;
        AppearanceOutlineWidthSlider.Value = preset.VisualEffects.OutlineWidth;
        AppearanceShadowBlurSlider.Value = preset.VisualEffects.ShadowWidth;
        AppearanceBackplateOpacitySlider.Value = preset.TextEffects.BackplateColor.A / 255.0;
    }

    private void PickColorInto(System.Windows.Controls.TextBox target, RgbaColor fallback)
    {
        RgbaColor current = TryParseColor(target.Text, fallback, out bool isValid);
        if (!isValid)
        {
            current = fallback;
        }

        using var dialog = new Forms.ColorDialog
        {
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true,
            Color = Drawing.Color.FromArgb(current.R, current.G, current.B)
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        target.Text = FormatColor(new RgbaColor(dialog.Color.R, dialog.Color.G, dialog.Color.B, current.A));
        UpdateAppearancePreview();
    }

    private static double ClampToSlider(double value, Slider slider)
    {
        return Math.Clamp(value, slider.Minimum, slider.Maximum);
    }

    private static string FormatColor(RgbaColor color)
    {
        return FormattableString.Invariant($"#{color.R:X2}{color.G:X2}{color.B:X2}");
    }

    private static RgbaColor TryParseColor(string? text, RgbaColor fallback, out bool isValid)
    {
        isValid = false;
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        string hex = text.Trim();
        if (hex.StartsWith("#", StringComparison.Ordinal))
        {
            hex = hex[1..];
        }

        if (hex.Length != 6 && hex.Length != 8)
        {
            return fallback;
        }

        if (!byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red) ||
            !byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green) ||
            !byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue))
        {
            return fallback;
        }

        byte alpha = 255;
        if (hex.Length == 8 &&
            !byte.TryParse(hex[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out alpha))
        {
            return fallback;
        }

        isValid = true;
        return new RgbaColor(red, green, blue, alpha);
    }

    private static RgbaColor WithAlpha(RgbaColor color, double alpha)
    {
        return color with
        {
            A = (byte)Math.Round(255.0 * Math.Clamp(alpha, 0.0, 1.0))
        };
    }

    private static SolidColorBrush Brush(RgbaColor color, double opacity)
    {
        byte alpha = (byte)Math.Round(Math.Clamp(color.A * Math.Clamp(opacity, 0.0, 1.0), 0.0, 255.0));
        return new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (System.Windows.Application.Current is App currentApp)
        {
            currentApp.ScheduleForcedExitFallback();
        }

        isClosing = true;
        Interlocked.Increment(ref captureSessionId);
        inputTimer.Stop();
        captureCancellation?.Cancel();
        updateCheckCancellation.Cancel();
        if (desktopOverlayWindow is not null)
        {
            appSettings = appSettings with
            {
                DesktopOverlay = desktopOverlayWindow.CaptureSettings(desktopOverlayWindow.IsVisible)
            };
            desktopOverlayWindow.Close();
            desktopOverlayWindow = null;
        }

        try
        {
            settingsStore.SaveAsync(appSettings).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            log.Error("Failed to persist app settings on shutdown.", exception);
        }

        if (trayIcon is not null)
        {
            trayIcon.Visible = false;
            trayIcon.ContextMenuStrip?.Dispose();
            trayIcon.Dispose();
            trayIcon = null;
        }

        browserSourceServer.Dispose();
        inputProvider.Dispose();
        releaseUpdateService.Dispose();
        updateCheckCancellation.Dispose();
        captureCancellation?.Dispose();
        if (System.Windows.Application.Current is App app)
        {
            app.CompleteShutdown();
        }
        else
        {
            System.Windows.Application.Current.Shutdown();
        }
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
        return !ProfileEditor.IsGeneratedBindingSource(source) &&
            source is KeyboardKeyInputSource or MouseButtonInputSource or JoystickAxisInputSource or JoystickButtonInputSource or VirtualButtonAxisInputSource or CompositeAxisInputSource or CompositeButtonInputSource;
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

        string identity = string.IsNullOrWhiteSpace(device.StableIdentity)
            ? string.Empty
            : $"  identity:{device.StableIdentity}";

        return $"{device.DeviceId}  {device.DisplayName}  {counts}{details}{identity}";
    }

    private void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(log.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = log.LogDirectory,
                UseShellExecute = true
            });
            FooterStatusText.Text = $"Opened logs folder: {log.LogDirectory}";
        }
        catch (Exception exception)
        {
            log.Error("Failed to open logs folder.", exception);
            FooterStatusText.Text = $"Could not open logs folder: {exception.Message}";
        }
    }

    private async Task ExportDiagnosticsAsync()
    {
        try
        {
            Directory.CreateDirectory(paths.DiagnosticsDirectory);
            var report = new DiagnosticsReport(
                GeneratedAt: DateTimeOffset.UtcNow,
                AppName: AppInfo.ProductName,
                AppVersion: AppInfo.Version,
                DataRoot: paths.DataRoot,
                ActiveProfileId: profile.Id,
                ActiveProfileName: profile.Name,
                ObsUrl: browserSourceServer.Url,
                InputProvider: inputProvider.Name,
                Settings: appSettings,
                Devices: latestDevices,
                RawSnapshot: latestInputSnapshot,
                EvaluatedInput: latestEvaluatedInputState,
                RecentLogLines: log.RecentLines(120),
                StarCitizenAxisTranslation: starCitizenAxisTranslationService.Status,
                StarCitizenAxisDiagnostics: starCitizenAxisTranslationService.AxisDiagnostics);
            string fileName = $"sc-overlay-diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json";
            string path = Path.Combine(paths.DiagnosticsDirectory, fileName);
            await File.WriteAllTextAsync(path, DiagnosticsReportWriter.CreateJson(report));
            FooterStatusText.Text = $"Exported diagnostics: {path}";
            log.Info($"Diagnostics exported to {path}.");
        }
        catch (Exception exception)
        {
            log.Error("Failed to export diagnostics.", exception);
            FooterStatusText.Text = $"Could not export diagnostics: {exception.Message}";
        }
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
            CompositeButtonInputSource composite => $"{composite.SourceIds.Count} buttons",
            _ => source.GetType().Name
        };
    }

    private IReadOnlyList<BindingDetailItem> CreateBindingDetails(InputSource action)
    {
        return action switch
        {
            CompositeAxisInputSource axis => axis.Components
                .Select(component => CreateBindingDetail(component.SourceId, component.SourceKind))
                .ToArray(),
            CompositeButtonInputSource button => button.SourceIds
                .Select(sourceId => CreateBindingDetail(sourceId, InputSourceKind.Button))
                .ToArray(),
            _ => new[]
            {
                new BindingDetailItem(action.Id, $"{action.DisplayName}: {FormatInputSource(action)}", action.Kind)
            }
        };
    }

    private BindingDetailItem CreateBindingDetail(string sourceId, InputSourceKind sourceKind)
    {
        InputSource? source = profile.InputSources.FirstOrDefault(item =>
            SourceIdEquals(item.Id, sourceId));
        string display = source is null
            ? sourceId
            : $"{source.DisplayName}: {FormatInputSource(source)}";
        return new BindingDetailItem(sourceId, display, sourceKind);
    }

    private sealed record ProfileSelectionItem(string Id, string Name);

    private sealed record BindableSourceItem(string Id, string DisplayName, InputSourceKind SourceKind, InputSourceKind? CaptureKind, string BindingText)
    {
        public string Kind => CaptureKind is null ? "Button or Axis" : CaptureKind.Value.ToString();
    }

    private sealed record AppearancePresetItem(
        string Id,
        string Name,
        RgbaColor RingColor,
        RgbaColor ActiveColor,
        RgbaColor FrameColor,
        RgbaColor FrameActiveColor,
        EffectSettings VisualEffects,
        EffectSettings TextEffects);

    private sealed record BindingDetailItem(string SourceId, string DisplayName, InputSourceKind SourceKind);

    private sealed record WidgetAppearanceItem(string Id, string DisplayName);

    private sealed record RollAssetItem(string AssetId, string DisplayName);

    private static IReadOnlyList<AppearancePresetItem> CreateAppearancePresets()
    {
        return new AppearancePresetItem[]
        {
            Preset(
                "clean-hud",
                "Clean HUD",
                new RgbaColor(228, 241, 255, 235),
                new RgbaColor(255, 84, 84, 255),
                new RgbaColor(228, 241, 255, 235),
                new RgbaColor(255, 84, 84, 255),
                outline: 2.0,
                shadow: 3.0,
                backplate: 0.0),
            Preset(
                "crusader-glass",
                "Crusader Glass",
                new RgbaColor(185, 239, 255, 230),
                new RgbaColor(64, 210, 255, 255),
                new RgbaColor(185, 239, 255, 230),
                new RgbaColor(64, 210, 255, 255),
                outline: 1.5,
                shadow: 10.0,
                backplate: 0.24),
            Preset(
                "anvil-amber",
                "Anvil Amber",
                new RgbaColor(255, 215, 128, 230),
                new RgbaColor(255, 164, 64, 255),
                new RgbaColor(255, 215, 128, 230),
                new RgbaColor(255, 164, 64, 255),
                outline: 2.0,
                shadow: 8.0,
                backplate: 0.18),
            Preset(
                "drake-industrial",
                "Drake Industrial",
                new RgbaColor(220, 236, 220, 225),
                new RgbaColor(255, 192, 72, 255),
                new RgbaColor(220, 236, 220, 225),
                new RgbaColor(255, 192, 72, 255),
                outline: 3.0,
                shadow: 6.0,
                backplate: 0.32),
            Preset(
                "stealth-green",
                "Stealth Green",
                new RgbaColor(148, 255, 191, 220),
                new RgbaColor(60, 235, 135, 255),
                new RgbaColor(148, 255, 191, 220),
                new RgbaColor(60, 235, 135, 255),
                outline: 1.5,
                shadow: 12.0,
                backplate: 0.22),
            Preset(
                "hostile-red",
                "Hostile Red",
                new RgbaColor(255, 204, 204, 220),
                new RgbaColor(255, 58, 58, 255),
                new RgbaColor(255, 204, 204, 220),
                new RgbaColor(255, 58, 58, 255),
                outline: 3.0,
                shadow: 14.0,
                backplate: 0.28)
        };
    }

    private static IReadOnlyList<RollAssetItem> CreateRollAssetItems()
    {
        return new[]
        {
            new RollAssetItem(RollAssets.Gladius, "Gladius image"),
            new RollAssetItem(RollAssets.Arrow, "Arrow image"),
            new RollAssetItem(RollAssets.Indicator, "Indicator arc")
        };
    }

    private static AppearancePresetItem Preset(
        string id,
        string name,
        RgbaColor ringColor,
        RgbaColor activeColor,
        RgbaColor frameColor,
        RgbaColor frameActiveColor,
        double outline,
        double shadow,
        double backplate)
    {
        var visualEffects = new EffectSettings
        {
            OutlineEnabled = outline > 0.0,
            OutlineWidth = outline,
            ShadowEnabled = shadow > 0.0,
            ShadowWidth = shadow,
            ShadowOffsetX = 0.0,
            ShadowOffsetY = 2.0,
            BackplateEnabled = false
        };
        var textEffects = visualEffects with
        {
            BackplateEnabled = backplate > 0.0,
            BackplateColor = new RgbaColor(0, 0, 0, (byte)Math.Round(255.0 * Math.Clamp(backplate, 0.0, 1.0))),
            BackplatePadding = 10.0,
            BackplateRadius = 6.0
        };

        return new AppearancePresetItem(id, name, ringColor, activeColor, frameColor, frameActiveColor, visualEffects, textEffects);
    }
}
