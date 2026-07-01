using Core.Shared.Converters.Base;
using System.Globalization;

namespace Core.Shared.Converters.Domain;

public sealed class ChatRoleToDisplayConverter : ConverterBase<ChatRole, string>
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