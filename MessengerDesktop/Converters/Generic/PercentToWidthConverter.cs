using System;
using System.Collections.Generic;
using System.Globalization;

namespace MessengerDesktop.Converters.Generic;

/// <summary>
/// Конвертирует процент (0-100) и ширину контейнера в абсолютную ширину.
/// values[0] = percentage (double/int)
/// values[1] = container Bounds (Rect) или width (double)
/// </summary>
public class PercentToWidthConverter : IMultiValueConverter
{
    /// <summary>
    /// Минимальная ширина при ненулевом проценте (чтобы полоска была видна)
    /// </summary>
    public double MinVisibleWidth { get; set; } = 8d;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return 0d;

        var percentage = values[0] switch
        {
            double d => d,
            int i => i,
            float f => (double)f,
            _ => 0d
        };

        var containerWidth = values[1] switch
        {
            double d => d,
            Rect rect => rect.Width,
            int i => i,
            _ => 0d
        };

        if (containerWidth <= 0 || percentage <= 0)
            return 0d;

        var width = containerWidth * Math.Clamp(percentage, 0, 100) / 100.0;

        return Math.Min(Math.Max(MinVisibleWidth, width), containerWidth);
    }
}