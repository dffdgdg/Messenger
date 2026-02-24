using MessengerDesktop.Converters.Base;
using MessengerShared.Dto.User;
using System.Globalization;

namespace MessengerDesktop.Converters.Domain;

public class DisplayOrUsernameConverter : ConverterBase<UserDto, string>
{
    protected override string? ConvertCore(UserDto user, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : user.Username;
}