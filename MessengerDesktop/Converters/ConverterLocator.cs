using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using MessengerDesktop.Converters.Boolean;
using MessengerDesktop.Converters.Comparison;
using MessengerDesktop.Converters.DateTime;
using MessengerDesktop.Converters.Domain;
using MessengerDesktop.Converters.Enum;
using MessengerDesktop.Converters.Generic;
using MessengerDesktop.Converters.Hierarchy;
using MessengerDesktop.Converters.Message;
using System;
using System.Collections.Generic;

namespace MessengerDesktop.Converters;

public sealed class ConverterLocator
{
    public static ConverterLocator Instance { get; } = new();

    private readonly Dictionary<string, IValueConverter> _converters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IMultiValueConverter> _multiConverters = new(StringComparer.OrdinalIgnoreCase);

    public ConverterLocator() => RegisterAll();

    private void RegisterAll()
    {
        // Boolean converters
        Add(new BoolToStringConverter { TrueValue = "True", FalseValue = "False" }, "BoolToString");
        Add(new BoolToDoubleConverter { TrueValue = 90, FalseValue = 0 }, "BoolToRotation", "BooleanToRotateTransform");
        Add(new BoolToDoubleConverter { TrueValue = 1.0, FalseValue = 0.5 }, "BoolToOpacity");
        Add(new BoolToColorConverter { TrueValue = Color.Parse("#22C55E"), FalseValue = Color.Parse("#6B7280") }, "BoolToOnline");
        Add(new BoolToHAlignmentConverter { TrueValue = HorizontalAlignment.Right, FalseValue = HorizontalAlignment.Left }, "BoolToHAlignment");
        Add<BoolToBrushConverter>("BoolToBrush");

        // Comparison converters
        Add(new ComparisonConverter { Mode = ComparisonMode.Equal }, "Equality", "Equals");
        Add(new ComparisonConverter { Mode = ComparisonMode.NotEqual }, "NotEquals", "NotEquality");
        Add(new ComparisonConverter { Mode = ComparisonMode.GreaterThanZero }, "GreaterThanZero");
        Add(new ComparisonConverter { Mode = ComparisonMode.Zero }, "ZeroToTrue");

        // DateTime converters
        Add(new DateTimeFormatConverter { Format = DateTimeFormat.Chat }, "MessageTime", "ChatTime");
        Add(new DateTimeFormatConverter { Format = DateTimeFormat.DateTime }, "FullDateTime");
        Add(new DateTimeFormatConverter { Format = DateTimeFormat.Relative }, "RelativeTime");
        Add(new DateTimeFormatConverter { Format = DateTimeFormat.Time }, "TimeOnly");
        Add<LastMessageDateConverter>("LastMessageDate");

        // Domain converters
        Add<ThemeToDisplayConverter>("ThemeToDisplay");
        Add<DisplayOrUsernameConverter>("DisplayOrUsername");
        Add<PollToPollViewModelConverter>("PollToPollViewModel");
        Add<InitialsConverter>("Initials", "InitialsConverter");

        // Enum converters
        Add<UserRoleToVisibilityConverter>("UserRoleToVisibility", "HasRole");

        // Hierarchy converters
        Add<LevelToMarginConverter>("LevelToMargin");
        Add<LevelToVisibilityConverter>("LevelToVisibility");

        // Message converters
        Add<MessageAlignmentConverter>("MessageAlignment");
        Add<MessageMarginConverter>("MessageToMargin", "MessageMargin");
        Add<HasContentConverter>("HasContent");

        // Generic converters
        Add<IndexToTextConverter>("IndexToText");
        Add<PluralizeConverter>("Pluralize");

        // Multi converters
        AddMulti<BooleanAndConverter>("BooleanAnd");
        AddMulti<LastSeenTextConverter>("LastSeen");
        AddMulti<HasTextOrAttachmentsMultiConverter>("HasTextOrAttachments");
        AddMulti<PercentToWidthConverter>("PercentToWidth");
    }

    private void Add<T>(params string[] names) where T : IValueConverter, new() => Add(new T(), names);

    private void Add(IValueConverter converter, params string[] names)
    {
        foreach (var name in names)
            _converters[name] = converter;
    }

    private void AddMulti<T>(params string[] names) where T : IMultiValueConverter, new()
    {
        var converter = new T();
        foreach (var name in names)
            _multiConverters[name] = converter;
    }

    public IValueConverter Get(string name) => _converters.TryGetValue(name, out var converter)
        ? converter : throw new KeyNotFoundException($"Converter '{name}' not found. Available: {string.Join(", ", _converters.Keys)}");

    public IMultiValueConverter GetMulti(string name) => _multiConverters.TryGetValue(name, out var converter)
        ? converter : throw new KeyNotFoundException($"MultiConverter '{name}' not found. Available: {string.Join(", ", _multiConverters.Keys)}");
}