using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Desktop.ViewModels.Chat;
using System.Collections.Generic;
using System.Linq;

namespace Desktop.Views.Controls;

public partial class FilterAutocomplete : UserControl
{
    public static readonly StyledProperty<string> SearchTextProperty = AvaloniaProperty.Register<FilterAutocomplete, string>(nameof(SearchText), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> PlaceholderProperty = AvaloniaProperty.Register<FilterAutocomplete, string>(nameof(Placeholder));

    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<FilterAutocomplete, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<SearchFilterItem?> SelectedItemProperty = AvaloniaProperty.Register<FilterAutocomplete, SearchFilterItem?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsDropdownOpenProperty = AvaloniaProperty.Register<FilterAutocomplete, bool>(nameof(IsDropdownOpen), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IEnumerable<SearchFilterItem>> SuggestionsProperty = AvaloniaProperty.Register<FilterAutocomplete, IEnumerable<SearchFilterItem>>(nameof(Suggestions));

    public string SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public SearchFilterItem? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public bool IsDropdownOpen
    {
        get => GetValue(IsDropdownOpenProperty);
        set => SetValue(IsDropdownOpenProperty, value);
    }

    public IEnumerable<SearchFilterItem> Suggestions
    {
        get => GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    public IRelayCommand ClearCommand { get; }
    public IRelayCommand<SearchFilterItem> SelectItemCommand { get; }

    private Popup? _popup;

    public FilterAutocomplete()
    {
        ClearCommand = new RelayCommand(() =>
        {
            SelectedItem = null;
            SearchText = string.Empty;
            IsDropdownOpen = false;
        });

        SelectItemCommand = new RelayCommand<SearchFilterItem>(item =>
        {
            if (item == null) return;
            SelectedItem = item;
            SearchText = item.DisplayName;
            IsDropdownOpen = false;
        });

        InitializeComponent();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Dispatcher.UIThread.Post(() => _popup = this.FindDescendantOfType<Popup>());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is InputElement inputElement)
        {
            inputElement.AddHandler(PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is InputElement inputElement)
        {
            inputElement.RemoveHandler(PointerPressedEvent, OnGlobalPointerPressed);
        }
    }

    private bool IsPointerOverPopup()
    {
        if (_popup?.Child is InputElement popupChild)
            return popupChild.IsPointerOver;

        return false;
    }

    private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsPointerOver)
            return;

        if (IsPointerOverPopup())
            return;

        IsDropdownOpen = false;
    }

    private void OnInputFocused(object? sender, FocusChangedEventArgs e)
    {
        if (Suggestions?.Any() == true)
            IsDropdownOpen = true;
    }

    private void OnInputLostFocus(object? sender, FocusChangedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (IsPointerOver)
            return;

        if (IsPointerOverPopup())
            return;

        IsDropdownOpen = false;
    }, DispatcherPriority.Background);
}