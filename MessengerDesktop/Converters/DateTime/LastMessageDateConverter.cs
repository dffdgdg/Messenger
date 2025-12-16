using MessengerDesktop.Converters.Base;
using MessengerShared.DTO;
using System.Globalization;

namespace MessengerDesktop.Converters.DateTime;

public class LastMessageDateConverter : ValueConverterBase<MessageDTO, string>
{
    private static readonly DateTimeFormatConverter InnerConverter = new()
    {
        Format = DateTimeFormat.Chat
    };

    protected override string ConvertCore(MessageDTO message, object? parameter, CultureInfo culture)
    {
        if (message.CreatedAt == default)
            return string.Empty;

        return InnerConverter.Convert(message.CreatedAt, typeof(string), null, culture) as string
               ?? string.Empty;
    }

    protected override object? HandleError(System.Exception ex, object? value) => string.Empty;

    protected override object? HandleInvalidInput(object? value) => string.Empty;
}