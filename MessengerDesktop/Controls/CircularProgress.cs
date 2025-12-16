// Controls/CircularProgress.cs
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace MessengerDesktop.Controls
{
    public class CircularProgress : Control
    {
        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<CircularProgress, double>(nameof(Value), 0);

        public static readonly StyledProperty<double> MaximumProperty =
            AvaloniaProperty.Register<CircularProgress, double>(nameof(Maximum), 100);

        public static readonly StyledProperty<double> StrokeWidthProperty =
            AvaloniaProperty.Register<CircularProgress, double>(nameof(StrokeWidth), 4);

        public static readonly StyledProperty<double> SizeProperty =
            AvaloniaProperty.Register<CircularProgress, double>(nameof(Size), 24);

        public static readonly StyledProperty<IBrush?> ForegroundProperty =
            AvaloniaProperty.Register<CircularProgress, IBrush?>(nameof(Foreground), Brushes.White);

        public static readonly StyledProperty<IBrush?> BackgroundStrokeProperty =
            AvaloniaProperty.Register<CircularProgress, IBrush?>(nameof(BackgroundStroke));

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

        public IBrush? BackgroundStroke
        {
            get => GetValue(BackgroundStrokeProperty);
            set => SetValue(BackgroundStrokeProperty, value);
        }

        static CircularProgress()
        {
            AffectsRender<CircularProgress>(
                ValueProperty,
                MaximumProperty,
                StrokeWidthProperty,
                SizeProperty,
                ForegroundProperty,
                BackgroundStrokeProperty);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var size = Size;
            var strokeWidth = StrokeWidth;
            var radius = (size - strokeWidth) / 2;
            var center = new Point(size / 2, size / 2);

            // Фоновый круг
            var bgBrush = BackgroundStroke ?? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            var bgPen = new Pen(bgBrush, strokeWidth);
            context.DrawEllipse(null, bgPen, center, radius, radius);

            // Прогресс
            if (Value > 0 && Maximum > 0)
            {
                var progress = Math.Min(Value / Maximum, 1.0);
                var angle = progress * 360;

                var foregroundPen = new Pen(Foreground, strokeWidth)
                {
                    LineCap = PenLineCap.Round
                };

                var startAngle = -90; // Начинаем сверху
                var sweepAngle = angle;

                var geometry = CreateArcGeometry(center, radius, startAngle, sweepAngle);
                context.DrawGeometry(null, foregroundPen, geometry);
            }
        }

        private static PathGeometry CreateArcGeometry(Point center, double radius, double startAngle, double sweepAngle)
        {
            var startRad = startAngle * Math.PI / 180;
            var endRad = (startAngle + sweepAngle) * Math.PI / 180;

            var startPoint = new Point(
                center.X + radius * Math.Cos(startRad),
                center.Y + radius * Math.Sin(startRad));

            var endPoint = new Point(
                center.X + radius * Math.Cos(endRad),
                center.Y + radius * Math.Sin(endRad));

            var isLargeArc = sweepAngle > 180;

            var figure = new PathFigure
            {
                StartPoint = startPoint,
                IsClosed = false
            };

            figure.Segments!.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = isLargeArc
            });

            var geometry = new PathGeometry();
            geometry.Figures!.Add(figure);

            return geometry;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(Size, Size);
        }
    }
}