using SCOverlay.Core.Domain;
using SCOverlay.Core.Input;
using SCOverlay.Core.Profiles;

namespace SCOverlay.Core.Rendering;

public sealed class OverlayStateEngine : IOverlayStateEngine
{
    private readonly Dictionary<string, WidgetMemory> memory = new(StringComparer.Ordinal);
    private DateTimeOffset? previousTimestamp;

    public OverlayState BuildState(OverlayProfile profile, InputSnapshot inputSnapshot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(inputSnapshot);

        EvaluatedInputState inputState = InputSourceEvaluator.Evaluate(profile.InputSources, inputSnapshot);
        IReadOnlyDictionary<string, InputSource> sourceMap = profile.InputSources
            .Where(source => !string.IsNullOrWhiteSpace(source.Id))
            .GroupBy(source => source.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        double elapsedSeconds = CalculateElapsedSeconds(inputSnapshot.Timestamp);
        var widgets = new List<WidgetState>(profile.Widgets.Count);

        foreach (WidgetDefinition widget in profile.Widgets)
        {
            widgets.Add(widget switch
            {
                StickWidgetDefinition stick => BuildStick(stick, inputState, inputSnapshot, sourceMap, elapsedSeconds),
                ThrottleWidgetDefinition throttle => BuildThrottle(throttle, inputState, inputSnapshot, sourceMap, elapsedSeconds),
                RollWidgetDefinition roll => BuildRoll(roll, inputState, inputSnapshot, sourceMap, elapsedSeconds),
                StateTextWidgetDefinition stateText => BuildStateText(stateText, inputState, inputSnapshot, sourceMap, elapsedSeconds),
                _ => throw new InvalidOperationException($"Unsupported widget type {widget.GetType().Name}.")
            });
        }

        previousTimestamp = inputSnapshot.Timestamp;
        bool connected = widgets.Count > 0 && widgets.Any(widget => widget.Connected);
        return new OverlayState(inputSnapshot.Timestamp, profile.Id, connected, widgets);
    }

    public void Reset()
    {
        memory.Clear();
        previousTimestamp = null;
    }

    private StickWidgetState BuildStick(
        StickWidgetDefinition widget,
        EvaluatedInputState inputState,
        InputSnapshot snapshot,
        IReadOnlyDictionary<string, InputSource> sourceMap,
        double elapsedSeconds)
    {
        WidgetMemory widgetMemory = GetMemory(widget.Id);
        double rawX = ApplyAxisGate(inputState.GetAxis(widget.XSourceId), widget.Tuning.Deadzone, widget.Tuning.InputNoiseGate);
        double rawY = ApplyAxisGate(inputState.GetAxis(widget.YSourceId), widget.Tuning.Deadzone, widget.Tuning.InputNoiseGate);

        double targetMagnitude = Math.Clamp(Math.Sqrt((rawX * rawX) + (rawY * rawY)), 0.0, 1.0);
        double targetAngle = targetMagnitude <= widget.Tuning.ZeroSnapThreshold
            ? widgetMemory.AngleDegrees
            : ToDegrees(Math.Atan2(rawY, rawX));

        double magnitude = Smooth(widgetMemory.Magnitude, targetMagnitude, widget.Tuning.MagnitudeSmoothingSpeed, elapsedSeconds, widgetMemory.Initialized);
        double angle = SmoothAngle(widgetMemory.AngleDegrees, targetAngle, widget.Tuning.AngleSmoothingSpeed, elapsedSeconds, widgetMemory.Initialized);

        if (magnitude <= widget.Tuning.ZeroSnapThreshold)
        {
            magnitude = 0.0;
        }

        double radians = angle * Math.PI / 180.0;
        double x = magnitude * Math.Cos(radians);
        double y = magnitude * Math.Sin(radians);
        widgetMemory.Magnitude = magnitude;
        widgetMemory.AngleDegrees = angle;
        widgetMemory.Primary = x;
        widgetMemory.Secondary = y;
        widgetMemory.Initialized = true;

        bool connected = IsAxisConnected(widget.XSourceId, sourceMap, snapshot) && IsAxisConnected(widget.YSourceId, sourceMap, snapshot);
        double activity = ApplyRamp(magnitude, widget.Tuning.ColorRampExponent);

        return ApplyCommon(widget, new StickWidgetState
        {
            Size = widget.Size,
            RawX = rawX,
            RawY = rawY,
            XValue = x * widget.Tuning.MaxThrowRatio,
            YValue = y * widget.Tuning.MaxThrowRatio,
            Magnitude = magnitude * widget.Tuning.MaxThrowRatio,
            AngleDegrees = angle,
            Labels = widget.Labels,
            Connected = connected,
            Activity = activity
        });
    }

    private ThrottleWidgetState BuildThrottle(
        ThrottleWidgetDefinition widget,
        EvaluatedInputState inputState,
        InputSnapshot snapshot,
        IReadOnlyDictionary<string, InputSource> sourceMap,
        double elapsedSeconds)
    {
        WidgetMemory widgetMemory = GetMemory(widget.Id);
        double rawValue = ApplyAxisGate(inputState.GetAxis(widget.SourceId), widget.Tuning.Deadzone, widget.Tuning.InputNoiseGate);
        double value = Smooth(widgetMemory.Primary, rawValue, widget.Tuning.ValueSmoothingSpeed, elapsedSeconds, widgetMemory.Initialized);
        value = ApplyZeroSnap(value, widget.Tuning.ZeroSnapThreshold);
        widgetMemory.Primary = value;
        widgetMemory.Initialized = true;

        double displayValue = value * widget.Tuning.MaxThrowRatio;
        double activity = ApplyRamp(Math.Abs(value), widget.Tuning.ColorRampExponent);

        return ApplyCommon(widget, new ThrottleWidgetState
        {
            Width = widget.Width,
            Height = widget.Height,
            RawValue = rawValue,
            Value = displayValue,
            FillRatio = (displayValue + widget.Tuning.MaxThrowRatio) / (widget.Tuning.MaxThrowRatio * 2.0),
            Labels = widget.Labels,
            Connected = IsAxisConnected(widget.SourceId, sourceMap, snapshot),
            Activity = activity
        });
    }

    private RollWidgetState BuildRoll(
        RollWidgetDefinition widget,
        EvaluatedInputState inputState,
        InputSnapshot snapshot,
        IReadOnlyDictionary<string, InputSource> sourceMap,
        double elapsedSeconds)
    {
        WidgetMemory widgetMemory = GetMemory(widget.Id);
        double rawValue = ApplyAxisGate(inputState.GetAxis(widget.SourceId), widget.Tuning.Deadzone, widget.Tuning.InputNoiseGate);
        double value = Smooth(widgetMemory.Primary, rawValue, widget.Tuning.ValueSmoothingSpeed, elapsedSeconds, widgetMemory.Initialized);
        value = ApplyZeroSnap(value, widget.Tuning.ZeroSnapThreshold);
        widgetMemory.Primary = value;
        widgetMemory.Initialized = true;

        double displayValue = value * widget.Tuning.MaxThrowRatio;
        double activity = ApplyRamp(Math.Abs(value), widget.Tuning.ColorRampExponent);

        return ApplyCommon(widget, new RollWidgetState
        {
            Width = widget.Width,
            Height = widget.Height,
            AssetId = widget.AssetId,
            RawValue = rawValue,
            Value = displayValue,
            RotationDegrees = displayValue * widget.MaxRotationDegrees,
            Connected = IsAxisConnected(widget.SourceId, sourceMap, snapshot),
            Activity = activity
        });
    }

    private StateTextWidgetState BuildStateText(
        StateTextWidgetDefinition widget,
        EvaluatedInputState inputState,
        InputSnapshot snapshot,
        IReadOnlyDictionary<string, InputSource> sourceMap,
        double elapsedSeconds)
    {
        WidgetMemory widgetMemory = GetMemory(widget.Id);
        double rawValue = widget.SourceKind == InputSourceKind.Axis
            ? Math.Abs(inputState.GetAxis(widget.SourceId))
            : inputState.GetButton(widget.SourceId) ? 1.0 : 0.0;
        bool active = rawValue > widget.Tuning.ActivationDeadzone;
        double targetIntensity = active ? Math.Clamp(rawValue, 0.0, 1.0) : 0.0;
        double speed = targetIntensity > widgetMemory.Primary ? widget.Tuning.RiseSpeed : widget.Tuning.FallSpeed;
        double intensity = Smooth(widgetMemory.Primary, targetIntensity, speed, elapsedSeconds, widgetMemory.Initialized);
        intensity = ApplyZeroSnap(intensity, 0.001);
        widgetMemory.Primary = intensity;
        widgetMemory.Initialized = true;

        double activity = ApplyRamp(intensity, widget.Tuning.ColorRampExponent);

        return ApplyCommon(widget, new StateTextWidgetState
        {
            Text = widget.Text,
            RawValue = rawValue,
            Active = active,
            Intensity = intensity,
            FontSize = Lerp(widget.FontSizeOff, widget.FontSizeOn, intensity),
            Connected = IsSourceConnected(widget.SourceId, widget.SourceKind, sourceMap, snapshot),
            Activity = activity
        });
    }

    private T ApplyCommon<T>(WidgetDefinition definition, T state)
        where T : WidgetState
    {
        return state with
        {
            Id = definition.Id,
            DisplayName = definition.DisplayName,
            X = definition.X,
            Y = definition.Y,
            RingColor = definition.RingColor,
            ActiveColor = definition.ActiveColor,
            DisplayColor = Blend(definition.RingColor, definition.ActiveColor, state.Activity),
            VisualEffects = definition.VisualEffects,
            TextEffects = definition.TextEffects
        };
    }

    private WidgetMemory GetMemory(string widgetId)
    {
        if (!memory.TryGetValue(widgetId, out WidgetMemory? widgetMemory))
        {
            widgetMemory = new WidgetMemory();
            memory[widgetId] = widgetMemory;
        }

        return widgetMemory;
    }

    private double CalculateElapsedSeconds(DateTimeOffset timestamp)
    {
        if (previousTimestamp is null)
        {
            return 1.0 / 60.0;
        }

        double seconds = (timestamp - previousTimestamp.Value).TotalSeconds;
        return Math.Clamp(seconds, 0.0, 0.25);
    }

    private static double ApplyAxisGate(double value, double deadzone, double noiseGate)
    {
        double threshold = Math.Max(Math.Abs(deadzone), Math.Abs(noiseGate));
        return Math.Abs(value) <= threshold ? 0.0 : Math.Clamp(value, -1.0, 1.0);
    }

    private static double ApplyZeroSnap(double value, double threshold)
    {
        return Math.Abs(value) <= threshold ? 0.0 : Math.Clamp(value, -1.0, 1.0);
    }

    private static double Smooth(double previous, double target, double speed, double elapsedSeconds, bool initialized)
    {
        if (!initialized || speed <= 0.0 || elapsedSeconds <= 0.0)
        {
            return target;
        }

        double alpha = 1.0 - Math.Exp(-speed * elapsedSeconds);
        return Lerp(previous, target, Math.Clamp(alpha, 0.0, 1.0));
    }

    private static double SmoothAngle(double previous, double target, double speed, double elapsedSeconds, bool initialized)
    {
        if (!initialized || speed <= 0.0 || elapsedSeconds <= 0.0)
        {
            return target;
        }

        double delta = NormalizeAngle(target - previous);
        double alpha = 1.0 - Math.Exp(-speed * elapsedSeconds);
        return NormalizeAngle(previous + (delta * Math.Clamp(alpha, 0.0, 1.0)));
    }

    private static double NormalizeAngle(double degrees)
    {
        while (degrees <= -180.0)
        {
            degrees += 360.0;
        }

        while (degrees > 180.0)
        {
            degrees -= 360.0;
        }

        return degrees;
    }

    private static double ApplyRamp(double value, double exponent)
    {
        double clamped = Math.Clamp(value, 0.0, 1.0);
        return exponent <= 0.0 ? clamped : Math.Pow(clamped, exponent);
    }

    private static double Lerp(double from, double to, double amount)
    {
        return from + ((to - from) * Math.Clamp(amount, 0.0, 1.0));
    }

    private static double ToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }

