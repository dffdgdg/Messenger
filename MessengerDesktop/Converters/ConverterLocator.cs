using Avalonia.Data.Converters;
using MessengerDesktop.Converters.Avatar;
using MessengerDesktop.Converters.Boolean;
using MessengerDesktop.Converters.Common;
using MessengerDesktop.Converters.DateTime;
using MessengerDesktop.Converters.Hierarchy;
using MessengerDesktop.Converters.Message;
using MessengerDesktop.Converters.UI;
using System;
using System.Collections.Generic;

namespace MessengerDesktop.Converters
{
    public sealed class ConverterLocator
    {
        private static readonly Lazy<ConverterLocator> _instance =
            new(() => new ConverterLocator());

        public static ConverterLocator Instance => _instance.Value;

        private readonly Dictionary<string, IValueConverter> _converters = [];
        private readonly Dictionary<string, IMultiValueConverter> _multiConverters = [];
        public ConverterLocator()
        {
            InitializeConverters();
        }

        private void InitializeConverters()
        {
            _converters[nameof(AvatarUrl)] = new AvatarUrlConverter();
            _converters[nameof(BooleanToRotateTransform)] = new Boolean.BooleanToRotateTransformConverter();
            _converters[nameof(UrlToImageBrush)] = new UrlToImageBrushConverter();
            _converters[nameof(LevelToVisibility)] = new LevelToVisibilityConverter();
            _converters[nameof(LevelToMargin)] = new LevelToMarginConverter();
            _converters[nameof(Equality)] = new EqualityConverter();
            _converters[nameof(StringNotNullOrEmpty)] = new StringNotNullOrEmptyConverter();
            _converters[nameof(BooleanNegation)] = new BooleanConverter { Invert = true };
            _converters[nameof(BooleanToVisibility)] = new BooleanConverter();
            _converters[nameof(BooleanToInvertedVisibility)] = new BooleanConverter { Invert = true };
            _converters[nameof(BooleanToText)] = new BooleanToTextConverter();
            _converters[nameof(ObjectNotNullToBoolean)] = new ObjectNotNullToBooleanConverter();
            _converters[nameof(IndexToText)] = new IndexToTextConverter();
            _converters[nameof(BoolToString)] = new BoolToStringConverter();
            _converters[nameof(ChildrenHeight)] = new ChildrenHeightConverter();
            _converters[nameof(StringToBoolean)] = new StringToBooleanConverter();
            _converters[nameof(StringToBrush)] = new StringToBrushConverter();
            _converters[nameof(MessageTime)] = new DateTimeFormatConverter { Format = "chat" };
            _converters[nameof(FullDateTime)] = new DateTimeFormatConverter { Format = "datetime", UseRelativeTime = false };
            _converters[nameof(RelativeTime)] = new DateTimeFormatConverter { Format = "relative" };
            _converters[nameof(TimeOnly)] = new DateTimeFormatConverter { Format = "time" };
            _converters[nameof(MessageToMargin)] = new MessageMarginConverter();
            _converters[nameof(MessageAlignment)] = new MessageAlignmentConverter();
            _converters[nameof(PollToPollViewModel)] = new PollToPollViewModelConverter();
            _converters[nameof(Visibility)] = new VisibilityConverter();
            _converters[nameof(InvertedVisibility)] = new VisibilityConverter { Invert = true };
            _converters[nameof(LastMessageDate)] = new LastMessageDateConverter();
            _converters[nameof(DisplayOrUsername)] = new DisplayOrUsernameConverter();
            _converters[nameof(NullToVisibility)] = new NullEmptyConverter
            {
                WhenNull = false,
                WhenEmpty = false,
                WhenHasValue = true
            };
            _converters[nameof(StringNotEmpty)] = new NullEmptyConverter
            {
                WhenNull = false,
                WhenEmpty = false,
                WhenHasValue = true
            };
        }

        public IValueConverter ChildrenHeight => GetConverter(nameof(ChildrenHeight));
        public IValueConverter AvatarUrl => GetConverter(nameof(AvatarUrl));
        public IValueConverter UrlToImageBrush => GetConverter(nameof(UrlToImageBrush));
        public IValueConverter BooleanToRotateTransform => GetConverter(nameof(BooleanToRotateTransform));
        public IValueConverter LevelToVisibility => GetConverter(nameof(LevelToVisibility));
        public IValueConverter LevelToMargin => GetConverter(nameof(LevelToMargin));
        public IValueConverter BoolToString => GetConverter(nameof(BoolToString));
        public IValueConverter Equality => GetConverter(nameof(Equality));
        public IValueConverter BooleanNegation => GetConverter(nameof(BooleanNegation));
        public IValueConverter StringNotNullOrEmpty => GetConverter(nameof(StringNotNullOrEmpty));
        public IValueConverter BooleanToVisibility => GetConverter(nameof(BooleanToVisibility));
        public IValueConverter BooleanToInvertedVisibility => GetConverter(nameof(BooleanToInvertedVisibility));
        public IValueConverter BooleanToText => GetConverter(nameof(BooleanToText));
        public IValueConverter ObjectNotNullToBoolean => GetConverter(nameof(ObjectNotNullToBoolean));
        public IValueConverter IndexToText => GetConverter(nameof(IndexToText));
        public IValueConverter StringToBoolean => GetConverter(nameof(StringToBoolean));
        public IValueConverter StringToBrush => GetConverter(nameof(StringToBrush));
        public IValueConverter MessageTime => GetConverter(nameof(MessageTime));
        public IValueConverter FullDateTime => GetConverter(nameof(FullDateTime));
        public IValueConverter RelativeTime => GetConverter(nameof(RelativeTime));
        public IValueConverter TimeOnly => GetConverter(nameof(TimeOnly));
        public IValueConverter MessageToMargin => GetConverter(nameof(MessageToMargin));
        public IValueConverter MessageAlignment => GetConverter(nameof(MessageAlignment));
        public IValueConverter PollToPollViewModel => GetConverter(nameof(PollToPollViewModel));
        public IValueConverter Visibility => GetConverter(nameof(Visibility));
        public IValueConverter InvertedVisibility => GetConverter(nameof(InvertedVisibility));
        public IValueConverter LastMessageDate => GetConverter(nameof(LastMessageDate));
        public IValueConverter DisplayOrUsername => GetConverter(nameof(DisplayOrUsername));
        public IValueConverter NullToVisibility => GetConverter(nameof(NullToVisibility));
        public IValueConverter StringNotEmpty => GetConverter(nameof(StringNotEmpty));

        private IValueConverter GetConverter(string name)
        {
            if (_converters.TryGetValue(name, out var converter))
                return converter;
            throw new InvalidOperationException($"Converter '{name}' not found. Available: {string.Join(", ", _converters.Keys)}");
        }
        private IMultiValueConverter GetMultiConverter(string name)
        {
            if (_multiConverters.TryGetValue(name, out var converter))
                return converter;
            throw new InvalidOperationException($"MultiConverter '{name}' not found.");
        }
        public IValueConverter GetConverterByName(string name) => GetConverter(name);
        public IMultiValueConverter GetMultiConverterByName(string name) => GetMultiConverter(name);
    }
}