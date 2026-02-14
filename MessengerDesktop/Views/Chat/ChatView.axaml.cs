using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
    private ScrollViewer? _sv;
    private ListBox? _list;
    private ChatViewModel? _vm;

    private bool _initialScrollDone;
    private int _scrollToEndRetries;
    private const int MaxRetries = 10;

    private int _loadingOlder;
    private int _loadingNewer;

    private bool _suppressScroll;

    private bool _svInitialized;

    /// <summary>
    /// Предыдущая высота Extent — для обнаружения layout-изменений (опрос и т.д.)
    /// </summary>
    private double _lastExtentHeight;

    private readonly HashSet<int> _seenMessageIds = [];
    private readonly DispatcherTimer _visibilityTimer;

    public ChatView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        _visibilityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _visibilityTimer.Tick += (_, _) =>
        {
            _visibilityTimer.Stop();
            CheckVisibleMessages();
        };
    }

    #region DataContext

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Detach();

        _vm = DataContext as ChatViewModel;
        _seenMessageIds.Clear();
        _initialScrollDone = false;
        _scrollToEndRetries = 0;
        _suppressScroll = false;
        _lastExtentHeight = 0;
        Interlocked.Exchange(ref _loadingOlder, 0);
        Interlocked.Exchange(ref _loadingNewer, 0);

        Attach();
    }

    private void Attach()
    {
        if (_vm == null) return;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.ScrollToMessageRequested += OnScrollToMessage;
        _vm.ScrollToIndexRequested += OnScrollToIndex;
        _vm.ScrollToBottomRequested += OnScrollToBottom;
        if (_vm.Messages != null)
            _vm.Messages.CollectionChanged += OnMessagesChanged;
    }

    private void Detach()
    {
        if (_vm == null) return;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.ScrollToMessageRequested -= OnScrollToMessage;
        _vm.ScrollToIndexRequested -= OnScrollToIndex;
        _vm.ScrollToBottomRequested -= OnScrollToBottom;
        if (_vm.Messages != null)
            _vm.Messages.CollectionChanged -= OnMessagesChanged;
        _vm = null;
    }

    #endregion

    #region VM Property Changes

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm == null) return;

        switch (e.PropertyName)
        {
            case nameof(ChatViewModel.Messages):
                if (_vm.Messages != null)
                {
                    _vm.Messages.CollectionChanged -= OnMessagesChanged;
                    _vm.Messages.CollectionChanged += OnMessagesChanged;
                }
                break;

            case nameof(ChatViewModel.IsInitialLoading) when !_vm.IsInitialLoading:
                Dispatcher.UIThread.Post(async () =>
                {
                    await Task.Delay(50);
                    FindScrollViewer();
                }, DispatcherPriority.Background);
                break;
        }
    }

    #endregion

    #region Collection Changes

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm == null || e.Action != NotifyCollectionChangedAction.Add)
            return;

        if (!_svInitialized)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(50);
                FindScrollViewer();
            }, DispatcherPriority.Loaded);
        }

        var isAppend = e.NewStartingIndex == _vm.Messages.Count - 1;
        if (!isAppend || !_initialScrollDone) return;

        if (Interlocked.CompareExchange(ref _loadingOlder, 0, 0) == 1)
            return;

        if (_vm.IsScrolledToBottom)
        {
            _scrollToEndRetries = 0;
            DoScrollToEnd();
        }
        else
        {
            _vm.UnreadCount += e.NewItems?.Count ?? 0;
            _vm.HasNewMessages = true;
        }
    }

    #endregion

    #region ScrollViewer Setup

    private void FindScrollViewer()
    {
        if (_svInitialized && _sv != null) return;

        _list ??= this.FindControl<ListBox>("MessagesList");
        if (_list == null) return;

        _sv = _list.FindDescendantOfType<ScrollViewer>();
        if (_sv == null)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(200);
                FindScrollViewer();
            }, DispatcherPriority.Background);
            return;
        }

        _sv.ScrollChanged -= OnScrollChanged;
        _sv.ScrollChanged += OnScrollChanged;
        _svInitialized = true;
        _lastExtentHeight = _sv.Extent.Height;
        Debug.WriteLine("[ChatView] ScrollViewer attached");
    }

    #endregion

    #region Scroll Requests

    private void OnScrollToBottom()
    {
        _scrollToEndRetries = 0;
        _suppressScroll = true;
        FindScrollViewer();
        DoScrollToEnd();
    }

    private void OnScrollToIndex(int index, bool highlight)
    {
        _suppressScroll = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                FindScrollViewer();
                await Task.Delay(100);

                if (_vm == null || index < 0 || index >= _vm.Messages.Count)
                    return;

                ScrollToItem(_vm.Messages[index]);
                await Task.Delay(150);
                _initialScrollDone = true;
            }
            finally
            {
                _suppressScroll = false;
            }
        }, DispatcherPriority.Background);
    }

    private void OnScrollToMessage(MessageViewModel msg, bool highlight)
    {
        if (msg == null || _vm == null) return;
        _suppressScroll = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                FindScrollViewer();
                await Task.Delay(100);
                ScrollToItem(msg);
                await Task.Delay(150);
                _initialScrollDone = true;
            }
            finally
            {
                _suppressScroll = false;
            }
        }, DispatcherPriority.Background);
    }

    private void ScrollToItem(MessageViewModel msg)
    {
        if (_list == null || _vm == null) return;
        FindScrollViewer();

        _list.ScrollIntoView(msg);

        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(50);
            var idx = _vm.Messages.IndexOf(msg);
            if (idx >= 0 && _list.ContainerFromIndex(idx) is Control c)
                c.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    #endregion

    #region Scroll To End

    private void DoScrollToEnd()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(50);
                FindScrollViewer();

                if (_sv == null || _vm == null)
                {
                    if (_scrollToEndRetries++ < MaxRetries)
                    {
                        await Task.Delay(100);
                        DoScrollToEnd();
                    }
                    else
                    {
                        _suppressScroll = false;
                    }
                    return;
                }

                var extent = _sv.Extent.Height;
                var viewport = _sv.Viewport.Height;

                if (extent <= viewport || extent < 1)
                {
                    if (_scrollToEndRetries++ < MaxRetries)
                    {
                        await Task.Delay(100);
                        DoScrollToEnd();
                        return;
                    }
                    FinishScrollToEnd();
                    return;
                }

                var target = extent - viewport;
                _sv.Offset = new Vector(_sv.Offset.X, target);

                await Task.Delay(80);

                if (_sv == null || _vm == null)
                {
                    _suppressScroll = false;
                    return;
                }

                var dist = Math.Abs(
                    _sv.Offset.Y -
                    Math.Max(0, _sv.Extent.Height - _sv.Viewport.Height));

                if (dist > 50 && _scrollToEndRetries++ < MaxRetries)
                {
                    DoScrollToEnd();
                }
                else
                {
                    FinishScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatView] ScrollToEnd error: {ex.Message}");
                _suppressScroll = false;
            }
        }, DispatcherPriority.Background);
    }

    private void FinishScrollToEnd()
    {
        _initialScrollDone = true;
        _suppressScroll = false;
        if (_vm != null)
        {
            _vm.IsScrolledToBottom = true;
            _vm.HasNewMessages = false;
            _vm.UnreadCount = 0;
        }
        if (_sv != null)
            _lastExtentHeight = _sv.Extent.Height;
    }

    #endregion

    #region Scroll Changed — FIXED

    private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_sv == null || _vm is not { } vm) return;

        _visibilityTimer.Stop();
        _visibilityTimer.Start();

        if (_suppressScroll || !_initialScrollDone || vm.IsSearchMode)
            return;

        try
        {
            var offset = _sv.Offset.Y;
            var extent = _sv.Extent.Height;
            var viewport = _sv.Viewport.Height;

            // ===== КЛЮЧЕВОЙ ФИКС =====
            // Если высота контента изменилась (опрос сменил состояние),
            // корректируем offset чтобы сохранить визуальную позицию
            var extentDelta = extent - _lastExtentHeight;
            if (Math.Abs(extentDelta) > 2)
            {
                // Контент изменил высоту — это не пользовательский скролл,
                // а layout-изменение (опрос, загрузка картинки и т.д.)

                // Если изменение произошло ВЫШЕ текущей позиции просмотра,
                // нужно скорректировать offset
                var correctedOffset = offset + extentDelta;

                // Но только если мы не у самого низа
                var distFromBottom = extent - viewport - offset;
                if (distFromBottom > 100 && Math.Abs(extentDelta) < 500)
                {
                    // Не корректируем offset напрямую — просто игнорируем это событие
                    _lastExtentHeight = extent;
                    Debug.WriteLine($"[ChatView] Extent changed by {extentDelta:F0}, ignoring scroll event");
                    return;
                }

                _lastExtentHeight = extent;
            }

            _lastExtentHeight = extent;

            var distBottom = extent - viewport - offset;

            var atBottom = distBottom < 50;
            vm.IsScrolledToBottom = atBottom;
            if (atBottom)
            {
                vm.HasNewMessages = false;
                vm.UnreadCount = 0;
            }

            // Загрузка старых — только по пользовательскому скроллу
            if (offset < 100 &&
                !vm.IsLoadingOlderMessages &&
                !vm.IsInitialLoading &&
                Interlocked.CompareExchange(ref _loadingOlder, 1, 0) == 0)
            {
                try
                {
                    await LoadOlderWithPreserve(vm);
                }
                finally
                {
                    Interlocked.Exchange(ref _loadingOlder, 0);
                }
            }

            if (distBottom < 100 &&
                vm.HasMoreNewer &&
                !vm.IsInitialLoading &&
                Interlocked.CompareExchange(ref _loadingNewer, 1, 0) == 0)
            {
                try
                {
                    await vm.LoadNewerMessagesCommand.ExecuteAsync(null);
                }
                finally
                {
                    Interlocked.Exchange(ref _loadingNewer, 0);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ChatView] OnScrollChanged error: {ex.Message}");
        }
    }

    private async Task LoadOlderWithPreserve(ChatViewModel vm)
    {
        if (_sv == null || _list == null) return;

        var prevExtent = _sv.Extent.Height;
        var prevOffset = _sv.Offset.Y;
        var prevCount = vm.Messages.Count;

        Debug.WriteLine($"[ChatView] Load older: extent={prevExtent:F0}, " +
                        $"offset={prevOffset:F0}, count={prevCount}");

        _suppressScroll = true;

        try
        {
            await vm.LoadOlderMessagesCommand.ExecuteAsync(null);

            var addedCount = vm.Messages.Count - prevCount;
            if (addedCount <= 0)
            {
                Debug.WriteLine("[ChatView] No messages added");
                return;
            }

            double newExtent = prevExtent;
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(20);

                _list.UpdateLayout();

                if (_sv == null) return;

                newExtent = _sv.Extent.Height;
                if (Math.Abs(newExtent - prevExtent) > 5)
                    break;
            }

            if (_sv == null) return;

            var heightDiff = newExtent - prevExtent;
            if (heightDiff > 0)
            {
                var correctedOffset = prevOffset + heightDiff;
                _sv.Offset = new Vector(_sv.Offset.X, correctedOffset);

                Debug.WriteLine($"[ChatView] Corrected: added={addedCount}, " +
                                $"heightDiff={heightDiff:F0}, " +
                                $"newOffset={correctedOffset:F0}");
            }

            await Task.Delay(30);
        }
        finally
        {
            // Обновляем lastExtentHeight после загрузки
            if (_sv != null)
                _lastExtentHeight = _sv.Extent.Height;
            _suppressScroll = false;
        }
    }

    #endregion

    #region Visibility Tracking

    private void CheckVisibleMessages()
    {
        if (_vm == null || _sv == null || _list == null || !_initialScrollDone)
            return;

        var viewportH = _sv.Viewport.Height;

        foreach (var item in _list.GetVisualDescendants().OfType<ListBoxItem>())
        {
            if (item.DataContext is not MessageViewModel msg) continue;
            if (!_seenMessageIds.Add(msg.Id)) continue;

            var transform = item.TransformToVisual(_sv);
            if (transform == null)
            {
                _seenMessageIds.Remove(msg.Id);
                continue;
            }

            var top = transform.Value.Transform(new Point(0, 0)).Y;
            var bottom = top + item.Bounds.Height;

            if (bottom > 0 && top < viewportH)
            {
                _ = _vm.OnMessageVisibleAsync(msg);
            }
            else
            {
                _seenMessageIds.Remove(msg.Id);
            }
        }
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
        if (_sv != null) _sv.ScrollChanged -= OnScrollChanged;
        Detach();
        _svInitialized = false;
        _sv = null;
        _list = null;
        base.OnUnloaded(e);
    }

    #endregion
}