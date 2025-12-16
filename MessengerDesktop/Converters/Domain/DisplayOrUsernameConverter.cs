using Avalonia.Data.Converters;
using MessengerShared.DTO;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Domain
{
    public class DisplayOrUsernameConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is UserDTO user)
            {
                return !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : user.Username;
            }
            return null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}