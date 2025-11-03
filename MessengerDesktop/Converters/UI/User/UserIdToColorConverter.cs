using System;
using System.Globalization;
using Avalonia.Media;
using MessengerDesktop.Converters.Base;
using MessengerDesktop.Converters.Constants;

namespace MessengerDesktop.Converters.User
{
    public class UserIdToColorConverter : ValueConverterBase<int, IBrush>
    {
        private static readonly IBrush[] UserColors = [
            new SolidColorBrush(0xFF2196F3),  
            new SolidColorBrush(0xFF4CAF50),  
            new SolidColorBrush(0xFFF44336),  
            new SolidColorBrush(0xFF9C27B0),  
            new SolidColorBrush(0xFFFF9800),
            new SolidColorBrush(0xFF795548),
            new SolidColorBrush(0xFF009688), 
            new SolidColorBrush(0xFF607D8B)  
        ];

        protected override IBrush ConvertValue(int userId, object? parameter, CultureInfo culture)
        {
            var index = Math.Abs(userId) % UserColors.Length;
            return UserColors[index];
        }

        protected override object? HandleConversionError(object? value) => ColorScheme.PrimaryTextBrush;
    }
}