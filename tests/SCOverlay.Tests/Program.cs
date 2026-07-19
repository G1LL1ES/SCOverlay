using System.Text.Json;
using SCOverlay.BrowserSource;
using SCOverlay.Core.Application;
using SCOverlay.Core.Diagnostics;
using SCOverlay.Core.Domain;
using SCOverlay.Core.Input;
using SCOverlay.Core.Profiles;
using SCOverlay.Core.Rendering;
using SCOverlay.Input;

var runner = new TestRunner();

runner.Test("App paths use SCOverlay app-data folder", () =>
{
    AppPaths paths = AppPathProvider.Create();
    Assert.Equal(AppInfo.AppDataFolderName, Path.GetFileName(paths.DataRoot));
    Assert.Equal("profiles", Path.GetFileName(paths.ProfilesDirectory));
    Assert.Equal("logs", Path.GetFileName(paths.LogsDirectory));
    Assert.Equal("profile-backups", Path.GetFileName(paths.ProfileBackupsDirectory));
    Assert.Equal("settings-backups", Path.GetFileName(paths.SettingsBackupsDirectory));
    Assert.Equal("diagnostics", Path.GetFileName(paths.DiagnosticsDirectory));
});

runner.Test("Default profiles are schema current and valid", () =>
{
    IReadOnlyList<OverlayProfile> profiles = DefaultProfiles.CreateAll();
    Assert.Equal(2, profiles.Count);
    Assert.Contains(profiles.Select(profile => profile.Id), "kbm-default");
    Assert.Contains(profiles.Select(profile => profile.Id), "hotas-reference");

    foreach (OverlayProfile profile in profiles)
    {
        Assert.Equal(AppInfo.CurrentProfileSchemaVersion, profile.SchemaVersion);
        ProfileValidationResult result = ProfileValidator.Validate(profile);
        Assert.True(result.IsValid);
        Assert.True(profile.InputSources.Count > 0);
        Assert.True(profile.Widgets.Count > 0);
    }
});

runner.Test("Foundation default profile maps to KBM default", () =>
{
    OverlayProfile profile = OverlayProfile.CreateFoundationDefault();
    Assert.Equal("kbm-default", profile.Id);
    Assert.True(profile.Runtime.BrowserSourceEnabled);
    Assert.True(profile.InputSources.OfType<KeyboardKeyInputSource>().Any(source => source.Key == "W"));
});

runner.Test("Invalid profile returns readable validation issues", () =>
{
    var invalid = new OverlayProfile
    {
        Id = "bad",
        Name = "Bad",
        Runtime = new RuntimeSettings
        {
            TargetHz = 0,
            BrowserSourcePort = 70000
        },
        InputSources = new InputSource[]
        {
            new KeyboardKeyInputSource
            {
                Id = "boost",
                DisplayName = "Boost"
            }
        },
        Widgets = new WidgetDefinition[]
        {
            new ThrottleWidgetDefinition
            {
                Id = "throttle-widget",
                SourceId = "missing-axis"
            }
        }
    };

    ProfileValidationResult result = ProfileValidator.Validate(invalid);
    Assert.False(result.IsValid);
    Assert.True(result.Issues.Any(issue => issue.Path == "runtime.targetHz"));
    Assert.True(result.Issues.Any(issue => issue.Path == "runtime.browserSourcePort"));
    Assert.True(result.Issues.Any(issue => issue.Path.EndsWith(".key", StringComparison.Ordinal)));
    Assert.True(result.Issues.Any(issue => issue.Message.Contains("missing-axis", StringComparison.Ordinal)));
});

runner.Test("Profile JSON round-trips polymorphic sources and widgets", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    JsonSerializerOptions options = ProfileJsonSerializerOptions.Create();
    string json = JsonSerializer.Serialize(profile, options);
    Assert.True(json.Contains("\"type\": \"keyboardKey\"", StringComparison.Ordinal));
    Assert.True(json.Contains("\"type\": \"stick\"", StringComparison.Ordinal));

    OverlayProfile? restored = JsonSerializer.Deserialize<OverlayProfile>(json, options);
    if (restored is null)
    {
        throw new InvalidOperationException("Expected deserialized profile.");
    }

    Assert.True(restored.InputSources.OfType<VirtualButtonAxisInputSource>().Any(source => source.Id == "strafe-x"));
    Assert.True(restored.Widgets.OfType<StateTextWidgetDefinition>().Any(widget => widget.Id == "boost-widget"));
    Assert.True(ProfileValidator.Validate(restored).IsValid);
});

runner.Test("File profile store saves, lists, loads, and validates", () =>
{
    string root = Path.Combine(Path.GetTempPath(), $"SCOverlayTests-{Guid.NewGuid():N}");
    var paths = new AppPaths(
        DataRoot: root,
        ProfilesDirectory: Path.Combine(root, "profiles"),
        LogsDirectory: Path.Combine(root, "logs"),
        AssetsDirectory: Path.Combine(root, "assets"),
        ProfileBackupsDirectory: Path.Combine(root, "profile-backups"),
        SettingsBackupsDirectory: Path.Combine(root, "settings-backups"),
        DiagnosticsDirectory: Path.Combine(root, "diagnostics"));
    var store = new FileProfileStore(paths);
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();

    store.SaveAsync(profile).AsTask().GetAwaiter().GetResult();
    IReadOnlyList<string> ids = store.ListProfileIdsAsync().AsTask().GetAwaiter().GetResult();
    OverlayProfile loaded = store.LoadAsync(profile.Id).AsTask().GetAwaiter().GetResult();

    Assert.Contains(ids, profile.Id);
    Assert.Equal(profile.Id, loaded.Id);
    Assert.True(loaded.InputSources.OfType<KeyboardKeyInputSource>().Any());
    Assert.True(ProfileValidator.Validate(loaded).IsValid);
});

runner.Test("File profile store creates rotating backups before overwriting profiles", () =>
{
    string root = Path.Combine(Path.GetTempPath(), $"SCOverlayTests-{Guid.NewGuid():N}");
    AppPaths paths = TestPaths(root);
    var store = new FileProfileStore(paths);
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();

    store.SaveAsync(profile).AsTask().GetAwaiter().GetResult();
    OverlayProfile renamed = profile with
    {
        Name = "Renamed KBM"
    };
    store.SaveAsync(renamed).AsTask().GetAwaiter().GetResult();

    string[] backups = Directory.GetFiles(paths.ProfileBackupsDirectory, "kbm-default.*.json");
    Assert.Equal(1, backups.Length);
    string backupJson = File.ReadAllText(backups[0]);
    Assert.True(backupJson.Contains("\"name\": \"Keyboard and Mouse Default\"", StringComparison.Ordinal));

    OverlayProfile loaded = store.LoadAsync("kbm-default").AsTask().GetAwaiter().GetResult();
    Assert.Equal("Renamed KBM", loaded.Name);
});

runner.Test("App settings store saves and loads active profile and desktop overlay settings", () =>
{
    string root = Path.Combine(Path.GetTempPath(), $"SCOverlayTests-{Guid.NewGuid():N}");
    var paths = TestPaths(root);
    var store = new FileAppSettingsStore(paths);
    var settings = new AppSettings
    {
        ActiveProfileId = "custom-profile",
        DesktopOverlay = new DesktopOverlaySettings
        {
            IsVisible = true,
            IsLocked = false,
            IsClickThrough = false,
            Left = 321,
            Top = 123,
            Width = 800,
            Height = 450
        }
    };

    store.SaveAsync(settings).AsTask().GetAwaiter().GetResult();
    AppSettings loaded = store.LoadAsync().AsTask().GetAwaiter().GetResult();

    Assert.Equal("custom-profile", loaded.ActiveProfileId);
    Assert.True(loaded.DesktopOverlay.IsVisible);
    Assert.False(loaded.DesktopOverlay.IsLocked);
    Assert.False(loaded.DesktopOverlay.IsClickThrough);
    Assert.Equal(321, loaded.DesktopOverlay.Left);
    Assert.Equal(123, loaded.DesktopOverlay.Top);
    Assert.Equal(800, loaded.DesktopOverlay.Width);
    Assert.Equal(450, loaded.DesktopOverlay.Height);
});

