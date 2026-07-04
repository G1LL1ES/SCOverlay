using SCOverlay.Core.Application;
using SCOverlay.Core.Domain;

namespace SCOverlay.Core.Profiles;

public static class ProfileValidator
{
    public static ProfileValidationResult Validate(OverlayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var issues = new List<ProfileValidationIssue>();
        Required(profile.Id, "id", "Profile id is required.", issues);
        Required(profile.Name, "name", "Profile name is required.", issues);

        if (profile.SchemaVersion != AppInfo.CurrentProfileSchemaVersion)
        {
            issues.Add(new ProfileValidationIssue("schemaVersion", $"Unsupported profile schema version {profile.SchemaVersion}. Expected {AppInfo.CurrentProfileSchemaVersion}."));
        }

        if (profile.Runtime.TargetHz is < 1 or > 500)
        {
            issues.Add(new ProfileValidationIssue("runtime.targetHz", "Target Hz must be between 1 and 500."));
        }

        if (profile.Runtime.BrowserSourcePort is < 1 or > 65535)
        {
            issues.Add(new ProfileValidationIssue("runtime.browserSourcePort", "Browser source port must be between 1 and 65535."));
        }

        if (string.IsNullOrWhiteSpace(profile.Runtime.BrowserSourceHost))
        {
            issues.Add(new ProfileValidationIssue("runtime.browserSourceHost", "Browser source host is required."));
        }

        if (profile.Appearance.Opacity is < 0.1 or > 1.0)
        {
            issues.Add(new ProfileValidationIssue("appearance.opacity", "Appearance opacity must be between 0.1 and 1.0."));
        }

        if (profile.Appearance.WidgetScale is < 0.5 or > 1.75)
        {
            issues.Add(new ProfileValidationIssue("appearance.widgetScale", "Appearance widget scale must be between 0.5 and 1.75."));
        }

        if (profile.Appearance.PrimaryOpacity is < 0.0 or > 1.0)
        {
            issues.Add(new ProfileValidationIssue("appearance.primaryOpacity", "Primary color opacity must be between 0 and 1."));
        }

        if (profile.Appearance.ActiveOpacity is < 0.0 or > 1.0)
        {
            issues.Add(new ProfileValidationIssue("appearance.activeOpacity", "Active color opacity must be between 0 and 1."));
        }

        if (profile.Appearance.FramePrimaryOpacity is < 0.0 or > 1.0)
        {
            issues.Add(new ProfileValidationIssue("appearance.framePrimaryOpacity", "Frame primary color opacity must be between 0 and 1."));
        }

        if (profile.Appearance.FrameActiveOpacity is < 0.0 or > 1.0)
        {
            issues.Add(new ProfileValidationIssue("appearance.frameActiveOpacity", "Frame active color opacity must be between 0 and 1."));
        }

        ValidateInputSources(profile.InputSources, issues);
        ValidateWidgets(profile.Widgets, profile.InputSources, issues);

        return issues.Count == 0 ? ProfileValidationResult.Valid : new ProfileValidationResult(issues);
    }

    public static void ThrowIfInvalid(OverlayProfile profile)
    {
        ProfileValidationResult result = Validate(profile);
        if (!result.IsValid)
        {
            throw new ProfileValidationException(result.Issues);
        }
    }

