using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Core.ViewModels.Chat;

namespace Desktop.Views.Controls;

public partial class FilterAutocomplete : UserControl
{
    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<FilterAutocomplete, string>(nameof(SearchText), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<FilterAutocomplete, string>(nameof(Placeholder));

    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<FilterAutocomplete, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<SearchFilterItem?> SelectedItemProperty =
        AvaloniaProperty.Register<FilterAutocomplete, SearchFilterItem?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsDropdownOpenProperty =
        AvaloniaProperty.Register<FilterAutocomplete, bool>(nameof(IsDropdownOpen), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IEnumerable<SearchFilterItem>> SuggestionsProperty =
        AvaloniaProperty.Register<FilterAutocomplete, IEnumerable<SearchFilterItem>>(nameof(Suggestions));

    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<FilterAutocomplete, bool>(nameof(IsLoading));

    public string SearchText { get => GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }
    public string Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public SearchFilterItem? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public bool IsDropdownOpen { get => GetValue(IsDropdownOpenProperty); set => SetValue(IsDropdownOpenProperty, value); }
    public IEnumerable<SearchFilterItem> Suggestions { get => GetValue(SuggestionsProperty); set => SetValue(SuggestionsProperty, value); }
    public bool IsLoading { get => GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }

    public IRelayCommand ClearCommand { get; }
    public IRelayCommand<SearchFilterItem> SelectItemCommand { get; }

    private bool _focusPending;
    private Border? _dropdownBorder;
    private bool _isAttachedToOverlay;
    private IDisposable? _boundsSubscription;
    private TextBox? _inputBox;

    public FilterAutocomplete()
    {
        ClearCommand = new RelayCommand(() =>
        {
            SelectedItem = null;
            SearchText = string.Empty;
            CloseDropdown();
        });

        SelectItemCommand = new RelayCommand<SearchFilterItem>(item =>
        {
            if (item == null) return;

            var displayName = !string.IsNullOrWhiteSpace(item.DisplayName)
                ? item.DisplayName
                : $"#{item.Id}";

            SelectedItem = new SearchFilterItem(item.Id, displayName, item.Avatar);
            SearchText = displayName;
            CloseDropdown();
            _focusPending = false;
        });

        InitializeComponent();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        PropertyChanged += OnPropertyChangedHandler;

        _inputBox = this.FindControl<TextBox>("InputTextBox");
        _inputBox?.AddHandler(PointerPressedEvent, OnInputBoxPressed, RoutingStrategies.Tunnel);
    }

    private void OnInputBoxPressed(object? sender, PointerPressedEventArgs e)
    {
        // При клике на поле ввода (даже если оно уже в фокусе) — показываем дропдаун
        if (IsDropdownOpen) return; // Уже открыт

        _focusPending = true;

        if (Suggestions?.Any() == true)
        {
            OpenDropdown();
            _focusPending = false;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BuildDropdownBorder();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        PropertyChanged -= OnPropertyChangedHandler;
        _boundsSubscription?.Dispose();
        CloseDropdown();
        _dropdownBorder = null;
    }

    private void BuildDropdownBorder()
    {
        this.TryGetResource("ControlBg", null, out var bgObj);
        this.TryGetResource("ControlBorder", null, out var borderObj);
        this.TryGetResource("DropdownShadow", null, out var shadowObj);

        var bgBrush = (bgObj as IBrush) ?? Brushes.White;
        var borderBrush = (borderObj as IBrush) ?? new SolidColorBrush(Color.Parse("#CED6E3"));

        var itemsControl = new ItemsControl
        {
            ItemTemplate = new FuncDataTemplate<SearchFilterItem>((item, _) =>
            {
                if (item == null) return new Control();

                var avatar = new AvatarControl
                {
                    Size = 32,
                    ShowOnlineIndicator = false,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                avatar.Bind(AvatarControl.SourceProperty, new Binding(nameof(SearchFilterItem.Avatar)));
                avatar.Bind(AvatarControl.DisplayNameProperty, new Binding(nameof(SearchFilterItem.DisplayName)));

                this.TryGetResource("TextPrimary", null, out var textPrimaryObj);
                var textBrush = (textPrimaryObj as IBrush) ?? Brushes.Black;

                var text = new TextBlock
                {
                    FontSize = 14,
                    FontWeight = FontWeight.Medium,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = textBrush
                };
                text.Bind(TextBlock.TextProperty, new Binding(nameof(SearchFilterItem.DisplayName)));

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                Grid.SetColumn(avatar, 0);
                Grid.SetColumn(text, 1);
                grid.Children.Add(avatar);
                grid.Children.Add(text);

                var button = new Button
                {
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Content = grid,
                    Command = SelectItemCommand,
                    CornerRadius = new CornerRadius(6),
                    Background = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Focusable = false
                };
                button.Classes.Add("MemberItem");
                button.Bind(Button.CommandParameterProperty, new Binding("."));

                return button;
            })
        };

        itemsControl.Bind(ItemsControl.ItemsSourceProperty,
            this.GetObservable(SuggestionsProperty));

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 240,
            Content = itemsControl
        };

        _dropdownBorder = new Border
        {
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = scrollViewer,
            Background = bgBrush,
            BorderBrush = borderBrush
        };

        if (shadowObj is BoxShadows boxShadows)
            _dropdownBorder.BoxShadow = boxShadows;
    }

    private void OpenDropdown()
    {
        if (_dropdownBorder == null || _isAttachedToOverlay) return;

        var overlayLayer = OverlayLayer.GetOverlayLayer(this);
        if (overlayLayer == null) return;

        var inputBox = _inputBox ?? this.FindControl<TextBox>("InputTextBox");
        if (inputBox == null) return;

        UpdateDropdownGeometry(overlayLayer, inputBox);

        overlayLayer.Children.Add(_dropdownBorder);
        _isAttachedToOverlay = true;
        IsDropdownOpen = true;

        if (TopLevel.GetTopLevel(this) is InputElement topLevel)
        {
            topLevel.AddHandler(PointerPressedEvent, OnOverlayPointerPressed, RoutingStrategies.Tunnel);
        }

        _boundsSubscription?.Dispose();
        _boundsSubscription = inputBox.GetObservable(BoundsProperty)
            .Subscribe(new AnonymousObserver<Rect>(_ =>
            {
                if (_isAttachedToOverlay && _dropdownBorder != null)
                    UpdateDropdownGeometry(overlayLayer, inputBox);
            }));
    }

    private void OnOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_dropdownBorder == null || !_isAttachedToOverlay) return;

        if (e.Source is Visual hit && (_dropdownBorder.IsVisualAncestorOf(hit) || this.IsVisualAncestorOf(hit)))
            return;

        CloseDropdown();
    }

    private void CloseDropdown()
    {
        if (!_isAttachedToOverlay || _dropdownBorder == null) return;

        var overlayLayer = OverlayLayer.GetOverlayLayer(this);
        overlayLayer?.Children.Remove(_dropdownBorder);
        _isAttachedToOverlay = false;
        IsDropdownOpen = false;
        _focusPending = false; // Сбрасываем фокус при закрытии
        _boundsSubscription?.Dispose();

        if (TopLevel.GetTopLevel(this) is InputElement topLevel)
        {
            topLevel.RemoveHandler(PointerPressedEvent, OnOverlayPointerPressed);
        }
    }

    private void UpdateDropdownGeometry(OverlayLayer overlayLayer, TextBox inputBox)
    {
        if (_dropdownBorder == null) return;

        var transform = inputBox.TransformToVisual(overlayLayer);
        if (transform == null) return;

        var topLeft = transform.Value.Transform(new Point(0, inputBox.Bounds.Height + 4));

        Canvas.SetLeft(_dropdownBorder, topLeft.X);
        Canvas.SetTop(_dropdownBorder, topLeft.Y);
        _dropdownBorder.Width = inputBox.Bounds.Width;
    }

    private void OnPropertyChangedHandler(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SuggestionsProperty)
        {
            if (Suggestions?.Any() == true && _focusPending)
            {
                OpenDropdown();
                _focusPending = false;
            }
        }

        if (e.Property == IsDropdownOpenProperty)
        {
            if (IsDropdownOpen && !_isAttachedToOverlay)
                OpenDropdown();
            else if (!IsDropdownOpen && _isAttachedToOverlay)
                CloseDropdown();
        }
    }

    private void OnInputFocused(object? sender, RoutedEventArgs e)
    {
        _focusPending = true;

        if (Suggestions?.Any() == true)
        {
            OpenDropdown();
            _focusPending = false;
        }
    }

    private void OnInputLostFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_dropdownBorder == null || !_isAttachedToOverlay) return;

            if (!this.IsKeyboardFocusWithin && !_dropdownBorder.IsKeyboardFocusWithin)
                CloseDropdown();
        }, DispatcherPriority.Background);
    }

    public void SetLoadingState(bool isLoading)
    {
        IsLoading = isLoading;

        if (!isLoading && _focusPending && Suggestions?.Any() == true)
        {
            OpenDropdown();
            _focusPending = false;
        }
    }

    public void OnSuggestionsReady()
    {
        if (_focusPending && Suggestions?.Any() == true)
        {
            OpenDropdown();
            _focusPending = false;
        }
    }
}