runner.Test("App settings store recovers from corrupt settings with newest valid backup", () =>
{
    string root = Path.Combine(Path.GetTempPath(), $"SCOverlayTests-{Guid.NewGuid():N}");
    AppPaths paths = TestPaths(root);
    var store = new FileAppSettingsStore(paths);

    store.SaveAsync(new AppSettings { ActiveProfileId = "first-profile" }).AsTask().GetAwaiter().GetResult();
    store.SaveAsync(new AppSettings { ActiveProfileId = "second-profile" }).AsTask().GetAwaiter().GetResult();
    File.WriteAllText(Path.Combine(root, "settings.json"), "{ bad json");

    AppSettings loaded = store.LoadAsync().AsTask().GetAwaiter().GetResult();

    Assert.Equal("first-profile", loaded.ActiveProfileId);
    Assert.NotNull(store.LastRecoveryMessage);
    Assert.True(Directory.GetFiles(paths.SettingsBackupsDirectory, "settings.*.invalid.json").Length == 1);
});

runner.Test("App settings store writes backups with bounded pruning", () =>
{
    string root = Path.Combine(Path.GetTempPath(), $"SCOverlayTests-{Guid.NewGuid():N}");
    AppPaths paths = TestPaths(root);
    var store = new FileAppSettingsStore(paths);

    for (int index = 0; index < 13; index++)
    {
        store.SaveAsync(new AppSettings { ActiveProfileId = $"profile-{index}" }).AsTask().GetAwaiter().GetResult();
    }

    Assert.True(Directory.GetFiles(paths.SettingsBackupsDirectory, "settings.*.json").Length <= 10);
    AppSettings loaded = store.LoadAsync().AsTask().GetAwaiter().GetResult();
    Assert.Equal("profile-12", loaded.ActiveProfileId);
});

runner.Test("App log writes session headers, recent lines, and rotates session files", () =>
{
    string root = Path.Combine(Path.GetTempPath(), $"SCOverlayTests-{Guid.NewGuid():N}");
    AppPaths paths = TestPaths(root);
    Directory.CreateDirectory(paths.LogsDirectory);

    for (int index = 0; index < 24; index++)
    {
        File.WriteAllText(Path.Combine(paths.LogsDirectory, $"sc-overlay-20000101-0000{index:D2}-1.log"), "old");
        File.SetLastWriteTimeUtc(Path.Combine(paths.LogsDirectory, $"sc-overlay-20000101-0000{index:D2}-1.log"), DateTime.UtcNow.AddDays(-index - 1));
    }

    var log = new AppLog(paths);
    log.WriteSessionHeader(paths, "kbm-default", "http://127.0.0.1:8765/obs.html", "Test Provider");
    log.Info("line one");
    log.Info("line two");

    Assert.True(File.Exists(log.LogPath));
    Assert.True(log.RecentLines(2).Any(line => line.Contains("line two", StringComparison.Ordinal)));
    Assert.True(File.ReadAllText(log.LogPath).Contains("ActiveProfile=kbm-default", StringComparison.Ordinal));
    Assert.True(Directory.GetFiles(paths.LogsDirectory, "sc-overlay-*.log").Length <= 20);
});

runner.Test("Diagnostics report serializes support fields", () =>
{
    var report = new DiagnosticsReport(
        GeneratedAt: DateTimeOffset.UtcNow,
        AppName: AppInfo.ProductName,
        AppVersion: AppInfo.Version,
        DataRoot: "C:\\DataRoot",
        ActiveProfileId: "kbm-default",
        ActiveProfileName: "Keyboard and Mouse Default",
        ObsUrl: "http://127.0.0.1:8765/obs.html",
        InputProvider: "Test Provider",
        Settings: new AppSettings(),
        Devices: new[]
        {
            new InputDeviceInfo("keyboard", "Keyboard", InputDeviceKind.Keyboard, 0, 256)
        },
        RawSnapshot: InputSnapshot.Empty(),
        EvaluatedInput: new EvaluatedInputState(DateTimeOffset.UtcNow, new Dictionary<string, double>(), new Dictionary<string, bool>()),
        RecentLogLines: new[] { "hello" });

    string json = DiagnosticsReportWriter.CreateJson(report);

    Assert.True(json.Contains("\"activeProfileId\": \"kbm-default\"", StringComparison.Ordinal));
    Assert.True(json.Contains("\"obsUrl\": \"http://127.0.0.1:8765/obs.html\"", StringComparison.Ordinal));
    Assert.True(json.Contains("\"deviceId\": \"keyboard\"", StringComparison.Ordinal));
    Assert.True(json.Contains("\"recentLogLines\"", StringComparison.Ordinal));
});

runner.Test("Desktop overlay placement clamps saved bounds to visible screens", () =>
{
    var settings = new DesktopOverlaySettings
    {
        Left = 5000,
        Top = -2000,
        Width = 2000,
        Height = 100
    };

    DesktopOverlaySettings clamped = DesktopOverlayPlacement.Clamp(settings, new DesktopOverlayBounds(100, 50, 1280, 720));

    Assert.Equal(1280, clamped.Width);
    Assert.Equal(220, clamped.Height);
    Assert.Equal(100, clamped.Left);
    Assert.Equal(50, clamped.Top);
});

runner.Test("Input device identity formats stable HID and WinMM identities", () =>
{
    string first = InputDeviceIdentity.CreateStableHidIdentity(0x231D, 0x0125, "VKB Gladiator NXT", "\\\\?\\hid#vid_231d&pid_0125#abc", 0);
    string second = InputDeviceIdentity.CreateStableHidIdentity(0x231D, 0x0125, "VKB Gladiator NXT", "\\\\?\\hid#vid_231d&pid_0125#abc", 1);
    string fallback = InputDeviceIdentity.CreateStableHidIdentity(0x1234, 0xBEAD, "", string.Empty, 2);
    string winMm = InputDeviceIdentity.CreateStableWinMmIdentity(3, "T.16000M FCS");

    Assert.Equal(first, second);
    Assert.True(first.StartsWith("hid:vid_231D&pid_0125:vkb_gladiator_nxt:", StringComparison.Ordinal));
    Assert.Equal("hid:vid_1234&pid_BEAD:unknown:ordinal_2", fallback);
    Assert.Equal("winmm:t_16000m_fcs:ordinal_3", winMm);
});

runner.Test("WinMM polling reuses cached capabilities", () =>
{
    var api = new FakeWinMmApi();
    var provider = new WinMmJoystickProvider(api);

    IReadOnlyList<InputDeviceInfo> devices = provider.EnumerateDevices();
    for (int index = 0; index < 100; index++)
    {
        provider.Poll(DateTimeOffset.UtcNow);
    }

    Assert.Equal(1, devices.Count);
    Assert.Equal(2, api.CapabilitiesCallCount);
    Assert.Equal(101, api.ActiveDevicePositionCallCount);
    Assert.Equal(0, api.InactiveDevicePositionCallCount);
});

runner.Test("Input snapshot keys match legacy HID ids to stable HID identities", () =>
{
    string legacy = "hid:vid_231D&pid_0125:3";
    string stable = "hid:vid_231D&pid_0125:vkb_gladiator_nxt:91dd3bb0";

    Assert.True(InputSnapshotKeys.DeviceIdsMatch(legacy, stable));
    Assert.True(InputSnapshotKeys.DeviceIdsMatch(stable, legacy));
    Assert.False(InputSnapshotKeys.DeviceIdsMatch("hid:vid_231D&pid_0125:3", "hid:vid_231D&pid_0126:vkb_gladiator_nxt:91dd3bb0"));
});

runner.Test("Profile bootstrapper materializes default profiles once", () =>
{
    string root = Path.Combine(Path.GetTempPath(), $"SCOverlayTests-{Guid.NewGuid():N}");
    var store = new FileProfileStore(TestPaths(root));

    ProfileBootstrapper.EnsureDefaultProfilesAsync(store).AsTask().GetAwaiter().GetResult();
    ProfileBootstrapper.EnsureDefaultProfilesAsync(store).AsTask().GetAwaiter().GetResult();
    IReadOnlyList<string> ids = store.ListProfileIdsAsync().AsTask().GetAwaiter().GetResult();

    Assert.Contains(ids, "kbm-default");
    Assert.Contains(ids, "hotas-reference");
    Assert.Equal(2, ids.Count);
});

