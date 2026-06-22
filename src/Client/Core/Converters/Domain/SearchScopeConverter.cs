using Core.Converters.Base;
using Core.ViewModels.Chat;
using System.Globalization;

namespace Core.Converters.Domain;

public sealed class SearchScopeToTitleConverter : ConverterBase<SearchScopeMode, string>
{
    protected override string ConvertCore(SearchScopeMode value, object? parameter, CultureInfo culture) => value switch
    {
        SearchScopeMode.Chats => "Поиск по чатам",
        SearchScopeMode.Contacts => "Поиск по контактам",
        SearchScopeMode.CurrentChatMessages => "Поиск по чату",
        _ => "Поиск"
    };
}

public sealed class SearchScopeToWatermarkConverter : ConverterBase<SearchScopeMode, string>
{
    protected override string ConvertCore(SearchScopeMode value, object? parameter, CultureInfo culture) => value switch
    {
        SearchScopeMode.Chats => "Поиск в чатах...",
        SearchScopeMode.Contacts => "Поиск в контактах...",
        SearchScopeMode.CurrentChatMessages => "Поиск по сообщениям открытого чата...",
        _ => "Поиск..."
    };
}

public sealed class SearchScopeToHintConverter : ConverterBase<SearchScopeMode, string>
{
    protected override string ConvertCore(SearchScopeMode value, object? parameter, CultureInfo culture)
        => value switch
        {
            SearchScopeMode.Chats => "Поиск по названиям групповых чатов и сообщениям",
            SearchScopeMode.Contacts => "Поиск по контактам и их сообщениям",
            SearchScopeMode.CurrentChatMessages => "Поиск только по сообщениям открытого чата",
            _ => "Введите поисковый запрос"
        };
}

public sealed class SearchScopeToMessagesHeaderConverter : ConverterBase<SearchScopeMode, string>
{
    protected override string ConvertCore(SearchScopeMode value, object? parameter, CultureInfo culture)
        => value == SearchScopeMode.Contacts ? "Сообщения контактов" : "Сообщения";
}
