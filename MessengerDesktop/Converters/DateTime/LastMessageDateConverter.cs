using MessengerDesktop.Converters.Base;
using MessengerShared.DTO.Message;
using System.Globalization;

namespace MessengerDesktop.Converters.DateTime;

public class LastMessageDateConverter : ConverterBase<MessageDTO, string>
{
    private static readonly DateTimeFormatConverter InnerConverter = new() { Format = DateTimeFormat.Chat };

    protected override string? DefaultValue => string.Empty;

    protected override string ConvertCore(MessageDTO message, object? parameter, CultureInfo culture)
    {
        if (message.CreatedAt == default)
            return string.Empty;

        return InnerConverter.Convert(message.CreatedAt, typeof(string), null, culture) as string ?? string.Empty;
    }
}