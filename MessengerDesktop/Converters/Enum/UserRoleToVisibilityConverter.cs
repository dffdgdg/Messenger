using MessengerDesktop.Converters.Base;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace MessengerDesktop.Converters.Enum;

public class UserRoleToVisibilityConverter : ConverterBase
{
    protected override object? DefaultValue => false;

    protected override object? ConvertCore(object? value, object? parameter, CultureInfo culture)
    {
        if (parameter is not string roleName || App.Current?.Services == null)
            return false;

        if (!System.Enum.TryParse<UserRole>(roleName, out var requiredRole))
            return false;

        var authManager = App.Current.Services.GetRequiredService<IAuthManager>();
        return authManager.Session.HasRole(requiredRole);
    }
}