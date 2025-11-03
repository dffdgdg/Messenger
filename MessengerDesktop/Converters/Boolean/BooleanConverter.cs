using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Boolean
{
    public class BooleanConverter : IValueConverter
    {
        public object? TrueValue { get; set; }
        public object? FalseValue { get; set; }
        public bool Invert { get; set; }
        public bool UseHidden { get; set; } 
        public double TrueRotation { get; set; } = 90;
        public double FalseRotation { get; set; } = 0; 
        public string TextFormat { get; set; } = ""; 

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var boolValue = value switch
            {
                bool b => b,
                string s => !string.IsNullOrEmpty(s),
                int i => i != 0,
                double d => d != 0,
                null => false,
                _ => true
            };

            if (Invert)
                boolValue = !boolValue;

            return targetType.Name switch
            {
                nameof(Thickness) => ConvertToThickness(boolValue, parameter),
                nameof(HorizontalAlignment) => ConvertToAlignment(boolValue),
                "Double" when parameter?.ToString()?.Contains("rotation", StringComparison.OrdinalIgnoreCase) == true
                    => ConvertToRotation(boolValue),
                _ => ConvertGeneric(boolValue, targetType)
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (TrueValue != null && value?.Equals(TrueValue) == true)
                return !Invert;

            if (FalseValue != null && value?.Equals(FalseValue) == true)
                return Invert;

            return false;
        }

        private object? ConvertGeneric(bool boolValue, Type targetType)
        {
            if (TrueValue != null || FalseValue != null)
                return boolValue ? TrueValue : FalseValue;

            if (targetType == typeof(string) && !string.IsNullOrEmpty(TextFormat))
            {
                var parts = TextFormat.Split('|');
                if (parts.Length == 2)
                    return boolValue ? parts[0] : parts[1];
            }

            if (targetType == typeof(bool) || targetType == typeof(bool?))
                return boolValue;

            return boolValue;
        }

        private static Thickness ConvertToThickness(bool boolValue, object? parameter)
        {
            if (parameter?.ToString()?.Contains("message", StringComparison.OrdinalIgnoreCase) == true)
                return boolValue ? new Thickness(0, 8, 0, 0) : new Thickness(32, 8, 0, 0);

            if (parameter is int level)
                return new Thickness(level * 20, 0, 0, 0);

            return new Thickness(0);
        }

        private static object ConvertToAlignment(bool boolValue) => boolValue ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        private double ConvertToRotation(bool boolValue)
        {
            return boolValue ? TrueRotation : FalseRotation;
        }
    }
}