runner.Test("Profile bootstrapper repairs empty default profile files", () =>
{
    string root = Path.Combine(Path.GetTempPath(), $"SCOverlayTests-{Guid.NewGuid():N}");
    AppPaths paths = TestPaths(root);
    Directory.CreateDirectory(paths.ProfilesDirectory);
    File.WriteAllText(Path.Combine(paths.ProfilesDirectory, "kbm-default.json"), string.Empty);
    var store = new FileProfileStore(paths);

    ProfileBootstrapper.EnsureDefaultProfilesAsync(store).AsTask().GetAwaiter().GetResult();
    OverlayProfile repaired = store.LoadAsync("kbm-default").AsTask().GetAwaiter().GetResult();

    Assert.Equal("kbm-default", repaired.Id);
    Assert.True(repaired.InputSources.Count > 0);
    Assert.True(Directory.GetFiles(paths.ProfileBackupsDirectory, "kbm-default.*.invalid.json").Length == 1);
});

runner.Test("Profile migrator restores KBM alternates for old direct joystick axis profiles", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    OverlayProfile oldBrokenProfile = profile with
    {
        SchemaVersion = 1,
        InputSources = profile.InputSources
            .Select<InputSource, InputSource>(source => source.Id == "look-x"
                ? new JoystickAxisInputSource
                {
                    Id = "look-x",
                    DisplayName = "Look X",
                    DeviceId = "hid:stick",
                    AxisIndex = 3
                }
                : source)
            .ToArray()
    };

    OverlayProfile repaired = ProfileMigrator.Migrate(oldBrokenProfile);
    var keyboardSnapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("Right")] = true
        });
    var joystickSnapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>
        {
            [InputSnapshotKeys.JoystickAxis("hid:stick", 3)] = 0.5
        },
        new Dictionary<string, bool>());

    Assert.True(repaired.InputSources.OfType<CompositeAxisInputSource>().Any(source => source.Id == "look-x"));
    Assert.Equal(1.0, InputSourceEvaluator.Evaluate(repaired.InputSources, keyboardSnapshot).GetAxis("look-x"));
    Assert.Equal(0.5, InputSourceEvaluator.Evaluate(repaired.InputSources, joystickSnapshot).GetAxis("look-x"));
    Assert.True(ProfileValidator.Validate(repaired).IsValid);
});

runner.Test("Profile migrator does not restore removed KBM alternates for current profiles", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    OverlayProfile currentProfile = profile with
    {
        InputSources = profile.InputSources
            .Select<InputSource, InputSource>(source => source.Id == "look-x"
                ? new JoystickAxisInputSource
                {
                    Id = "look-x",
                    DisplayName = "Look X",
                    DeviceId = "hid:stick",
                    AxisIndex = 3
                }
                : source)
            .ToArray()
    };

    OverlayProfile migrated = ProfileMigrator.Migrate(currentProfile);

    Assert.True(migrated.InputSources.OfType<JoystickAxisInputSource>().Any(source => source.Id == "look-x"));
    Assert.False(migrated.InputSources.OfType<CompositeAxisInputSource>().Any(source => source.Id == "look-x"));
});

runner.Test("Profile migrator repairs transparent appearance colors from older saves", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault() with
    {
        Appearance = new AppearanceSettings
        {
            RingColor = new RgbaColor(0, 0, 0, 0),
            ActiveColor = new RgbaColor(24, 80, 140, 0),
            FrameColor = new RgbaColor(0, 0, 0, 0),
            FrameActiveColor = new RgbaColor(0, 0, 0, 0),
            PrimaryOpacity = 1.0,
            ActiveOpacity = 0.85,
            FramePrimaryOpacity = 1.0,
            FrameActiveOpacity = 1.0
        }
    };

    OverlayProfile migrated = ProfileMigrator.Migrate(profile);

    Assert.Equal((byte)255, migrated.Appearance.RingColor.A);
    Assert.Equal((byte)255, migrated.Appearance.ActiveColor.A);
    Assert.Equal((byte)255, migrated.Appearance.FrameColor.A);
    Assert.Equal((byte)255, migrated.Appearance.FrameActiveColor.A);
    Assert.Equal(migrated.Appearance.RingColor, migrated.Appearance.FrameColor);
    Assert.Equal(migrated.Appearance.ActiveColor, migrated.Appearance.FrameActiveColor);
    Assert.Equal(1.0, migrated.Appearance.PrimaryOpacity);
    Assert.Equal(0.85, migrated.Appearance.ActiveOpacity);
});

runner.Test("Profile editor creates safe profile copies", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    string id = ProfileEditor.CreateSafeProfileId("My SC Profile!", new[] { "my-sc-profile" });
    OverlayProfile copy = ProfileEditor.CreateCopy(profile, id, "My SC Profile!");

    Assert.Equal("my-sc-profile-2", id);
    Assert.Equal("My SC Profile!", copy.Name);
    Assert.Equal(profile.InputSources.Count, copy.InputSources.Count);
});

runner.Test("Profile editor keeps direct button actions active when adding another button binding", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var captured = new MouseButtonInputSource
    {
        Id = "mouse-x1",
        DisplayName = "Mouse X1",
        Button = "X1"
    };

    OverlayProfile updated = ProfileEditor.ReplaceInputSource(profile, "boost", captured);
    CompositeButtonInputSource boost = updated.InputSources.OfType<CompositeButtonInputSource>().Single(source => source.Id == "boost");
    var snapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("LeftShift")] = true
        });

    Assert.Equal("Boost", boost.DisplayName);
    Assert.Equal(2, boost.SourceIds.Count);
    Assert.True(InputSourceEvaluator.Evaluate(updated.InputSources, snapshot).GetButton("boost"));
    Assert.True(ProfileValidator.Validate(updated).IsValid);
});

runner.Test("Profile editor adds joystick axis as an alternate to a virtual KBM axis action", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var captured = new JoystickAxisInputSource
    {
        Id = "captured-axis",
        DisplayName = "Captured Axis",
        DeviceId = "hid:stick",
        AxisIndex = 2
    };

    OverlayProfile updated = ProfileEditor.ReplaceInputSource(profile, "look-x", captured);
    CompositeAxisInputSource lookX = updated.InputSources.OfType<CompositeAxisInputSource>().Single(source => source.Id == "look-x");
    StickWidgetDefinition lookWidget = updated.Widgets.OfType<StickWidgetDefinition>().Single(widget => widget.Id == "look-widget");

    Assert.Equal("Look X", lookX.DisplayName);
    Assert.Equal(2, lookX.Components.Count);
    Assert.Equal("look-x", lookWidget.XSourceId);
    Assert.True(ProfileValidator.Validate(updated).IsValid);

    var keyboardSnapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("Right")] = true
        });
    var joystickSnapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>
        {
            [InputSnapshotKeys.JoystickAxis("hid:stick", 2)] = -0.7
        },
        new Dictionary<string, bool>());

    Assert.Equal(1.0, InputSourceEvaluator.Evaluate(updated.InputSources, keyboardSnapshot).GetAxis("look-x"));
    Assert.Equal(-0.7, InputSourceEvaluator.Evaluate(updated.InputSources, joystickSnapshot).GetAxis("look-x"));
});

