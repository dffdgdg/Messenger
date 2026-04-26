using Avalonia.Input;
using System;
using System.Windows.Input;

namespace MessengerDesktop.Views.Controls.Shared;

public class WaveformView : Control
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<WaveformView, double>(nameof(Progress));

    public static readonly StyledProperty<string?> WaveformProperty =
        AvaloniaProperty.Register<WaveformView, string?>(nameof(Waveform));

    public static readonly StyledProperty<ICommand?> SeekCommandProperty =
        AvaloniaProperty.Register<WaveformView, ICommand?>(nameof(SeekCommand));

    public static readonly StyledProperty<IBrush> PlayedBrushProperty =
        AvaloniaProperty.Register<WaveformView, IBrush>(nameof(PlayedBrush), new SolidColorBrush(Colors.DodgerBlue));

    public static readonly StyledProperty<IBrush> UnplayedBrushProperty =
        AvaloniaProperty.Register<WaveformView, IBrush>(nameof(UnplayedBrush), new SolidColorBrush(Colors.LightGray));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(Background));

    private byte[]? _peaks;
    private bool _isDragging;
    private Point _startPoint;

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public string? Waveform
    {
        get => GetValue(WaveformProperty);
        set => SetValue(WaveformProperty, value);
    }

    public ICommand? SeekCommand
    {
        get => GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    public IBrush PlayedBrush
    {
        get => GetValue(PlayedBrushProperty);
        set => SetValue(PlayedBrushProperty, value);
    }

    public IBrush UnplayedBrush
    {
        get => GetValue(UnplayedBrushProperty);
        set => SetValue(UnplayedBrushProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    static WaveformView()
    {
        AffectsRender<WaveformView>(ProgressProperty, WaveformProperty, PlayedBrushProperty, UnplayedBrushProperty, BackgroundProperty);
        FocusableProperty.OverrideDefaultValue<WaveformView>(true);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WaveformProperty)
        {
            ParseWaveform();
            InvalidateVisual();
        }
    }

    private void ParseWaveform()
    {
        try
        {
            _peaks = string.IsNullOrEmpty(Waveform) ? null : Convert.FromBase64String(Waveform);
        }
        catch
        {
            _peaks = null;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Background != null)
        {
            context.FillRectangle(Background, new Rect(Bounds.Size));
        }

        if (_peaks == null || _peaks.Length == 0)
        {
            var y = Bounds.Height / 2;
            context.DrawLine(new Pen(UnplayedBrush, 2), new Point(0, y), new Point(Bounds.Width, y));
            return;
        }

        const double barWidth = 3.0;
        const double gap = 2.0;
        const double totalBarSpace = barWidth + gap;
        var barsCount = _peaks.Length;
        var totalWidth = (barsCount * totalBarSpace) - gap;
        var startX = (Bounds.Width - totalWidth) / 2;

        var progressX = (Progress / 100.0) * Bounds.Width;

        for (var i = 0; i < barsCount; i++)
        {
            var x = startX + (i * totalBarSpace);
            var normalizedPeak = 0.2 + (_peaks[i] / 255.0 * 0.8);
            var barHeight = normalizedPeak * Bounds.Height;
            var y = (Bounds.Height - barHeight) / 2;

            var brush = x < progressX ? PlayedBrush : UnplayedBrush;
            context.DrawRectangle(brush, null, new Rect(x, y, barWidth, barHeight), barWidth / 2, barWidth / 2);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _startPoint = e.GetPosition(this);

            e.Pointer?.Capture(this);

            UpdateProgressFromPoint(_startPoint);

            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isDragging)
        {
            var currentPoint = e.GetPosition(this);

            if (Math.Abs(currentPoint.X - _startPoint.X) > 2 || Math.Abs(currentPoint.Y - _startPoint.Y) > 2)
            {
                UpdateProgressFromPoint(currentPoint);
                _startPoint = currentPoint;
            }
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isDragging)
        {
            var point = e.GetPosition(this);
            UpdateProgressFromPoint(point);

            // Освобождаем захват
            e.Pointer?.Capture(null);
            _isDragging = false;
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isDragging = false;
    }

    private void UpdateProgressFromPoint(Point position)
    {
        if (Bounds.Width <= 0)
            return;

        var clampedX = Math.Clamp(position.X, 0, Bounds.Width);
        var pct = (clampedX / Bounds.Width) * 100.0;

        if (SeekCommand?.CanExecute(pct) == true)
        {
            SeekCommand.Execute(pct);
        }
        else
        {
            Progress = pct;
        }
    }
}