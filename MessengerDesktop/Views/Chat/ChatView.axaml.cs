using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MessengerDesktop.ViewModels.Chat;

namespace MessengerDesktop.Views.Chat;

public partial class ChatView : UserControl
{
    private ScrollViewer? _scrollViewer;
    private ListBox? _messagesList;
    private ChatViewModel? _currentVm;

    private bool _isInitialScrollDone;
    private int _scrollToEndRetryCount;
    private const int MaxScrollRetries = 10; // Увеличено

    private bool _isAdjustingScroll;
    private bool _isLoadingOlder;
    private bool _suppressScrollEvents;
    private bool _scrollViewerInitialized;

    private readonly HashSet<int> _processedMessageIds = [];
    private readonly DispatcherTimer _visibilityCheckTimer;

    public ChatView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        _visibilityCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _visibilityCheckTimer.Tick += OnVisibilityCheckTick;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null)
        {
            _currentVm.PropertyChanged -= OnViewModelPropertyChanged;
            _currentVm.ScrollToMessageRequested -= OnScrollToMessageRequested;
            _currentVm.ScrollToIndexRequested -= OnScrollToIndexRequested;
            _currentVm.ScrollToBottomRequested -= OnScrollToBottomRequested;

            if (_currentVm.Messages != null)
            {
                _currentVm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
            }
        }

        _currentVm = DataContext as ChatViewModel;
        _processedMessageIds.Clear();

        _isInitialScrollDone = false;
        _scrollToEndRetryCount = 0;
        _isAdjustingScroll = false;
        _isLoadingOlder = false;
        _suppressScrollEvents = false;

        if (_currentVm != null)
        {
            _currentVm.PropertyChanged += OnViewModelPropertyChanged;
            _currentVm.ScrollToMessageRequested += OnScrollToMessageRequested;
            _currentVm.ScrollToIndexRequested += OnScrollToIndexRequested;
            _currentVm.ScrollToBottomRequested += OnScrollToBottomRequested;

            if (_currentVm.Messages != null)
            {
                _currentVm.Messages.CollectionChanged += OnMessagesCollectionChanged;
            }

            Debug.WriteLine("[ChatView] DataContext changed");
        }
    }

    private void OnScrollToBottomRequested()
    {
        Debug.WriteLine("[ChatView] ScrollToBottomRequested");
        _scrollToEndRetryCount = 0;
        _suppressScrollEvents = true;

        // Если ScrollViewer ещё не найден - ищем
        if (_scrollViewer == null)
        {
            TryFindScrollViewer();
        }

        ScrollToEndWithRetry();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_currentVm == null) return;

        if (e.PropertyName == nameof(ChatViewModel.Messages))
        {
            if (_currentVm.Messages != null)
            {
                _currentVm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
                _currentVm.Messages.CollectionChanged += OnMessagesCollectionChanged;
            }
        }

        // Когда загрузка завершена - пробуем найти ScrollViewer
        if (e.PropertyName == nameof(ChatViewModel.IsInitialLoading) && !_currentVm.IsInitialLoading)
        {
            // Даём время на рендеринг
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(100);
                TryFindScrollViewer();
            }, DispatcherPriority.Background);
        }
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_currentVm == null) return;

        // При первом добавлении сообщений - ищем ScrollViewer
        if (e.Action == NotifyCollectionChangedAction.Add && !_scrollViewerInitialized)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(50);
                TryFindScrollViewer();
            }, DispatcherPriority.Loaded);
        }

        // Автоскролл только если мы внизу и это новое сообщение в конец
        if (e.Action == NotifyCollectionChangedAction.Add &&
            _currentVm.IsScrolledToBottom &&
            e.NewStartingIndex == (_currentVm.Messages.Count - 1) &&
            _isInitialScrollDone &&
            !_isLoadingOlder)
        {
            Debug.WriteLine("[ChatView] New message added, auto-scrolling");
            _scrollToEndRetryCount = 0;
            ScrollToEndWithRetry();
        }

        // Обновляем счётчик непрочитанных
        if (e.Action == NotifyCollectionChangedAction.Add &&
            !_currentVm.IsScrolledToBottom &&
            _isInitialScrollDone &&
            e.NewStartingIndex == (_currentVm.Messages.Count - 1))
        {
            _currentVm.UnreadCount += e.NewItems?.Count ?? 0;
            _currentVm.HasNewMessages = true;
        }
    }

    /// <summary>
    /// Пытается найти ScrollViewer внутри ListBox
    /// </summary>
    private void TryFindScrollViewer()
    {
        if (_scrollViewerInitialized && _scrollViewer != null) return;

        _messagesList = this.FindControl<ListBox>("MessagesList");

        if (_messagesList == null)
        {
            Debug.WriteLine("[ChatView] MessagesList not found!");
            return;
        }

        // Способ 1: Через визуальное дерево
        _scrollViewer = _messagesList.FindDescendantOfType<ScrollViewer>();

        // Способ 2: Если не нашли - через шаблон
        // Пробуем получить через TemplatedParent
        _scrollViewer ??= FindScrollViewerInTemplate(_messagesList);

        if (_scrollViewer != null)
        {
            // Отписываемся от старого (если был)
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.ScrollChanged += OnScrollChanged;

            _scrollViewerInitialized = true;
            Debug.WriteLine("[ChatView] ScrollViewer found and initialized!");
        }
        else
        {
            Debug.WriteLine("[ChatView] ScrollViewer still not found, will retry...");

            // Повторная попытка через некоторое время
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(200);
                if (!_scrollViewerInitialized)
                {
                    TryFindScrollViewer();
                }
            }, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Поиск ScrollViewer в шаблоне контрола
    /// </summary>
    private static ScrollViewer? FindScrollViewerInTemplate(Control control)
    {
        // Рекурсивный поиск
        foreach (var child in control.GetVisualChildren())
        {
            if (child is ScrollViewer sv)
                return sv;

            if (child is Control childControl)
            {
                var found = FindScrollViewerInTemplate(childControl);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    private void OnScrollToIndexRequested(int index, bool withHighlight)
    {
        Debug.WriteLine($"[ChatView] ScrollToIndexRequested({index}, highlight={withHighlight})");

        _suppressScrollEvents = true;

        Dispatcher.UIThread.Post(async () =>
        {
            // Убеждаемся, что ScrollViewer найден
            TryFindScrollViewer();

            await Task.Delay(100);

            if (_currentVm == null || index < 0 || index >= _currentVm.Messages.Count)
            {
                _suppressScrollEvents = false;
                return;
            }

            var message = _currentVm.Messages[index];
            ScrollToMessageInternal(message);

            await Task.Delay(150);

            _isInitialScrollDone = true;
            _suppressScrollEvents = false;

        }, DispatcherPriority.Background);
    }

    private void OnScrollToMessageRequested(MessageViewModel message, bool withHighlight)
    {
        if (message == null || _currentVm == null) return;

        Debug.WriteLine($"[ChatView] ScrollToMessageRequested({message.Id}, highlight={withHighlight})");

        _suppressScrollEvents = true;

        Dispatcher.UIThread.Post(async () =>
        {
            TryFindScrollViewer();

            await Task.Delay(100);

            ScrollToMessageInternal(message);

            await Task.Delay(150);

            _isInitialScrollDone = true;
            _suppressScrollEvents = false;

        }, DispatcherPriority.Background);
    }

    private void ScrollToMessageInternal(MessageViewModel message)
    {
        if (_currentVm == null || _messagesList == null) return;

        // Ещё раз пробуем найти ScrollViewer
        if (_scrollViewer == null)
        {
            TryFindScrollViewer();
        }

        var index = _currentVm.Messages.IndexOf(message);
        if (index < 0)
        {
            Debug.WriteLine($"[ChatView] Message {message.Id} not found");
            return;
        }

        // Способ 1: ScrollIntoView (лучший для виртуализации)
        _messagesList.ScrollIntoView(message);
        Debug.WriteLine($"[ChatView] Called ScrollIntoView for index={index}");

        // Способ 2: Через контейнер (резервный)
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(50);

            var container = _messagesList.ContainerFromIndex(index);
            if (container is Control control)
            {
                control.BringIntoView();
                Debug.WriteLine($"[ChatView] BringIntoView for container at index={index}");
            }
        }, DispatcherPriority.Loaded);
    }

    private void ScrollToEndWithRetry()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(50);

            // Пробуем найти ScrollViewer если ещё не нашли
            if (_scrollViewer == null)
            {
                TryFindScrollViewer();
            }

            if (_scrollViewer == null)
            {
                if (_scrollToEndRetryCount < MaxScrollRetries)
                {
                    _scrollToEndRetryCount++;
                    Debug.WriteLine($"[ChatView] ScrollViewer null, retry {_scrollToEndRetryCount}");
                    await Task.Delay(100);
                    ScrollToEndWithRetry();
                }
                else
                {
                    Debug.WriteLine("[ChatView] Max retries, ScrollViewer not found!");
                    _suppressScrollEvents = false;
                }
                return;
            }

            if (_currentVm == null)
            {
                _suppressScrollEvents = false;
                return;
            }

            var extent = _scrollViewer.Extent.Height;
            var viewport = _scrollViewer.Viewport.Height;

            if (extent <= viewport || extent < 1)
            {
                if (_scrollToEndRetryCount < MaxScrollRetries)
                {
                    _scrollToEndRetryCount++;
                    Debug.WriteLine($"[ChatView] Extent not ready ({extent:F0}), retry {_scrollToEndRetryCount}");
                    await Task.Delay(100);
                    ScrollToEndWithRetry();
                    return;
                }

                MarkScrollComplete();
                Debug.WriteLine("[ChatView] Max retries, assuming at bottom");
                return;
            }

            var targetOffset = extent - viewport;
            _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, targetOffset);
            Debug.WriteLine($"[ChatView] Scrolling to offset {targetOffset:F0}");

            await Task.Delay(100);

            if (_scrollViewer == null || _currentVm == null)
            {
                _suppressScrollEvents = false;
                return;
            }

            var newExtent = _scrollViewer.Extent.Height;
            var newViewport = _scrollViewer.Viewport.Height;
            var newOffset = _scrollViewer.Offset.Y;
            var newTarget = Math.Max(0, newExtent - newViewport);

            var distanceFromBottom = Math.Abs(newOffset - newTarget);

            if (distanceFromBottom > 50 && _scrollToEndRetryCount < MaxScrollRetries)
            {
                _scrollToEndRetryCount++;
                Debug.WriteLine($"[ChatView] Not at bottom (dist={distanceFromBottom:F0}), retry {_scrollToEndRetryCount}");
                ScrollToEndWithRetry();
            }
            else
            {
                MarkScrollComplete();
                Debug.WriteLine($"[ChatView] ScrollToEnd complete, offset={newOffset:F0}, target={newTarget:F0}");
            }

        }, DispatcherPriority.Background);
    }

    private void MarkScrollComplete()
    {
        _isInitialScrollDone = true;
        _suppressScrollEvents = false;

        if (_currentVm != null)
        {
            _currentVm.IsScrolledToBottom = true;
            _currentVm.HasNewMessages = false;
            _currentVm.UnreadCount = 0;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Первая попытка найти ScrollViewer
        TryFindScrollViewer();

        Debug.WriteLine($"[ChatView] OnLoaded, ScrollViewer found: {_scrollViewer != null}");
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // Ещё одна попытка после применения шаблона
        Dispatcher.UIThread.Post(TryFindScrollViewer, DispatcherPriority.Loaded);
    }

    private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer == null || DataContext is not ChatViewModel vm) return;

        _visibilityCheckTimer.Stop();
        _visibilityCheckTimer.Start();

        if (_suppressScrollEvents)
        {
            Debug.WriteLine("[ChatView] ScrollChanged suppressed");
            return;
        }

        if (_isAdjustingScroll)
        {
            Debug.WriteLine("[ChatView] ScrollChanged during adjustment, skipping");
            return;
        }

        if (!_isInitialScrollDone) return;

        if (vm.IsSearchMode) return;

        // Загрузка старых сообщений
        if (_scrollViewer.Offset.Y < 100 &&
            !vm.IsLoadingOlderMessages &&
            !vm.IsInitialLoading &&
            !_isLoadingOlder)
        {
            await LoadOlderMessagesWithScrollPreserve(vm);
        }

        // Загрузка новых сообщений
        var distanceFromBottom = _scrollViewer.Extent.Height
            - _scrollViewer.Viewport.Height
            - _scrollViewer.Offset.Y;

        if (distanceFromBottom < 100 && vm.HasMoreNewer && !vm.IsInitialLoading)
        {
            await vm.LoadNewerMessagesCommand.ExecuteAsync(null);
        }

        // Обновление состояния
        var isAtBottom = distanceFromBottom < 50;
        vm.IsScrolledToBottom = isAtBottom;

        if (isAtBottom)
        {
            vm.HasNewMessages = false;
            vm.UnreadCount = 0;
        }
    }

    private async Task LoadOlderMessagesWithScrollPreserve(ChatViewModel vm)
    {
        if (_scrollViewer == null || _messagesList == null) return;

        _isLoadingOlder = true;
        _isAdjustingScroll = true;
        _suppressScrollEvents = true;

        try
        {
            // Запоминаем первый видимый элемент и его позицию
            var firstVisibleIndex = GetFirstVisibleItemIndex();
            var previousOffset = _scrollViewer.Offset.Y;
            var previousExtent = _scrollViewer.Extent.Height;
            var previousCount = vm.Messages.Count;

            Debug.WriteLine($"[ChatView] Before load: extent={previousExtent:F0}, offset={previousOffset:F0}, firstVisible={firstVisibleIndex}, count={previousCount}");

            // Загружаем
            await vm.LoadOlderMessagesCommand.ExecuteAsync(null);

            // Ждём рендеринга
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(30);
                _messagesList.UpdateLayout();

                var currentExtent = _scrollViewer.Extent.Height;
                var currentCount = vm.Messages.Count;

                if (currentCount > previousCount && Math.Abs(currentExtent - previousExtent) > 10)
                {
                    break;
                }
            }

            // Корректируем позицию
            var newExtent = _scrollViewer.Extent.Height;
            var addedCount = vm.Messages.Count - previousCount;
            var heightDiff = newExtent - previousExtent;

            if (heightDiff > 0 && addedCount > 0)
            {
                var newOffset = previousOffset + heightDiff;
                _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, newOffset);

                Debug.WriteLine($"[ChatView] After load: extent={newExtent:F0}, added={addedCount}, heightDiff={heightDiff:F0}, newOffset={newOffset:F0}");
            }

            await Task.Delay(50);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatView] Error in LoadOlderMessages: {ex.Message}");
        }
        finally
        {
            _isLoadingOlder = false;
            _isAdjustingScroll = false;
            _suppressScrollEvents = false;
        }
    }

    /// <summary>
    /// Получает индекс первого видимого элемента
    /// </summary>
    private int GetFirstVisibleItemIndex()
    {
        if (_scrollViewer == null || _messagesList == null || _currentVm == null)
            return 0;
        _ = _scrollViewer.Offset.Y;

        for (int i = 0; i < _currentVm.Messages.Count; i++)
        {
            var container = _messagesList.ContainerFromIndex(i);
            if (container is Control control)
            {
                var transform = control.TransformToVisual(_scrollViewer);
                if (transform != null)
                {
                    var pos = transform.Value.Transform(new Point(0, 0));
                    if (pos.Y >= -control.Bounds.Height && pos.Y < _scrollViewer.Viewport.Height)
                    {
                        return i;
                    }
                }
            }
        }

        return 0;
    }

    private void OnVisibilityCheckTick(object? sender, EventArgs e)
    {
        _visibilityCheckTimer.Stop();
        CheckVisibleMessages();
    }

    private void CheckVisibleMessages()
    {
        if (_currentVm == null || _scrollViewer == null || _messagesList == null)
            return;

        if (!_isInitialScrollDone) return;

        var viewportHeight = _scrollViewer.Viewport.Height;

        foreach (var message in _currentVm.Messages)
        {
            if (!message.IsUnread || message.SenderId == _currentVm.UserId)
                continue;

            if (_processedMessageIds.Contains(message.Id))
                continue;

            var index = _currentVm.Messages.IndexOf(message);
            if (index < 0) continue;

            var container = _messagesList.ContainerFromIndex(index);
            if (container is not Control control)
                continue;

            var transform = control.TransformToVisual(_scrollViewer);
            if (transform == null)
                continue;

            var topLeft = transform.Value.Transform(new Point(0, 0));
            var bottomRight = transform.Value.Transform(new Point(
                control.Bounds.Width, control.Bounds.Height));

            var isVisible = bottomRight.Y > 0 && topLeft.Y < viewportHeight;

            if (isVisible)
            {
                _processedMessageIds.Add(message.Id);
                _ = _currentVm.OnMessageVisibleAsync(message);
            }
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _visibilityCheckTimer.Stop();

        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        }

        if (_currentVm != null)
        {
            _currentVm.PropertyChanged -= OnViewModelPropertyChanged;
            _currentVm.ScrollToMessageRequested -= OnScrollToMessageRequested;
            _currentVm.ScrollToIndexRequested -= OnScrollToIndexRequested;
            _currentVm.ScrollToBottomRequested -= OnScrollToBottomRequested;

            if (_currentVm.Messages != null)
            {
                _currentVm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
            }
        }

        _scrollViewerInitialized = false;
        _scrollViewer = null;
        _messagesList = null;

        base.OnUnloaded(e);
    }
}