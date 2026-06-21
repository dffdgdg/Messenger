using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Reactive;
using Avalonia.VisualTree;
using Core.Infrastructure;
using Core.ViewModels;
using Core.ViewModels.Chat;
using Core.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace Desktop.Views;

public partial class MainWindow : Window
{
    private readonly IDialogService _dialogService;
    private readonly IPlatformService _platformService;
    private readonly INotificationService _notificationService;

    private const int AnimationDurationMs = 250;
    private const int FrameDelayMs = 16;
    private const double MaximizedPadding = 7;

    private const double UltraCompactThreshold = 500;
    private const double CompactThreshold = 700;
    private const double WideThreshold = 900;

    private const string OpenClass = "Open";
    private const string ClosingClass = "Closing";

    private CancellationTokenSource? _animationCts;
    private readonly Lock _animationLock = new();

    private bool _searchBoxSubscribed;
    private bool _isDrawerOpen;

    private bool _compactSearchOpen;
    private bool _compactSearchVmSubscribed;

    public static readonly DirectProperty<MainWindow, LayoutMode> LayoutModeProperty =
        AvaloniaProperty.RegisterDirect<MainWindow, LayoutMode>(nameof(LayoutMode), o => o.LayoutMode);

    private LayoutMode _layoutMode = LayoutMode.Normal;
    public LayoutMode LayoutMode
    {
        get => _layoutMode;
        private set => SetAndRaise(LayoutModeProperty, ref _layoutMode, value);
    }

    public static readonly DirectProperty<MainWindow, bool> IsCompactModeProperty =
        AvaloniaProperty.RegisterDirect<MainWindow, bool>(nameof(IsCompactMode), o => o.IsCompactMode);

    private bool _isCompactMode;
    public bool IsCompactMode
    {
        get => _isCompactMode;
        private set => SetAndRaise(IsCompactModeProperty, ref _isCompactMode, value);
    }

    public MainWindow()
    {
        InitializeComponent();

        _platformService = AppConfig.Services.GetRequiredService<IPlatformService>();
        _dialogService = AppConfig.Services.GetRequiredService<IDialogService>();
        _notificationService = AppConfig.Services.GetRequiredService<INotificationService>();

        SubscribeSearchBox();
        _platformService.Initialize(this);
        _notificationService.Initialize();
        _dialogService.OnDialogAnimationRequested += OnDialogAnimationRequested;

        UpdateWindowPadding();
        UpdateLayoutMode();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
            UpdateWindowPadding();

        if (change.Property == BoundsProperty)
        {
            UpdateLayoutMode();
            PushLayoutModeToMainMenu();
        }
    }

    private void UpdateLayoutMode()
    {
        var w = Bounds.Width;

        var mode = w < UltraCompactThreshold ? LayoutMode.UltraCompact : w < CompactThreshold ? LayoutMode.Compact
                 : w < WideThreshold ? LayoutMode.Normal : LayoutMode.Wide;

        LayoutMode = mode;
        IsCompactMode = mode is LayoutMode.UltraCompact or LayoutMode.Compact;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);

        this.GetObservable(LayoutModeProperty)
            .Subscribe(new AnonymousObserver<LayoutMode>(mode =>
            {
                PushLayoutModeToMainMenu();

                if (mode is LayoutMode.Normal or LayoutMode.Wide)
                {
                    CloseDrawer();
                    CloseCompactSearch();
                }

                AdaptDialogToLayoutMode(mode);
            }));

