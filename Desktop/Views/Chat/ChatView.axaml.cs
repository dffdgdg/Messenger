using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Desktop.ViewModels.Chat;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop.Views.Chat;

public partial class ChatView : UserControl
{
    private CancellationTokenSource? _scheduleCts;
    private CancellationTokenSource? _scrollCts;
    private const int MaxScrollToEndRetries = 10;
    private const double VisibilityCheckDelayMs = 1000;
    private const double NearBottomThreshold = 200;
    private const double NearTopThreshold = 400;
    private const int ScrollStateSaveDebounceMs = 350;
    private const string ScrollStateKeyPrefix = "chat_scroll_state:";
    private const int SeenIdsCleanupThreshold = 500;
    private EventHandler? _visibilityTimerHandler;
    private EventHandler? _saveScrollStateTimerHandler;
    private ScrollViewer? _scrollViewer;
    private ListBox? _messagesList;
    private ChatViewModel? _viewModel;
    private readonly ISettingsService? _settingsService;

    private bool _isInitialScrollDone;
    private bool _suppressScrollEvents;
    private bool _isScrollViewerInitialized;
    private bool _isRestoringScrollState;
    private bool _scrollStateRestored;
    private ChatScrollState? _pendingScrollState;
    private bool _suppressPositionTracking;

    private int _scrollToEndRetries;
    private int _loadingOlderMessages;
    private int _loadingNewerMessages;

    private readonly HashSet<int> _seenMessageIds = [];
    private readonly DispatcherTimer _visibilityTimer;
    private readonly DispatcherTimer _saveScrollStateTimer;

    public ChatView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        _settingsService = App.Current.Services.GetService<ISettingsService>();

