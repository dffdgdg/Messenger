using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MessengerDesktop.Converters.Base;
using System.Globalization;

namespace MessengerDesktop.Converters.Boolean;

/// <summary>
/// Конвертирует bool в кисть по имени ресурса.
/// ConverterParameter = "Accent|TextSecondary"
/// true → первый ресурс, false → второй
/// </summary>
public class BoolToBrushConverter : ConverterBase<bool, IBrush>
{
    public char Separator { get; set; } = '|';
    public IBrush? TrueBrush { get; set; }
    public IBrush? FalseBrush { get; set; }

    protected override IBrush? DefaultValue => Brushes.Transparent;

    protected override IBrush? ConvertCore(bool value, object? parameter, CultureInfo culture)
    {
        // Если задан параметр — ищем ресурсы по имени
        if (parameter is string param)
        {
            var parts = param.Split(Separator);
            if (parts.Length == 2)
            {
                var resourceKey = value ? parts[0] : parts[1];
                return ResolveResource(resourceKey);
            }
        }

        // Иначе используем свойства TrueBrush/FalseBrush
        return value ? TrueBrush : FalseBrush;
    }

    private static IBrush? ResolveResource(string key)
    {
        if (Application.Current?.TryFindResource(key, out var resource) == true)
        {
            return resource switch
            {
                IBrush brush => brush,
                Color color => new SolidColorBrush(color),
                _ => Brushes.Transparent
            };
        }

        return Brushes.Transparent;
    }
}