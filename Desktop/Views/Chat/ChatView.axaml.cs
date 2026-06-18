using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Reactive;
using Avalonia.VisualTree;
using Core.Infrastructure;
using Core.ViewModels.Chat;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;

namespace Desktop.Views.Chat;

public partial class ChatView : UserControl
{
    private const int MaxScrollToEndRetries = 10;
    private const double VisibilityCheckDelayMs = 300;
    private const double NearBottomThreshold = 200;
    private const double NearTopThreshold = 800;
    private const int ScrollStateSaveDebounceMs = 350;
    private const string ScrollStateKeyPrefix = "chat_scroll_state:";
    private const int SeenIdsCleanupThreshold = 500;
    private const int ScrollStateMaxAgeHours = 72;
    private const int MaxScrollAdjustRetries = 3;
    private const int OlderLoadCooldownMs = 2000;

    private readonly ISettingsService? _settingsService;
    private readonly HashSet<int> _seenMessageIds = [];

    private ScrollViewer? _scrollViewer;
    private ListBox? _messagesList;
    private ChatViewModel? _viewModel;

    private bool _isInitialScrollDone;
    private bool _suppressScrollEvents;
    private bool _suppressPositionTracking;
    private bool _isScrollViewerInitialized;
    private bool _scrollStateRestored;
    private bool _isRestoringScrollState;
    private ChatScrollState? _pendingScrollState;
    private int _scrollToEndRetries;
    private int _scrollAdjustRetries;

    private int _loadingOlderMessages;
    private int _loadingNewerMessages;

    private DateTime _lastOlderTrigger = DateTime.MinValue;
    private DateTime _lastNewerTrigger = DateTime.MinValue;

    private DateTime _lastScrollTime;
    private DateTime _visibilityTimerLastStarted;

    private CancellationTokenSource? _findCts;
    private CancellationTokenSource? _restoreCts;
    private CancellationTokenSource? _scrollCts;
    private CancellationTokenSource? _fallbackVisibilityCts;

    private DispatcherTimer? _visibilityTimer;
    private DispatcherTimer? _saveScrollStateTimer;
    public static readonly DirectProperty<ChatView, LayoutMode> LayoutModeProperty =
    AvaloniaProperty.RegisterDirect<ChatView, LayoutMode>(
        nameof(LayoutMode), o => o.LayoutMode);

    private LayoutMode _layoutMode;
    public LayoutMode LayoutMode
    {
        get => _layoutMode;
        private set => SetAndRaise(LayoutModeProperty, ref _layoutMode, value);
    }

    private IDisposable? _layoutModeSubscription;

    public ChatView()
    {
        InitializeComponent();
        _settingsService = App.Current.Services.GetService<ISettingsService>();
        DataContextChanged += OnDataContextChanged;
    }

    private readonly SemaphoreSlim _olderLoadSemaphore = new(1, 1);
    private readonly SemaphoreSlim _newerLoadSemaphore = new(1, 1);

    private async Task LoadOlderMessagesAsync()
    {
        if (_viewModel is null || _scrollViewer is null || _messagesList is null) return;
        if (!await _olderLoadSemaphore.WaitAsync(0)) return;

        _lastOlderTrigger = DateTime.UtcNow;
        _suppressPositionTracking = true;
        _viewModel.IsLoadingOlderMessages = true;

        try
        {
            double extentBefore = _scrollViewer.Extent.Height;
            double offsetBefore = _scrollViewer.Offset.Y;

            await _viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            if (_scrollViewer is null) return;

            double extentAfter = _scrollViewer.Extent.Height;
            double extentDelta = extentAfter - extentBefore;

            if (extentDelta > 0.5)
            {
                double newOffset = offsetBefore + extentDelta;
                double maxOffset = Math.Max(0, extentAfter - _scrollViewer.Viewport.Height);
                _scrollViewer.Offset = new Vector(
                    _scrollViewer.Offset.X,
                    Math.Clamp(newOffset, 0, maxOffset));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SCROLL] LoadOlder error: {ex.Message}");
        }
        finally
        {
            _viewModel.IsLoadingOlderMessages = false;
            Dispatcher.UIThread.Post(
                () => _suppressPositionTracking = false,
                DispatcherPriority.Background);
            _olderLoadSemaphore.Release();
        }
    }


