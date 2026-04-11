using MessengerDesktop.Converters.Base;
using MessengerDesktop.ViewModels.Chat;
using System.Globalization;

namespace MessengerDesktop.Converters.Domain;

public sealed class ContentFilterToLabelConverter : ConverterBase
{
    protected override object? DefaultValue => string.Empty;

    protected override object? ConvertCore(object? value, object? parameter, CultureInfo culture)
    {
        return value is SearchContentFilter f ? f switch
        {
            SearchContentFilter.OnlyText => "Только текст",
            SearchContentFilter.WithFiles => "С файлами",
            SearchContentFilter.WithVoice => "Голосовые",
            SearchContentFilter.WithPolls => "Опросы",
            _ => string.Empty
        } : string.Empty;
    }
}