    private static RgbaColor Blend(RgbaColor from, RgbaColor to, double amount)
    {
        double t = Math.Clamp(amount, 0.0, 1.0);
        return new RgbaColor(
            (byte)Math.Round(Lerp(from.R, to.R, t)),
            (byte)Math.Round(Lerp(from.G, to.G, t)),
            (byte)Math.Round(Lerp(from.B, to.B, t)),
            (byte)Math.Round(Lerp(from.A, to.A, t)));
    }

    private static bool IsAxisConnected(
        string sourceId,
        IReadOnlyDictionary<string, InputSource> sourceMap,
        InputSnapshot snapshot)
    {
        return IsSourceConnected(sourceId, InputSourceKind.Axis, sourceMap, snapshot);
    }

    private static bool IsSourceConnected(
        string sourceId,
        InputSourceKind expectedKind,
        IReadOnlyDictionary<string, InputSource> sourceMap,
        InputSnapshot snapshot)
    {
        if (!sourceMap.TryGetValue(sourceId, out InputSource? source) || source.Kind != expectedKind)
        {
            return false;
        }

        return source switch
        {
            KeyboardKeyInputSource => true,
            MouseButtonInputSource => true,
            JoystickAxisInputSource joystickAxis => snapshot.Axes.ContainsKey(InputSnapshotKeys.JoystickAxis(joystickAxis.DeviceId, joystickAxis.AxisIndex)),
            JoystickButtonInputSource joystickButton => snapshot.Buttons.ContainsKey(InputSnapshotKeys.JoystickButton(joystickButton.DeviceId, joystickButton.ButtonIndex)),
            VirtualButtonAxisInputSource virtualAxis =>
                IsSourceConnected(virtualAxis.NegativeButtonSourceId, InputSourceKind.Button, sourceMap, snapshot) &&
                IsSourceConnected(virtualAxis.PositiveButtonSourceId, InputSourceKind.Button, sourceMap, snapshot),
            CompositeAxisInputSource compositeAxis => compositeAxis.Components.Count > 0 &&
                compositeAxis.Components.All(component => IsSourceConnected(component.SourceId, component.SourceKind, sourceMap, snapshot)),
            _ => false
        };
    }

    private sealed class WidgetMemory
    {
        public bool Initialized { get; set; }

        public double Primary { get; set; }

        public double Secondary { get; set; }

        public double Magnitude { get; set; }

        public double AngleDegrees { get; set; }
    }
}