    private static void ValidateInputSources(IReadOnlyList<InputSource> inputSources, List<ProfileValidationIssue> issues)
    {
        if (inputSources.Count == 0)
        {
            issues.Add(new ProfileValidationIssue("inputSources", "At least one input source is required."));
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<string, InputSource>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < inputSources.Count; i++)
        {
            InputSource source = inputSources[i];
            string path = $"inputSources[{i}]";
            Required(source.Id, $"{path}.id", "Input source id is required.", issues);
            if (!string.IsNullOrWhiteSpace(source.Id) && !seen.Add(source.Id))
            {
                issues.Add(new ProfileValidationIssue($"{path}.id", $"Duplicate input source id '{source.Id}'."));
            }
            else if (!string.IsNullOrWhiteSpace(source.Id))
            {
                byId[source.Id] = source;
            }

            ValidateInputSource(source, path, issues);
        }

        foreach (InputSource source in inputSources)
        {
            if (source is VirtualButtonAxisInputSource buttonAxis)
            {
                ValidateButtonReference(buttonAxis.NegativeButtonSourceId, byId, $"inputSources.{source.Id}.negativeButtonSourceId", issues);
                ValidateButtonReference(buttonAxis.PositiveButtonSourceId, byId, $"inputSources.{source.Id}.positiveButtonSourceId", issues);
            }

            if (source is CompositeAxisInputSource composite)
            {
                if (composite.Components.Count == 0)
                {
                    issues.Add(new ProfileValidationIssue($"inputSources.{source.Id}.components", "Composite axis must include at least one component."));
                }

                for (int i = 0; i < composite.Components.Count; i++)
                {
                    AxisComponent component = composite.Components[i];
                    string path = $"inputSources.{source.Id}.components[{i}].sourceId";
                    if (!byId.TryGetValue(component.SourceId, out InputSource? target))
                    {
                        issues.Add(new ProfileValidationIssue(path, $"Referenced input source '{component.SourceId}' does not exist."));
                    }
                    else if (target.Kind != component.SourceKind)
                    {
                        issues.Add(new ProfileValidationIssue(path, $"Referenced source '{component.SourceId}' is {target.Kind}, not {component.SourceKind}."));
                    }
                }
            }

            if (source is CompositeButtonInputSource compositeButton)
            {
                if (compositeButton.SourceIds.Count == 0)
                {
                    issues.Add(new ProfileValidationIssue($"inputSources.{source.Id}.sourceIds", "Composite button must include at least one source."));
                }

                for (int i = 0; i < compositeButton.SourceIds.Count; i++)
                {
                    ValidateButtonReference(compositeButton.SourceIds[i], byId, $"inputSources.{source.Id}.sourceIds[{i}]", issues);
                }
            }
        }
    }

    private static void ValidateInputSource(InputSource source, string path, List<ProfileValidationIssue> issues)
    {
        switch (source)
        {
            case KeyboardKeyInputSource keyboard:
                Required(keyboard.Key, $"{path}.key", "Keyboard key is required.", issues);
                break;
            case MouseButtonInputSource mouse:
                Required(mouse.Button, $"{path}.button", "Mouse button is required.", issues);
                break;
            case JoystickAxisInputSource axis:
                Required(axis.DeviceId, $"{path}.deviceId", "Joystick device id is required.", issues);
                if (axis.AxisIndex < 0)
                {
                    issues.Add(new ProfileValidationIssue($"{path}.axisIndex", "Axis index must be zero or greater."));
                }
                if (axis.Scale <= 0)
                {
                    issues.Add(new ProfileValidationIssue($"{path}.scale", "Axis scale must be greater than zero."));
                }
                break;
            case JoystickButtonInputSource button:
                Required(button.DeviceId, $"{path}.deviceId", "Joystick device id is required.", issues);
                if (button.ButtonIndex < 0)
                {
                    issues.Add(new ProfileValidationIssue($"{path}.buttonIndex", "Button index must be zero or greater."));
                }
                break;
        }
    }