    private void HandleScrollPosition()
    {
        if (_scrollViewer is null || _viewModel is null) return;
        if (!_isInitialScrollDone) return;

        if (_suppressPositionTracking) return;
        if (_viewModel.IsLoadingOlderMessages || _viewModel.IsLoadingNewerMessages) return;

        double offset = _scrollViewer.Offset.Y;
        double extent = _scrollViewer.Extent.Height;
        double viewport = _scrollViewer.Viewport.Height;

        bool isNearBottom = extent - viewport - offset < NearBottomThreshold;
        bool isNearTop = offset < NearTopThreshold;

        _viewModel.IsScrolledToBottom = isNearBottom;

        if (isNearBottom)
        {
            _viewModel.HasNewMessages = false;
            _viewModel.UnreadCount = 0;
        }

        var now = DateTime.UtcNow;

        if (isNearTop
            && !_viewModel.IsInitialLoading
            && _viewModel.HasMoreOlder
            && !_viewModel.IsLoadingOlderMessages
            && _olderLoadSemaphore.CurrentCount > 0
            && (now - _lastOlderTrigger).TotalMilliseconds > OlderLoadCooldownMs)
        {
            _ = LoadOlderMessagesAsync();
        }

        if (isNearBottom
            && _viewModel.HasMoreNewer
            && !_viewModel.IsInitialLoading
            && !_viewModel.IsLoadingNewerMessages
            && _newerLoadSemaphore.CurrentCount > 0
            && (now - _lastNewerTrigger).TotalMilliseconds > OlderLoadCooldownMs)
        {
            _ = LoadNewerMessagesAsync();
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_suppressPositionTracking) return;

        _lastScrollTime = DateTime.UtcNow;

        if (_visibilityTimer?.IsEnabled == false &&
            (DateTime.UtcNow - _visibilityTimerLastStarted).TotalMilliseconds > 500)
        {
            _visibilityTimerLastStarted = DateTime.UtcNow;
            _visibilityTimer?.Start();
        }

        if (_viewModel is null || !_isInitialScrollDone || _viewModel.IsSearchMode) return;

        HandleScrollPosition();

        _saveScrollStateTimer?.Stop();
        _saveScrollStateTimer?.Start();
    }

