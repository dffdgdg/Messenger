using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace MessengerDesktop.Controls;

public class CircularProgress : Control
{
    private double _animationAngle;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CircularProgress, double>(nameof(Value), 0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<CircularProgress, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<CircularProgress, double>(nameof(Minimum), 0);

    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<CircularProgress, double>(nameof(StrokeWidth), 4);

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<CircularProgress, double>(nameof(Size), 32);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<CircularProgress, IBrush?>(nameof(Foreground), Brushes.DodgerBlue);

    public static readonly StyledProperty<IBrush?> BackgroundTrackProperty =
        AvaloniaProperty.Register<CircularProgress, IBrush?>(nameof(BackgroundTrack));

    public static readonly StyledProperty<bool> IsIndeterminateProperty =
        AvaloniaProperty.Register<CircularProgress, bool>(nameof(IsIndeterminate), false);

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? BackgroundTrack
    {
        get => GetValue(BackgroundTrackProperty);
        set => SetValue(BackgroundTrackProperty, value);
    }

    public bool IsIndeterminate
    {
        get => GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    private IDisposable? _timerSubscription;

    static CircularProgress()
    {
        AffectsRender<CircularProgress>(
            ValueProperty,
            MaximumProperty,
            MinimumProperty,
            StrokeWidthProperty,
            SizeProperty,
            ForegroundProperty,
            BackgroundTrackProperty,
            IsIndeterminateProperty);
    }

    public CircularProgress()
    {
        Width = Size;
        Height = Size;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SizeProperty)
        {
            Width = Size;
            Height = Size;
        }
        else if (change.Property == IsIndeterminateProperty)
        {
            if (IsIndeterminate)
            {
                StartAnimation();
            }
            else
            {
                StopAnimation();
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsIndeterminate)
        {
            StartAnimation();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopAnimation();
    }

    private void StartAnimation()
    {
        StopAnimation();

        _timerSubscription = Avalonia.Threading.DispatcherTimer.Run(() =>
        {
            _animationAngle = (_animationAngle + 6) % 360;
            InvalidateVisual();
            return true;
        },TimeSpan.FromMilliseconds(16)); // ~60 FPS
    }

    private void StopAnimation()
    {
        _timerSubscription?.Dispose();
        _timerSubscription = null;
        _animationAngle = 0;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        var strokeWidth = StrokeWidth;
        var radius = (size - strokeWidth) / 2;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

        // Фоновый трек
        if (BackgroundTrack != null)
        {
            var trackPen = new Pen(BackgroundTrack, strokeWidth, lineCap: PenLineCap.Round);
            context.DrawEllipse(null, trackPen, center, radius, radius);
        }

        // Прогресс
        if (Foreground == null) return;

        var pen = new Pen(Foreground, strokeWidth, lineCap: PenLineCap.Round);

        if (IsIndeterminate)
        {
            // Анимированная дуга для неопределённого состояния
            DrawIndeterminateArc(context, center, radius, pen);
        }
        else
        {
            // Обычный прогресс
            DrawProgressArc(context, center, radius, pen);
        }
    }

    private void DrawProgressArc(DrawingContext context, Point center, double radius, Pen pen)
    {
        var range = Maximum - Minimum;
        if (range <= 0) return;

        var normalizedValue = Math.Clamp((Value - Minimum) / range, 0, 1);
        if (normalizedValue <= 0) return;

        var sweepAngle = normalizedValue * 360;
        DrawArc(context, center, radius, -90, sweepAngle, pen);
    }

    private void DrawIndeterminateArc(DrawingContext context, Point center, double radius, Pen pen)
    {
        var startAngle = _animationAngle - 90;
        var sweepAngle = 90.0;

        DrawArc(context, center, radius, startAngle, sweepAngle, pen);
    }

    private static void DrawArc(DrawingContext context, Point center, double radius, double startAngle, double sweepAngle, Pen pen)
    {
        if (sweepAngle >= 360)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var startRad = startAngle * Math.PI / 180;
        var endRad = (startAngle + sweepAngle) * Math.PI / 180;

        var startPoint = new Point(center.X + radius * Math.Cos(startRad),center.Y + radius * Math.Sin(startRad));

        var endPoint = new Point(center.X + radius * Math.Cos(endRad), center.Y + radius * Math.Sin(endRad));

        var isLargeArc = sweepAngle > 180;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(startPoint, false);
            ctx.ArcTo(endPoint, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, pen, geometry);
    }
}