runner.Test("Profile editor inverts only selected controller axis bindings", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var captured = new JoystickAxisInputSource
    {
        Id = "captured-axis",
        DisplayName = "Captured Axis",
        DeviceId = "hid:stick",
        AxisIndex = 2
    };

    OverlayProfile mixed = ProfileEditor.ReplaceInputSource(profile, "look-x", captured);
    CompositeAxisInputSource lookX = mixed.InputSources.OfType<CompositeAxisInputSource>().Single(source => source.Id == "look-x");
    string controllerBindingId = lookX.Components
        .Select(component => component.SourceId)
        .Single(id => mixed.InputSources.OfType<JoystickAxisInputSource>().Any(source => source.Id == id));

    OverlayProfile inverted = ProfileEditor.SetJoystickAxisInverted(mixed, controllerBindingId, true);
    var keyboardSnapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("Right")] = true
        });
    var joystickSnapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>
        {
            [InputSnapshotKeys.JoystickAxis("hid:stick", 2)] = 0.7
        },
        new Dictionary<string, bool>());

    Assert.True(inverted.InputSources.OfType<JoystickAxisInputSource>().Single(source => source.Id == controllerBindingId).Invert);
    Assert.Equal(1.0, InputSourceEvaluator.Evaluate(inverted.InputSources, keyboardSnapshot).GetAxis("look-x"));
    Assert.Equal(-0.7, InputSourceEvaluator.Evaluate(inverted.InputSources, joystickSnapshot).GetAxis("look-x"));

    bool rejectedKeyboardAxis = false;
    try
    {
        string keyboardBindingId = lookX.Components
            .Select(component => component.SourceId)
            .Single(id => inverted.InputSources.OfType<VirtualButtonAxisInputSource>().Any(source => source.Id == id));
        ProfileEditor.SetJoystickAxisInverted(inverted, keyboardBindingId, true);
    }
    catch (InvalidOperationException)
    {
        rejectedKeyboardAxis = true;
    }

    Assert.True(rejectedKeyboardAxis);
});

runner.Test("Profile editor adds button captures as alternate button bindings", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var captured = new JoystickButtonInputSource
    {
        Id = "captured-button",
        DisplayName = "Captured Button",
        DeviceId = "hid:stick",
        ButtonIndex = 6
    };

    OverlayProfile updated = ProfileEditor.ReplaceInputSource(profile, "boost", captured);
    CompositeButtonInputSource boost = updated.InputSources.OfType<CompositeButtonInputSource>().Single(source => source.Id == "boost");
    var keyboardSnapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("LeftShift")] = true
        });
    var joystickSnapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.JoystickButton("hid:stick", 6)] = true
        });

    Assert.Equal(2, boost.SourceIds.Count);
    Assert.True(InputSourceEvaluator.Evaluate(updated.InputSources, keyboardSnapshot).GetButton("boost"));
    Assert.True(InputSourceEvaluator.Evaluate(updated.InputSources, joystickSnapshot).GetButton("boost"));
    Assert.True(ProfileValidator.Validate(updated).IsValid);
});

runner.Test("Profile editor removes an alternate axis binding and keeps the remaining binding", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var captured = new JoystickAxisInputSource
    {
        Id = "captured-axis",
        DisplayName = "Captured Axis",
        DeviceId = "hid:stick",
        AxisIndex = 2
    };
    OverlayProfile updated = ProfileEditor.ReplaceInputSource(profile, "look-x", captured);
    CompositeAxisInputSource composite = updated.InputSources.OfType<CompositeAxisInputSource>().Single(source => source.Id == "look-x");
    string joystickBindingId = composite.Components
        .Select(component => component.SourceId)
        .Single(id => updated.InputSources.OfType<JoystickAxisInputSource>().Any(source => source.Id == id));

    OverlayProfile pruned = ProfileEditor.RemoveInputBinding(updated, "look-x", joystickBindingId);
    VirtualButtonAxisInputSource lookX = pruned.InputSources.OfType<VirtualButtonAxisInputSource>().Single(source => source.Id == "look-x");
    var keyboardSnapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("Right")] = true
        });

    Assert.Equal("yaw-left", lookX.NegativeButtonSourceId);
    Assert.False(pruned.InputSources.Any(source => source.Id == joystickBindingId));
    Assert.Equal(1.0, InputSourceEvaluator.Evaluate(pruned.InputSources, keyboardSnapshot).GetAxis("look-x"));
    Assert.True(ProfileValidator.Validate(pruned).IsValid);
});

runner.Test("Profile editor removes an alternate button binding and keeps the remaining binding", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var captured = new MouseButtonInputSource
    {
        Id = "mouse-x1",
        DisplayName = "Mouse X1",
        Button = "X1"
    };
    OverlayProfile updated = ProfileEditor.ReplaceInputSource(profile, "boost", captured);
    CompositeButtonInputSource composite = updated.InputSources.OfType<CompositeButtonInputSource>().Single(source => source.Id == "boost");
    string mouseBindingId = composite.SourceIds
        .Single(id => updated.InputSources.OfType<MouseButtonInputSource>().Any(source => source.Id == id));

    OverlayProfile pruned = ProfileEditor.RemoveInputBinding(updated, "boost", mouseBindingId);
    KeyboardKeyInputSource boost = pruned.InputSources.OfType<KeyboardKeyInputSource>().Single(source => source.Id == "boost");
    var keyboardSnapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("LeftShift")] = true
        });

    Assert.Equal("LeftShift", boost.Key);
    Assert.False(pruned.InputSources.Any(source => source.Id == mouseBindingId));
    Assert.True(InputSourceEvaluator.Evaluate(pruned.InputSources, keyboardSnapshot).GetButton("boost"));
    Assert.True(ProfileValidator.Validate(pruned).IsValid);
});

runner.Test("Profile editor rejects removing the only action binding", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    bool rejected = false;

    try
    {
        ProfileEditor.RemoveInputBinding(profile, "boost", "boost");
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }

    Assert.True(rejected);
});

runner.Test("Profile editor can replace state text axis with a button", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateHotasReference();
    var captured = new JoystickButtonInputSource
    {
        Id = "captured-button",
        DisplayName = "Captured Button",
        DeviceId = "hid:stick",
        ButtonIndex = 4
    };

    OverlayProfile updated = ProfileEditor.ReplaceInputSource(profile, "brake-axis", captured);
    JoystickButtonInputSource brake = updated.InputSources.OfType<JoystickButtonInputSource>().Single(source => source.Id == "brake-axis");
    StateTextWidgetDefinition brakeWidget = updated.Widgets.OfType<StateTextWidgetDefinition>().Single(widget => widget.Id == "brake-widget");

    Assert.Equal("Brake Axis", brake.DisplayName);
    Assert.Equal(4, brake.ButtonIndex);
    Assert.Equal(InputSourceKind.Button, brakeWidget.SourceKind);
    Assert.True(ProfileValidator.Validate(updated).IsValid);
});

runner.Test("Profile editor rejects button captures for axis widgets", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var captured = new JoystickButtonInputSource
    {
        Id = "captured-button",
        DisplayName = "Captured Button",
        DeviceId = "hid:stick",
        ButtonIndex = 1
    };

    bool rejected = false;
    try
    {
        ProfileEditor.ReplaceInputSource(profile, "look-x", captured);
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }

    Assert.True(rejected);
});

runner.Test("Profile editor rejects captures with the wrong source kind", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var captured = new JoystickAxisInputSource
    {
        Id = "axis",
        DisplayName = "Axis",
        DeviceId = "hid:test",
        AxisIndex = 0
    };

    bool rejected = false;
    try
    {
        ProfileEditor.ReplaceInputSource(profile, "strafe-left", captured);
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }

    Assert.True(rejected);
});

runner.Test("Profile editor applies appearance with safe limits", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var appearance = new AppearanceSettings
    {
        PresetId = "test",
        RingColor = new RgbaColor(10, 20, 30, 200),
        ActiveColor = new RgbaColor(200, 80, 40, 255),
        FrameColor = new RgbaColor(1, 2, 3, 128),
        FrameActiveColor = new RgbaColor(4, 5, 6, 200),
        Opacity = 2.0,
        PrimaryOpacity = -1.0,
        ActiveOpacity = 3.0,
        FramePrimaryOpacity = -0.5,
        FrameActiveOpacity = 2.0,
        WidgetScale = 0.1
    };

    OverlayProfile updated = ProfileEditor.ApplyAppearance(profile, appearance);

    Assert.Equal("test", updated.Appearance.PresetId);
    Assert.Equal(1.0, updated.Appearance.Opacity);
    Assert.Equal(0.0, updated.Appearance.PrimaryOpacity);
    Assert.Equal(1.0, updated.Appearance.ActiveOpacity);
    Assert.Equal(0.0, updated.Appearance.FramePrimaryOpacity);
    Assert.Equal(1.0, updated.Appearance.FrameActiveOpacity);
    Assert.Equal(0.5, updated.Appearance.WidgetScale);
    Assert.True(ProfileValidator.Validate(updated).IsValid);
});