        PushLayoutModeToMainMenu();
    }

    private void PushLayoutModeToMainMenu()
        => this.FindDescendantOfType<MainMenuView>()?.WindowLayoutMode = LayoutMode;

    private void AdaptDialogToLayoutMode(LayoutMode mode)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.CurrentDialog == null) return;

        var isCompact = mode is LayoutMode.UltraCompact or LayoutMode.Compact;

        if (isCompact && vm.CurrentDialog.IsFullscreenInCompactMode)
        {
            DialogAnimWrapper.Margin = new Thickness(0, 48, 0, 0);
            DialogAnimWrapper.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            DialogAnimWrapper.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            DialogAnimWrapper.MaxWidth = double.PositiveInfinity;
            DialogAnimWrapper.MaxHeight = double.PositiveInfinity;

            DialogOverlay.IsHitTestVisible = false;
            DialogOverlay.Classes.Remove(OpenClass);
        }
        else
        {
            DialogAnimWrapper.Margin = new Thickness(0, 56, 0, 24);
            DialogAnimWrapper.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            DialogAnimWrapper.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            DialogAnimWrapper.MaxWidth = double.PositiveInfinity;
            DialogAnimWrapper.MaxHeight = double.PositiveInfinity;

            if (vm.IsDialogVisible)
            {
                DialogOverlay.IsHitTestVisible = true;
                if (!DialogOverlay.Classes.Contains(OpenClass))
                    DialogOverlay.Classes.Add(OpenClass);
            }
        }
    }

    private void OnHamburgerButtonClick(object? sender, RoutedEventArgs e)
        => ToggleDrawer();

    private void ToggleDrawer()
    {
        if (_isDrawerOpen) CloseDrawer();
        else OpenDrawer();
    }

    private void OpenDrawer()
    {
        if (_isDrawerOpen) return;
        _isDrawerOpen = true;

        DrawerScrim.IsVisible = true;
        DrawerScrim.IsHitTestVisible = true;
        DrawerPanel.IsVisible = true;

        Dispatcher.UIThread.Post(() =>
        {
            DrawerScrim.Opacity = 1;
            DrawerPanel.RenderTransform = TransformOperations.Parse("translateX(0)");
        }, DispatcherPriority.Render);
    }

    public void CloseDrawer()
    {
        if (!_isDrawerOpen) return;
        _isDrawerOpen = false;

        DrawerScrim.Opacity = 0;
        DrawerScrim.IsHitTestVisible = false;
        DrawerPanel.RenderTransform = TransformOperations.Parse("translateX(-280px)");

        _ = Task.Delay(250).ContinueWith(_ =>
            Dispatcher.UIThread.Post(() =>
            {
                DrawerScrim.IsVisible = false;
                DrawerPanel.IsVisible = false;
            }));
    }

    private void OnDrawerScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseDrawer();
        e.Handled = true;
    }

    private void OnCompactSearchButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { CurrentViewModel: MainMenuViewModel menu })
            return;

        menu.SearchManager.EnterSearchMode();
        OpenCompactSearch(menu);
        CloseDrawer();
    }

    private void OpenCompactSearch(MainMenuViewModel menu)
    {
        if (_compactSearchOpen) return;
        _compactSearchOpen = true;

        CompactSearchOverlay.IsVisible = true;
        CompactSearchScrim.IsHitTestVisible = true;

        SubscribeCompactSearchVm(menu);

        Dispatcher.UIThread.Post(() => CompactSearchBox?.Focus(), DispatcherPriority.Loaded);
    }

    private void CloseCompactSearch()
    {
        if (!_compactSearchOpen) return;
        _compactSearchOpen = false;

        CompactSearchOverlay.IsVisible = false;
        CompactSearchScrim.IsHitTestVisible = false;
        UnsubscribeCompactSearchVm();

        if (DataContext is MainWindowViewModel { CurrentViewModel: MainMenuViewModel menu })
            menu.CloseSearchCommand.Execute(null);
    }

    private void SubscribeCompactSearchVm(MainMenuViewModel menu)
    {
        if (_compactSearchVmSubscribed) return;
        menu.SearchManager.PropertyChanged += OnCompactSearchManagerPropertyChanged;
        _compactSearchVmSubscribed = true;
    }

    private void UnsubscribeCompactSearchVm()
    {
        if (!_compactSearchVmSubscribed) return;

        if (DataContext is MainWindowViewModel { CurrentViewModel: MainMenuViewModel menu })
            menu.SearchManager.PropertyChanged -= OnCompactSearchManagerPropertyChanged;

        _compactSearchVmSubscribed = false;
    }

    private void OnCompactSearchManagerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GlobalSearchManager.IsSearchMode)) return;

        if (DataContext is MainWindowViewModel { CurrentViewModel: MainMenuViewModel menu } && !menu.SearchManager.IsSearchMode)
        {
            _compactSearchOpen = false;
            CompactSearchOverlay.IsVisible = false;
            CompactSearchScrim.IsHitTestVisible = false;
            UnsubscribeCompactSearchVm();
        }
    }

    private void OnCompactSearchScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        var hitPanel = (e.Source as Avalonia.Visual) ?.GetSelfAndVisualAncestors()
            .Any(v => ReferenceEquals(v, CompactSearchPanel)) ?? false;

        if (!hitPanel)
        {
            CloseCompactSearch();
            e.Handled = true;
        }
    }

    private void SubscribeSearchBox()
    {
        if (_searchBoxSubscribed || GlobalSearchBox == null) return;
        GlobalSearchBox.SearchFocused += OnGlobalSearchFocused;
        _searchBoxSubscribed = true;
    }

    private void UnsubscribeSearchBox()
    {
        if (!_searchBoxSubscribed) return;
        GlobalSearchBox?.SearchFocused -= OnGlobalSearchFocused;
        _searchBoxSubscribed = false;
    }

    private void OnGlobalSearchFocused(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { CurrentViewModel: MainMenuViewModel menu })
        {
            menu.SearchManager.EnterSearchMode();
            CloseDrawer();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        RemoveHandler(PointerPressedEvent, OnWindowPointerPressed);
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { CurrentViewModel: MainMenuViewModel menu })
            return;
        if (!menu.SearchManager.IsSearchMode)
            return;

        if (LayoutMode is LayoutMode.Normal or LayoutMode.Wide)
        {
            var inSearch = GlobalSearchBox?.IsPointerOver ?? false;

            var menuView = this.FindDescendantOfType<MainMenuView>();
            var popup = menuView?.FindControl<Border>("SearchPopup");
            var inPopup = popup?.IsPointerOver ?? false;

            if (!inSearch && !inPopup)
                menu.CloseSearchCommand.Execute(null);
        }
    }

    private void UpdateWindowPadding()
        => Padding = WindowState == WindowState.Maximized ? new Thickness(MaximizedPadding) : default;

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    private void OnDialogAnimationRequested(bool isOpening)
    {
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var completed = false;
            try
            {
                completed = await RunAnimationAsync(isOpening);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Dialog animation error: {ex.Message}");
                ResetAnimationState();
            }
            finally
            {
                if (completed)
                    _dialogService.NotifyAnimationComplete();
            }
        });
    }

    private async Task<bool> RunAnimationAsync(bool isOpening)
    {
        CancellationTokenSource? oldCts;
        var newCts = new CancellationTokenSource();

        lock (_animationLock)
        {
            oldCts = _animationCts;
            _animationCts = newCts;
        }

        if (oldCts is not null)
        {
            await oldCts.CancelAsync();
            oldCts.Dispose();
        }

        try
        {
            if (isOpening)
            {
                ResetAnimationState();
                await PlayOpenAnimationAsync(newCts.Token);
            }
            else
            {
                await PlayCloseAnimationAsync(newCts.Token);
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            ResetAnimationState();
            return false;
        }
    }

    private void ResetAnimationState()
    {
        DialogOverlay.Classes.Remove(OpenClass);
        DialogOverlay.Classes.Remove(ClosingClass);
        DialogAnimWrapper.Classes.Remove(OpenClass);
        DialogAnimWrapper.Classes.Remove(ClosingClass);

        DialogAnimWrapper.Margin = new Thickness(0, 56, 0, 24);
        DialogAnimWrapper.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        DialogAnimWrapper.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        DialogAnimWrapper.MaxWidth = double.PositiveInfinity;
        DialogAnimWrapper.MaxHeight = double.PositiveInfinity;

        DialogOverlay.IsHitTestVisible = false;
    }

    private async Task PlayOpenAnimationAsync(CancellationToken ct)
    {
        AdaptDialogToLayoutMode(LayoutMode);

        await Task.Delay(FrameDelayMs, ct);
        DialogAnimWrapper.Classes.Add(OpenClass);

        var isCompact = LayoutMode is LayoutMode.UltraCompact or LayoutMode.Compact;
        if (DataContext is MainWindowViewModel vm && vm.CurrentDialog?.IsFullscreenInCompactMode == true && isCompact)
        {
        }
        else
        {
            DialogOverlay.Classes.Add(OpenClass);
        }

        await Task.Delay(AnimationDurationMs, ct);
    }

    private async Task PlayCloseAnimationAsync(CancellationToken ct)
    {
        DialogOverlay.Classes.Remove(OpenClass);
        DialogAnimWrapper.Classes.Remove(OpenClass);
        DialogAnimWrapper.Classes.Add(ClosingClass);
        await Task.Delay(AnimationDurationMs, ct);
        ct.ThrowIfCancellationRequested();
        DialogAnimWrapper.Classes.Remove(ClosingClass);
        DialogAnimWrapper.Margin = new Thickness(0, 56, 0, 24);
        DialogAnimWrapper.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        DialogAnimWrapper.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        DialogOverlay.IsHitTestVisible = false;
    }

    private void OnDialogBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentDialog?.CloseOnBackgroundClickCommand?.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _dialogService.OnDialogAnimationRequested -= OnDialogAnimationRequested;
        UnsubscribeSearchBox();
        UnsubscribeCompactSearchVm();

        lock (_animationLock)
        {
            _animationCts?.Cancel();
            _animationCts?.Dispose();
            _animationCts = null;
        }

        _platformService.Cleanup();
        _notificationService.Dispose();

        base.OnClosed(e);
    }
}