using Avalonia.Data.Converters;
using MessengerDesktop.Converters.Boolean;
using MessengerDesktop.Converters.Comparison;
using MessengerDesktop.Converters.DateTime;
using MessengerDesktop.Converters.Domain;
using MessengerDesktop.Converters.Generic;
using MessengerDesktop.Converters.Hierarchy;
using MessengerDesktop.Converters.Message;
using System;
using System.Collections.Generic;

namespace MessengerDesktop.Converters;

public sealed class ConverterLocator
{
    private static readonly Lazy<ConverterLocator> LazyInstance = new(() => new ConverterLocator());
    public static ConverterLocator Instance => LazyInstance.Value;

    private readonly Dictionary<string, IValueConverter> _converters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IMultiValueConverter> _multiConverters = new(StringComparer.OrdinalIgnoreCase);

    public ConverterLocator() 
        => RegisterConverters();

    private void RegisterConverters()
    {
        Register<BoolToStringConverter>("BoolToString");
        Register<ZeroToTrueConverter>("ZeroToTrue");
        Register<BoolToRotationConverter>("BoolToRotation", "BooleanToRotateTransform");
        Register<BoolToRotationConverter>("Initials", "InitialsConverter");

        Register<EqualityConverter>("Equality", "Equals");
        Register<NotEqualityConverter>("NotEquals", "NotEquality");

        Register(new DateTimeFormatConverter { Format = DateTimeFormat.Chat }, "MessageTime", "ChatTime");
        Register(new DateTimeFormatConverter { Format = DateTimeFormat.DateTime }, "FullDateTime");
        Register(new DateTimeFormatConverter { Format = DateTimeFormat.Relative }, "RelativeTime");
        Register(new DateTimeFormatConverter { Format = DateTimeFormat.Time }, "TimeOnly");
        Register<LastMessageDateConverter>("LastMessageDate");

        Register<LevelToMarginConverter>("LevelToMargin");
        Register<LevelToVisibilityConverter>("LevelToVisibility");

        Register<MessageAlignmentConverter>("MessageAlignment");
        Register<MessageMarginConverter>("MessageToMargin", "MessageMargin");
        Register<HasContentConverter>("HasContent");

        Register<DisplayOrUsernameConverter>("DisplayOrUsername");
        Register<PollToPollViewModelConverter>("PollToPollViewModel");

        Register<IndexToTextConverter>("IndexToText");

        RegisterMulti<HasTextOrAttachmentsMultiConverter>("HasTextOrAttachments");
    }

    #region Registration helpers

    private void Register<T>(params string[] names) where T : IValueConverter, new()
    {
        var converter = new T();
        foreach (var name in names)
            _converters[name] = converter;
    }

    private void Register(IValueConverter converter, params string[] names)
    {
        foreach (var name in names)
            _converters[name] = converter;
    }

    private void RegisterMulti<T>(params string[] names) where T : IMultiValueConverter, new()
    {
        var converter = new T();
        foreach (var name in names)
            _multiConverters[name] = converter;
    }

    #endregion

    #region Public access

    public IValueConverter GetConverter(string name)
    {
        if (_converters.TryGetValue(name, out var converter))
            return converter;

        throw new InvalidOperationException($"Converter '{name}' not found. Available: {string.Join(", ", _converters.Keys)}");
    }

    public IMultiValueConverter GetMultiConverter(string name)
    {
        if (_multiConverters.TryGetValue(name, out var converter))
            return converter;

        throw new InvalidOperationException($"MultiConverter '{name}' not found.");
    }

    public IValueConverter InvertedVisibility => GetConverter("InvertedVisibility");
    public IValueConverter Equality => GetConverter("Equality");
    public IValueConverter MessageTime => GetConverter("MessageTime");
    public IValueConverter DisplayOrUsername => GetConverter("DisplayOrUsername");

    public IValueConverter GetConverterByName(string name) => GetConverter(name);
    public IMultiValueConverter GetMultiConverterByName(string name) => GetMultiConverter(name);

    #endregion
}