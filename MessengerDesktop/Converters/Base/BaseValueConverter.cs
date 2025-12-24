using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace MessengerDesktop.Converters.Base;

/// <summary>
/// Нетипизированный базовый конвертер
/// </summary>
public abstract class ValueConverterBase : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            return ConvertCore(value, targetType, parameter, culture);
        }
        catch (Exception ex)
        {
            return HandleError(ex, value);
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!SupportsConvertBack)
            throw new NotSupportedException($"{GetType().Name} does not support ConvertBack");

        try
        {
            return ConvertBackCore(value, targetType, parameter, culture);
        }
        catch (Exception ex)
        {
            return HandleError(ex, value);
        }
    }

    protected abstract object? ConvertCore(object? value, Type targetType, object? parameter, CultureInfo culture);

    protected virtual object? ConvertBackCore(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    protected virtual bool SupportsConvertBack => false;

    protected virtual object? HandleError(Exception ex, object? value) => null;
}

/// <summary>
/// Типизированный базовый конвертер
/// </summary>
public abstract class ValueConverterBase<TIn, TOut> : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TIn typedValue)
        {
            try
            {
                return ConvertCore(typedValue, parameter, culture);
            }
            catch (Exception ex)
            {
                return HandleError(ex, value);
            }
        }

        return HandleInvalidInput(value);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!SupportsConvertBack)
            throw new NotSupportedException($"{GetType().Name} does not support ConvertBack");

        if (value is TOut typedValue)
        {
            try
            {
                return ConvertBackCore(typedValue, parameter, culture);
            }
            catch (Exception ex)
            {
                return HandleError(ex, value);
            }
        }

        return HandleInvalidInput(value);
    }

    protected abstract TOut? ConvertCore(TIn value, object? parameter, CultureInfo culture);

    protected virtual TIn? ConvertBackCore(TOut value, object? parameter, CultureInfo culture) => throw new NotSupportedException();

    protected virtual bool SupportsConvertBack => false;
    protected virtual object? HandleError(Exception ex, object? value) => default(TOut);
    protected virtual object? HandleInvalidInput(object? value) => default(TOut);
}