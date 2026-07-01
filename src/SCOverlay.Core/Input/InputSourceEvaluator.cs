using SCOverlay.Core.Domain;

namespace SCOverlay.Core.Input;

public static class InputSourceEvaluator
{
    public static EvaluatedInputState Evaluate(IEnumerable<InputSource> sources, InputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(snapshot);

        Dictionary<string, InputSource> sourceMap = sources
            .Where(source => !string.IsNullOrWhiteSpace(source.Id))
            .GroupBy(source => source.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var axes = new Dictionary<string, double>(StringComparer.Ordinal);
        var buttons = new Dictionary<string, bool>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        foreach (InputSource source in sourceMap.Values)
        {
            if (source.Kind == InputSourceKind.Axis)
            {
                axes[source.Id] = EvaluateAxis(source.Id, sourceMap, snapshot, axes, buttons, visiting);
            }
            else
            {
                buttons[source.Id] = EvaluateButton(source.Id, sourceMap, snapshot, axes, buttons, visiting);
            }
        }

        return new EvaluatedInputState(snapshot.Timestamp, axes, buttons);
    }

    private static double EvaluateAxis(
        string sourceId,
        IReadOnlyDictionary<string, InputSource> sourceMap,
        InputSnapshot snapshot,
        IDictionary<string, double> axes,
        IDictionary<string, bool> buttons,
        ISet<string> visiting)
    {
        if (axes.TryGetValue(sourceId, out double cached))
        {
            return cached;
        }

        if (!sourceMap.TryGetValue(sourceId, out InputSource? source) || source.Kind != InputSourceKind.Axis)
        {
            return 0.0;
        }

        if (!visiting.Add(sourceId))
        {
            return 0.0;
        }

        double value = source switch
        {
            JoystickAxisInputSource joystickAxis => EvaluateJoystickAxis(joystickAxis, snapshot),
            VirtualButtonAxisInputSource virtualAxis => EvaluateVirtualButtonAxis(virtualAxis, sourceMap, snapshot, axes, buttons, visiting),
            CompositeAxisInputSource compositeAxis => EvaluateCompositeAxis(compositeAxis, sourceMap, snapshot, axes, buttons, visiting),
            _ => 0.0
        };

        visiting.Remove(sourceId);
        value = Clamp(value);
        axes[sourceId] = value;
        return value;
    }

    private static bool EvaluateButton(
        string sourceId,
        IReadOnlyDictionary<string, InputSource> sourceMap,
        InputSnapshot snapshot,
        IDictionary<string, double> axes,
        IDictionary<string, bool> buttons,
        ISet<string> visiting)
    {
        if (buttons.TryGetValue(sourceId, out bool cached))
        {
            return cached;
        }

        if (!sourceMap.TryGetValue(sourceId, out InputSource? source) || source.Kind != InputSourceKind.Button)
        {
            return false;
        }

        if (!visiting.Add(sourceId))
        {
            return false;
        }

        bool value = source switch
        {
            KeyboardKeyInputSource keyboardKey => snapshot.Buttons.TryGetValue(InputSnapshotKeys.KeyboardButton(keyboardKey.Key), out bool pressed) && pressed,
            MouseButtonInputSource mouseButton => snapshot.Buttons.TryGetValue(InputSnapshotKeys.MouseButton(mouseButton.Button), out bool pressed) && pressed,
            JoystickButtonInputSource joystickButton => EvaluateJoystickButton(joystickButton, snapshot),
            _ => false
        };

        visiting.Remove(sourceId);
        buttons[sourceId] = value;
        return value;
    }

    private static double EvaluateJoystickAxis(JoystickAxisInputSource source, InputSnapshot snapshot)
    {
        string key = InputSnapshotKeys.JoystickAxis(source.DeviceId, source.AxisIndex);
        if (!snapshot.Axes.TryGetValue(key, out double value))
        {
            return 0.0;
        }

        double scaled = value * source.Scale;
        return source.Invert ? -scaled : scaled;
    }

    private static bool EvaluateJoystickButton(JoystickButtonInputSource source, InputSnapshot snapshot)
    {
        string key = InputSnapshotKeys.JoystickButton(source.DeviceId, source.ButtonIndex);
        bool pressed = snapshot.Buttons.TryGetValue(key, out bool value) && value;
        return source.Invert ? !pressed : pressed;
    }

    private static double EvaluateVirtualButtonAxis(
        VirtualButtonAxisInputSource source,
        IReadOnlyDictionary<string, InputSource> sourceMap,
        InputSnapshot snapshot,
        IDictionary<string, double> axes,
        IDictionary<string, bool> buttons,
        ISet<string> visiting)
    {
        bool negative = EvaluateButton(source.NegativeButtonSourceId, sourceMap, snapshot, axes, buttons, visiting);
        bool positive = EvaluateButton(source.PositiveButtonSourceId, sourceMap, snapshot, axes, buttons, visiting);

        return (positive ? 1.0 : 0.0) - (negative ? 1.0 : 0.0);
    }

    private static double EvaluateCompositeAxis(
        CompositeAxisInputSource source,
        IReadOnlyDictionary<string, InputSource> sourceMap,
        InputSnapshot snapshot,
        IDictionary<string, double> axes,
        IDictionary<string, bool> buttons,
        ISet<string> visiting)
    {
        double value = 0.0;

        foreach (AxisComponent component in source.Components)
        {
            double componentValue = component.SourceKind == InputSourceKind.Axis
                ? EvaluateAxis(component.SourceId, sourceMap, snapshot, axes, buttons, visiting)
                : EvaluateButton(component.SourceId, sourceMap, snapshot, axes, buttons, visiting) ? 1.0 : 0.0;

            componentValue = component.Region switch
            {
                AxisRegion.Positive => Math.Max(0.0, componentValue),
                AxisRegion.Negative => Math.Min(0.0, componentValue),
                _ => componentValue
            };

            if (component.Invert)
            {
                componentValue = -componentValue;
            }

            value += componentValue * component.Scale;
        }

        return source.ClampOutput ? Clamp(value) : value;
    }

    private static double Clamp(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0.0;
        }

        return Math.Clamp(value, -1.0, 1.0);
    }
}
