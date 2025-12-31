using MessengerDesktop.Converters.Base;
using MessengerShared.DTO.User;
using System.Globalization;

namespace MessengerDesktop.Converters.Domain;

public class DisplayOrUsernameConverter : ConverterBase<UserDTO, string>
{
    protected override string? ConvertCore(UserDTO user, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : user.Username;
}