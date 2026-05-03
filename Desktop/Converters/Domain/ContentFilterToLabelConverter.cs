using Desktop.Converters.Base;
using Desktop.ViewModels.Chat;
using System.Globalization;

namespace Desktop.Converters.Domain;

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