using Avalonia.Input;
using Avalonia.Reactive;
using Avalonia.VisualTree;
using Core.Features.ChatList.ViewModels;
using Core.Infrastructure;
using Core.Services.Chat.Abstractions;
using Core.Services.Platform.Abstractions;
using Core.Shared.Configuration;
using Core.Shared.Controls;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;

namespace Core.Features.ChatList.Views;

public partial class ChatListView : UserControl
{
    private const double COMPACT_WIDTH = 96;
    private const double NORMAL_DEFAULT_WIDTH = 280;
    private const double MIN_WIDTH = COMPACT_WIDTH;
    private const double MAX_WIDTH = 400;

    private const double ENTER_COMPACT_THRESHOLD = 120;
    private const double EXIT_COMPACT_THRESHOLD = 160;

    private bool _isDragging;
    private IDisposable? _layoutModeSubscription;
    private IDisposable? _windowBoundsSubscription;
    private ChatListViewModel? _previousVm;
    private IDisposable? _layoutModeSub;
    private IDisposable? _windowBoundsSub;

    private double _userDefinedWidth = NORMAL_DEFAULT_WIDTH;

    private readonly IChatInfoPanelStateStore _chatInfoPanelStateStore;

    public static readonly StyledProperty<bool> IsCompactModeProperty =
        AvaloniaProperty.Register<ChatListView, bool>(nameof(IsCompactMode));

    public bool IsCompactMode
    {
        get => GetValue(IsCompactModeProperty);
        set => SetValue(IsCompactModeProperty, value);
    }

    public static readonly DirectProperty<ChatListView, LayoutMode> LayoutModeProperty =
        AvaloniaProperty.RegisterDirect<ChatListView, LayoutMode>(nameof(LayoutMode), o => o.LayoutMode);

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

    public ChatListView()
    {
        InitializeComponent();

        _chatInfoPanelStateStore = AppConfig.Services.GetService<IChatInfoPanelStateStore>();

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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var layoutProvider = AppConfig.Services.GetService<ILayoutModeProvider>();
        if (layoutProvider is null) return;

        var mode = layoutProvider.LayoutMode;

        SetAndRaise(LayoutModeProperty, ref _layoutMode, mode);
        OnLayoutModeChanged(mode, mode);

        _layoutModeSub = layoutProvider.LayoutModeChanged
            .Subscribe(new AnonymousObserver<LayoutMode>(m => LayoutMode = m));

        _windowBoundsSub = layoutProvider.WindowBoundsChanged
            .Subscribe(new AnonymousObserver<Rect>(_ =>
            {
                if (LayoutMode is LayoutMode.Normal or LayoutMode.Wide)
                    OnLayoutModeChanged(LayoutMode, LayoutMode);
            }));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _layoutModeSubscription?.Dispose();
        _layoutModeSubscription = null;
        _windowBoundsSubscription?.Dispose();
        _windowBoundsSubscription = null;
        _layoutModeSub?.Dispose();
        _windowBoundsSub?.Dispose();
    }

    private void OnLayoutModeChanged(LayoutMode previous, LayoutMode current)
    {
        var mainGrid = this.FindControl<Grid>("MainGrid");
        if (mainGrid == null) return;

        var col0 = mainGrid.ColumnDefinitions[0];
        var col1 = mainGrid.ColumnDefinitions[1];
        var col2 = mainGrid.ColumnDefinitions[2];
        var col3 = mainGrid.ColumnDefinitions[3];
        switch (current)
        {
            case LayoutMode.UltraCompact:
                var hasChatOpen = DataContext is ChatListViewModel { CurrentChatViewModel: not null };

                col0.MinWidth = 0;
                col0.MaxWidth = double.PositiveInfinity;
                col0.Width = hasChatOpen
                    ? new GridLength(0)
                    : new GridLength(1, GridUnitType.Star);

                col2.Width = hasChatOpen
                    ? new GridLength(1, GridUnitType.Star)
                    : new GridLength(0);
                col2.MinWidth = 0;

                col1.Width = new GridLength(0);
                col3.Width = new GridLength(0);

                IsCompactMode = false;
                break;

            case LayoutMode.Compact:
                col0.MinWidth = COMPACT_WIDTH;
                col0.MaxWidth = COMPACT_WIDTH;
                col0.Width = new GridLength(COMPACT_WIDTH);

                col2.Width = new GridLength(1, GridUnitType.Star);
                col2.MinWidth = 0;

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
                var hasOpenChat = DataContext is ChatListViewModel { CurrentChatViewModel: not null };

                if (infoPanelOpen && hasOpenChat && windowWidth > 0)
                {
                    var minChatWidth = 380.0;
                    var compactListWidth = COMPACT_WIDTH;

                    if (windowWidth < 320 + minChatWidth + targetWidth)
                        targetWidth = compactListWidth;

                    var remainingForChat = windowWidth - 320 - targetWidth;
                    if (remainingForChat < minChatWidth)
                        col2.MinWidth = Math.Max(320, remainingForChat);
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

        if (DataContext is ChatListViewModel vm)
            UpdateInfoPanelVisibility(vm);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_previousVm != null)
            _previousVm.PropertyChanged -= OnVmPropertyChanged;

        _previousVm = DataContext as ChatListViewModel;

        if (_previousVm != null)
        {
            _previousVm.PropertyChanged += OnVmPropertyChanged;
            UpdateInfoPanelVisibility(_previousVm);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not ChatListViewModel vm) return;

        if (e.PropertyName is nameof(ChatListViewModel.CurrentChatViewModel)
                           or nameof(ChatListViewModel.CombinedIsInfoPanelVisible))
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
                OnLayoutModeChanged(LayoutMode, LayoutMode);
            }
        }
    }

    private void UpdateInfoPanelVisibility(ChatListViewModel vm)
    {
        var panel = this.FindControl<Control>("InfoPanel");
        if (panel == null) return;

        var isAnyChatSelected = vm.CurrentChatViewModel != null;
        var globalOpen = _chatInfoPanelStateStore.IsOpen;
        var hideForWidth = LayoutMode is LayoutMode.UltraCompact or LayoutMode.Compact;

        panel.IsVisible = isAnyChatSelected && globalOpen && !hideForWidth;
    }

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
        if (DataContext is ChatListViewModel vm && vm.UserProfileDialog != null)
        {
            vm.UserProfileDialog = null;
            e.Handled = true;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsCompactModeProperty && DataContext is ChatListViewModel vm)
        {
            vm.IsCompactMode = IsCompactMode;
        }
    }

    private void OnSearchBoxFocused(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ChatListViewModel vm)
            vm.SearchManager?.EnterSearchMode();
    }
}