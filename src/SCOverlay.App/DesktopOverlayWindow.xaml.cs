using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private bool isLocked = true;
    private bool isClickThrough = true;
    private OverlayState? latestState;

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
        Left = settings.Left;
        Top = settings.Top;
        Width = Math.Max(settings.Width, MinWidth > 0 ? MinWidth : 320);
        Height = Math.Max(settings.Height, MinHeight > 0 ? MinHeight : 220);
        IsLocked = settings.IsLocked;
        IsClickThrough = settings.IsClickThrough;
        RefreshEditorChrome();
    }

    public DesktopOverlaySettings CaptureSettings(bool isVisible)
    {
        return new DesktopOverlaySettings
        {
            IsVisible = isVisible,
            IsLocked = IsLocked,
            IsClickThrough = IsClickThrough,
            Left = Left,
            Top = Top,
            Width = Width,
            Height = Height
        };
    }

    public void ResetPlacement()
    {
        Width = 900;
        Height = 520;
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

        double centerX = ActualWidth > 0 ? ActualWidth / 2.0 : Width / 2.0;
        double centerY = ActualHeight > 0 ? ActualHeight / 2.0 : Height / 2.0;
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

    private void DrawStick(StickWidgetState widget, double centerX, double centerY)
    {
        double radius = Math.Max(widget.Size / 2.0, 12);
        double x = centerX + widget.X;
        double y = centerY + widget.Y;
        double opacity = WidgetOpacity(widget);
        MediaBrush display = Brush(widget.DisplayColor, opacity);
        MediaBrush ring = Brush(widget.RingColor, opacity * 0.35);

        Add(new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = display,
            StrokeThickness = 3,
            Effect = ShadowEffect(widget.VisualEffects, opacity)
        }, x - radius, y - radius);

        Add(new Line { X1 = x - radius, Y1 = y, X2 = x + radius, Y2 = y, Stroke = ring, StrokeThickness = 2, Effect = ShadowEffect(widget.VisualEffects, opacity) });
        Add(new Line { X1 = x, Y1 = y - radius, X2 = x, Y2 = y + radius, Stroke = ring, StrokeThickness = 2, Effect = ShadowEffect(widget.VisualEffects, opacity) });

        double knobX = x + (widget.XValue * radius);
        double knobY = y - (widget.YValue * radius);
        Add(new Line { X1 = x, Y1 = y, X2 = knobX, Y2 = knobY, Stroke = Brush(widget.DisplayColor, opacity * 0.7), StrokeThickness = 2, Effect = ShadowEffect(widget.VisualEffects, opacity) });
        double knob = 24 + (widget.Activity * 14);
        Add(new Ellipse { Width = knob, Height = knob, Fill = display, Effect = ShadowEffect(widget.VisualEffects, opacity) }, knobX - (knob / 2.0), knobY - (knob / 2.0));
    }

    private void DrawThrottle(ThrottleWidgetState widget, double centerX, double centerY)
    {
        double x = centerX + widget.X;
        double y = centerY + widget.Y;
        double width = Math.Max(widget.Width, 16);
        double height = Math.Max(widget.Height, 32);
        double opacity = WidgetOpacity(widget);
        MediaBrush display = Brush(widget.DisplayColor, opacity);

        Add(new WpfRectangle
        {
            Width = width,
            Height = height,
            Stroke = display,
            StrokeThickness = 3,
            Effect = ShadowEffect(widget.VisualEffects, opacity)
        }, x - (width / 2.0), y - (height / 2.0));

        double ratio = Math.Clamp(widget.FillRatio, 0.0, 1.0);
        double fillHeight = Math.Max((height - 10) * ratio, 0.0);
        Add(new WpfRectangle
        {
            Width = Math.Max(width - 10, 1),
            Height = fillHeight,
            Fill = Brush(widget.DisplayColor, opacity * 0.75),
            Effect = ShadowEffect(widget.VisualEffects, opacity)
        }, x - (width / 2.0) + 5, y + (height / 2.0) - fillHeight - 5);
    }

    private void DrawRoll(RollWidgetState widget, double centerX, double centerY)
    {
        double x = centerX + widget.X;
        double y = centerY + widget.Y;
        double width = Math.Max(widget.Width, 80);
        double height = Math.Max(widget.Height, 60);
        double opacity = WidgetOpacity(widget);
        MediaBrush display = Brush(widget.DisplayColor, opacity);

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
            Stroke = display,
            StrokeThickness = 5,
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

    private void DrawStateText(StateTextWidgetState widget, double centerX, double centerY)
    {
        double opacity = WidgetOpacity(widget) * (0.55 + (widget.Intensity * 0.45));
        double textX = centerX + widget.X;
        double textY = centerY + widget.Y;
        var text = new TextBlock
        {
            Text = widget.Text,
            Foreground = Brush(widget.DisplayColor, opacity),
            FontSize = Math.Max(widget.FontSize, 8),
            FontWeight = FontWeights.Bold,
            Effect = ShadowEffect(widget.TextEffects, opacity)
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
                Fill = Brush(widget.TextEffects.BackplateColor, WidgetOpacity(widget))
            }, left - padding, top - (padding * 0.6));
        }

        if (widget.TextEffects.OutlineEnabled && widget.TextEffects.OutlineWidth > 0.0)
        {
            AddTextOutline(widget, left, top, text.DesiredSize, opacity);
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

    private static MediaBrush Brush(RgbaColor color, double opacity)
    {
        byte alpha = (byte)Math.Round(Math.Clamp(color.A * Math.Clamp(opacity, 0.0, 1.0), 0.0, 255.0));
        return new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
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
                effects.ShadowColor.A,
                effects.ShadowColor.R,
                effects.ShadowColor.G,
                effects.ShadowColor.B),
            BlurRadius = Math.Max(effects.ShadowWidth, 0.0),
            Direction = direction,
            ShadowDepth = depth,
            Opacity = Math.Clamp(opacity, 0.0, 1.0)
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
