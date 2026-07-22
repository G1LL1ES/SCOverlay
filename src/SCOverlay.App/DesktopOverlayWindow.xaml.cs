using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SCOverlay.Core.Application;
using SCOverlay.Core.Domain;
using SCOverlay.Core.Rendering;
using MediaBrush = System.Windows.Media.Brush;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfSize = System.Windows.Size;

namespace SCOverlay.App;

public partial class DesktopOverlayWindow : Window
{
    private const double DesignWidth = 900;
    private const double DesignHeight = 520;
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private bool isLocked = true;
    private bool isClickThrough = true;
    private OverlayState? latestState;
    private readonly Dictionary<string, BitmapImage?> rollImageCache = new(StringComparer.OrdinalIgnoreCase);

    public DesktopOverlayWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redraw();
        SourceInitialized += (_, _) => ApplyWindowStyles();
    }

    public bool IsLocked
    {
        get => isLocked;
        set
        {
            isLocked = value;
            RefreshEditorChrome();
        }
    }

    public bool IsClickThrough
    {
        get => isClickThrough;
        set
        {
            isClickThrough = value;
            ApplyWindowStyles();
            RefreshEditorChrome();
        }
    }

    public void ApplySettings(DesktopOverlaySettings settings)
    {
        DesktopOverlaySettings clamped = ClampToVisibleScreens(settings);
        Left = clamped.Left;
        Top = clamped.Top;
        Width = clamped.Width;
        Height = clamped.Height;
        IsLocked = clamped.IsLocked;
        IsClickThrough = clamped.IsClickThrough;
        RefreshEditorChrome();
    }

    public DesktopOverlaySettings CaptureSettings(bool isVisible)
    {
        return ClampToVisibleScreens(new DesktopOverlaySettings
        {
            IsVisible = isVisible,
            IsLocked = IsLocked,
            IsClickThrough = IsClickThrough,
            Left = Left,
            Top = Top,
            Width = Width,
            Height = Height
        });
    }

    public void ResetPlacement()
    {
        Width = DesignWidth;
        Height = DesignHeight;
        Left = Math.Max(SystemParameters.WorkArea.Left + 80, 0);
        Top = Math.Max(SystemParameters.WorkArea.Top + 80, 0);
        Redraw();
    }

    public void UpdateState(OverlayState state)
    {
        latestState = state;
        Redraw();
    }

    private void Root_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsLocked || IsClickThrough || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (IsLocked || IsClickThrough)
        {
            return;
        }

        Width = Math.Max(320, Width + e.HorizontalChange);
        Height = Math.Max(220, Height + e.VerticalChange);
        Redraw();
    }

    private void RefreshEditorChrome()
    {
        bool editable = !IsLocked && !IsClickThrough;
        EditFrame.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        Toolbar.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        ResizeThumb.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        ToolbarText.Text = editable ? "SC Overlay - drag to move, corner to resize" : string.Empty;
    }

    private void ApplyWindowStyles()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int style = GetWindowLong(hwnd, GwlExStyle);
        style |= WsExToolWindow | WsExNoActivate;
        if (IsClickThrough)
        {
            style |= WsExTransparent;
        }
        else
        {
            style &= ~WsExTransparent;
        }

        SetWindowLong(hwnd, GwlExStyle, style);
    }

    private void Redraw()
    {
        OverlayCanvas.Children.Clear();
        if (latestState is null)
        {
            return;
        }

        ApplyCanvasScale();
        double centerX = DesignWidth / 2.0;
        double centerY = DesignHeight / 2.0;
        foreach (WidgetState widget in latestState.Widgets)
        {
            switch (widget)
            {
                case StickWidgetState stick:
                    DrawStick(stick, centerX, centerY);
                    break;
                case ThrottleWidgetState throttle:
                    DrawThrottle(throttle, centerX, centerY);
                    break;
                case RollWidgetState roll:
                    DrawRoll(roll, centerX, centerY);
                    break;
                case StateTextWidgetState stateText:
                    DrawStateText(stateText, centerX, centerY);
                    break;
            }
        }
    }

    private void ApplyCanvasScale()
    {
        double actualWidth = ActualWidth > 0 ? ActualWidth : Width;
        double actualHeight = ActualHeight > 0 ? ActualHeight : Height;
        double scale = Math.Max(Math.Min(actualWidth / DesignWidth, actualHeight / DesignHeight), 0.01);
        double offsetX = Math.Max((actualWidth - (DesignWidth * scale)) / 2.0, 0.0);
        double offsetY = Math.Max((actualHeight - (DesignHeight * scale)) / 2.0, 0.0);

        OverlayCanvas.Width = DesignWidth;
        OverlayCanvas.Height = DesignHeight;
        OverlayCanvas.RenderTransform = new TransformGroup
        {
            Children = new TransformCollection
            {
                new ScaleTransform(scale, scale),
                new TranslateTransform(offsetX, offsetY)
            }
        };
    }

    private static DesktopOverlaySettings ClampToVisibleScreens(DesktopOverlaySettings settings)
    {
        return DesktopOverlayPlacement.Clamp(
            settings,
            new DesktopOverlayBounds(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight));
    }

    private void DrawStick(StickWidgetState widget, double centerX, double centerY)
    {
        double radius = Math.Max(widget.Size / 2.0, 12);
        double x = centerX + widget.X;
        double y = centerY + widget.Y;
        double opacity = WidgetOpacity(widget);
        MediaBrush display = Brush(widget.DisplayColor, opacity);
        MediaBrush frame = Brush(widget.FrameDisplayColor, opacity);
        MediaBrush guide = Brush(widget.FrameDisplayColor, opacity * 0.35);

        Add(new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = frame,
            StrokeThickness = Math.Max(widget.LineThickness, 0.0),
            Effect = ShadowEffect(widget.VisualEffects, opacity)
        }, x - radius, y - radius);

        double guideThickness = Math.Max(widget.LineThickness * 0.66, 0.0);
        Add(new Line { X1 = x - radius, Y1 = y, X2 = x + radius, Y2 = y, Stroke = guide, StrokeThickness = guideThickness, Effect = ShadowEffect(widget.VisualEffects, opacity) });
        Add(new Line { X1 = x, Y1 = y - radius, X2 = x, Y2 = y + radius, Stroke = guide, StrokeThickness = guideThickness, Effect = ShadowEffect(widget.VisualEffects, opacity) });

        double offsetX = widget.XValue * radius;
        double offsetY = -widget.YValue * radius;
        double distance = Math.Sqrt((offsetX * offsetX) + (offsetY * offsetY));
        double pillWidth = 20.0 + (widget.Activity * 10.0);
        double pillLength = Math.Max(pillWidth, distance + pillWidth);
        double angle = distance <= 0.01 ? 0.0 : Math.Atan2(offsetY, offsetX) * 180.0 / Math.PI;
        Add(new WpfRectangle
        {
            Width = pillLength,
            Height = pillWidth,
            RadiusX = pillWidth / 2.0,
            RadiusY = pillWidth / 2.0,
            Fill = Brush(widget.DisplayColor, opacity * 0.86),
            Effect = ShadowEffect(widget.VisualEffects, opacity),
            RenderTransform = new RotateTransform(angle, pillWidth / 2.0, pillWidth / 2.0)
        }, x - (pillWidth / 2.0), y - (pillWidth / 2.0));
    }

    private void DrawThrottle(ThrottleWidgetState widget, double centerX, double centerY)
    {
        double x = centerX + widget.X;
        double y = centerY + widget.Y;
        double width = Math.Max(widget.Width, 16);
        double height = Math.Max(widget.Height, 32);
        double opacity = WidgetOpacity(widget);
        MediaBrush display = Brush(widget.DisplayColor, opacity);
        MediaBrush frame = Brush(widget.FrameDisplayColor, opacity);
        double frameRadius = ClampCornerRadius(widget.CornerRadius, width, height);

        Add(new WpfRectangle
        {
            Width = width,
            Height = height,
            Stroke = frame,
            StrokeThickness = Math.Max(widget.LineThickness, 0.0),
            RadiusX = frameRadius,
            RadiusY = frameRadius,
            Effect = ShadowEffect(widget.VisualEffects, opacity)
        }, x - (width / 2.0), y - (height / 2.0));

        double ratio = Math.Clamp(widget.FillRatio, 0.0, 1.0);
        double inset = Math.Max(4.0, widget.LineThickness + 2.0);
        double innerWidth = Math.Max(width - (inset * 2.0), 1);
        double innerHeight = Math.Max(height - (inset * 2.0), 1);
        double centerBand = Math.Min(innerHeight, Math.Max(4.0, widget.LineThickness + 1.0));
        double travel = Math.Max((innerHeight - centerBand) / 2.0, 0.0);
        double extension = travel * ratio;
        double fillHeight = centerBand + extension;
        double fillRadius = ClampCornerRadius(widget.CornerRadius - inset, innerWidth, fillHeight);
        double fillTop = widget.Value >= 0.0
            ? y - (centerBand / 2.0) - extension
            : y - (centerBand / 2.0);
        Add(new WpfRectangle
        {
            Width = innerWidth,
            Height = fillHeight,
            RadiusX = fillRadius,
            RadiusY = fillRadius,
            Fill = Brush(widget.DisplayColor, opacity * 0.82),
            Effect = ShadowEffect(widget.VisualEffects, opacity)
        }, x - (width / 2.0) + inset, fillTop);
    }

    private void DrawRoll(RollWidgetState widget, double centerX, double centerY)
    {
        double x = centerX + widget.X;
        double y = centerY + widget.Y;
        double width = Math.Max(widget.Width, 80);
        double height = Math.Max(widget.Height, 60);
        double opacity = WidgetOpacity(widget);
        MediaBrush display = Brush(widget.DisplayColor, opacity);
        MediaBrush frame = Brush(widget.FrameDisplayColor, opacity);

        if (widget.RenderMode == RollRenderMode.Image)
        {
            DrawRollImage(widget, x, y, width, height, opacity, display);
            return;
        }

        var figure = new PathFigure { StartPoint = new WpfPoint(x - (width / 2.0), y + 18) };
        figure.Segments.Add(new ArcSegment
        {
            Point = new WpfPoint(x + (width / 2.0), y + 18),
            Size = new WpfSize(width / 2.0, height / 2.0),
            SweepDirection = SweepDirection.Clockwise
        });
        Add(new Path
        {
            Data = new PathGeometry(new[] { figure }),
            Stroke = frame,
            StrokeThickness = Math.Max(widget.LineThickness, 0.0),
            Effect = ShadowEffect(widget.VisualEffects, opacity)
        });

        var indicator = new Polygon
        {
            Fill = display,
            Effect = ShadowEffect(widget.VisualEffects, opacity),
            Points = new PointCollection
            {
                new WpfPoint(0, -height / 2.0),
                new WpfPoint(16, 12),
                new WpfPoint(0, 2),
                new WpfPoint(-16, 12)
            },
            RenderTransform = new TransformGroup
            {
                Children = new TransformCollection
                {
                    new RotateTransform(widget.RotationDegrees),
                    new TranslateTransform(x, y)
                }
            }
        };
        Add(indicator);
    }

    private void DrawRollImage(RollWidgetState widget, double x, double y, double width, double height, double opacity, MediaBrush display)
    {
        BitmapImage? source = LoadRollImage(widget.AssetId);
        if (source is not null)
        {
            var imageMask = new ImageBrush(source)
            {
                Stretch = Stretch.Fill
            };
            var tintedImage = new WpfRectangle
            {
                Width = width,
                Height = height,
                Fill = display,
                OpacityMask = imageMask,
                Effect = ShadowEffect(widget.VisualEffects, opacity),
                RenderTransformOrigin = new WpfPoint(0.5, 0.5),
                RenderTransform = new RotateTransform(widget.RotationDegrees)
            };
            Add(tintedImage, x - (width / 2.0), y - (height / 2.0));
            return;
        }

        double scale = Math.Min(width / 160.0, height / 160.0);
        WpfPoint[] points =
        {
            new(0, -62),
            new(19, -12),
            new(56, 13),
            new(18, 19),
            new(0, 62),
            new(-18, 19),
            new(-56, 13),
            new(-19, -12)
        };

        var marker = new Polygon
        {
            Fill = display,
            Stroke = Brush(widget.FrameDisplayColor, opacity * 0.28),
            StrokeThickness = Math.Max(widget.LineThickness, 0.0),
            Effect = ShadowEffect(widget.VisualEffects, opacity),
            Points = new PointCollection(points),
            RenderTransform = new TransformGroup
            {
                Children = new TransformCollection
                {
                    new ScaleTransform(scale, scale),
                    new RotateTransform(widget.RotationDegrees),
                    new TranslateTransform(x, y)
                }
            }
        };
        Add(marker);
    }

    private BitmapImage? LoadRollImage(string assetId)
    {
        string fileName = string.Equals(assetId, RollAssets.Arrow, StringComparison.OrdinalIgnoreCase)
            ? "roll_indicator_arrow.png"
            : "roll_indicator.png";
        if (rollImageCache.TryGetValue(fileName, out BitmapImage? cached))
        {
            return cached;
        }

        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        if (!System.IO.File.Exists(path))
        {
            rollImageCache[fileName] = null;
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        rollImageCache[fileName] = image;
        return image;
    }

    private void DrawStateText(StateTextWidgetState widget, double centerX, double centerY)
    {
        double connectedOpacity = WidgetOpacity(widget);
        double textOpacity = connectedOpacity * (0.55 + (widget.Intensity * 0.45));
        double textX = centerX + widget.X;
        double textY = centerY + widget.Y;
        if (widget.ShakeIntensity > 0.0)
        {
            double ticks = Environment.TickCount64;
            textX += Math.Sin(ticks * 0.095) * 1.8 * widget.ShakeIntensity;
            textY += Math.Cos(ticks * 0.12) * 1.2 * widget.ShakeIntensity;
        }
        var text = new TextBlock
        {
            Text = widget.Text,
            Foreground = Brush(widget.DisplayColor, textOpacity),
            FontSize = Math.Max(widget.FontSize, 8),
            FontWeight = FontWeights.Bold,
            Effect = ShadowEffect(widget.TextEffects, connectedOpacity)
        };
        text.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        double left = textX - (text.DesiredSize.Width / 2.0);
        double top = textY - (text.DesiredSize.Height / 2.0);
        if (widget.TextEffects.BackplateEnabled)
        {
            double padding = Math.Max(widget.TextEffects.BackplatePadding, 0.0);
            Add(new WpfRectangle
            {
                Width = text.DesiredSize.Width + (padding * 2),
                Height = text.DesiredSize.Height + (padding * 1.2),
                RadiusX = Math.Max(widget.TextEffects.BackplateRadius, 0.0),
                RadiusY = Math.Max(widget.TextEffects.BackplateRadius, 0.0),
                Fill = Brush(widget.TextEffects.BackplateColor, connectedOpacity)
            }, left - padding, top - (padding * 0.6));
        }

        if (widget.TextEffects.OutlineEnabled && widget.TextEffects.OutlineWidth > 0.0)
        {
            AddTextOutline(widget, left, top, text.DesiredSize, connectedOpacity);
        }

        Add(text, left, top);
    }

    private void Add(UIElement element)
    {
        OverlayCanvas.Children.Add(element);
    }

    private void Add(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        OverlayCanvas.Children.Add(element);
    }

    private static double WidgetOpacity(WidgetState widget)
    {
        return widget.Connected ? 1.0 : 0.32;
    }

    private static double ClampCornerRadius(double radius, double width, double height)
    {
        return Math.Max(0.0, Math.Min(radius, Math.Min(width / 2.0, height / 2.0)));
    }

    private static MediaBrush Brush(RgbaColor color, double opacity)
    {
        byte alpha = (byte)Math.Round(Math.Clamp(color.A * Math.Clamp(opacity, 0.0, 1.0), 0.0, 255.0));
        return new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private static double AlphaFactor(RgbaColor color)
    {
        return Math.Clamp(color.A / 255.0, 0.0, 1.0);
    }

    private void AddTextOutline(StateTextWidgetState widget, double left, double top, WpfSize desiredSize, double opacity)
    {
        double offset = Math.Max(widget.TextEffects.OutlineWidth, 1.0);
        WpfPoint[] offsets =
        {
            new(-offset, 0),
            new(offset, 0),
            new(0, -offset),
            new(0, offset),
            new(-offset, -offset),
            new(offset, -offset),
            new(-offset, offset),
            new(offset, offset)
        };

        foreach (WpfPoint point in offsets)
        {
            var outline = new TextBlock
            {
                Text = widget.Text,
                Foreground = Brush(widget.TextEffects.OutlineColor, opacity),
                FontSize = Math.Max(widget.FontSize, 8),
                FontWeight = FontWeights.Bold
            };
            outline.Measure(desiredSize);
            Add(outline, left + point.X, top + point.Y);
        }
    }

    private static System.Windows.Media.Effects.DropShadowEffect? ShadowEffect(EffectSettings effects, double opacity)
    {
        if (!effects.ShadowEnabled || effects.ShadowWidth <= 0.0)
        {
            return null;
        }

        double depth = Math.Sqrt((effects.ShadowOffsetX * effects.ShadowOffsetX) + (effects.ShadowOffsetY * effects.ShadowOffsetY));
        double direction = depth <= 0.0
            ? 315.0
            : Math.Atan2(-effects.ShadowOffsetY, effects.ShadowOffsetX) * 180.0 / Math.PI;
        return new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = System.Windows.Media.Color.FromArgb(
                255,
                effects.ShadowColor.R,
                effects.ShadowColor.G,
                effects.ShadowColor.B),
            BlurRadius = Math.Max(effects.ShadowWidth, 0.0),
            Direction = direction,
            ShadowDepth = depth,
            Opacity = AlphaFactor(effects.ShadowColor) * Math.Clamp(opacity, 0.0, 1.0)
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
