using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Hierarchy
{
    public class ChildrenHeightConverter : IValueConverter
    {
        // Высота одного дочернего блока
        private const double ItemHeight = 60; // можно подогнать под фактическую визуальную

        // Коррекция, чтобы линия не вылезала ниже последнего элемента
        private const double Offset = 30;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int childCount && childCount > 0)
            {
                // Общая высота всех элементов минус небольшая корректировка
                double height = childCount * ItemHeight - Offset;
                return Math.Max(0, height);
            }

            return 0.0;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
