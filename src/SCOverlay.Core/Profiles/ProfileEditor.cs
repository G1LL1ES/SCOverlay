using SCOverlay.Core.Domain;

namespace SCOverlay.Core.Profiles;

public static class ProfileEditor
{
    public static OverlayProfile CreateCopy(OverlayProfile source, string id, string name)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Profile id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Profile name is required.", nameof(name));
        }

        return source with
        {
            Id = id,
            Name = name
        };
    }

    public static OverlayProfile ReplaceInputSource(OverlayProfile profile, string sourceId, InputSource capturedSource)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(capturedSource);

        InputSource? existing = profile.InputSources.FirstOrDefault(source =>
            string.Equals(source.Id, sourceId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            throw new ArgumentException($"Input source '{sourceId}' does not exist.", nameof(sourceId));
        }

        if (!CanReplaceSourceKind(profile, sourceId, existing.Kind, capturedSource.Kind))
        {
            throw new InvalidOperationException($"'{existing.DisplayName}' cannot use a {capturedSource.Kind} binding because another action or widget expects it to remain {existing.Kind}.");
        }

        InputSource replacement = PreserveIdentity(existing, capturedSource);
        IReadOnlyList<InputSource> updated = profile.InputSources
            .Select(source => string.Equals(source.Id, sourceId, StringComparison.OrdinalIgnoreCase) ? replacement : source)
            .ToArray();
        IReadOnlyList<WidgetDefinition> updatedWidgets = profile.Widgets
            .Select(widget => UpdateStateTextSourceKind(widget, sourceId, capturedSource.Kind))
            .ToArray();

        return profile with
        {
            InputSources = updated,
            Widgets = updatedWidgets
        };
    }

    public static string CreateSafeProfileId(string displayName, IReadOnlyCollection<string> existingIds)
    {
        ArgumentNullException.ThrowIfNull(existingIds);
        string normalized = new string(displayName
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        normalized = normalized.Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "custom-profile";
        }

        string candidate = normalized;
        int suffix = 2;
        while (existingIds.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            candidate = $"{normalized}-{suffix++}";
        }

        return candidate;
    }

    private static InputSource PreserveIdentity(InputSource existing, InputSource captured)
    {
        return captured switch
        {
            KeyboardKeyInputSource keyboard => keyboard with
            {
                Id = existing.Id,
                DisplayName = existing.DisplayName
            },
            MouseButtonInputSource mouse => mouse with
            {
                Id = existing.Id,
                DisplayName = existing.DisplayName
            },
            JoystickAxisInputSource axis => axis with
            {
                Id = existing.Id,
                DisplayName = existing.DisplayName
            },
            JoystickButtonInputSource button => button with
            {
                Id = existing.Id,
                DisplayName = existing.DisplayName
            },
            _ => throw new InvalidOperationException($"Captured source type '{captured.GetType().Name}' is not bindable.")
        };
    }

    private static bool CanReplaceSourceKind(
        OverlayProfile profile,
        string sourceId,
        InputSourceKind existingKind,
        InputSourceKind replacementKind)
    {
        if (existingKind == replacementKind)
        {
            return true;
        }

        return !HasInputSourceDependencyRequiringKind(profile.InputSources, sourceId, replacementKind) &&
            !HasWidgetDependencyRequiringKind(profile.Widgets, sourceId, replacementKind);
    }

    private static bool HasInputSourceDependencyRequiringKind(
        IReadOnlyList<InputSource> sources,
        string sourceId,
        InputSourceKind replacementKind)
    {
        foreach (InputSource source in sources)
        {
            if (source is VirtualButtonAxisInputSource virtualAxis)
            {
                if (Matches(virtualAxis.NegativeButtonSourceId, sourceId) || Matches(virtualAxis.PositiveButtonSourceId, sourceId))
                {
                    return replacementKind != InputSourceKind.Button;
                }
            }

            if (source is CompositeAxisInputSource compositeAxis)
            {
                foreach (AxisComponent component in compositeAxis.Components)
                {
                    if (Matches(component.SourceId, sourceId) && component.SourceKind != replacementKind)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool HasWidgetDependencyRequiringKind(
        IReadOnlyList<WidgetDefinition> widgets,
        string sourceId,
        InputSourceKind replacementKind)
    {
        foreach (WidgetDefinition widget in widgets)
        {
            switch (widget)
            {
                case StickWidgetDefinition stick when Matches(stick.XSourceId, sourceId) || Matches(stick.YSourceId, sourceId):
                case ThrottleWidgetDefinition throttle when Matches(throttle.SourceId, sourceId):
                case RollWidgetDefinition roll when Matches(roll.SourceId, sourceId):
                    return replacementKind != InputSourceKind.Axis;
                case StateTextWidgetDefinition:
                    break;
            }
        }

        return false;
    }

    private static WidgetDefinition UpdateStateTextSourceKind(WidgetDefinition widget, string sourceId, InputSourceKind replacementKind)
    {
        return widget is StateTextWidgetDefinition stateText && Matches(stateText.SourceId, sourceId)
            ? stateText with
            {
                SourceKind = replacementKind
            }
            : widget;
    }

    private static bool Matches(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
