using Avalonia;
using Avalonia.Input;
using Avalonia.Reactive;
using Avalonia.VisualTree;
using Core.Infrastructure;
using Core.ViewModels;
using Desktop.Views.Controls;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;

namespace Desktop.Views;

public partial class ChatsView : UserControl
{
    // ── Константы ─────────────────────────────────────────────────────────────

    private const double COMPACT_WIDTH = 96;
    private const double NORMAL_DEFAULT_WIDTH = 280;
    private const double MIN_WIDTH = COMPACT_WIDTH;
    private const double MAX_WIDTH = 400;

    // Пороги для ручного сплиттера (drag)
    private const double ENTER_COMPACT_THRESHOLD = 120;
    private const double EXIT_COMPACT_THRESHOLD = 160;

    // ── Состояние ─────────────────────────────────────────────────────────────

    private bool _isDragging;
    private IDisposable? _layoutModeSubscription;
    private IDisposable? _windowBoundsSubscription;
    private ChatsViewModel? _previousVm;

    // Запоминаем ширину которую пользователь выставил вручную
    private double _userDefinedWidth = NORMAL_DEFAULT_WIDTH;

    private readonly IChatInfoPanelStateStore _chatInfoPanelStateStore;

    // ── IsCompactMode ─────────────────────────────────────────────────────────

    public static readonly StyledProperty<bool> IsCompactModeProperty =
        AvaloniaProperty.Register<ChatsView, bool>(nameof(IsCompactMode));

    public bool IsCompactMode
    {
        get => GetValue(IsCompactModeProperty);
        set => SetValue(IsCompactModeProperty, value);
    }

    // ── LayoutMode ────────────────────────────────────────────────────────────

    public static readonly DirectProperty<ChatsView, LayoutMode> LayoutModeProperty =
        AvaloniaProperty.RegisterDirect<ChatsView, LayoutMode>(
            nameof(LayoutMode), o => o.LayoutMode);

    private LayoutMode _layoutMode;
    public LayoutMode LayoutMode
    {
        get => _layoutMode;
        private set
        {
            if (_layoutMode == value) return;
            var previous = _layoutMode;
            SetAndRaise(LayoutModeProperty, ref _layoutMode, value);
            OnLayoutModeChanged(previous, value);
        }
    }

    // ── Конструктор ───────────────────────────────────────────────────────────

    public ChatsView()
    {
        InitializeComponent();

        _chatInfoPanelStateStore = App.Current.Services
            .GetRequiredService<IChatInfoPanelStateStore>();

        DataContextChanged += OnDataContextChanged;

        var mainGrid = this.FindControl<Grid>("MainGrid");
        if (mainGrid != null)
        {
            var column = mainGrid.ColumnDefinitions[0];
            var splitter = this.FindControl<GridSplitter>("GridSplitter");

            column?.PropertyChanged += ChatListColumn_PropertyChanged;

            if (splitter != null)
            {
                splitter.DragStarted += Splitter_DragStarted;
                splitter.DragCompleted += Splitter_DragCompleted;
            }
        }
    }

    // ── Visual Tree ───────────────────────────────────────────────────────────

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var window = this.FindAncestorOfType<Window>();
        if (window is MainWindow mainWindow)
        {
            LayoutMode = mainWindow.LayoutMode;

            _layoutModeSubscription = mainWindow
                .GetObservable(MainWindow.LayoutModeProperty)
                .Subscribe(new AnonymousObserver<LayoutMode>(m => LayoutMode = m));

            // Подписка на размер окна — для перерасчёта в Normal/Wide
            _windowBoundsSubscription = mainWindow
                .GetObservable(Window.BoundsProperty)
                .Subscribe(new AnonymousObserver<Rect>(_ =>
                {
                    if (LayoutMode is LayoutMode.Normal or LayoutMode.Wide)
                        OnLayoutModeChanged(LayoutMode, LayoutMode);
                }));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _layoutModeSubscription?.Dispose();
        _layoutModeSubscription = null;
        _windowBoundsSubscription?.Dispose();
        _windowBoundsSubscription = null;
    }

    // ── LayoutMode changed ────────────────────────────────────────────────────

