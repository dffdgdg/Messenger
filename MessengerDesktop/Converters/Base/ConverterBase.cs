using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Base;

/// <summary>
/// Типизированный базовый конвертер
/// </summary>
public abstract class ConverterBase<TIn, TOut> : IValueConverter
{
    protected virtual bool AllowNull => false;
    protected virtual TOut? DefaultValue => default;
    protected virtual bool SupportsConvertBack => false;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return AllowNull ? ConvertCore(default!, parameter, culture) : DefaultValue;

        if (value is not TIn typed)
            return DefaultValue;

        try
        {
            return ConvertCore(typed, parameter, culture);
        }
        catch
        {
            return DefaultValue;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!SupportsConvertBack)
            throw new NotSupportedException($"{GetType().Name} не поддерживает ConvertBack");

        if (value is not TOut typed)
            return default(TIn);

        try
        {
            return ConvertBackCore(typed, parameter, culture);
        }
        catch
        {
            return default(TIn);
        }
    }

    protected abstract TOut? ConvertCore(TIn value, object? parameter, CultureInfo culture);

    protected virtual TIn? ConvertBackCore(TOut value, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Нетипизированный базовый конвертер
/// </summary>
public abstract class ConverterBase : IValueConverter
{
    protected virtual object? DefaultValue => null;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            return ConvertCore(value, parameter, culture);
        }
        catch
        {
            return DefaultValue;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{GetType().Name} не поддерживает ConvertBack");

    protected abstract object? ConvertCore(object? value, object? parameter, CultureInfo culture);
}