runner.Test("Profile editor strips embedded color alpha when applying appearance", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var appearance = new AppearanceSettings
    {
        RingColor = new RgbaColor(20, 30, 40, 0),
        ActiveColor = new RgbaColor(50, 60, 70, 128),
        FrameColor = new RgbaColor(0, 0, 0, 0),
        FrameActiveColor = new RgbaColor(80, 90, 100, 0),
        PrimaryOpacity = 0.7,
        ActiveOpacity = 0.8,
        FramePrimaryOpacity = 0.9,
        FrameActiveOpacity = 1.0
    };

    OverlayProfile updated = ProfileEditor.ApplyAppearance(profile, appearance);

    Assert.Equal(new RgbaColor(20, 30, 40, 255), updated.Appearance.RingColor);
    Assert.Equal(new RgbaColor(50, 60, 70, 255), updated.Appearance.ActiveColor);
    Assert.Equal(new RgbaColor(20, 30, 40, 255), updated.Appearance.FrameColor);
    Assert.Equal(new RgbaColor(80, 90, 100, 255), updated.Appearance.FrameActiveColor);
    Assert.Equal(0.7, updated.Appearance.PrimaryOpacity);
    Assert.Equal(0.8 * (128.0 / 255.0), updated.Appearance.ActiveOpacity);
    Assert.Equal(0.9, updated.Appearance.FramePrimaryOpacity);
    Assert.Equal(1.0, updated.Appearance.FrameActiveOpacity);
});

runner.Test("Profile editor applies per-widget appearance with safe limits", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();

    OverlayProfile updated = ProfileEditor.ApplyWidgetAppearance(
        profile,
        "roll-widget",
        x: 1300.0,
        y: -1300.0,
        scale: 5.0,
        opacity: -1.0,
        lineThickness: 99.0,
        throttleCornerRadius: 99.0,
        rollAssetId: RollAssets.Indicator,
        rollMaxRotationDegrees: 500.0,
        stateTextMaxedShakeEnabled: true);

    RollWidgetDefinition roll = updated.Widgets.OfType<RollWidgetDefinition>().Single(widget => widget.Id == "roll-widget");
    Assert.Equal(1000.0, roll.X);
    Assert.Equal(-1000.0, roll.Y);
    Assert.Equal(3.0, roll.Scale);
    Assert.Equal(0.0, roll.Opacity);
    Assert.Equal(20.0, roll.LineThickness);
    Assert.Equal(RollAssets.Indicator, roll.AssetId);
    Assert.Equal(RollRenderMode.Indicator, roll.RenderMode);
    Assert.Equal(180.0, roll.MaxRotationDegrees);
    Assert.True(ProfileValidator.Validate(updated).IsValid);
});

runner.Test("Profile editor resets widget appearance to defaults", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    profile = ProfileEditor.ApplyWidgetAppearance(
        profile,
        "roll-widget",
        x: 500.0,
        y: 300.0,
        scale: 2.0,
        opacity: 0.25,
        lineThickness: 12.0,
        throttleCornerRadius: 20.0,
        rollAssetId: RollAssets.Arrow,
        rollMaxRotationDegrees: 120.0,
        stateTextMaxedShakeEnabled: true);

    OverlayProfile updated = ProfileEditor.ResetWidgetAppearance(profile, "roll-widget");

    RollWidgetDefinition roll = updated.Widgets.OfType<RollWidgetDefinition>().Single(widget => widget.Id == "roll-widget");
    RollWidgetDefinition defaultRoll = DefaultProfiles.CreateKbmDefault().Widgets.OfType<RollWidgetDefinition>().Single(widget => widget.Id == "roll-widget");
    Assert.Equal(defaultRoll.X, roll.X);
    Assert.Equal(defaultRoll.Y, roll.Y);
    Assert.Equal(defaultRoll.Scale, roll.Scale);
    Assert.Equal(defaultRoll.Opacity, roll.Opacity);
    Assert.Equal(defaultRoll.LineThickness, roll.LineThickness);
    Assert.Equal(defaultRoll.AssetId, roll.AssetId);
    Assert.Equal(defaultRoll.RenderMode, roll.RenderMode);
    Assert.Equal(defaultRoll.MaxRotationDegrees, roll.MaxRotationDegrees);
});

runner.Test("Profile editor applies widget effects with safe limits", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var visualEffects = new EffectSettings
    {
        OutlineEnabled = false,
        ShadowEnabled = false,
        OutlineWidth = 99.0,
        ShadowWidth = 99.0
    };
    var textEffects = new EffectSettings
    {
        OutlineEnabled = true,
        ShadowEnabled = true,
        BackplateEnabled = true,
        BackplatePadding = 99.0,
        BackplateRadius = 99.0
    };

    OverlayProfile updated = ProfileEditor.ApplyWidgetEffects(profile, visualEffects, textEffects);

    Assert.True(updated.Widgets.All(widget => !widget.VisualEffects.OutlineEnabled));
    Assert.True(updated.Widgets.All(widget => !widget.VisualEffects.ShadowEnabled));
    Assert.True(updated.Widgets.All(widget => widget.VisualEffects.OutlineWidth == 16.0));
    Assert.True(updated.Widgets.All(widget => widget.VisualEffects.ShadowWidth == 32.0));
    Assert.True(updated.Widgets.All(widget => widget.TextEffects.BackplateEnabled));
    Assert.True(updated.Widgets.All(widget => widget.TextEffects.BackplatePadding == 64.0));
    Assert.True(updated.Widgets.All(widget => widget.TextEffects.BackplateRadius == 32.0));
    Assert.True(ProfileValidator.Validate(updated).IsValid);
});

runner.Test("Foundation input provider returns an empty snapshot", () =>
{
    var provider = new FoundationInputProvider();
    InputSnapshot snapshot = provider.Poll();
    Assert.Equal("Foundation Input Provider", provider.Name);
    Assert.Equal(0, snapshot.Axes.Count);
    Assert.Equal(0, snapshot.Buttons.Count);
});

runner.Test("Keyboard and mouse sources evaluate from normalized snapshot buttons", () =>
{
    var sources = new InputSource[]
    {
        new KeyboardKeyInputSource
        {
            Id = "boost",
            DisplayName = "Boost",
            Key = "LeftShift"
        },
        new MouseButtonInputSource
        {
            Id = "fire",
            DisplayName = "Fire",
            Button = "Left"
        }
    };

    var snapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("LeftShift")] = true,
            [InputSnapshotKeys.MouseButton("Left")] = true
        });

    EvaluatedInputState state = InputSourceEvaluator.Evaluate(sources, snapshot);

    Assert.True(state.GetButton("boost"));
    Assert.True(state.GetButton("fire"));
});

runner.Test("Virtual button axes convert opposite buttons into a signed axis", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var snapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("A")] = true,
            [InputSnapshotKeys.KeyboardButton("D")] = false
        });

    EvaluatedInputState state = InputSourceEvaluator.Evaluate(profile.InputSources, snapshot);

    Assert.Equal(-1.0, state.GetAxis("strafe-x"));
});

runner.Test("Joystick axis sources apply scale and inversion", () =>
{
    var sources = new InputSource[]
    {
        new JoystickAxisInputSource
        {
            Id = "roll",
            DisplayName = "Roll",
            DeviceId = "joystick:0",
            AxisIndex = 2,
            Scale = 0.5,
            Invert = true
        }
    };
    var snapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>
        {
            [InputSnapshotKeys.JoystickAxis("joystick:0", 2)] = 0.8
        },
        new Dictionary<string, bool>());

    EvaluatedInputState state = InputSourceEvaluator.Evaluate(sources, snapshot);

    Assert.Equal(-0.4, state.GetAxis("roll"));
});