    private void OnLayoutModeChanged(LayoutMode previous, LayoutMode current)
    {
        var mainGrid = this.FindControl<Grid>("MainGrid");
        if (mainGrid == null) return;

        var col0 = mainGrid.ColumnDefinitions[0]; // список чатов
        var col1 = mainGrid.ColumnDefinitions[1]; // сплиттер
        var col2 = mainGrid.ColumnDefinitions[2]; // область чата
        var col3 = mainGrid.ColumnDefinitions[3]; // инфопанель

        switch (current)
        {
            case LayoutMode.UltraCompact:
                var hasChatOpen = DataContext is ChatsViewModel { CurrentChatViewModel: not null };

                // Список чатов: на весь экран когда чат не открыт, скрыт когда открыт
                col0.MinWidth = 0;
                col0.MaxWidth = double.PositiveInfinity;
                col0.Width = hasChatOpen
                    ? new GridLength(0)
                    : new GridLength(1, GridUnitType.Star);

                // Область чата: видна только когда чат открыт
                col2.Width = hasChatOpen
                    ? new GridLength(1, GridUnitType.Star)
                    : new GridLength(0);
                col2.MinWidth = 0;

                // Сплиттер и инфопанель скрыты
                col1.Width = new GridLength(0);
                col3.Width = new GridLength(0);

                IsCompactMode = false;
                break;

            case LayoutMode.Compact:
                // Список чатов: всегда виден, 72px
                col0.MinWidth = COMPACT_WIDTH;
                col0.MaxWidth = COMPACT_WIDTH;
                col0.Width = new GridLength(COMPACT_WIDTH);

                // Область чата: всегда видна
                col2.Width = new GridLength(1, GridUnitType.Star);
                col2.MinWidth = 0;

                // Сплиттер скрыт, инфопанель скрыта
                col1.Width = new GridLength(0);
                col3.Width = new GridLength(0);

                IsCompactMode = true;
                break;

            case LayoutMode.Normal:
            case LayoutMode.Wide:
                col0.MinWidth = MIN_WIDTH;
                col0.MaxWidth = MAX_WIDTH;

                var targetWidth = _userDefinedWidth;
                if (targetWidth < EXIT_COMPACT_THRESHOLD)
                    targetWidth = NORMAL_DEFAULT_WIDTH;

                var windowWidth = this.FindAncestorOfType<Window>()?.Bounds.Width ?? 0;
                var infoPanelOpen = _chatInfoPanelStateStore.IsOpen;
                var hasOpenChat = DataContext is ChatsViewModel { CurrentChatViewModel: not null };

                if (infoPanelOpen && hasOpenChat && windowWidth > 0)
                {
                    // Инфопанель 320px, чат минимум 380px, список: сначала сжимаем до 96px
                    var minChatWidth = 380.0;
                    var compactListWidth = COMPACT_WIDTH;

                    // Если инфопанель + чат + текущий список не влезают — сжимаем список
                    if (windowWidth < 320 + minChatWidth + targetWidth)
                        targetWidth = compactListWidth;

                    // Если даже с компактным списком не влезает — сжимаем чат
                    var remainingForChat = windowWidth - 320 - targetWidth;
                    if (remainingForChat < minChatWidth)
                        col2.MinWidth = Math.Max(320, remainingForChat); // чат не меньше 320px
                    else
                        col2.MinWidth = minChatWidth;
                }
                else
                {
                    col2.MinWidth = 0;
                }

                col0.Width = new GridLength(targetWidth);
                col2.Width = new GridLength(1, GridUnitType.Star);
                col1.Width = new GridLength(4);
                col3.Width = GridLength.Auto;

                IsCompactMode = targetWidth <= ENTER_COMPACT_THRESHOLD;
                break;
        }

        if (DataContext is ChatsViewModel vm)
            UpdateInfoPanelVisibility(vm);
    }

