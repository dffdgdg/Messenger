using System.Globalization;
using Avalonia.Layout;
using Core.Converters.Base;
using Core.Infrastructure;

namespace Core.Converters.Domain;

/// <summary>Боковая nav видна только в Normal/Wide.</summary>
public class LayoutModeToSideNavVisibleConverter : ConverterBase<LayoutMode, bool>
{
    protected override bool ConvertCore(LayoutMode value, object? parameter, CultureInfo culture) =>
        value is LayoutMode.Normal or LayoutMode.Wide;
}

/// <summary>Нижняя nav видна только в UltraCompact/Compact.</summary>
public class LayoutModeToBottomNavVisibleConverter : ConverterBase<LayoutMode, bool>
{
    protected override bool ConvertCore(LayoutMode value, object? parameter, CultureInfo culture) =>
        value is LayoutMode.UltraCompact or LayoutMode.Compact;
}

/// <summary>Ширина колонки боковой nav: 82 или 0.</summary>
public class LayoutModeToNavColumnWidthConverter : ConverterBase<LayoutMode, GridLength>
{
    protected override GridLength ConvertCore(LayoutMode value, object? parameter, CultureInfo culture) =>
        value is LayoutMode.Normal or LayoutMode.Wide
            ? new GridLength(82)
            : new GridLength(0);
}

/// <summary>Ширина колонки списка чатов.</summary>
public class LayoutModeToChatListWidthConverter : ConverterBase<LayoutMode, GridLength>
{
    protected override GridLength ConvertCore(LayoutMode value, object? parameter, CultureInfo culture) => value switch
    {
        LayoutMode.UltraCompact => new GridLength(0),
        LayoutMode.Compact => new GridLength(72),
        _ => new GridLength(280)
    };
}

/// <summary>MinWidth колонки списка чатов.</summary>
public class LayoutModeToChatListMinWidthConverter : ConverterBase<LayoutMode, double>
{
    protected override double ConvertCore(LayoutMode value, object? parameter, CultureInfo culture) => value switch
    {
        LayoutMode.UltraCompact => 0,
        LayoutMode.Compact => 72,
        _ => 96
    };
}

/// <summary>GridSplitter виден только в Normal/Wide.</summary>
public class LayoutModeToSplitterVisibleConverter : ConverterBase<LayoutMode, bool>
{
    protected override bool ConvertCore(LayoutMode value, object? parameter, CultureInfo culture) =>
        value is LayoutMode.Normal or LayoutMode.Wide;
}

/// <summary>InfoPanel всегда видна в Wide, иначе управляется вручную.</summary>
public class LayoutModeToInfoPanelAlwaysVisibleConverter : ConverterBase<LayoutMode, bool>
{
    protected override bool ConvertCore(LayoutMode value, object? parameter, CultureInfo culture) =>
        value is LayoutMode.Wide;
}

/// <summary>Кнопка "назад" в чате — только UltraCompact.</summary>
public class LayoutModeIsUltraCompactConverter : ConverterBase<LayoutMode, bool>
{
    protected override bool ConvertCore(LayoutMode value, object? parameter, CultureInfo culture) =>
        value is LayoutMode.UltraCompact;
}