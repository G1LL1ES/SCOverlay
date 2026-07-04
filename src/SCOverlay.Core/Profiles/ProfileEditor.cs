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

        IReadOnlyList<InputSource> updated = existing.Kind == capturedSource.Kind
            ? AddAlternateBinding(profile.InputSources, existing, capturedSource)
            : profile.InputSources
                .Select(source => Matches(source.Id, sourceId) ? PreserveIdentity(existing, capturedSource) : source)
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

    public static bool IsGeneratedBindingSource(InputSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Id.Contains("__binding__", StringComparison.OrdinalIgnoreCase);
    }

    public static OverlayProfile RemoveInputBinding(OverlayProfile profile, string actionSourceId, string bindingSourceId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(actionSourceId))
        {
            throw new ArgumentException("Action source id is required.", nameof(actionSourceId));
        }

        if (string.IsNullOrWhiteSpace(bindingSourceId))
        {
            throw new ArgumentException("Binding source id is required.", nameof(bindingSourceId));
        }

        InputSource action = profile.InputSources.FirstOrDefault(source => Matches(source.Id, actionSourceId)) ??
            throw new ArgumentException($"Input source '{actionSourceId}' does not exist.", nameof(actionSourceId));

        if (action is CompositeAxisInputSource axis)
        {
            return RemoveAxisBinding(profile, axis, bindingSourceId);
        }

        if (action is CompositeButtonInputSource button)
        {
            return RemoveButtonBinding(profile, button, bindingSourceId);
        }

        if (Matches(action.Id, bindingSourceId))
        {
            throw new InvalidOperationException($"'{action.DisplayName}' needs at least one binding.");
        }

        throw new InvalidOperationException($"'{bindingSourceId}' is not a binding for '{action.DisplayName}'.");
    }

    public static OverlayProfile SetJoystickAxisInverted(OverlayProfile profile, string sourceId, bool invert)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException("Input source id is required.", nameof(sourceId));
        }

        bool updated = false;
        InputSource[] sources = profile.InputSources
            .Select(source =>
            {
                if (!Matches(source.Id, sourceId))
                {
                    return source;
                }

                updated = true;
                if (source is not JoystickAxisInputSource axis)
                {
                    throw new InvalidOperationException($"'{source.DisplayName}' is not a controller axis binding.");
                }

                return axis with
                {
                    Invert = invert
                };
            })
            .ToArray();

        if (!updated)
        {
            throw new ArgumentException($"Input source '{sourceId}' does not exist.", nameof(sourceId));
        }

        return profile with
        {
            InputSources = sources
        };
    }

    public static OverlayProfile ApplyAppearance(OverlayProfile profile, AppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(appearance);

        return profile with
        {
            Appearance = AppearanceSettingsNormalizer.Normalize(appearance)
        };
    }

    public static OverlayProfile ApplyWidgetAppearance(
        OverlayProfile profile,
        string widgetId,
        double x,
        double y,
        double scale,
        double opacity,
        double lineThickness,
        double throttleCornerRadius,
        string rollAssetId,
        double rollMaxRotationDegrees,
        bool stateTextMaxedShakeEnabled)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(widgetId))
        {
            throw new ArgumentException("Widget id is required.", nameof(widgetId));
        }

        bool updated = false;
        WidgetDefinition[] widgets = profile.Widgets
            .Select(widget =>
            {
                if (!Matches(widget.Id, widgetId))
                {
                    return widget;
                }

                updated = true;
                WidgetDefinition common = ApplyCommonWidgetAppearance(widget, x, y, scale, opacity, lineThickness);
                return common switch
                {
                    ThrottleWidgetDefinition throttle => throttle with
                    {
                        CornerRadius = Math.Clamp(throttleCornerRadius, 0.0, 40.0)
                    },
                    RollWidgetDefinition roll => roll with
                    {
                        AssetId = NormalizeRollAssetId(rollAssetId),
                        RenderMode = string.Equals(NormalizeRollAssetId(rollAssetId), RollAssets.Indicator, StringComparison.OrdinalIgnoreCase)
                            ? RollRenderMode.Indicator
                            : RollRenderMode.Image,
                        MaxRotationDegrees = Math.Clamp(rollMaxRotationDegrees, 5.0, 180.0)
                    },
                    StateTextWidgetDefinition stateText => stateText with
                    {
                        Tuning = stateText.Tuning with
                        {
                            MaxedShakeEnabled = stateTextMaxedShakeEnabled
                        }
                    },
                    _ => common
                };
            })
            .ToArray();

        if (!updated)
        {
            throw new ArgumentException($"Widget '{widgetId}' does not exist.", nameof(widgetId));
        }

        return profile with
        {
            Widgets = widgets
        };
    }

    public static OverlayProfile ResetWidgetAppearance(OverlayProfile profile, string widgetId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(widgetId))
        {
            throw new ArgumentException("Widget id is required.", nameof(widgetId));
        }

        WidgetDefinition existing = profile.Widgets.First(widget => Matches(widget.Id, widgetId));
        WidgetDefinition defaults = DefaultProfiles.CreateKbmDefault().Widgets.FirstOrDefault(widget => Matches(widget.Id, widgetId)) ?? existing;
        return profile with
        {
            Widgets = profile.Widgets
                .Select(widget => Matches(widget.Id, widgetId) ? ResetWidgetAppearance(widget, defaults) : widget)
                .ToArray()
        };
    }

    public static OverlayProfile ApplyWidgetEffects(OverlayProfile profile, EffectSettings visualEffects, EffectSettings textEffects)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(visualEffects);
        ArgumentNullException.ThrowIfNull(textEffects);

        return profile with
        {
            Widgets = profile.Widgets
                .Select(widget => ApplyEffects(widget, ClampEffects(visualEffects), ClampEffects(textEffects)))
                .ToArray()
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

    private static OverlayProfile RemoveAxisBinding(
        OverlayProfile profile,
        CompositeAxisInputSource action,
        string bindingSourceId)
    {
        AxisComponent[] remaining = action.Components
            .Where(component => !Matches(component.SourceId, bindingSourceId))
            .ToArray();
        if (remaining.Length == action.Components.Count)
        {
            throw new InvalidOperationException($"'{bindingSourceId}' is not a binding for '{action.DisplayName}'.");
        }

        return ReplaceAfterBindingRemoval(
            profile,
            action,
            bindingSourceId,
            remaining.Select(component => component.SourceId).ToArray(),
            remaining.Length == 1 ? remaining[0].SourceId : null,
            remaining.Length > 1
                ? action with
                {
                    Components = remaining
                }
                : null);
    }

    private static OverlayProfile RemoveButtonBinding(
        OverlayProfile profile,
        CompositeButtonInputSource action,
        string bindingSourceId)
    {
        string[] remaining = action.SourceIds
            .Where(sourceId => !Matches(sourceId, bindingSourceId))
            .ToArray();
        if (remaining.Length == action.SourceIds.Count)
        {
            throw new InvalidOperationException($"'{bindingSourceId}' is not a binding for '{action.DisplayName}'.");
        }

        return ReplaceAfterBindingRemoval(
            profile,
            action,
            bindingSourceId,
            remaining,
            remaining.Length == 1 ? remaining[0] : null,
            remaining.Length > 1
                ? action with
                {
                    SourceIds = remaining
                }
                : null);
    }

    private static OverlayProfile ReplaceAfterBindingRemoval(
        OverlayProfile profile,
        InputSource action,
        string removedBindingId,
        IReadOnlyCollection<string> remainingBindingIds,
        string? bindingIdToPromote,
        InputSource? replacementComposite)
    {
        if (remainingBindingIds.Count == 0)
        {
            throw new InvalidOperationException($"'{action.DisplayName}' needs at least one binding.");
        }

        InputSource? promoted = null;
        if (bindingIdToPromote is not null)
        {
            promoted = profile.InputSources.FirstOrDefault(source => Matches(source.Id, bindingIdToPromote)) ??
                throw new InvalidOperationException($"Remaining binding '{bindingIdToPromote}' does not exist.");
            promoted = PreserveBindingIdentity(promoted, action.Id, action.DisplayName);
        }

        HashSet<string> removableIds = new(StringComparer.OrdinalIgnoreCase)
        {
            removedBindingId
        };
        if (bindingIdToPromote is not null)
        {
            removableIds.Add(bindingIdToPromote);
        }

        var updated = new List<InputSource>(profile.InputSources.Count);
        foreach (InputSource source in profile.InputSources)
        {
            if (Matches(source.Id, action.Id))
            {
                updated.Add(replacementComposite ?? promoted ?? throw new InvalidOperationException("No replacement binding was produced."));
                continue;
            }

            if (removableIds.Contains(source.Id) && IsGeneratedBindingSource(source))
            {
                continue;
            }

            updated.Add(source);
        }

        return profile with
        {
            InputSources = updated
        };
    }

    private static IReadOnlyList<InputSource> AddAlternateBinding(
        IReadOnlyList<InputSource> sources,
        InputSource existing,
        InputSource captured)
    {
        return existing.Kind switch
        {
            InputSourceKind.Axis => AddAlternateAxisBinding(sources, existing, captured),
            InputSourceKind.Button => AddAlternateButtonBinding(sources, existing, captured),
            _ => throw new InvalidOperationException($"Unsupported input source kind '{existing.Kind}'.")
        };
    }

    private static IReadOnlyList<InputSource> AddAlternateAxisBinding(
        IReadOnlyList<InputSource> sources,
        InputSource existing,
        InputSource captured)
    {
        var updated = new List<InputSource>(sources.Count + 2);
        CompositeAxisInputSource? existingComposite = existing as CompositeAxisInputSource;
        string capturedId = existingComposite is null
            ? CreateBindingSourceId(existing.Id, sources, CreateBindingSourceId(existing.Id, sources))
            : CreateBindingSourceId(existing.Id, sources);
        InputSource capturedBinding = PreserveBindingIdentity(captured, capturedId, captured.DisplayName);

        foreach (InputSource source in sources)
        {
            if (!Matches(source.Id, existing.Id))
            {
                updated.Add(source);
                continue;
            }

            if (existingComposite is not null)
            {
                updated.Add(existingComposite with
                {
                    Components = existingComposite.Components
                        .Append(new AxisComponent
                        {
                            SourceId = capturedId,
                            SourceKind = InputSourceKind.Axis
                        })
                        .ToArray()
                });
            }
            else
            {
                string originalId = CreateBindingSourceId(existing.Id, sources);
                InputSource originalBinding = PreserveBindingIdentity(existing, originalId, $"{existing.DisplayName} Existing");
                updated.Add(originalBinding);
                updated.Add(new CompositeAxisInputSource
                {
                    Id = existing.Id,
                    DisplayName = existing.DisplayName,
                    Components = new[]
                    {
                        new AxisComponent
                        {
                            SourceId = originalId,
                            SourceKind = InputSourceKind.Axis
                        },
                        new AxisComponent
                        {
                            SourceId = capturedId,
                            SourceKind = InputSourceKind.Axis
                        }
                    }
                });
            }
        }

        updated.Add(capturedBinding);
        return updated;
    }

    private static IReadOnlyList<InputSource> AddAlternateButtonBinding(
        IReadOnlyList<InputSource> sources,
        InputSource existing,
        InputSource captured)
    {
        var updated = new List<InputSource>(sources.Count + 2);
        CompositeButtonInputSource? existingComposite = existing as CompositeButtonInputSource;
        string capturedId = existingComposite is null
            ? CreateBindingSourceId(existing.Id, sources, CreateBindingSourceId(existing.Id, sources))
            : CreateBindingSourceId(existing.Id, sources);
        InputSource capturedBinding = PreserveBindingIdentity(captured, capturedId, captured.DisplayName);

        foreach (InputSource source in sources)
        {
            if (!Matches(source.Id, existing.Id))
            {
                updated.Add(source);
                continue;
            }

            if (existingComposite is not null)
            {
                updated.Add(existingComposite with
                {
                    SourceIds = existingComposite.SourceIds.Append(capturedId).ToArray()
                });
            }
            else
            {
                string originalId = CreateBindingSourceId(existing.Id, sources);
                InputSource originalBinding = PreserveBindingIdentity(existing, originalId, $"{existing.DisplayName} Existing");
                updated.Add(originalBinding);
                updated.Add(new CompositeButtonInputSource
                {
                    Id = existing.Id,
                    DisplayName = existing.DisplayName,
                    SourceIds = new[]
                    {
                        originalId,
                        capturedId
                    }
                });
            }
        }

        updated.Add(capturedBinding);
        return updated;
    }

    private static string CreateBindingSourceId(string actionId, IReadOnlyList<InputSource> sources, params string[] reservedIds)
    {
        var ids = new HashSet<string>(sources.Select(source => source.Id), StringComparer.OrdinalIgnoreCase);
        foreach (string reservedId in reservedIds)
        {
            ids.Add(reservedId);
        }

        for (int i = 1; i < 1000; i++)
        {
            string candidate = $"{actionId}__binding__{i}";
            if (!ids.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{actionId}__binding__{Guid.NewGuid():N}";
    }

    private static InputSource PreserveBindingIdentity(InputSource source, string id, string displayName)
    {
        return source switch
        {
            KeyboardKeyInputSource keyboard => keyboard with
            {
                Id = id,
                DisplayName = displayName
            },
            MouseButtonInputSource mouse => mouse with
            {
                Id = id,
                DisplayName = displayName
            },
            JoystickAxisInputSource axis => axis with
            {
                Id = id,
                DisplayName = displayName
            },
            JoystickButtonInputSource button => button with
            {
                Id = id,
                DisplayName = displayName
            },
            VirtualButtonAxisInputSource virtualAxis => virtualAxis with
            {
                Id = id,
                DisplayName = displayName
            },
            CompositeAxisInputSource compositeAxis => compositeAxis with
            {
                Id = id,
                DisplayName = displayName
            },
            CompositeButtonInputSource compositeButton => compositeButton with
            {
                Id = id,
                DisplayName = displayName
            },
            _ => throw new InvalidOperationException($"Source type '{source.GetType().Name}' is not bindable.")
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

    private static WidgetDefinition ApplyEffects(WidgetDefinition widget, EffectSettings visualEffects, EffectSettings textEffects)
    {
        return widget switch
        {
            StickWidgetDefinition stick => stick with
            {
                VisualEffects = visualEffects,
                TextEffects = textEffects
            },
            ThrottleWidgetDefinition throttle => throttle with
            {
                VisualEffects = visualEffects,
                TextEffects = textEffects
            },
            RollWidgetDefinition roll => roll with
            {
                VisualEffects = visualEffects,
                TextEffects = textEffects
            },
            StateTextWidgetDefinition stateText => stateText with
            {
                VisualEffects = visualEffects,
                TextEffects = textEffects
            },
            _ => widget
        };
    }

    private static WidgetDefinition ApplyCommonWidgetAppearance(
        WidgetDefinition widget,
        double x,
        double y,
        double scale,
        double opacity,
        double lineThickness)
    {
        return widget switch
        {
            StickWidgetDefinition stick => stick with
            {
                X = Math.Clamp(x, -1000.0, 1000.0),
                Y = Math.Clamp(y, -1000.0, 1000.0),
                Scale = Math.Clamp(scale, 0.25, 3.0),
                Opacity = Math.Clamp(opacity, 0.0, 1.0),
                LineThickness = Math.Clamp(lineThickness, 0.0, 20.0)
            },
            ThrottleWidgetDefinition throttle => throttle with
            {
                X = Math.Clamp(x, -1000.0, 1000.0),
                Y = Math.Clamp(y, -1000.0, 1000.0),
                Scale = Math.Clamp(scale, 0.25, 3.0),
                Opacity = Math.Clamp(opacity, 0.0, 1.0),
                LineThickness = Math.Clamp(lineThickness, 0.0, 20.0)
            },
            RollWidgetDefinition roll => roll with
            {
                X = Math.Clamp(x, -1000.0, 1000.0),
                Y = Math.Clamp(y, -1000.0, 1000.0),
                Scale = Math.Clamp(scale, 0.25, 3.0),
                Opacity = Math.Clamp(opacity, 0.0, 1.0),
                LineThickness = Math.Clamp(lineThickness, 0.0, 20.0)
            },
            StateTextWidgetDefinition stateText => stateText with
            {
                X = Math.Clamp(x, -1000.0, 1000.0),
                Y = Math.Clamp(y, -1000.0, 1000.0),
                Scale = Math.Clamp(scale, 0.25, 3.0),
                Opacity = Math.Clamp(opacity, 0.0, 1.0),
                LineThickness = Math.Clamp(lineThickness, 0.0, 20.0)
            },
            _ => widget
        };
    }

    private static WidgetDefinition ResetWidgetAppearance(WidgetDefinition widget, WidgetDefinition defaults)
    {
        WidgetDefinition common = ApplyCommonWidgetAppearance(
            widget,
            defaults.X,
            defaults.Y,
            defaults.Scale,
            defaults.Opacity,
            defaults.LineThickness);
        return (common, defaults) switch
        {
            (ThrottleWidgetDefinition throttle, ThrottleWidgetDefinition defaultThrottle) => throttle with
            {
                CornerRadius = defaultThrottle.CornerRadius
            },
            (RollWidgetDefinition roll, RollWidgetDefinition defaultRoll) => roll with
            {
                AssetId = defaultRoll.AssetId,
                RenderMode = defaultRoll.RenderMode,
                MaxRotationDegrees = defaultRoll.MaxRotationDegrees
            },
            (StateTextWidgetDefinition stateText, StateTextWidgetDefinition defaultStateText) => stateText with
            {
                Tuning = stateText.Tuning with
                {
                    MaxedShakeEnabled = defaultStateText.Tuning.MaxedShakeEnabled
                }
            },
            _ => common
        };
    }

    private static string NormalizeRollAssetId(string assetId)
    {
        return RollAssets.IsKnown(assetId) ? assetId : RollAssets.Gladius;
    }

    private static EffectSettings ClampEffects(EffectSettings effects)
    {
        return effects with
        {
            OutlineWidth = Math.Clamp(effects.OutlineWidth, 0.0, 16.0),
            ShadowWidth = Math.Clamp(effects.ShadowWidth, 0.0, 32.0),
            ShadowOffsetX = Math.Clamp(effects.ShadowOffsetX, -32.0, 32.0),
            ShadowOffsetY = Math.Clamp(effects.ShadowOffsetY, -32.0, 32.0),
            BackplatePadding = Math.Clamp(effects.BackplatePadding, 0.0, 64.0),
            BackplateRadius = Math.Clamp(effects.BackplateRadius, 0.0, 32.0)
        };
    }

    private static bool Matches(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