    private async Task LoadNewerMessagesAsync()
    {
        if (_viewModel is null) return;
        if (!await _newerLoadSemaphore.WaitAsync(0)) return;

        _lastNewerTrigger = DateTime.UtcNow;
        _viewModel.IsLoadingNewerMessages = true;

        try
        {
            await _viewModel.LoadNewerMessagesCommand.ExecuteAsync(null);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SCROLL] LoadNewer error: {ex.Message}");
        }
        finally
        {
            _lastNewerTrigger = DateTime.UtcNow;
            if (_viewModel is not null)
                _viewModel.IsLoadingNewerMessages = false;
            _newerLoadSemaphore.Release();
        }
    }

    // Вспомогательные методы для атомарного чтения флагов
    private int IsLoadingOlderAtomic() => Interlocked.CompareExchange(ref _loadingOlderMessages, 0, 0);
    private int IsLoadingNewerAtomic() => Interlocked.CompareExchange(ref _loadingNewerMessages, 0, 0);

    // Оставить старые методы для обратной совместимости с остальным кодом
    private bool IsLoadingOlder() => IsLoadingOlderAtomic() == 1;

    // ══════════════════════════════════════════════════
    // Остальные методы без изменений
    // ══════════════════════════════════════════════════

    private void SetMessagesVisible(bool visible)
    {
        if (_messagesList is null) return;
        Dispatcher.UIThread.Post(()
            => _messagesList?.Opacity = visible ? 1.0 : 0.0, DispatcherPriority.Background);
    }

    private void ScheduleFallbackVisibility()
    {
        _fallbackVisibilityCts?.Cancel();
        _fallbackVisibilityCts?.Dispose();
        _fallbackVisibilityCts = new CancellationTokenSource();
        var token = _fallbackVisibilityCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500, token);
                if (!token.IsCancellationRequested)
                    SetMessagesVisible(true);
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        CancelAllPendingOperations();
        DetachFromViewModel();
        StopTimers();

        _viewModel = DataContext as ChatViewModel;
        ResetScrollState();
        CreateTimers();
        AttachToViewModel();
    }

    private void ResetScrollState()
    {
        _seenMessageIds.Clear();
        _isInitialScrollDone = false;
        _scrollToEndRetries = 0;
        _suppressScrollEvents = false;
        if (_olderLoadSemaphore.CurrentCount == 0)
            _olderLoadSemaphore.Release();
        if (_newerLoadSemaphore.CurrentCount == 0)
            _newerLoadSemaphore.Release();
        _suppressPositionTracking = false;
        _isScrollViewerInitialized = false;
        _scrollStateRestored = false;
        _isRestoringScrollState = false;

        // Сбрасываем cooldown при смене чата
        _lastOlderTrigger = DateTime.MinValue;
        _lastNewerTrigger = DateTime.MinValue;

        SetMessagesVisible(false);

        Interlocked.Exchange(ref _loadingOlderMessages, 0);
        Interlocked.Exchange(ref _loadingNewerMessages, 0);

        _pendingScrollState = LoadValidScrollState();
    }

    private void CreateTimers()
    {
        _visibilityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(VisibilityCheckDelayMs) };
        _visibilityTimer.Tick += OnVisibilityTimerTick;

        _saveScrollStateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ScrollStateSaveDebounceMs) };
        _saveScrollStateTimer.Tick += OnSaveScrollStateTimerTick;
    }

    private void StopTimers()
    {
        if (_visibilityTimer != null) { _visibilityTimer.Stop(); _visibilityTimer.Tick -= OnVisibilityTimerTick; _visibilityTimer = null; }
        if (_saveScrollStateTimer != null) { _saveScrollStateTimer.Stop(); _saveScrollStateTimer.Tick -= OnSaveScrollStateTimerTick; _saveScrollStateTimer = null; }
    }

    private void OnVisibilityTimerTick(object? s, EventArgs e)
    {
        if ((DateTime.UtcNow - _lastScrollTime).TotalMilliseconds < 200) return;
        _visibilityTimer?.Stop();
        CheckVisibleMessages();
    }

    private void OnSaveScrollStateTimerTick(object? s, EventArgs e)
    {
        _saveScrollStateTimer?.Stop();
        SaveScrollState();
    }

    private void AttachToViewModel()
    {
        if (_viewModel is null) return;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ScrollToMessageRequested += OnScrollToMessageRequested;
        _viewModel.ScrollToIndexRequested += OnScrollToIndexRequested;
        _viewModel.ScrollToBottomRequested += OnScrollToBottomRequested;
        _viewModel.Messages?.CollectionChanged += OnMessagesCollectionChanged;
        ScheduleFallbackVisibility();
    }

    private void DetachFromViewModel()
    {
        if (_viewModel is null) return;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.ScrollToMessageRequested -= OnScrollToMessageRequested;
        _viewModel.ScrollToIndexRequested -= OnScrollToIndexRequested;
        _viewModel.ScrollToBottomRequested -= OnScrollToBottomRequested;
        _viewModel.Messages?.CollectionChanged -= OnMessagesCollectionChanged;
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null) return;

        if (e.PropertyName == nameof(ChatViewModel.Messages) && _viewModel.Messages is not null)
        {
            _viewModel.Messages.CollectionChanged -= OnMessagesCollectionChanged;
            _viewModel.Messages.CollectionChanged += OnMessagesCollectionChanged;
        }

        if (e.PropertyName == nameof(ChatViewModel.IsInitialLoading) && !_viewModel.IsInitialLoading)
        {
            ScheduleFind(FindScrollViewer, 50);
            ScheduleRestore(150);
        }
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel is null || e.Action != NotifyCollectionChangedAction.Add) return;

        if (!_isScrollViewerInitialized)
            ScheduleFind(FindScrollViewer, 50);
        if (!_isInitialScrollDone) return;

        bool isNewAtBottom = e.NewStartingIndex == (_viewModel.Messages?.Count ?? 0) - 1;
        if (!isNewAtBottom || IsLoadingOlder()) return;

        if (_viewModel.IsScrolledToBottom)
        {
            _scrollToEndRetries = 0;
            BeginScrollToBottom();
        }
        else
        {
            _viewModel.UnreadCount += e.NewItems?.Count ?? 0;
            _viewModel.HasNewMessages = true;
        }
    }

    private void FindScrollViewer()
    {
        if (_isScrollViewerInitialized && _scrollViewer is not null) return;

        _messagesList ??= this.FindControl<ListBox>("MessagesList");
        if (_messagesList is null) return;

        _scrollViewer = _messagesList.FindDescendantOfType<ScrollViewer>();
        if (_scrollViewer is null) { ScheduleFind(FindScrollViewer, 200); return; }

        _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer.ScrollChanged += OnScrollChanged;
        _isScrollViewerInitialized = true;
    }

    private void EnsureScrollViewer() => FindScrollViewer();

    private void OnScrollToBottomRequested()
    {
        Debug.WriteLine($"[View] OnScrollToBottomRequested: _isInitialScrollDone={_isInitialScrollDone}");

        if (_suppressPositionTracking) return;

        if (ShouldDeferScrollRequest()) return;
        _scrollToEndRetries = 0;
        _suppressScrollEvents = true;
        EnsureScrollViewer();
        BeginScrollToBottom();
    }

    private void BeginScrollToBottom()
    {
        if (_viewModel is null) return;
        if (_scrollViewer is null) EnsureScrollViewer();
        Dispatcher.UIThread.Post(() => PerformScrollToBottom(_viewModel, _scrollViewer), DispatcherPriority.Render);
    }

    private void PerformScrollToBottom(ChatViewModel vm, ScrollViewer? sv)
    {
        if (_viewModel != vm) return;

        if (sv is null) { EnsureScrollViewer(); sv = _scrollViewer; }
        if (sv is null) { RetryScrollToBottom(vm); return; }

        double extent = sv.Extent.Height;
        double viewport = sv.Viewport.Height;

        if (extent <= viewport || extent < 1) { RetryScrollToBottom(vm); return; }

        sv.Offset = new Avalonia.Vector(sv.Offset.X, extent - viewport);
        Dispatcher.UIThread.Post(() => FinishScrollToBottom(vm), DispatcherPriority.Background);
    }

    private void RetryScrollToBottom(ChatViewModel vm)
    {
        if (_viewModel != vm) return;
        if (_scrollToEndRetries++ < MaxScrollToEndRetries)
            Dispatcher.UIThread.Post(() => PerformScrollToBottom(vm, _scrollViewer), DispatcherPriority.Render);
        else
            FinishScrollToBottom(vm);
    }

    private void FinishScrollToBottom(ChatViewModel vm)
    {
        if (_viewModel != vm) return;
        _suppressScrollEvents = false;
        _isInitialScrollDone = true;
        vm.IsScrolledToBottom = true;
        vm.HasNewMessages = false;
        vm.UnreadCount = 0;
        _fallbackVisibilityCts?.Cancel();
        SetMessagesVisible(true);
    }

    private void OnScrollToIndexRequested(int index, bool highlight)
    {
        if (ShouldDeferScrollRequest(highlight)) return;
        ScheduleScrollAction(() =>
        {
            if (_viewModel is null || index < 0 || index >= (_viewModel.Messages?.Count ?? 0)) return;
            ScrollToItemCentered(_viewModel.Messages![index], highlight);
            CompleteInitialScroll(atBottom: false);
        });
    }

    private void OnScrollToMessageRequested(MessageViewModel message, bool highlight)
    {
        if (message is null || _viewModel is null) return;
        if (ShouldDeferScrollRequest(highlight)) return;
        ScheduleScrollAction(() =>
        {
            ScrollToItemCentered(message, highlight);
            CompleteInitialScroll(atBottom: false);
        });
    }

    private void ScrollToItemCentered(MessageViewModel message, bool highlight = false)
    {
        EnsureScrollViewer();
        if (_messagesList is null || _scrollViewer is null) return;

        _scrollAdjustRetries = 0;
        _messagesList.ScrollIntoView(message);
        Dispatcher.UIThread.Post(() => AdjustScrollToCenterItem(message, highlight), DispatcherPriority.Render);
    }

    private void AdjustScrollToCenterItem(MessageViewModel message, bool highlight = false)
    {
        if (_messagesList is null || _scrollViewer is null) return;

        var container = FindContainer(message);
        if (container is null)
        {
            if (_scrollAdjustRetries++ < MaxScrollAdjustRetries)
                Dispatcher.UIThread.Post(() => AdjustScrollToCenterItem(message, highlight), DispatcherPriority.Background);
            return;
        }

        _scrollAdjustRetries = 0;
        CenterItemInViewport(container);

        if (highlight)
        {
            message.IsHighlighted = true;
            _ = Task.Run(async () =>
            {
                await Task.Delay(AppConstants.HighlightDurationMs);
                Dispatcher.UIThread.Post(() => message.IsHighlighted = false);
            });
        }
    }

    private ListBoxItem? FindContainer(MessageViewModel message)
    {
        if (_messagesList is null) return null;
        foreach (var c in _messagesList.GetRealizedContainers())
        {
            if (c is ListBoxItem item && item.DataContext == message)
                return item;
        }
        return null;
    }

    private void CenterItemInViewport(ListBoxItem container)
    {
        if (_scrollViewer is null) return;

        var transform = container.TransformToVisual(_scrollViewer);
        if (transform is null) return;

        double itemTop = transform.Value.Transform(new Point(0, 0)).Y;
        double itemHeight = container.Bounds.Height;
        double viewportHeight = _scrollViewer.Viewport.Height;
        double currentOffset = _scrollViewer.Offset.Y;

        double targetOffset = currentOffset + itemTop - (viewportHeight / 2) + (itemHeight / 2);
        double maxOffset = Math.Max(0, _scrollViewer.Extent.Height - viewportHeight);
        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, Math.Clamp(targetOffset, 0, maxOffset));
    }

    private bool ShouldDeferScrollRequest(bool isExplicitMessageNavigation = false)
    {
        if (_scrollStateRestored || _pendingScrollState is null) return false;
        if (!isExplicitMessageNavigation && _viewModel?.HasInitialMessageTarget != true) return true;

        _pendingScrollState = null;
        _scrollStateRestored = true;
        _restoreCts?.Cancel();
        _restoreCts?.Dispose();
        _restoreCts = null;
        return false;
    }

    private void CompleteInitialScroll(bool atBottom)
    {
        _isInitialScrollDone = true;
        _fallbackVisibilityCts?.Cancel();
        SetMessagesVisible(true);

        if (_viewModel is null) return;
        if (atBottom)
            _viewModel.IsScrolledToBottom = true;
        else
            Dispatcher.UIThread.Post(UpdateIsScrolledToBottomFromOffset, DispatcherPriority.Background);
    }

    private void UpdateIsScrolledToBottomFromOffset()
    {
        if (_scrollViewer is null || _viewModel is null) return;
        _viewModel.IsScrolledToBottom = _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height - _scrollViewer.Offset.Y < NearBottomThreshold;
    }

    private void SaveScrollState()
    {
        if (_settingsService is null || _scrollViewer is null || _viewModel is null || !_isInitialScrollDone) return;

        double extent = _scrollViewer.Extent.Height;
        double viewport = _scrollViewer.Viewport.Height;
        bool isAtBottom = extent - viewport - _scrollViewer.Offset.Y < NearBottomThreshold;

        var anchor = FindAnchorMessage();
        if (anchor is null && !isAtBottom) return;

        var state = new ChatScrollState(anchor?.MessageId ?? -1, anchor?.OffsetFromTop ?? 0, isAtBottom, DateTime.UtcNow);
        _settingsService.Set(GetScrollStateKey(_viewModel.Context.ChatId), state);
    }

    private AnchorInfo? FindAnchorMessage()
    {
        if (_messagesList is null || _scrollViewer is null) return null;

        AnchorInfo? best = null;
        double bestTop = double.MaxValue;

        foreach (var container in _messagesList.GetRealizedContainers())
        {
            if (container is not ListBoxItem { DataContext: MessageViewModel msg }) continue;

            var transform = container.TransformToVisual(_scrollViewer);
            if (transform is null) continue;

            double top = transform.Value.Transform(new Point(0, 0)).Y;
            double bottom = top + container.Bounds.Height;

            if (bottom > 0 && top < _scrollViewer.Viewport.Height && top < bestTop)
            {
                bestTop = top;
                best = new AnchorInfo(msg.Id, top);
            }
        }

        return best;
    }

    private ChatScrollState? LoadValidScrollState()
    {
        if (_settingsService is null || _viewModel is null) return null;

        var key = GetScrollStateKey(_viewModel.Context.ChatId);
        var state = _settingsService.Get<ChatScrollState>(key);

        if (state is null) return null;
        if ((DateTime.UtcNow - state.SavedAtUtc).TotalHours > ScrollStateMaxAgeHours)
        {
            _settingsService.Remove(key);
            return null;
        }

        return state;
    }

    private void ScheduleRestore(int delayMs)
    {
        _restoreCts?.Cancel();
        _restoreCts?.Dispose();
        _restoreCts = new CancellationTokenSource();
        _ = RunDelayedAsync(TryRestoreSavedScrollState, delayMs, _restoreCts.Token);
    }

    private void TryRestoreSavedScrollState()
    {
        if (_pendingScrollState is null) { _scrollStateRestored = true; return; }
        if (_scrollStateRestored || _isRestoringScrollState) return;
        if (_viewModel?.IsInitialLoading != false) return;

        EnsureScrollViewer();
        if (_scrollViewer is null) { ScheduleRestore(100); return; }
        if (_scrollViewer.Extent.Height < 1) { ScheduleRestore(80); return; }

        _isRestoringScrollState = true;
        _suppressScrollEvents = true;
        _scrollStateRestored = true;

        var state = _pendingScrollState;
        try
        {
            if (state.IsAtBottom) { _scrollToEndRetries = 0; BeginScrollToBottom(); }
            else { RestoreToAnchor(state.AnchorMessageId, state.AnchorOffset); }
        }
        finally { _isRestoringScrollState = false; }
    }

    private void RestoreToAnchor(int anchorMessageId, double anchorOffsetFromTop)
    {
        if (_viewModel is null || _messagesList is null) return;

        var message = _viewModel.Messages.FirstOrDefault(m => m.Id == anchorMessageId)
            ?? _viewModel.Messages.Where(m => m.Id <= anchorMessageId).OrderByDescending(m => m.Id).FirstOrDefault()
            ?? _viewModel.Messages.FirstOrDefault();

        if (message is null) { CompleteInitialScroll(atBottom: false); return; }

        _messagesList.ScrollIntoView(message);
        Dispatcher.UIThread.Post(() => AdjustOffsetAfterScrollIntoView(message, anchorOffsetFromTop), DispatcherPriority.Render);
    }

    private void AdjustOffsetAfterScrollIntoView(MessageViewModel targetMessage, double desiredOffsetFromTop)
    {
        if (_scrollViewer is null || _messagesList is null) return;

        var container = FindContainer(targetMessage);
        if (container is null)
        {
            Dispatcher.UIThread.Post(() => AdjustOffsetAfterScrollIntoView(targetMessage, desiredOffsetFromTop), DispatcherPriority.Background);
            return;
        }

        var transform = container.TransformToVisual(_scrollViewer);
        if (transform is null) { CompleteInitialScroll(atBottom: false); return; }

        double currentTop = transform.Value.Transform(new Point(0, 0)).Y;
        double newOffset = _scrollViewer.Offset.Y + (currentTop - desiredOffsetFromTop);
        double maxOffset = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);

        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, Math.Clamp(newOffset, 0, maxOffset));
        CompleteInitialScroll(atBottom: false);
    }

    private static string GetScrollStateKey(int chatId) => $"{ScrollStateKeyPrefix}{chatId}";

    private void CheckVisibleMessages()
    {
        if (_viewModel is null || _scrollViewer is null || _messagesList is null || !_isInitialScrollDone) return;

        double viewportHeight = _scrollViewer.Viewport.Height;
        foreach (var container in _messagesList.GetRealizedContainers())
            TryTrackVisibleMessage(container, viewportHeight);
        TrimSeenIdsIfNeeded();
    }

    private void TryTrackVisibleMessage(Avalonia.Controls.Control container, double viewportHeight)
    {
        if (container is not ListBoxItem { DataContext: MessageViewModel msg }) return;
        if (!_seenMessageIds.Add(msg.Id)) return;

        if (IsItemVisible(container, viewportHeight))
            _ = _viewModel!.OnMessageVisibleAsync(msg);
        else
            _seenMessageIds.Remove(msg.Id);
    }

    private void TrimSeenIdsIfNeeded()
    {
        if (_seenMessageIds.Count <= SeenIdsCleanupThreshold) return;

        var current = _messagesList!.GetRealizedContainers()
            .OfType<ListBoxItem>()
            .Select(c => (c.DataContext as MessageViewModel)?.Id)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        _seenMessageIds.IntersectWith(current);
    }

    private bool IsItemVisible(Avalonia.Controls.Control item, double viewportHeight)
    {
        var t = item.TransformToVisual(_scrollViewer!);
        if (t is null) return false;
        double top = t.Value.Transform(new Avalonia.Point(0, 0)).Y;
        return top + item.Bounds.Height > 0 && top < viewportHeight;
    }

    private void ComposerTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel?.HandleMentionNavigationKey(e.Key) == true)
            e.Handled = true;
    }

    private void ComposerTextBox_OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not null && sender is TextBox tb)
            _viewModel.OnComposerSelectionChanged(tb.CaretIndex);
    }

    private void ComposerTextBox_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_viewModel is not null && sender is TextBox tb)
            _viewModel.OnComposerSelectionChanged(tb.CaretIndex);
    }

    private void ScheduleFind(Action action, int delayMs)
    {
        _findCts?.Cancel();
        _findCts?.Dispose();
        _findCts = new CancellationTokenSource();
        _ = RunDelayedAsync(action, delayMs, _findCts.Token);
    }

    private void ScheduleScrollAction(Action action)
    {
        _suppressScrollEvents = true;
        _scrollCts?.Cancel();
        _scrollCts = new CancellationTokenSource();
        _ = RunDelayedAsync(() =>
        {
            try { EnsureScrollViewer(); action(); }
            finally { _suppressScrollEvents = false; }
        }, 100, _scrollCts.Token);
    }

    private static async Task RunDelayedAsync(Action action, int delayMs, CancellationToken ct)
    {
        try { await Task.Delay(delayMs, ct); if (!ct.IsCancellationRequested) action(); }
        catch (OperationCanceledException) { }
    }

    private void CancelAllPendingOperations()
    {
        _findCts?.Cancel(); _findCts?.Dispose(); _findCts = null;
        _restoreCts?.Cancel(); _restoreCts?.Dispose(); _restoreCts = null;
        _scrollCts?.Cancel(); _scrollCts?.Dispose(); _scrollCts = null;
        _fallbackVisibilityCts?.Cancel(); _fallbackVisibilityCts?.Dispose(); _fallbackVisibilityCts = null;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        FindScrollViewer();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        Dispatcher.UIThread.Post(FindScrollViewer, DispatcherPriority.Loaded);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        SaveScrollState();
        Cleanup();
        base.OnUnloaded(e);
    }
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
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _layoutModeSubscription?.Dispose();

        if (DataContext is ChatViewModel)
            DataContext = null;
        Cleanup();
    }

    private void Cleanup()
    {
        StopTimers();
        CancelAllPendingOperations();
        _scrollViewer?.ScrollChanged -= OnScrollChanged;
        _scrollViewer = null;
        DetachFromViewModel();
        _seenMessageIds.Clear();
        _isScrollViewerInitialized = false;
        _messagesList = null;

        _olderLoadSemaphore?.Dispose();
        _newerLoadSemaphore?.Dispose();
    }

    private sealed record AnchorInfo(int MessageId, double OffsetFromTop);
    private sealed record ChatScrollState(int AnchorMessageId, double AnchorOffset, bool IsAtBottom, DateTime SavedAtUtc);
}