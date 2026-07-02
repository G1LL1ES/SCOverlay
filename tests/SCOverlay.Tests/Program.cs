using System.Text.Json;
using SCOverlay.BrowserSource;
using SCOverlay.Core.Application;
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
        AssetsDirectory: Path.Combine(root, "assets"));
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
        Opacity = 2.0,
        WidgetScale = 0.1
    };

    OverlayProfile updated = ProfileEditor.ApplyAppearance(profile, appearance);

    Assert.Equal("test", updated.Appearance.PresetId);
    Assert.Equal(1.0, updated.Appearance.Opacity);
    Assert.Equal(0.5, updated.Appearance.WidgetScale);
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

runner.Test("Overlay state engine applies profile appearance", () =>
{
    OverlayProfile profile = DefaultProfiles.CreateKbmDefault() with
    {
        Appearance = new AppearanceSettings
        {
            PresetId = "test",
            RingColor = new RgbaColor(10, 20, 30, 200),
            ActiveColor = new RgbaColor(200, 80, 40, 255),
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
    server.UpdateState(OverlayState.Empty("updated-profile"));
    string updatedJson = client.GetStringAsync("/state").GetAwaiter().GetResult();

    Assert.True(html.Contains("<canvas", StringComparison.OrdinalIgnoreCase));
    Assert.True(html.Contains("fetch('/state'", StringComparison.Ordinal));
    Assert.True(json.Contains("\"profileId\":\"kbm-default\"", StringComparison.Ordinal));
    Assert.True(json.Contains("\"type\":\"stateText\"", StringComparison.Ordinal));
    Assert.True(assets.Contains("roll-indicator-default", StringComparison.Ordinal));
    Assert.True(svg.Contains("<svg", StringComparison.OrdinalIgnoreCase));
    Assert.True(updatedJson.Contains("\"profileId\":\"updated-profile\"", StringComparison.Ordinal));
});

return runner.Finish();

static AppPaths TestPaths(string root)
{
    return new AppPaths(
        DataRoot: root,
        ProfilesDirectory: Path.Combine(root, "profiles"),
        LogsDirectory: Path.Combine(root, "logs"),
        AssetsDirectory: Path.Combine(root, "assets"));
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
