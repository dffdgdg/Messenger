using MessengerDesktop.Converters.Base;
using System.Globalization;

namespace MessengerDesktop.Converters.Domain
{
    internal class ChatRoleToDisplayConverter : ConverterBase<ChatRole, string>
    {
        protected override bool AllowNull => true;

        protected override string? ConvertCore(ChatRole value, object? parameter, CultureInfo culture) => value switch
        {
            ChatRole.Owner => "Владелец",
            ChatRole.Admin => "Администратор",
            ChatRole.Member => "Участник",
            _ => value.ToString()
        };
    }
}
