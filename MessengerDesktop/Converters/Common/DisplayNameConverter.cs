using Avalonia.Data.Converters;
using MessengerShared.DTO;
using MessengerShared.Enum;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Common
{
    public class DisplayNameConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                UserDTO user => string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
                string name => name,
                _ => string.Empty
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}