    // ── DataContext ───────────────────────────────────────────────────────────

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_previousVm != null)
            _previousVm.PropertyChanged -= OnVmPropertyChanged;

        _previousVm = DataContext as ChatsViewModel;

        if (_previousVm != null)
        {
            _previousVm.PropertyChanged += OnVmPropertyChanged;
            UpdateInfoPanelVisibility(_previousVm);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not ChatsViewModel vm) return;

        if (e.PropertyName is nameof(ChatsViewModel.CurrentChatViewModel)
                           or nameof(ChatsViewModel.CombinedIsInfoPanelVisible))
        {
            UpdateInfoPanelVisibility(vm);

            if (LayoutMode == LayoutMode.UltraCompact)
            {
                var mainGrid = this.FindControl<Grid>("MainGrid");
                if (mainGrid == null) return;

                var hasChatOpen = vm.CurrentChatViewModel != null;

                mainGrid.ColumnDefinitions[0].Width = hasChatOpen
                    ? new GridLength(0)
                    : new GridLength(1, GridUnitType.Star);

                mainGrid.ColumnDefinitions[2].Width = hasChatOpen
                    ? new GridLength(1, GridUnitType.Star)
                    : new GridLength(0);
            }
            else if (LayoutMode is LayoutMode.Normal or LayoutMode.Wide)
            {
                // Пересчитываем — возможно нужно сжать список под инфопанель
                OnLayoutModeChanged(LayoutMode, LayoutMode);
            }
        }
    }

    // ── Info Panel ────────────────────────────────────────────────────────────

    private void UpdateInfoPanelVisibility(ChatsViewModel vm)
    {
        var panel = this.FindControl<Control>("InfoPanel");
        if (panel == null) return;

        var isAnyChatSelected = vm.CurrentChatViewModel != null;
        var globalOpen = _chatInfoPanelStateStore.IsOpen;
        var hideForWidth = LayoutMode is LayoutMode.UltraCompact or LayoutMode.Compact;

        panel.IsVisible = isAnyChatSelected && globalOpen && !hideForWidth;
    }

    // ── GridSplitter (только Normal/Wide) ─────────────────────────────────────

    private void Splitter_DragStarted(object? sender, VectorEventArgs e)
        => _isDragging = true;

    private void Splitter_DragCompleted(object? sender, VectorEventArgs e)
    {
        _isDragging = false;

        if (LayoutMode is not (LayoutMode.Normal or LayoutMode.Wide)) return;

        var mainGrid = this.FindControl<Grid>("MainGrid");
        if (mainGrid?.ColumnDefinitions[0] is not ColumnDefinition column) return;

        var currentWidth = column.Width.Value;

        if (currentWidth <= ENTER_COMPACT_THRESHOLD)
        {
            column.Width = new GridLength(COMPACT_WIDTH);
            IsCompactMode = true;
        }
        else
        {
            _userDefinedWidth = currentWidth;
            IsCompactMode = currentWidth <= ENTER_COMPACT_THRESHOLD;
        }
    }

    private void ChatListColumn_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_isDragging) return;
        if (LayoutMode is not (LayoutMode.Normal or LayoutMode.Wide)) return;
        if (sender is not ColumnDefinition column) return;
        if (e.Property != ColumnDefinition.WidthProperty) return;

        var currentWidth = column.Width.Value;

        if (currentWidth <= ENTER_COMPACT_THRESHOLD && !IsCompactMode)
            IsCompactMode = true;
        else if (currentWidth >= EXIT_COMPACT_THRESHOLD && IsCompactMode)
            IsCompactMode = false;
    }

    // ── Кнопки ────────────────────────────────────────────────────────────────

    public void ToggleCompactMode()
    {
        if (LayoutMode is LayoutMode.UltraCompact or LayoutMode.Compact) return;

        var mainGrid = this.FindControl<Grid>("MainGrid");
        if (mainGrid?.ColumnDefinitions[0] is not ColumnDefinition column) return;

        if (IsCompactMode)
        {
            var targetWidth = _userDefinedWidth >= EXIT_COMPACT_THRESHOLD
                ? _userDefinedWidth
                : NORMAL_DEFAULT_WIDTH;

            column.Width = new GridLength(targetWidth);
            IsCompactMode = false;
        }
        else
        {
            _userDefinedWidth = column.Width.Value;
            column.Width = new GridLength(COMPACT_WIDTH);
            IsCompactMode = true;
        }
    }

    public void ExpandFromCompact()
    {
        if (IsCompactMode) ToggleCompactMode();
    }

    private void OnExpandButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ToggleCompactMode();

    private void OnSearchButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ExpandFromCompact();
        Dispatcher.UIThread.Post(() =>
        {
            var searchBox = this.FindControl<SearchBox>("SearchTextBox");
            searchBox?.FocusInput();
        }, DispatcherPriority.Background);
    }

    private void OnProfileBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ChatsViewModel vm && vm.UserProfileDialog != null)
        {
            vm.UserProfileDialog = null;
            e.Handled = true;
        }
    }

    private void OnSearchBoxFocused(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ChatsViewModel vm)
            vm.SearchManager?.EnterSearchMode();
    }
}