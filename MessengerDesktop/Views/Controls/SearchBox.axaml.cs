using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using System;

namespace MessengerDesktop.Views.Controls;

public partial class SearchBox : UserControl
{
    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<SearchBox, string>(nameof(SearchText), defaultValue: string.Empty, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<SearchBox, string>(nameof(Watermark), defaultValue: "Поиск...");

    public static readonly RoutedEvent<RoutedEventArgs> SearchFocusedEvent =
        RoutedEvent.Register<SearchBox, RoutedEventArgs>(nameof(SearchFocused), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> SearchFocused
    {
        add => AddHandler(SearchFocusedEvent, value);
        remove => RemoveHandler(SearchFocusedEvent, value);
    }

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

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        SubscribeToFocus();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToFocus();
    }

    private bool _focusSubscribed;

    private void SubscribeToFocus()
    {
        if (_focusSubscribed) return;

        var input = this.FindControl<TextBox>("SearchInput");
        if (input == null) return;

        input.GotFocus += OnSearchInputGotFocus;
        _focusSubscribed = true;
    }

    private void OnSearchInputGotFocus(object? sender, Avalonia.Input.FocusChangedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(SearchFocusedEvent));
    }

    public void FocusInput() => this.FindControl<TextBox>("SearchInput")?.Focus();
}