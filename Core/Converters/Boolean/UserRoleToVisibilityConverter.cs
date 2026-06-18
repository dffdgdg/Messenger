using Core.Converters.Base;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace Core.Converters.Boolean;

public class UserRoleToVisibilityConverter : ConverterBase
{
    protected override object? DefaultValue => false;

    protected override object? ConvertCore(object? value, object? parameter, CultureInfo culture)
    {
        if (parameter is not string roleName || AppConfig.Services == null)
            return false;

        if (!System.Enum.TryParse<UserRole>(roleName, out var requiredRole))
            return false;

        var authManager = AppConfig.Services.GetRequiredService<IAuthManager>();
        return authManager.Session.HasRole(requiredRole);
    }
}