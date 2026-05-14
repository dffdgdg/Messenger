using Avalonia.Input;
using System.Windows.Input;

namespace Desktop.Views.Controls.Shared;

public class WaveformView : Control
{
    public static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<WaveformView, double>(nameof(Progress));

    public static readonly StyledProperty<string?> WaveformProperty = AvaloniaProperty.Register<WaveformView, string?>(nameof(Waveform));

    public static readonly StyledProperty<ICommand?> SeekCommandProperty = AvaloniaProperty.Register<WaveformView, ICommand?>(nameof(SeekCommand));

    public static readonly StyledProperty<IBrush> PlayedBrushProperty = AvaloniaProperty.Register<WaveformView, IBrush>(nameof(PlayedBrush), new SolidColorBrush(Colors.DodgerBlue));

    public static readonly StyledProperty<IBrush> UnplayedBrushProperty = AvaloniaProperty.Register<WaveformView, IBrush>(nameof(UnplayedBrush), new SolidColorBrush(Colors.LightGray));

    public static readonly StyledProperty<IBrush?> BackgroundProperty = AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(Background));

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

        // Фон
        if (Background != null)
            context.FillRectangle(Background, new Rect(Bounds.Size));

        // Нет данных — рисуем линию
        if (_peaks == null || _peaks.Length == 0)
        {
            var y = Bounds.Height / 2;
            context.DrawLine(new Pen(UnplayedBrush, 2), new Point(0, y), new Point(Bounds.Width, y));
            return;
        }

        const double barWidth = 3.0;
        const double gap = 2.0;
        const double totalBarSpace = barWidth + gap; // 5.0

        // Максимальное количество столбцов, которые поместятся в ширину
        int maxBars = (int)((Bounds.Width + gap) / totalBarSpace);
        if (maxBars <= 0) return;

        // Подгоняем массив пиков под maxBars
        byte[] peaksToDraw = ResamplePeaks(_peaks, maxBars);

        int barCount = peaksToDraw.Length;
        double totalWidth = (barCount * totalBarSpace) - gap;
        double startX = (Bounds.Width - totalWidth) / 2; // центрирование, всегда >=0

        // Прогресс: позиция в координатах волны
        double progressX = startX + ((Progress / 100.0) * totalWidth);

        for (int i = 0; i < barCount; i++)
        {
            var x = startX + (i * totalBarSpace);
            var normalizedPeak = 0.2 + (peaksToDraw[i] / 255.0 * 0.8);
            var barHeight = normalizedPeak * Bounds.Height;
            var y = (Bounds.Height - barHeight) / 2;

            // Сравниваем центр столбца
            var barCenter = x + (barWidth / 2.0);
            var brush = barCenter <= progressX ? PlayedBrush : UnplayedBrush;
            context.DrawRectangle(brush, null, new Rect(x, y, barWidth, barHeight), barWidth / 2, barWidth / 2);
        }
    }

    /// <summary>
    /// Прореживает или усредняет массив байт до требуемой длины.
    /// Если исходный массив меньше или равен maxLength — возвращает его копию.
    /// </summary>
    private static byte[] ResamplePeaks(byte[] source, int maxLength)
    {
        if (maxLength <= 0) return [];
        if (source.Length <= maxLength) return [.. source];

        var resampled = new byte[maxLength];
        double step = (double)source.Length / maxLength;

        for (int i = 0; i < maxLength; i++)
        {
            double srcIndex = i * step;
            int idx1 = (int)srcIndex;
            int idx2 = Math.Min(idx1 + 1, source.Length - 1);
            double frac = srcIndex - idx1;

            resampled[i] = (byte)((source[idx1] * (1 - frac)) + (source[idx2] * frac));
        }

        return resampled;
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
        if (Bounds.Width <= 0) return;

        const double barWidth = 3.0, gap = 2.0, totalBarSpace = barWidth + gap;
        int maxBars = (int)((Bounds.Width + gap) / totalBarSpace);
        var peaks = _peaks?.Length > 0 ? ResamplePeaks(_peaks, maxBars) : null;

        double pct;
        if (peaks?.Length > 0)
        {
            double totalWidth = (peaks.Length * totalBarSpace) - gap;
            double startX = (Bounds.Width - totalWidth) / 2;

            double relX = Math.Clamp(position.X - startX, 0, totalWidth);
            pct = relX / totalWidth * 100.0;
        }
        else
        {
            pct = Math.Clamp(position.X / Bounds.Width * 100.0, 0, 100);
        }

        if (SeekCommand?.CanExecute(pct) == true)
            SeekCommand.Execute(pct);
        else
            Progress = pct;
    }
}