runner.Test("Joystick axis sources resolve legacy HID device ids after restart", () =>
{
    var sources = new InputSource[]
    {
        new JoystickAxisInputSource
        {
            Id = "roll",
            DisplayName = "Roll",
            DeviceId = "hid:vid_231D&pid_0125:0",
            AxisIndex = 2
        }
    };
    var snapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>
        {
            [InputSnapshotKeys.JoystickAxis("hid:vid_231D&pid_0125:vkb_gladiator_nxt:91dd3bb0", 2)] = 0.8
        },
        new Dictionary<string, bool>());

    EvaluatedInputState state = InputSourceEvaluator.Evaluate(sources, snapshot);

    Assert.Equal(0.8, state.GetAxis("roll"));
});

runner.Test("Joystick button sources resolve legacy HID device ids after restart", () =>
{
    var sources = new InputSource[]
    {
        new JoystickButtonInputSource
        {
            Id = "boost",
            DisplayName = "Boost",
            DeviceId = "hid:vid_231D&pid_0125:0",
            ButtonIndex = 4
        }
    };
    var snapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.JoystickButton("hid:vid_231D&pid_0125:vkb_gladiator_nxt:91dd3bb0", 4)] = true
        });

    EvaluatedInputState state = InputSourceEvaluator.Evaluate(sources, snapshot);

    Assert.True(state.GetButton("boost"));
});

runner.Test("Composite axes combine axis and button components with clamping", () =>
{
    var sources = new InputSource[]
    {
        new KeyboardKeyInputSource
        {
            Id = "boost-button",
            DisplayName = "Boost",
            Key = "LeftShift"
        },
        new JoystickAxisInputSource
        {
            Id = "base-axis",
            DisplayName = "Base",
            DeviceId = "joystick:0",
            AxisIndex = 0
        },
        new CompositeAxisInputSource
        {
            Id = "combined",
            DisplayName = "Combined",
            Components = new[]
            {
                new AxisComponent
                {
                    SourceId = "base-axis",
                    SourceKind = InputSourceKind.Axis,
                    Scale = 0.75
                },
                new AxisComponent
                {
                    SourceId = "boost-button",
                    SourceKind = InputSourceKind.Button,
                    Scale = 0.5
                }
            }
        }
    };
    var snapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>
        {
            [InputSnapshotKeys.JoystickAxis("joystick:0", 0)] = 0.8
        },
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("LeftShift")] = true
        });

    EvaluatedInputState state = InputSourceEvaluator.Evaluate(sources, snapshot);

    Assert.Equal(1.0, state.GetAxis("combined"));
});

runner.Test("Overlay state engine emits typed widget states for default profile", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var snapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("D")] = true,
            [InputSnapshotKeys.KeyboardButton("W")] = true,
            [InputSnapshotKeys.KeyboardButton("LeftShift")] = true
        });
    var engine = new OverlayStateEngine();

    OverlayState state = engine.BuildState(profile, snapshot);

    Assert.Equal(profile.Id, state.ProfileId);
    Assert.True(state.Connected);
    Assert.Equal(6, state.Widgets.Count);
    Assert.True(state.Widgets.OfType<StickWidgetState>().Any(widget => widget.Id == "strafe-widget"));
    Assert.True(state.Widgets.OfType<ThrottleWidgetState>().Any(widget => widget.Id == "throttle-widget"));
    Assert.True(state.Widgets.OfType<RollWidgetState>().Any(widget => widget.Id == "roll-widget"));
    Assert.True(state.Widgets.OfType<StateTextWidgetState>().Any(widget => widget.Id == "boost-widget" && widget.Active));
});

runner.Test("Overlay state engine applies deadzone and zero snap", () =>
{
    var profile = new OverlayProfile
    {
        Id = "deadzone-test",
        Name = "Deadzone Test",
        InputSources = new InputSource[]
        {
            new JoystickAxisInputSource
            {
                Id = "axis",
                DisplayName = "Axis",
                DeviceId = "joystick:0",
                AxisIndex = 0
            }
        },
        Widgets = new WidgetDefinition[]
        {
            new ThrottleWidgetDefinition
            {
                Id = "throttle",
                DisplayName = "Throttle",
                SourceId = "axis",
                Tuning = new AxisTuning
                {
                    Deadzone = 0.10,
                    InputNoiseGate = 0.02,
                    ZeroSnapThreshold = 0.001
                }
            }
        }
    };
    var snapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>
        {
            [InputSnapshotKeys.JoystickAxis("joystick:0", 0)] = 0.05
        },
        new Dictionary<string, bool>());

    OverlayState state = new OverlayStateEngine().BuildState(profile, snapshot);
    var throttle = state.Widgets.OfType<ThrottleWidgetState>().Single();

    Assert.Equal(0.0, throttle.RawValue);
    Assert.Equal(0.0, throttle.Value);
    Assert.True(throttle.Connected);
});

runner.Test("Overlay state engine reports throttle fill from center", () =>
{
    var profile = new OverlayProfile
    {
        Id = "throttle-center-test",
        Name = "Throttle Center Test",
        InputSources = new InputSource[]
        {
            new JoystickAxisInputSource
            {
                Id = "axis",
                DisplayName = "Axis",
                DeviceId = "joystick:0",
                AxisIndex = 0
            }
        },
        Widgets = new WidgetDefinition[]
        {
            new ThrottleWidgetDefinition
            {
                Id = "throttle",
                DisplayName = "Throttle",
                SourceId = "axis",
                Tuning = new AxisTuning
                {
                    InputNoiseGate = 0,
                    ValueSmoothingSpeed = 0,
                    MaxThrowRatio = 0.8
                }
            }
        }
    };

    OverlayState idleState = new OverlayStateEngine().BuildState(profile, AxisSnapshot(DateTimeOffset.UtcNow, 0.0));
    ThrottleWidgetState idleThrottle = idleState.Widgets.OfType<ThrottleWidgetState>().Single();
    Assert.Equal(0.0, idleThrottle.Value);
    Assert.Equal(0.0, idleThrottle.FillRatio);

    OverlayState forwardState = new OverlayStateEngine().BuildState(profile, AxisSnapshot(DateTimeOffset.UtcNow, 1.0));
    ThrottleWidgetState forwardThrottle = forwardState.Widgets.OfType<ThrottleWidgetState>().Single();
    Assert.Equal(0.8, forwardThrottle.Value);
    Assert.Equal(1.0, forwardThrottle.FillRatio);

    OverlayState reverseState = new OverlayStateEngine().BuildState(profile, AxisSnapshot(DateTimeOffset.UtcNow, -0.5));
    ThrottleWidgetState reverseThrottle = reverseState.Widgets.OfType<ThrottleWidgetState>().Single();
    Assert.Equal(-0.4, reverseThrottle.Value);
    Assert.Equal(0.5, reverseThrottle.FillRatio);
});

runner.Test("Overlay state engine smooths axis values across samples", () =>
{
    var profile = new OverlayProfile
    {
        Id = "smoothing-test",
        Name = "Smoothing Test",
        InputSources = new InputSource[]
        {
            new JoystickAxisInputSource
            {
                Id = "axis",
                DisplayName = "Axis",
                DeviceId = "joystick:0",
                AxisIndex = 0
            }
        },
        Widgets = new WidgetDefinition[]
        {
            new ThrottleWidgetDefinition
            {
                Id = "throttle",
                DisplayName = "Throttle",
                SourceId = "axis",
                Tuning = new AxisTuning
                {
                    InputNoiseGate = 0,
                    ValueSmoothingSpeed = 2.0,
                    MaxThrowRatio = 1.0
                }
            }
        }
    };
    var engine = new OverlayStateEngine();
    DateTimeOffset start = DateTimeOffset.UtcNow;
    engine.BuildState(profile, AxisSnapshot(start, 0.0));

    OverlayState state = engine.BuildState(profile, AxisSnapshot(start.AddMilliseconds(100), 1.0));
    var throttle = state.Widgets.OfType<ThrottleWidgetState>().Single();

    Assert.True(throttle.Value > 0.0);
    Assert.True(throttle.Value < 1.0);
});

