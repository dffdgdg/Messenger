using Avalonia.Data.Converters;
using MessengerDesktop.Services.Auth;
using MessengerShared.Enum;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Enum
{
    public class UserRoleToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (parameter is not string roleName || App.Current?.Services == null)
                return false;

            try
            {
                if (!System.Enum.TryParse<UserRole>(roleName, out var requiredRole))
                    return false;

                var authManager = App.Current.Services.GetRequiredService<IAuthManager>();
                return authManager.Session.HasRole(requiredRole); 
            }
            catch
            {
                return false;
            }
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}