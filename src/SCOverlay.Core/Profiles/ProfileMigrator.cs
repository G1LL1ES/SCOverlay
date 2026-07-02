using SCOverlay.Core.Application;
using SCOverlay.Core.Domain;

namespace SCOverlay.Core.Profiles;

public static class ProfileMigrator
{
    public static OverlayProfile Migrate(OverlayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        bool shouldRepairKeyboardAlternates = profile.SchemaVersion < 2;

        if (profile.SchemaVersion != AppInfo.CurrentProfileSchemaVersion)
        {
            profile = profile with
            {
                SchemaVersion = AppInfo.CurrentProfileSchemaVersion
            };
        }

        return shouldRepairKeyboardAlternates
            ? RepairDefaultKeyboardAlternates(profile)
            : profile;
    }

    private static OverlayProfile RepairDefaultKeyboardAlternates(OverlayProfile profile)
    {
        IReadOnlyDictionary<string, (string Negative, string Positive)> defaults = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["strafe-x"] = ("strafe-left", "strafe-right"),
            ["strafe-y"] = ("strafe-down", "strafe-up"),
            ["look-x"] = ("yaw-left", "yaw-right"),
            ["look-y"] = ("pitch-down", "pitch-up"),
            ["throttle"] = ("throttle-backward", "throttle-forward"),
            ["roll"] = ("roll-left", "roll-right")
        };
        var sourceById = profile.InputSources
            .Where(source => !string.IsNullOrWhiteSpace(source.Id))
            .GroupBy(source => source.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var updated = new List<InputSource>(profile.InputSources.Count + defaults.Count * 2);
        bool changed = false;

        foreach (InputSource source in profile.InputSources)
        {
            if (!defaults.TryGetValue(source.Id, out (string Negative, string Positive) buttons) ||
                source is VirtualButtonAxisInputSource or CompositeAxisInputSource ||
                source.Kind != InputSourceKind.Axis ||
                !IsButtonSource(sourceById, buttons.Negative) ||
                !IsButtonSource(sourceById, buttons.Positive))
            {
                updated.Add(source);
                continue;
            }

            string keyboardAxisId = CreateRepairSourceId(source.Id, sourceById, updated);
            string existingAxisId = CreateRepairSourceId(source.Id, sourceById, updated, keyboardAxisId);
            updated.Add(new VirtualButtonAxisInputSource
            {
                Id = keyboardAxisId,
                DisplayName = $"{source.DisplayName} Keyboard",
                NegativeButtonSourceId = buttons.Negative,
                PositiveButtonSourceId = buttons.Positive
            });
            updated.Add(CloneWithIdentity(source, existingAxisId, $"{source.DisplayName} Existing"));
            updated.Add(new CompositeAxisInputSource
            {
                Id = source.Id,
                DisplayName = source.DisplayName,
                Components = new[]
                {
                    new AxisComponent
                    {
                        SourceId = keyboardAxisId,
                        SourceKind = InputSourceKind.Axis
                    },
                    new AxisComponent
                    {
                        SourceId = existingAxisId,
                        SourceKind = InputSourceKind.Axis
                    }
                }
            });
            changed = true;
        }

        return changed
            ? profile with
            {
                InputSources = updated
            }
            : profile;
    }

    private static bool IsButtonSource(IReadOnlyDictionary<string, InputSource> sourceById, string sourceId)
    {
        return sourceById.TryGetValue(sourceId, out InputSource? source) && source.Kind == InputSourceKind.Button;
    }

    private static string CreateRepairSourceId(
        string actionId,
        IReadOnlyDictionary<string, InputSource> sourceById,
        IReadOnlyList<InputSource> updated,
        params string[] reservedIds)
    {
        var ids = new HashSet<string>(sourceById.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (InputSource source in updated)
        {
            ids.Add(source.Id);
        }

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

    private static InputSource CloneWithIdentity(InputSource source, string id, string displayName)
    {
        return source switch
        {
            JoystickAxisInputSource axis => axis with
            {
                Id = id,
                DisplayName = displayName
            },
            CompositeAxisInputSource composite => composite with
            {
                Id = id,
                DisplayName = displayName
            },
            VirtualButtonAxisInputSource virtualAxis => virtualAxis with
            {
                Id = id,
                DisplayName = displayName
            },
            _ => throw new InvalidOperationException($"Source type '{source.GetType().Name}' cannot be repaired as an axis binding.")
        };
    }
}
