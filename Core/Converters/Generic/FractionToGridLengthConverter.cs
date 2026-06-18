using Avalonia.Controls;
using Core.Converters.Base;
using System;
using System.Globalization;

namespace Core.Converters.Generic;

public class FractionToGridLengthConverter : ConverterBase<double, GridLength>
{
    // Безопасное значение по умолчанию, если пришел null или не double
    protected override GridLength DefaultValue => new(0, GridUnitType.Pixel);

    protected override GridLength ConvertCore(double value, object? parameter, CultureInfo culture)
    {
        // Ограничиваем от 0.0 до 1.0 (на случай, если с бэкенда придет 1.2 или -0.1)
        var fraction = Math.Clamp(value, 0.0, 1.0);

        // Если передан параметр Inverse — возвращаем оставшуюся пустую часть
        if (parameter is string paramStr && paramStr.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
        {
            return new GridLength(1.0 - fraction, GridUnitType.Star);
        }

        // Возвращаем заполненную часть
        return new GridLength(fraction, GridUnitType.Star);
    }
}