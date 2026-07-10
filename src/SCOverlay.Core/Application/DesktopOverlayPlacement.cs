namespace SCOverlay.Core.Application;

public sealed record DesktopOverlayBounds(double Left, double Top, double Width, double Height);

public static class DesktopOverlayPlacement
{
    public static DesktopOverlaySettings Clamp(
        DesktopOverlaySettings settings,
        DesktopOverlayBounds visibleBounds,
        double minimumWidth = 320,
        double minimumHeight = 220)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(visibleBounds);

        double availableWidth = Math.Max(visibleBounds.Width, minimumWidth);
        double availableHeight = Math.Max(visibleBounds.Height, minimumHeight);
        double width = Math.Clamp(settings.Width, minimumWidth, availableWidth);
        double height = Math.Clamp(settings.Height, minimumHeight, availableHeight);
        double maxLeft = visibleBounds.Left + availableWidth - width;
        double maxTop = visibleBounds.Top + availableHeight - height;

        return settings with
        {
            Width = width,
            Height = height,
            Left = Math.Clamp(settings.Left, visibleBounds.Left, maxLeft),
            Top = Math.Clamp(settings.Top, visibleBounds.Top, maxTop)
        };
    }
}