        _visibilityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(VisibilityCheckDelayMs)
        };
        _visibilityTimerHandler = (_, _) =>
        {
            _visibilityTimer.Stop();
            CheckVisibleMessages();
        };
        _visibilityTimer.Tick += _visibilityTimerHandler;

        _saveScrollStateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ScrollStateSaveDebounceMs)
        };
        _saveScrollStateTimerHandler = (_, _) =>
        {
            _saveScrollStateTimer.Stop();
            SaveScrollState();
        };
        _saveScrollStateTimer.Tick += _saveScrollStateTimerHandler;
    }

    #region DataContext Management

    private void CleanupResources()
    {
        _visibilityTimer.Stop();
        _saveScrollStateTimer.Stop();

        if (_visibilityTimerHandler != null)
        {
            _visibilityTimer.Tick -= _visibilityTimerHandler;
            _visibilityTimerHandler = null;
        }
        if (_saveScrollStateTimerHandler != null)
        {
            _saveScrollStateTimer.Tick -= _saveScrollStateTimerHandler;
            _saveScrollStateTimerHandler = null;
        }

        _scheduleCts?.Cancel();
        _scheduleCts?.Dispose();
        _scheduleCts = null;

        _scrollCts?.Cancel();
        _scrollCts?.Dispose();
        _scrollCts = null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        CleanupResources();
        DetachFromViewModel();
        _viewModel = DataContext as ChatViewModel;
        ResetState();
        AttachToViewModel();
    }

    private void ResetState()
    {
        _seenMessageIds.Clear();
        _isInitialScrollDone = false;
        _scrollToEndRetries = 0;
        _suppressScrollEvents = false;
        _isScrollViewerInitialized = false;
        _isRestoringScrollState = false;
        _scrollStateRestored = false;

        Interlocked.Exchange(ref _loadingOlderMessages, 0);
        Interlocked.Exchange(ref _loadingNewerMessages, 0);

        _pendingScrollState = LoadScrollState();
    }

    private void AttachToViewModel()
    {
        if (_viewModel is null) return;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ScrollToMessageRequested += OnScrollToMessageRequested;
        _viewModel.ScrollToIndexRequested += OnScrollToIndexRequested;
        _viewModel.ScrollToBottomRequested += OnScrollToBottomRequested;
        _viewModel.Messages?.CollectionChanged += OnMessagesCollectionChanged;
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

    #endregion

    #region ViewModel Event Handlers

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
            ScheduleAction(FindScrollViewer, 50);
            ScheduleAction(TryRestoreSavedScrollState, 120);
        }
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel is null || e.Action != NotifyCollectionChangedAction.Add)
            return;

        if (!_isScrollViewerInitialized)
            ScheduleAction(FindScrollViewer, 50);

        bool isNewMessageAtBottom = e.NewStartingIndex == _viewModel.Messages?.Count - 1;

        if (!isNewMessageAtBottom || !_isInitialScrollDone)
            return;

        if (Interlocked.CompareExchange(ref _loadingOlderMessages, 0, 0) == 1)
            return;

        if (_viewModel.IsScrolledToBottom)
        {
            _scrollToEndRetries = 0;
            ScrollToBottom();
        }
        else
        {
            _viewModel.UnreadCount += e.NewItems?.Count ?? 0;
            _viewModel.HasNewMessages = true;
        }
    }

    #endregion

    #region ScrollViewer Management

    private void FindScrollViewer()
    {
        if (_isScrollViewerInitialized && _scrollViewer is not null)
            return;

        _messagesList ??= this.FindControl<ListBox>("MessagesList");
        if (_messagesList is null) return;

        _scrollViewer = _messagesList.FindDescendantOfType<ScrollViewer>();

        if (_scrollViewer is null)
        {
            ScheduleAction(FindScrollViewer, 200);
            return;
        }

        _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer.ScrollChanged += OnScrollChanged;

        _isScrollViewerInitialized = true;
    }

    private void EnsureScrollViewer() => FindScrollViewer();

    #endregion

    #region Scroll Requests from ViewModel

    private void OnScrollToBottomRequested()
    {
        if (ShouldIgnoreInitialScrollRequest())
            return;

        _scrollToEndRetries = 0;
        _suppressScrollEvents = true;
        EnsureScrollViewer();
        ScrollToBottom();
    }

    private void OnScrollToIndexRequested(int index, bool highlight)
    {
        if (ShouldIgnoreInitialScrollRequest())
            return;

        ScheduleScrollAction(() =>
        {
            if (_viewModel is null || index < 0 || index >= _viewModel.Messages.Count)
                return;

            ScrollToItem(_viewModel.Messages[index]);
            _isInitialScrollDone = true;
        });
    }

    private void OnScrollToMessageRequested(MessageViewModel message, bool highlight)
    {
        if (message is null || _viewModel is null) return;

        ScheduleScrollAction(() =>
        {
            ScrollToItem(message);
            _isInitialScrollDone = true;
        });
    }

    private void ScrollToItem(MessageViewModel message)
    {
        EnsureScrollViewer();
        if (_messagesList is null) return;
        _messagesList.ScrollIntoView(message);
    }

    #endregion

    #region Scroll To Bottom

    private void ScrollToBottom() => ScheduleAction(PerformScrollToBottom, 50);

    private void PerformScrollToBottom()
    {
        EnsureScrollViewer();

        if (_scrollViewer is null || _viewModel is null)
        {
            RetryScrollToEndIfNeeded();
            return;
        }

        double extent = _scrollViewer.Extent.Height;
        double viewport = _scrollViewer.Viewport.Height;

        if (extent <= viewport || extent < 1)
        {
            RetryScrollToEndIfNeeded();
            return;
        }

        _scrollViewer.Offset = new Avalonia.Vector(_scrollViewer.Offset.X, extent - viewport);
        Dispatcher.UIThread.Post(FinishScrollToBottom, DispatcherPriority.Render);
    }

    private void RetryScrollToEndIfNeeded()
    {
        if (_scrollToEndRetries++ < MaxScrollToEndRetries)
            ScrollToBottom();
        else
            FinishScrollToBottom();
    }

    private void FinishScrollToBottom()
    {
        _isInitialScrollDone = true;
        _suppressScrollEvents = false;

        if (_viewModel is not null)
        {
            _viewModel.IsScrolledToBottom = true;
            _viewModel.HasNewMessages = false;
            _viewModel.UnreadCount = 0;
        }
    }

    #endregion

    #region Main Scroll Handler

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer is null || _viewModel is null) return;

        _visibilityTimer.Stop();
        _visibilityTimer.Start();

        if (_suppressScrollEvents || !_isInitialScrollDone || _viewModel.IsSearchMode)
            return;

        bool isLoading = IsLoadingOlder() || IsLoadingNewer();

        if (!_suppressPositionTracking && !isLoading)
            HandleScrollPosition();

        _saveScrollStateTimer.Stop();
        _saveScrollStateTimer.Start();
    }

    private void HandleScrollPosition()
    {
        if (_scrollViewer is null || _viewModel is null) return;

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

        if (isNearTop && !IsLoadingOlder() && !IsInitialLoading())
            _ = LoadOlderMessagesAsync();

        if (ShouldLoadNewerMessages(isNearBottom))
            _ = LoadNewerMessagesAsync();
    }

    private bool IsLoadingOlder() => Interlocked.CompareExchange(ref _loadingOlderMessages, 0, 0) == 1;
    private bool IsLoadingNewer() => Interlocked.CompareExchange(ref _loadingNewerMessages, 0, 0) == 1;
    private bool IsInitialLoading() => _viewModel?.IsInitialLoading == true;
    private bool ShouldLoadNewerMessages(bool isNearBottom) =>
        isNearBottom && _viewModel?.HasMoreNewer == true && !IsInitialLoading();

    #endregion

    #region Composer Event Handlers

    private void ComposerTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null) return;
        if (_viewModel.HandleMentionNavigationKey(e.Key))
            e.Handled = true;
    }

    private void ComposerTextBox_OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null || sender is not TextBox textBox) return;
        _viewModel.OnComposerSelectionChanged(textBox.CaretIndex);
    }

    private void ComposerTextBox_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_viewModel is null || sender is not TextBox textBox) return;
        _viewModel.OnComposerSelectionChanged(textBox.CaretIndex);
    }

    #endregion

    #region Scroll State Persistence

    private ChatScrollState? LoadScrollState()
    {
        if (_settingsService is null || _viewModel is null)
            return null;

        var key = GetScrollStateKey(_viewModel.Context.ChatId);
        return _settingsService.Get<ChatScrollState>(key);
    }

    private void TryRestoreSavedScrollState()
    {
        if (_scrollStateRestored || _isRestoringScrollState || _pendingScrollState is null)
            return;

        if (_viewModel?.IsInitialLoading != false)
            return;

        EnsureScrollViewer();
        if (_scrollViewer is null)
        {
            ScheduleAction(TryRestoreSavedScrollState, 120);
            return;
        }

        if (_scrollViewer.Extent.Height < 1)
        {
            ScheduleAction(TryRestoreSavedScrollState, 80);
            return;
        }

        _isRestoringScrollState = true;
        _suppressScrollEvents = true;

        try
        {
            var state = _pendingScrollState;

            if (state.IsAtBottom)
            {
                _scrollToEndRetries = 0;
                ScrollToBottom();
            }
            else
            {
                WaitForStableExtentAndRestore(state.OffsetY);
            }

            _scrollStateRestored = true;
            _isInitialScrollDone = true;
        }
        finally
        {
            _isRestoringScrollState = false;
        }
    }

    private void WaitForStableExtentAndRestore(double targetOffsetY) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (_scrollViewer is null) return;

            var maxOffset = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
            var targetOffset = Math.Clamp(targetOffsetY, 0, maxOffset);

            _suppressScrollEvents = true;
            _scrollViewer.Offset = new Avalonia.Vector(_scrollViewer.Offset.X, targetOffset);

            Dispatcher.UIThread.Post(() => _suppressScrollEvents = false, DispatcherPriority.Background);
        }, DispatcherPriority.Render);

    private void SaveScrollState()
    {
        if (_settingsService is null || _scrollViewer is null || _viewModel is null || !_isInitialScrollDone)
            return;

        double extent = _scrollViewer.Extent.Height;
        double viewport = _scrollViewer.Viewport.Height;
        double offset = _scrollViewer.Offset.Y;
        bool isAtBottom = extent - viewport - offset < NearBottomThreshold;

        var state = new ChatScrollState(offset, isAtBottom, DateTime.UtcNow);
        _settingsService.Set(GetScrollStateKey(_viewModel.Context.ChatId), state);
    }

    private static string GetScrollStateKey(int chatId) => $"{ScrollStateKeyPrefix}{chatId}";

    private sealed record ChatScrollState(double OffsetY, bool IsAtBottom, DateTime SavedAtUtc);

    #endregion

    #region Loading Messages

    private async Task LoadOlderMessagesAsync()
    {
        if (_viewModel is null || _scrollViewer is null) return;
        if (Interlocked.CompareExchange(ref _loadingOlderMessages, 1, 0) != 0)
            return;

        try
        {
            await LoadOlderWithPositionPreservationAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _loadingOlderMessages, 0);
        }
    }

    private async Task LoadNewerMessagesAsync()
    {
        if (_viewModel is null) return;
        if (Interlocked.CompareExchange(ref _loadingNewerMessages, 1, 0) != 0)
            return;

        try
        {
            await _viewModel.LoadNewerMessagesCommand.ExecuteAsync(null);
        }
        finally
        {
            Interlocked.Exchange(ref _loadingNewerMessages, 0);
        }
    }

    private async Task LoadOlderWithPositionPreservationAsync()
    {
        if (_scrollViewer is null || _messagesList is null || _viewModel is null)
            return;

        var prevExtent = _scrollViewer.Extent.Height;
        _suppressPositionTracking = true;
        _suppressScrollEvents = true;

        try
        {
            await _viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);

            Dispatcher.UIThread.Post(() =>
            {
                if (_scrollViewer is null) return;

                double newExtent = _scrollViewer.Extent.Height;
                double delta = newExtent - prevExtent;

                if (delta > 0)
                    _scrollViewer.Offset = new Avalonia.Vector(_scrollViewer.Offset.X, _scrollViewer.Offset.Y + delta);

                _suppressScrollEvents = false;
                _suppressPositionTracking = false;
            }, DispatcherPriority.Render);
        }
        catch
        {
            _suppressScrollEvents = false;
            _suppressPositionTracking = false;
        }
    }

    #endregion

    #region Visibility Tracking

    private void CheckVisibleMessages()
    {
        if (_viewModel is null || _scrollViewer is null || _messagesList is null || !_isInitialScrollDone)
            return;

        double viewportHeight = _scrollViewer.Viewport.Height;

        foreach (var container in _messagesList.GetRealizedContainers())
        {
            TryTrackVisibleMessage(container, viewportHeight);
        }

        TrimSeenIdsIfNeeded();
    }

    private void TryTrackVisibleMessage(Avalonia.Controls.Control container, double viewportHeight)
    {
        if (container is not ListBoxItem item) return;
        if (item.DataContext is not MessageViewModel msg) return;
        if (!_seenMessageIds.Add(msg.Id)) return;

        if (IsItemVisible(item, viewportHeight))
            _ = _viewModel!.OnMessageVisibleAsync(msg);
        else
            _seenMessageIds.Remove(msg.Id);
    }

    private void TrimSeenIdsIfNeeded()
    {
        if (_seenMessageIds.Count <= SeenIdsCleanupThreshold) return;

        var currentIds = BuildCurrentlyRealizedIdSet();
        _seenMessageIds.IntersectWith(currentIds);
    }

    private HashSet<int> BuildCurrentlyRealizedIdSet() => [.. _messagesList!.GetRealizedContainers()
        .OfType<ListBoxItem>().Select(c => (c.DataContext as MessageViewModel)?.Id).Where(id => id.HasValue).Select(id => id!.Value)];

    private bool IsItemVisible(ListBoxItem item, double viewportHeight)
    {
        var transform = item.TransformToVisual(_scrollViewer!);
        if (transform is null) return false;

        var top = transform.Value.Transform(new Avalonia.Point(0, 0)).Y;
        var bottom = top + item.Bounds.Height;

        return bottom > 0 && top < viewportHeight;
    }

    #endregion

    #region Helpers

    private bool ShouldIgnoreInitialScrollRequest() => !_scrollStateRestored && _pendingScrollState is not null;

    private void ScheduleAction(Action action, int delayMs = 50)
    {
        _scheduleCts?.Cancel();
        _scheduleCts?.Dispose();
        _scheduleCts = new CancellationTokenSource();
        var token = _scheduleCts.Token;

        _ = RunDelayedActionAsync(action, delayMs, token);
    }

    private void ScheduleScrollAction(Action action)
    {
        _suppressScrollEvents = true;

        _scrollCts?.Cancel();
        _scrollCts?.Dispose();
        _scrollCts = new CancellationTokenSource();
        var token = _scrollCts.Token;

        _ = RunDelayedActionAsync(() =>
        {
            try
            {
                EnsureScrollViewer();
                action();
            }
            finally
            {
                _suppressScrollEvents = false;
            }
        }, 100, token);
    }

    private static async Task RunDelayedActionAsync(Action action, int delayMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delayMs, ct);
            if (!ct.IsCancellationRequested)
                action();
        }
        catch (OperationCanceledException) { /* Ожидаемо */ }
    }

    #endregion

    #region Lifecycle

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
        CleanupResources();
        SaveScrollState();
        _scrollViewer?.ScrollChanged -= OnScrollChanged;
        DetachFromViewModel();
        _seenMessageIds.Clear();
        _isScrollViewerInitialized = false;
        _scrollViewer = null;
        _messagesList = null;
        base.OnUnloaded(e);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is ChatViewModel)
            DataContext = null;

        CleanupResources();
        _scrollViewer?.ScrollChanged -= OnScrollChanged;
        DetachFromViewModel();

        _isScrollViewerInitialized = false;
        _scrollViewer = null;
        _messagesList = null;
        base.OnDetachedFromVisualTree(e);
    }

    #endregion
}