using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MessengerDesktop.ViewModels.Chat;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.Views.Chat;

public partial class ChatView : UserControl
{
    private const int MaxScrollToEndRetries = 10;
    private const int VisibilityCheckDelayMs = 300;
    private const double NearBottomThreshold = 50;
    private const double NearTopThreshold = 100;

    private ScrollViewer? _scrollViewer;
    private ListBox? _messagesList;
    private ChatViewModel? _viewModel;

    private bool _isInitialScrollDone;
    private bool _suppressScrollEvents;
    private bool _isScrollViewerInitialized;

    private double _lastExtentHeight;
    private int _scrollToEndRetries;

    private int _loadingOlderMessages;
    private int _loadingNewerMessages;

    private readonly HashSet<int> _seenMessageIds = [];
    private readonly DispatcherTimer _visibilityTimer;

    public ChatView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        _visibilityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(VisibilityCheckDelayMs)
        };
        _visibilityTimer.Tick += (_, _) =>
        {
            _visibilityTimer.Stop();
            CheckVisibleMessages();
        };
    }

    #region DataContext Management

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
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
        _lastExtentHeight = 0;
        _isScrollViewerInitialized = false;

        Interlocked.Exchange(ref _loadingOlderMessages, 0);
        Interlocked.Exchange(ref _loadingNewerMessages, 0);
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
        _lastExtentHeight = _scrollViewer.Extent.Height;

        Debug.WriteLine("[ChatView] ScrollViewer attached successfully");
    }

    private void EnsureScrollViewer() => FindScrollViewer();

    #endregion

    #region Scroll Requests from ViewModel

    private void OnScrollToBottomRequested()
    {
        _scrollToEndRetries = 0;
        _suppressScrollEvents = true;
        EnsureScrollViewer();
        ScrollToBottom();
    }

    private void OnScrollToIndexRequested(int index, bool highlight)
    {
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

        ScheduleAction(() =>
        {
            if (_messagesList.ContainerFromItem(message) is Control control)
                control.BringIntoView();
        }, 50);
    }

    #endregion

    #region Scroll To Bottom

    private void ScrollToBottom() => ScheduleAction(PerformScrollToBottomAsync, 50);

    private async Task PerformScrollToBottomAsync()
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

        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, extent - viewport);
        await Task.Delay(80);

        FinishScrollToBottom();
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

        if (_scrollViewer is not null)
            _lastExtentHeight = _scrollViewer.Extent.Height;
    }

    #endregion

    #region Main Scroll Handler

    private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer is null || _viewModel is null) return;

        _visibilityTimer.Stop();
        _visibilityTimer.Start();

        if (_suppressScrollEvents || !_isInitialScrollDone || _viewModel.IsSearchMode)
            return;

        HandleExtentChange();
        HandleScrollPosition();
    }

    private void HandleExtentChange()
    {
        if (_scrollViewer is null) return;
        double currentExtent = _scrollViewer.Extent.Height;

        if (Math.Abs(currentExtent - _lastExtentHeight) > 2)
            _lastExtentHeight = currentExtent;
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
    private bool IsInitialLoading() => _viewModel?.IsInitialLoading == true;
    private bool ShouldLoadNewerMessages(bool isNearBottom)
        => isNearBottom && _viewModel?.HasMoreNewer == true && !IsInitialLoading();

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
        var prevOffset = _scrollViewer.Offset.Y;
        var prevCount = _viewModel.Messages.Count;

        _suppressScrollEvents = true;

        try
        {
            await _viewModel.LoadOlderMessagesCommand.ExecuteAsync(null);
            await PreserveScrollPositionAfterLoadingAsync(prevExtent, prevOffset, prevCount);
        }
        finally
        {
            _lastExtentHeight = _scrollViewer.Extent.Height;
            _suppressScrollEvents = false;
        }
    }

    private async Task PreserveScrollPositionAfterLoadingAsync(double prevExtent, double prevOffset, int prevCount)
    {
        if (_scrollViewer is null || _viewModel is null) return;

        int added = _viewModel.Messages.Count - prevCount;
        if (added <= 0) return;

        double newExtent = await WaitForExtentChangeAsync(prevExtent);

        if (newExtent - prevExtent > 5)
        {
            _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, prevOffset + (newExtent - prevExtent));
        }
    }

    private async Task<double> WaitForExtentChangeAsync(double previousExtent)
    {
        if (_scrollViewer is null) return previousExtent;

        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(20);
            _messagesList?.UpdateLayout();

            if (_scrollViewer.Extent.Height - previousExtent > 5)
                break;
        }

        return _scrollViewer.Extent.Height;
    }

    #endregion

    #region Visibility Tracking

    private void CheckVisibleMessages()
    {
        if (_viewModel is null || _scrollViewer is null || _messagesList is null || !_isInitialScrollDone)
            return;

        double viewportHeight = _scrollViewer.Viewport.Height;

        foreach (var item in _messagesList.GetVisualDescendants().OfType<ListBoxItem>())
        {
            if (item.DataContext is not MessageViewModel msg) continue;
            if (!_seenMessageIds.Add(msg.Id)) continue;

            if (IsItemVisible(item, viewportHeight))
                _ = _viewModel.OnMessageVisibleAsync(msg);
            else
                _seenMessageIds.Remove(msg.Id);
        }
    }

    private bool IsItemVisible(ListBoxItem item, double viewportHeight)
    {
        var transform = item.TransformToVisual(_scrollViewer);
        if (transform is null) return false;

        var top = transform.Value.Transform(new Point(0, 0)).Y;
        var bottom = top + item.Bounds.Height;

        return bottom > 0 && top < viewportHeight;
    }

    #endregion

    #region Helpers

    private void ScheduleAction(Func<Task> action, int delayMs = 50)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(delayMs);
            await action();
        }, DispatcherPriority.Background);
    }

    private void ScheduleAction(Action action, int delayMs = 50)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(delayMs);
            action();
        }, DispatcherPriority.Background);
    }

    private void ScheduleScrollAction(Action action)
    {
        _suppressScrollEvents = true;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(100);
                EnsureScrollViewer();
                action();
            }
            finally
            {
                _suppressScrollEvents = false;
            }
        }, DispatcherPriority.Background);
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
        _visibilityTimer.Stop();
        _scrollViewer?.ScrollChanged -= OnScrollChanged;

        DetachFromViewModel();

        _isScrollViewerInitialized = false;
        _scrollViewer = null;
        _messagesList = null;

        base.OnUnloaded(e);
    }

    #endregion
}