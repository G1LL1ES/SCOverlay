using SCOverlay.Core.Domain;

namespace SCOverlay.Core.Profiles;

public static class DefaultProfiles
{
    public static OverlayProfile CreateKbmDefault()
    {
        var sources = new InputSource[]
        {
            Key("strafe-left", "Strafe Left", "A"),
            Key("strafe-right", "Strafe Right", "D"),
            Key("strafe-up", "Strafe Up", "Space"),
            Key("strafe-down", "Strafe Down", "LeftCtrl"),
            Key("pitch-up", "Pitch Up", "Down"),
            Key("pitch-down", "Pitch Down", "Up"),
            Key("yaw-left", "Yaw Left", "Left"),
            Key("yaw-right", "Yaw Right", "Right"),
            Key("throttle-forward", "Throttle Forward", "W"),
            Key("throttle-backward", "Throttle Backward", "S"),
            Key("roll-left", "Roll Left", "Q"),
            Key("roll-right", "Roll Right", "E"),
            Key("boost", "Boost", "LeftShift"),
            Key("brake", "Brake", "X"),
            ButtonAxis("strafe-x", "Strafe X", "strafe-left", "strafe-right"),
            ButtonAxis("strafe-y", "Strafe Y", "strafe-down", "strafe-up"),
            ButtonAxis("look-x", "Look X", "yaw-left", "yaw-right"),
            ButtonAxis("look-y", "Look Y", "pitch-down", "pitch-up"),
            ButtonAxis("throttle", "Throttle", "throttle-backward", "throttle-forward"),
            ButtonAxis("roll", "Roll", "roll-left", "roll-right")
        };

        return new OverlayProfile
        {
            Id = "kbm-default",
            Name = "Keyboard and Mouse Default",
            InputSources = sources,
            Widgets = StandardWidgets()
        };
    }

    public static OverlayProfile CreateHotasReference()
    {
        var sources = new InputSource[]
        {
            Axis("strafe-x", "Strafe X", "joystick:0", 0),
            Axis("strafe-y", "Strafe Y", "joystick:0", 1),
            Axis("look-x", "Yaw", "joystick:1", 0),
            Axis("look-y", "Pitch", "joystick:1", 1),
            Axis("throttle", "Throttle", "joystick:2", 0),
            Axis("roll", "Roll", "joystick:2", 1),
            Axis("brake-axis", "Brake Axis", "joystick:1", 7),
            JoyButton("boost", "Boost", "joystick:0", 0),
            JoyButton("brake", "Brake Button", "joystick:0", 1)
        };

        return new OverlayProfile
        {
            Id = "hotas-reference",
            Name = "HOTAS Reference",
            InputSources = sources,
            Widgets = StandardWidgets(brakeSourceId: "brake-axis", brakeSourceKind: InputSourceKind.Axis)
        };
    }

    public static IReadOnlyList<OverlayProfile> CreateAll()
    {
        return new[]
        {
            CreateKbmDefault(),
            CreateHotasReference()
        };
    }

    private static IReadOnlyList<WidgetDefinition> StandardWidgets(
        string brakeSourceId = "brake",
        InputSourceKind brakeSourceKind = InputSourceKind.Button)
    {
        return new WidgetDefinition[]
        {
            new StickWidgetDefinition
            {
                Id = "strafe-widget",
                DisplayName = "Strafe",
                X = -220,
                Y = 0,
                XSourceId = "strafe-x",
                YSourceId = "strafe-y",
                Labels = new DirectionLabels
                {
                    Up = "U",
                    Down = "D",
                    Left = "L",
                    Right = "R"
                }
            },
            new StickWidgetDefinition
            {
                Id = "look-widget",
                DisplayName = "Look",
                X = 220,
                Y = 0,
                XSourceId = "look-x",
                YSourceId = "look-y",
                Labels = new DirectionLabels
                {
                    Up = "P+",
                    Down = "P-",
                    Left = "Y-",
                    Right = "Y+"
                }
            },
            new ThrottleWidgetDefinition
            {
                Id = "throttle-widget",
                DisplayName = "Throttle",
                X = 0,
                Y = 45,
                SourceId = "throttle",
                Labels = new VerticalLabels
                {
                    Top = "+",
                    Bottom = "-"
                }
            },
            new RollWidgetDefinition
            {
                Id = "roll-widget",
                DisplayName = "Roll",
                X = 0,
                Y = -120,
                SourceId = "roll"
            },
            new StateTextWidgetDefinition
            {
                Id = "boost-widget",
                DisplayName = "Boost",
                X = -110,
                Y = 165,
                Text = "BOOST",
                SourceId = "boost",
                SourceKind = InputSourceKind.Button
            },
            new StateTextWidgetDefinition
            {
                Id = "brake-widget",
                DisplayName = "Brake",
                X = 110,
                Y = 165,
                Text = "BRAKE",
                SourceId = brakeSourceId,
                SourceKind = brakeSourceKind
            }
        };
    }

    private static KeyboardKeyInputSource Key(string id, string name, string key)
    {
        return new KeyboardKeyInputSource
        {
            Id = id,
            DisplayName = name,
            Key = key
        };
    }

    private static VirtualButtonAxisInputSource ButtonAxis(string id, string name, string negative, string positive)
    {
        return new VirtualButtonAxisInputSource
        {
            Id = id,
            DisplayName = name,
            NegativeButtonSourceId = negative,
            PositiveButtonSourceId = positive
        };
    }

    private static JoystickAxisInputSource Axis(string id, string name, string deviceId, int axisIndex)
    {
        return new JoystickAxisInputSource
        {
            Id = id,
            DisplayName = name,
            DeviceId = deviceId,
            AxisIndex = axisIndex
        };
    }

    private static JoystickButtonInputSource JoyButton(string id, string name, string deviceId, int buttonIndex)
    {
        return new JoystickButtonInputSource
        {
            Id = id,
            DisplayName = name,
            DeviceId = deviceId,
            ButtonIndex = buttonIndex
        };
    }
}