    private static void ValidateWidgets(IReadOnlyList<WidgetDefinition> widgets, IReadOnlyList<InputSource> sources, List<ProfileValidationIssue> issues)
    {
        if (widgets.Count == 0)
        {
            issues.Add(new ProfileValidationIssue("widgets", "At least one widget is required."));
            return;
        }

        var sourceById = new Dictionary<string, InputSource>(StringComparer.OrdinalIgnoreCase);
        foreach (InputSource source in sources)
        {
            if (!string.IsNullOrWhiteSpace(source.Id) && !sourceById.ContainsKey(source.Id))
            {
                sourceById[source.Id] = source;
            }
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < widgets.Count; i++)
        {
            WidgetDefinition widget = widgets[i];
            string path = $"widgets[{i}]";
            Required(widget.Id, $"{path}.id", "Widget id is required.", issues);
            if (widget.Scale is < 0.25 or > 3.0)
            {
                issues.Add(new ProfileValidationIssue($"{path}.scale", "Widget scale must be between 0.25 and 3.0."));
            }
            if (widget.Opacity is < 0.0 or > 1.0)
            {
                issues.Add(new ProfileValidationIssue($"{path}.opacity", "Widget opacity must be between 0 and 1."));
            }
            if (widget.LineThickness is < 0.0 or > 20.0)
            {
                issues.Add(new ProfileValidationIssue($"{path}.lineThickness", "Widget line thickness must be between 0 and 20."));
            }
            if (!string.IsNullOrWhiteSpace(widget.Id) && !seen.Add(widget.Id))
            {
                issues.Add(new ProfileValidationIssue($"{path}.id", $"Duplicate widget id '{widget.Id}'."));
            }

            switch (widget)
            {
                case StickWidgetDefinition stick:
                    ValidateSourceReference(stick.XSourceId, InputSourceKind.Axis, sourceById, $"{path}.xSourceId", issues);
                    ValidateSourceReference(stick.YSourceId, InputSourceKind.Axis, sourceById, $"{path}.ySourceId", issues);
                    break;
                case ThrottleWidgetDefinition throttle:
                    ValidateSourceReference(throttle.SourceId, InputSourceKind.Axis, sourceById, $"{path}.sourceId", issues);
                    if (throttle.CornerRadius is < 0.0 or > 40.0)
                    {
                        issues.Add(new ProfileValidationIssue($"{path}.cornerRadius", "Throttle corner radius must be between 0 and 40."));
                    }
                    break;
                case RollWidgetDefinition roll:
                    ValidateSourceReference(roll.SourceId, InputSourceKind.Axis, sourceById, $"{path}.sourceId", issues);
                    Required(roll.AssetId, $"{path}.assetId", "Roll widget asset id is required.", issues);
                    if (!string.IsNullOrWhiteSpace(roll.AssetId) && !RollAssets.IsKnown(roll.AssetId))
                    {
                        issues.Add(new ProfileValidationIssue($"{path}.assetId", $"Roll widget asset '{roll.AssetId}' is not supported."));
                    }
                    if (roll.MaxRotationDegrees is < 5.0 or > 180.0)
                    {
                        issues.Add(new ProfileValidationIssue($"{path}.maxRotationDegrees", "Roll max rotation must be between 5 and 180 degrees."));
                    }
                    break;
                case StateTextWidgetDefinition stateText:
                    Required(stateText.Text, $"{path}.text", "State text widget text is required.", issues);
                    ValidateSourceReference(stateText.SourceId, stateText.SourceKind, sourceById, $"{path}.sourceId", issues);
                    break;
            }
        }
    }

    private static void ValidateButtonReference(string sourceId, IReadOnlyDictionary<string, InputSource> sourceById, string path, List<ProfileValidationIssue> issues)
    {
        ValidateSourceReference(sourceId, InputSourceKind.Button, sourceById, path, issues);
    }

    private static void ValidateSourceReference(string sourceId, InputSourceKind expectedKind, IReadOnlyDictionary<string, InputSource> sourceById, string path, List<ProfileValidationIssue> issues)
    {
        Required(sourceId, path, "Input source reference is required.", issues);
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return;
        }

        if (!sourceById.TryGetValue(sourceId, out InputSource? source))
        {
            issues.Add(new ProfileValidationIssue(path, $"Referenced input source '{sourceId}' does not exist."));
            return;
        }

        if (source.Kind != expectedKind)
        {
            issues.Add(new ProfileValidationIssue(path, $"Referenced input source '{sourceId}' is {source.Kind}, not {expectedKind}."));
        }
    }

    private static void Required(string value, string path, string message, List<ProfileValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new ProfileValidationIssue(path, message));
        }
    }
}