runner.Test("Overlay state engine reports joystick widgets disconnected when source is absent", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateHotasReference();
    OverlayState state = new OverlayStateEngine().BuildState(profile, InputSnapshot.Empty());

    Assert.False(state.Connected);
    Assert.True(state.Widgets.All(widget => !widget.Connected));
});

runner.Test("Overlay state engine computes button and axis state text intensity", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var snapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("LeftShift")] = true,
            [InputSnapshotKeys.KeyboardButton("X")] = false
        });

    OverlayState state = new OverlayStateEngine().BuildState(profile, snapshot);
    var boost = state.Widgets.OfType<StateTextWidgetState>().Single(widget => widget.Id == "boost-widget");
    var brake = state.Widgets.OfType<StateTextWidgetState>().Single(widget => widget.Id == "brake-widget");

    Assert.True(boost.Active);
    Assert.Equal(1.0, boost.Intensity);
    Assert.False(brake.Active);
    Assert.Equal(0.0, brake.Intensity);
});

runner.Test("Overlay state engine emits maxed shake for state text", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var snapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("LeftShift")] = true
        });

    OverlayState state = new OverlayStateEngine().BuildState(profile, snapshot);
    StateTextWidgetState boost = state.Widgets.OfType<StateTextWidgetState>().Single(widget => widget.Id == "boost-widget");
    Assert.True(boost.ShakeIntensity > 0.9);

    OverlayProfile disabled = profile with
    {
        Widgets = profile.Widgets
            .Select(widget => widget is StateTextWidgetDefinition stateText && stateText.Id == "boost-widget"
                ? stateText with
                {
                    Tuning = stateText.Tuning with
                    {
                        MaxedShakeEnabled = false
                    }
                }
                : widget)
            .ToArray()
    };

    OverlayState disabledState = new OverlayStateEngine().BuildState(disabled, snapshot);
    StateTextWidgetState disabledBoost = disabledState.Widgets.OfType<StateTextWidgetState>().Single(widget => widget.Id == "boost-widget");
    Assert.Equal(0.0, disabledBoost.ShakeIntensity);
});

runner.Test("Overlay state engine treats state text axes as unipolar intensity", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateHotasReference();
    var idleSnapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>
        {
            [InputSnapshotKeys.JoystickAxis("joystick:1", 7)] = -1.0
        },
        new Dictionary<string, bool>());

    var activeSnapshot = new InputSnapshot(
        idleSnapshot.Timestamp.AddMilliseconds(100),
        new Dictionary<string, double>
        {
            [InputSnapshotKeys.JoystickAxis("joystick:1", 7)] = 1.0
        },
        new Dictionary<string, bool>());

    var engine = new OverlayStateEngine();
    OverlayState idleState = engine.BuildState(profile, idleSnapshot);
    StateTextWidgetState idleBrake = idleState.Widgets.OfType<StateTextWidgetState>().Single(widget => widget.Id == "brake-widget");
    Assert.False(idleBrake.Active);
    Assert.Equal(0.0, idleBrake.Intensity);

    OverlayState activeState = engine.BuildState(profile, activeSnapshot);
    StateTextWidgetState activeBrake = activeState.Widgets.OfType<StateTextWidgetState>().Single(widget => widget.Id == "brake-widget");
    Assert.True(activeBrake.Active);
    Assert.True(activeBrake.Intensity > 0.0);
});

runner.Test("Overlay state engine applies profile appearance", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault() with
    {
        Appearance = new AppearanceSettings
        {
            PresetId = "test",
            RingColor = new RgbaColor(10, 20, 30, 200),
            ActiveColor = new RgbaColor(200, 80, 40, 255),
            FrameColor = new RgbaColor(40, 50, 60, 220),
            FrameActiveColor = new RgbaColor(80, 90, 100, 240),
            Opacity = 0.5,
            WidgetScale = 1.25
        }
    };
    var snapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("W")] = true
        });

    OverlayState state = new OverlayStateEngine().BuildState(profile, snapshot);
    ThrottleWidgetState throttle = state.Widgets.OfType<ThrottleWidgetState>().Single();

    Assert.Equal(56.25, throttle.Width);
    Assert.Equal(162.5, throttle.Height);
    Assert.Equal(56.25, throttle.Y);
    Assert.Equal((byte)10, throttle.RingColor.R);
    Assert.Equal((byte)100, throttle.RingColor.A);
    Assert.Equal((byte)128, throttle.ActiveColor.A);
    Assert.Equal((byte)40, throttle.FrameColor.R);
    Assert.Equal((byte)110, throttle.FrameColor.A);
    Assert.Equal((byte)120, throttle.FrameActiveColor.A);
});

runner.Test("Overlay state engine applies widget appearance", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault() with
    {
        Appearance = new AppearanceSettings
        {
            RingColor = new RgbaColor(10, 20, 30, 200),
            ActiveColor = new RgbaColor(200, 80, 40, 255),
            FrameColor = new RgbaColor(90, 100, 110, 240),
            FrameActiveColor = new RgbaColor(120, 130, 140, 255),
            Opacity = 0.5,
            PrimaryOpacity = 0.25,
            ActiveOpacity = 0.75,
            FramePrimaryOpacity = 1.0,
            FrameActiveOpacity = 0.5,
            WidgetScale = 1.0
        },
        Widgets = DefaultProfiles.CreateKbmDefault().Widgets
            .Select(widget => widget.Id == "roll-widget"
                ? ((RollWidgetDefinition)widget) with
                {
                    Scale = 1.5,
                    Opacity = 0.8,
                    LineThickness = 7.0,
                    RenderMode = RollRenderMode.Indicator,
                    MaxRotationDegrees = 90.0
                }
                : widget)
            .ToArray()
    };
    var snapshot = new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("E")] = true
        });

    OverlayState state = new OverlayStateEngine().BuildState(profile, snapshot);
    RollWidgetState roll = state.Widgets.OfType<RollWidgetState>().Single();

    Assert.Equal(243.75, roll.Width);
    Assert.Equal(168.75, roll.Height);
    Assert.Equal(7.0, roll.LineThickness);
    Assert.Equal(RollRenderMode.Indicator, roll.RenderMode);
    Assert.Equal(90.0, roll.RotationDegrees);
    Assert.Equal((byte)20, roll.RingColor.A);
    Assert.Equal((byte)77, roll.ActiveColor.A);
    Assert.Equal((byte)51, roll.FrameDisplayColor.A);
});

runner.Test("Overlay state engine keeps frame visible when input primary alpha is hidden", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault() with
    {
        Appearance = new AppearanceSettings
        {
            RingColor = new RgbaColor(10, 20, 30, 255),
            ActiveColor = new RgbaColor(200, 80, 40, 255),
            FrameColor = new RgbaColor(50, 60, 70, 255),
            FrameActiveColor = new RgbaColor(150, 160, 170, 255),
            Opacity = 1.0,
            PrimaryOpacity = 0.0,
            ActiveOpacity = 1.0,
            FramePrimaryOpacity = 1.0,
            FrameActiveOpacity = 1.0
        }
    };

    OverlayState state = new OverlayStateEngine().BuildState(profile, new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>()));
    StickWidgetState strafe = state.Widgets.OfType<StickWidgetState>().Single(widget => widget.Id == "strafe-widget");

    Assert.Equal((byte)0, strafe.DisplayColor.A);
    Assert.Equal((byte)255, strafe.FrameDisplayColor.A);
    Assert.Equal((byte)220, strafe.VisualEffects.OutlineColor.A);
});

