using SCOverlay.Core.Domain;

namespace SCOverlay.Core.Profiles;

public static class AppearanceSettingsNormalizer
{
    public static AppearanceSettings Normalize(AppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        var defaults = new AppearanceSettings();
        RgbaColor ringColor = NormalizeColor(appearance.RingColor, defaults.RingColor);
        RgbaColor activeColor = NormalizeColor(appearance.ActiveColor, defaults.ActiveColor);
        RgbaColor frameColor = NormalizeColor(appearance.FrameColor, ringColor);
        RgbaColor frameActiveColor = NormalizeColor(appearance.FrameActiveColor, activeColor);

        return appearance with
        {
            RingColor = ringColor,
            ActiveColor = activeColor,
            FrameColor = frameColor,
            FrameActiveColor = frameActiveColor,
            Opacity = Math.Clamp(appearance.Opacity, 0.1, 1.0),
            PrimaryOpacity = Math.Clamp(ApplyLegacyAlpha(appearance.PrimaryOpacity, appearance.RingColor), 0.0, 1.0),
            ActiveOpacity = Math.Clamp(ApplyLegacyAlpha(appearance.ActiveOpacity, appearance.ActiveColor), 0.0, 1.0),
            FramePrimaryOpacity = Math.Clamp(ApplyLegacyAlpha(appearance.FramePrimaryOpacity, appearance.FrameColor), 0.0, 1.0),
            FrameActiveOpacity = Math.Clamp(ApplyLegacyAlpha(appearance.FrameActiveOpacity, appearance.FrameActiveColor), 0.0, 1.0),
            WidgetScale = Math.Clamp(appearance.WidgetScale, 0.5, 1.75)
        };
    }

    private static RgbaColor NormalizeColor(RgbaColor color, RgbaColor fallback)
    {
        if (color.A == 0 && color.R == 0 && color.G == 0 && color.B == 0)
        {
            return Opaque(fallback);
        }

        return Opaque(color);
    }

    private static double ApplyLegacyAlpha(double opacity, RgbaColor color)
    {
        if (color.A == 0)
        {
            return opacity;
        }

        return opacity * (color.A / 255.0);
    }

    private static RgbaColor Opaque(RgbaColor color)
    {
        return color with
        {
            A = 255
        };
    }
}
