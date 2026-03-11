using Avalonia.Data;

namespace MessengerDesktop.Views.Controls;

public partial class SearchBox : UserControl
{
    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<SearchBox, string>(nameof(SearchText), defaultValue: string.Empty, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<SearchBox, string>(nameof(Watermark), defaultValue: "Поиск...");

    public string SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public string Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public IRelayCommand ClearCommand { get; }

    public SearchBox()
    {
        ClearCommand = new RelayCommand(() => SearchText = string.Empty);
        InitializeComponent();
    }
}