runner.Test("Overlay state engine applies blended display alpha to effects", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault() with
    {
        Appearance = new AppearanceSettings
        {
            RingColor = new RgbaColor(10, 20, 30, 255),
            ActiveColor = new RgbaColor(200, 80, 40, 255),
            Opacity = 1.0,
            PrimaryOpacity = 0.0,
            ActiveOpacity = 1.0
        },
        Widgets = DefaultProfiles.CreateKbmDefault().Widgets
            .Select(widget => widget.Id == "boost-widget"
                ? ((StateTextWidgetDefinition)widget) with
                {
                    TextEffects = new EffectSettings
                    {
                        OutlineEnabled = true,
                        OutlineColor = new RgbaColor(0, 0, 0, 255),
                        ShadowEnabled = true,
                        ShadowColor = new RgbaColor(0, 0, 0, 255),
                        BackplateEnabled = true,
                        BackplateColor = new RgbaColor(0, 0, 0, 255)
                    }
                }
                : widget)
            .ToArray()
    };

    OverlayState state = new OverlayStateEngine().BuildState(profile, new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>()));
    StateTextWidgetState boost = state.Widgets.OfType<StateTextWidgetState>().Single(widget => widget.Id == "boost-widget");

    Assert.Equal((byte)0, boost.DisplayColor.A);
    Assert.Equal((byte)0, boost.TextEffects.OutlineColor.A);
    Assert.Equal((byte)0, boost.TextEffects.ShadowColor.A);
    Assert.Equal((byte)0, boost.TextEffects.BackplateColor.A);
});

runner.Test("Overlay state sampler polls input and publishes renderer-neutral state", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    var provider = new FakeInputProvider(new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("W")] = true
        }));
    var sampler = new OverlayStateSampler(profile, provider, new OverlayStateEngine());
    int updates = 0;
    sampler.StateUpdated += (_, _) => updates++;

    OverlayState state = sampler.SampleOnce();

    Assert.Equal(1, provider.PollCount);
    Assert.Equal(1, updates);
    Assert.Equal(profile.Id, sampler.CurrentState.ProfileId);
    Assert.True(state.Widgets.OfType<ThrottleWidgetState>().Single().Value > 0.0);
});

runner.Test("Browser source URL uses runtime settings", () =>
{
    int port = BrowserSourceServer.FindAvailablePort();
    var server = new BrowserSourceServer(new RuntimeSettings
    {
        BrowserSourcePort = port
    });
    Assert.Equal($"http://127.0.0.1:{port}/obs.html", server.Url);
    Assert.False(server.IsRunning);
    server.Start();
    Assert.True(server.IsRunning);
    server.Stop();
    Assert.False(server.IsRunning);
});

runner.Test("Browser source serves OBS page, state JSON, and assets", () =>
{
    int port = BrowserSourceServer.FindAvailablePort();
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault();
    OverlayState state = new OverlayStateEngine().BuildState(profile, new InputSnapshot(
        DateTimeOffset.UtcNow,
        new Dictionary<string, double>(),
        new Dictionary<string, bool>
        {
            [InputSnapshotKeys.KeyboardButton("LeftShift")] = true
        }));
    using var server = new BrowserSourceServer(new RuntimeSettings
    {
        BrowserSourcePort = port
    }, state);
    using var client = new HttpClient
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}")
    };

    server.Start();
    string html = client.GetStringAsync("/obs.html").GetAwaiter().GetResult();
    string json = client.GetStringAsync("/state").GetAwaiter().GetResult();
    string assets = client.GetStringAsync("/assets").GetAwaiter().GetResult();
    string svg = client.GetStringAsync("/assets/roll-indicator-default.svg").GetAwaiter().GetResult();
    byte[] gladius = client.GetByteArrayAsync("/assets/roll-indicator-gladius.png").GetAwaiter().GetResult();
    byte[] arrow = client.GetByteArrayAsync("/assets/roll-indicator-arrow.png").GetAwaiter().GetResult();
    server.UpdateState(OverlayState.Empty("updated-profile"));
    string updatedJson = client.GetStringAsync("/state").GetAwaiter().GetResult();

    Assert.True(html.Contains("<canvas", StringComparison.OrdinalIgnoreCase));
    Assert.True(html.Contains("fetch('/state'", StringComparison.Ordinal));
    Assert.True(html.Contains("function tintImage", StringComparison.Ordinal));
    Assert.True(json.Contains("\"profileId\":\"kbm-default\"", StringComparison.Ordinal));
    Assert.True(json.Contains("\"type\":\"stateText\"", StringComparison.Ordinal));
    Assert.True(assets.Contains("roll-indicator-default", StringComparison.Ordinal));
    Assert.True(assets.Contains("roll-indicator-gladius", StringComparison.Ordinal));
    Assert.True(assets.Contains("roll-indicator-arrow", StringComparison.Ordinal));
    Assert.True(svg.Contains("<svg", StringComparison.OrdinalIgnoreCase));
    Assert.True(gladius.Length > 1000);
    Assert.True(arrow.Length > 1000);
    Assert.True(updatedJson.Contains("\"profileId\":\"updated-profile\"", StringComparison.Ordinal));
});

return runner.Finish();

static AppPaths TestPaths(string root)
{
    return new AppPaths(
        DataRoot: root,
        ProfilesDirectory: Path.Combine(root, "profiles"),
        LogsDirectory: Path.Combine(root, "logs"),
        AssetsDirectory: Path.Combine(root, "assets"),
        ProfileBackupsDirectory: Path.Combine(root, "profile-backups"),
        SettingsBackupsDirectory: Path.Combine(root, "settings-backups"),
        DiagnosticsDirectory: Path.Combine(root, "diagnostics"));
}

static InputSnapshot AxisSnapshot(DateTimeOffset timestamp, double value)
{
    return new InputSnapshot(
        timestamp,
        new Dictionary<string, double>
        {
            [InputSnapshotKeys.JoystickAxis("joystick:0", 0)] = value
        },
        new Dictionary<string, bool>());
}

internal sealed class TestRunner
{
    private int passed;
    private int failed;

    public void Test(string name, Action action)
    {
        try
        {
            action();
            passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            failed++;
            Console.WriteLine($"FAIL {name}");
            Console.WriteLine(exception);
        }
    }

    public int Finish()
    {
        Console.WriteLine();
        Console.WriteLine($"Passed: {passed}");
        Console.WriteLine($"Failed: {failed}");
        return failed == 0 ? 0 : 1;
    }
}

internal static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }
    }

    public static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    public static void False(bool value)
    {
        if (value)
        {
            throw new InvalidOperationException("Expected false.");
        }
    }

    public static void NotNull<T>(T? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("Expected non-null value.");
        }
    }

    public static void Contains<T>(IEnumerable<T> values, T expected)
    {
        if (!values.Contains(expected))
        {
            throw new InvalidOperationException($"Expected collection to contain {expected}.");
        }
    }
}

internal sealed class FakeInputProvider : IInputProvider
{
    private readonly InputSnapshot snapshot;

    public FakeInputProvider(InputSnapshot snapshot)
    {
        this.snapshot = snapshot;
    }

    public string Name => "Fake Input Provider";

    public int PollCount { get; private set; }

    public ValueTask<IReadOnlyList<InputDeviceInfo>> EnumerateDevicesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InputDeviceInfo> devices = Array.Empty<InputDeviceInfo>();
        return ValueTask.FromResult(devices);
    }

    public InputSnapshot Poll()
    {
        PollCount++;
        return snapshot;
    }

    public ValueTask<InputCaptureResult> CaptureNextBindingAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

internal sealed class FakeWinMmApi : WinMmJoystickProvider.IWinMmApi
{
    public int CapabilitiesCallCount { get; private set; }

    public int ActiveDevicePositionCallCount { get; private set; }

    public int InactiveDevicePositionCallCount { get; private set; }

    public uint GetDeviceCount() => 2;

    public bool TryGetCapabilities(uint index, out NativeMethods.JoyCaps capabilities)
    {
        CapabilitiesCallCount++;
        capabilities = new NativeMethods.JoyCaps
        {
            ProductName = index == 0 ? "Test Joystick" : "Microsoft PC-joystick driver",
            XMin = 0,
            XMax = 65535,
            YMin = 0,
            YMax = 65535,
            NumButtons = 4,
            NumAxes = 2,
            RegistryKey = string.Empty,
            OemVxD = string.Empty
        };
        return true;
    }

    public bool TryGetPosition(uint index, out NativeMethods.JoyInfoEx position)
    {
        if (index == 0)
        {
            ActiveDevicePositionCallCount++;
        }
        else
        {
            InactiveDevicePositionCallCount++;
        }

        position = new NativeMethods.JoyInfoEx
        {
            XPosition = 32767,
            YPosition = 32767
        };
        return true;
    }
}
