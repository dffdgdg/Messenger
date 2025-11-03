using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MessengerDesktop.Converters.Avatar
{
    public class AvatarUrlConverter : IValueConverter
    {
        public static string ApiBaseUrl { get; set; } = App.ApiUrl;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return "/Assets/Images/default-avatar.jpg";
            if (value == null)
                return "/Assets/Images/default-avatar.jpg";

            if (value is string avatar && !string.IsNullOrWhiteSpace(avatar))
            {
                if (avatar.StartsWith('/'))
                    return $"{ApiBaseUrl.TrimEnd('/')}{avatar}";

                if (avatar.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return avatar;
            }

            return "/Assets/Images/default-avatar.jpg";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}