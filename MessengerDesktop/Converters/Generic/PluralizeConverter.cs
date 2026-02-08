using MessengerDesktop.Converters.Base;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Generic;

/// <summary>
/// Склонение существительных по числу (русский язык).
/// ConverterParameter = "голос|голоса|голосов"
/// </summary>
public class PluralizeConverter : ConverterBase<int, string>
{
    public char Separator { get; set; } = '|';

    protected override string? DefaultValue => string.Empty;

    protected override string ConvertCore(int count, object? parameter, CultureInfo culture)
    {
        if (parameter is not string forms)
            return count.ToString();

        var parts = forms.Split(Separator);
        if (parts.Length != 3)
            return count.ToString();

        return Pluralize(Math.Abs(count), parts[0], parts[1], parts[2]);
    }

    private static string Pluralize(int n, string one, string few, string many)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;

        if (mod10 == 1 && mod100 != 11)
            return one;

        if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20))
            return few;

        return many;
    }
}