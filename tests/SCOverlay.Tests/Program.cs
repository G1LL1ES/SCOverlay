using System.Text.Json;
using SCOverlay.BrowserSource;
using SCOverlay.Core.Application;
using SCOverlay.Core.Domain;
using SCOverlay.Core.Input;
using SCOverlay.Core.Profiles;
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

runner.Test("Browser source URL uses runtime settings", () =>
{
    var server = new BrowserSourceServer(new RuntimeSettings());
    Assert.Equal("http://127.0.0.1:8765/obs.html", server.Url);
    Assert.False(server.IsRunning);
    server.Start();
    Assert.True(server.IsRunning);
    server.Stop();
    Assert.False(server.IsRunning);
});

return runner.Finish();

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
