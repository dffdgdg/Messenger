// ChatView.axaml.cs
// Code-behind для ChatView. Управляет:
// - подключением к ScrollViewer и обработкой скролла,
// - загрузкой старых/новых сообщений при достижении краёв,
// - сохранением позиции скролла при подгрузке истории,
// - отслеживанием видимых сообщений для отметки прочитанных,
// - обработкой запросов скролла из ViewModel.
//
// Вся бизнес-логика остаётся в ViewModel — здесь только UI-координация.

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
    // ── UI-элементы ──────────────────────────────────────────────────

    private ScrollViewer? _sv;
    private ListBox? _list;
    private ChatViewModel? _vm;

    // ── Состояние скролла ────────────────────────────────────────────

    /// <summary>Флаг: начальный скролл уже выполнен.</summary>
    private bool _initialScrollDone;

    /// <summary>Счётчик попыток скролла к концу (для ожидания layout).</summary>
    private int _scrollToEndRetries;
    private const int MaxRetries = 10;

    /// <summary>Блокировка параллельной загрузки старых сообщений (0 = свободно).</summary>
    private int _loadingOlder;

    /// <summary>Блокировка параллельной загрузки новых сообщений (0 = свободно).</summary>
    private int _loadingNewer;

    /// <summary>Подавление обработки scroll-событий (во время программного скролла).</summary>
    private bool _suppressScroll;

    /// <summary>ScrollViewer найден и подключён.</summary>
    private bool _svInitialized;

    /// <summary>
    /// Предыдущая высота Extent — для обнаружения layout-изменений
    /// (голосование в опросе, загрузка изображения и т.д.).
    /// </summary>
    private double _lastExtentHeight;

    // ── Отслеживание видимости сообщений ─────────────────────────────

    /// <summary>ID сообщений, которые уже были отмечены как видимые.</summary>
    private readonly HashSet<int> _seenMessageIds = [];

    /// <summary>Debounce-таймер для проверки видимых сообщений.</summary>
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

    #region DataContext — подписка/отписка от ViewModel

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Detach();

        _vm = DataContext as ChatViewModel;

        // Сброс состояния при смене чата
        _seenMessageIds.Clear();
        _initialScrollDone = false;
        _scrollToEndRetries = 0;
        _suppressScroll = false;
        _lastExtentHeight = 0;
        Interlocked.Exchange(ref _loadingOlder, 0);
        Interlocked.Exchange(ref _loadingNewer, 0);

        Attach();
    }

    /// <summary>Подписка на события ViewModel.</summary>
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

    /// <summary>Отписка от событий ViewModel.</summary>
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
            // Переподписка при замене коллекции Messages
            case nameof(ChatViewModel.Messages):
                if (_vm.Messages != null)
                {
                    _vm.Messages.CollectionChanged -= OnMessagesChanged;
                    _vm.Messages.CollectionChanged += OnMessagesChanged;
                }
                break;

            // После завершения загрузки — ищем ScrollViewer
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

    #region Collection Changes — обработка добавления сообщений

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm == null || e.Action != NotifyCollectionChangedAction.Add)
            return;

        // При первом добавлении — пытаемся найти ScrollViewer
        if (!_svInitialized)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(50);
                FindScrollViewer();
            }, DispatcherPriority.Loaded);
        }

        // Реагируем только на сообщения, добавленные в конец (новые)
        var isAppend = e.NewStartingIndex == _vm.Messages.Count - 1;
        if (!isAppend || !_initialScrollDone) return;

        // Не скроллим, если идёт загрузка старых (вставка в начало)
        if (Interlocked.CompareExchange(ref _loadingOlder, 0, 0) == 1)
            return;

        if (_vm.IsScrolledToBottom)
        {
            // Пользователь в конце — автоскролл к новому сообщению
            _scrollToEndRetries = 0;
            DoScrollToEnd();
        }
        else
        {
            // Пользователь прокрутил вверх — показываем бейдж
            _vm.UnreadCount += e.NewItems?.Count ?? 0;
            _vm.HasNewMessages = true;
        }
    }

    #endregion

    #region ScrollViewer Setup

    /// <summary>
    /// Находит ScrollViewer внутри ListBox.
    /// Avalonia не гарантирует его наличие сразу — повторяем с задержкой.
    /// </summary>
    private void FindScrollViewer()
    {
        if (_svInitialized && _sv != null) return;

        _list ??= this.FindControl<ListBox>("MessagesList");
        if (_list == null) return;

        _sv = _list.FindDescendantOfType<ScrollViewer>();
        if (_sv == null)
        {
            // ScrollViewer ещё не создан — повторяем
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

    #region Scroll Requests — обработка запросов из ViewModel

    /// <summary>Скролл в самый низ списка сообщений.</summary>
    private void OnScrollToBottom()
    {
        _scrollToEndRetries = 0;
        _suppressScroll = true;
        FindScrollViewer();
        DoScrollToEnd();
    }

    /// <summary>Скролл к сообщению по индексу в коллекции.</summary>
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

    /// <summary>Скролл к конкретному сообщению.</summary>
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

    /// <summary>Скроллит ListBox к указанному элементу.</summary>
    private void ScrollToItem(MessageViewModel msg)
    {
        if (_list == null || _vm == null) return;
        FindScrollViewer();

        _list.ScrollIntoView(msg);

        // Дополнительная корректировка через BringIntoView
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(50);
            var idx = _vm.Messages.IndexOf(msg);
            if (idx >= 0 && _list.ContainerFromIndex(idx) is Control c)
                c.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    #endregion

    #region Scroll To End — с retry для ожидания layout

    /// <summary>
    /// Скроллит к концу списка. Повторяет попытки (до MaxRetries),
    /// пока layout не стабилизируется и скролл не достигнет цели.
    /// </summary>
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

                // Контент меньше viewport — ждём
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

                // Устанавливаем offset в самый конец
                var target = extent - viewport;
                _sv.Offset = new Vector(_sv.Offset.X, target);

                await Task.Delay(80);

                if (_sv == null || _vm == null)
                {
                    _suppressScroll = false;
                    return;
                }

                // Проверяем, достигли ли цели
                var dist = Math.Abs(
                    _sv.Offset.Y -
                    Math.Max(0, _sv.Extent.Height - _sv.Viewport.Height));

                if (dist > 50 && _scrollToEndRetries++ < MaxRetries)
                    DoScrollToEnd();
                else
                    FinishScrollToEnd();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ChatView] ScrollToEnd error: {ex.Message}");
                _suppressScroll = false;
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>Завершение скролла — обновление состояния.</summary>
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

    #region Scroll Changed — основной обработчик скролла

    /// <summary>
    /// Обработчик события скролла. Определяет:
    /// - находится ли пользователь внизу списка,
    /// - нужно ли подгрузить старые/новые сообщения,
    /// - игнорировать ли layout-изменения (опросы, изображения).
    /// </summary>
    private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_sv == null || _vm is not { } vm) return;

        // Debounce для проверки видимых сообщений
        _visibilityTimer.Stop();
        _visibilityTimer.Start();

        if (_suppressScroll || !_initialScrollDone || vm.IsSearchMode)
            return;

        try
        {
            var offset = _sv.Offset.Y;
            var extent = _sv.Extent.Height;
            var viewport = _sv.Viewport.Height;

            // ── Компенсация layout-изменений ──
            // Если высота контента изменилась (опрос сменил состояние,
            // загрузилось изображение) — это не пользовательский скролл.
            var extentDelta = extent - _lastExtentHeight;
            if (Math.Abs(extentDelta) > 2)
            {
                var distFromBottom = extent - viewport - offset;

                // Не у самого низа и изменение не огромное — игнорируем
                if (distFromBottom > 100 && Math.Abs(extentDelta) < 500)
                {
                    _lastExtentHeight = extent;
                    Debug.WriteLine(
                        $"[ChatView] Extent changed by {extentDelta:F0}, ignoring scroll event");
                    return;
                }

                _lastExtentHeight = extent;
            }

            _lastExtentHeight = extent;

            // ── Определение позиции ──
            var distBottom = extent - viewport - offset;
            var atBottom = distBottom < 50;

            vm.IsScrolledToBottom = atBottom;
            if (atBottom)
            {
                vm.HasNewMessages = false;
                vm.UnreadCount = 0;
            }

            // ── Подгрузка старых сообщений (скролл вверх) ──
            if (offset < 100
                && !vm.IsLoadingOlderMessages
                && !vm.IsInitialLoading
                && Interlocked.CompareExchange(ref _loadingOlder, 1, 0) == 0)
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

            // ── Подгрузка новых сообщений (скролл вниз при пропуске) ──
            if (distBottom < 100
                && vm.HasMoreNewer
                && !vm.IsInitialLoading
                && Interlocked.CompareExchange(ref _loadingNewer, 1, 0) == 0)
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

    /// <summary>
    /// Загружает старые сообщения и корректирует offset,
    /// чтобы пользователь не «прыгнул» при вставке сообщений выше.
    /// </summary>
    private async Task LoadOlderWithPreserve(ChatViewModel vm)
    {
        if (_sv == null || _list == null) return;

        var prevExtent = _sv.Extent.Height;
        var prevOffset = _sv.Offset.Y;
        var prevCount = vm.Messages.Count;

        Debug.WriteLine(
            $"[ChatView] Load older: extent={prevExtent:F0}, " +
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

            // Ждём пока layout обновится с новыми элементами
            double newExtent = prevExtent;
            for (var i = 0; i < 15; i++)
            {
                await Task.Delay(20);
                _list.UpdateLayout();

                if (_sv == null) return;

                newExtent = _sv.Extent.Height;
                if (Math.Abs(newExtent - prevExtent) > 5)
                    break;
            }

            if (_sv == null) return;

            // Корректируем offset: сдвигаем на разницу высот
            var heightDiff = newExtent - prevExtent;
            if (heightDiff > 0)
            {
                var correctedOffset = prevOffset + heightDiff;
                _sv.Offset = new Vector(_sv.Offset.X, correctedOffset);

                Debug.WriteLine(
                    $"[ChatView] Corrected: added={addedCount}, " +
                    $"heightDiff={heightDiff:F0}, " +
                    $"newOffset={correctedOffset:F0}");
            }

            await Task.Delay(30);
        }
        finally
        {
            if (_sv != null)
                _lastExtentHeight = _sv.Extent.Height;
            _suppressScroll = false;
        }
    }

    #endregion

    #region Visibility Tracking — отслеживание видимых сообщений

    /// <summary>
    /// Проверяет, какие сообщения сейчас видны в viewport,
    /// и уведомляет ViewModel для отметки прочитанных.
    /// </summary>
    private void CheckVisibleMessages()
    {
        if (_vm == null || _sv == null || _list == null || !_initialScrollDone)
            return;

        var viewportH = _sv.Viewport.Height;

        foreach (var item in _list.GetVisualDescendants().OfType<ListBoxItem>())
        {
            if (item.DataContext is not MessageViewModel msg) continue;

            // Уже видели это сообщение — пропускаем
            if (!_seenMessageIds.Add(msg.Id)) continue;

            var transform = item.TransformToVisual(_sv);
            if (transform == null)
            {
                _seenMessageIds.Remove(msg.Id);
                continue;
            }

            var top = transform.Value.Transform(new Point(0, 0)).Y;
            var bottom = top + item.Bounds.Height;

            // Сообщение пересекает видимую область
            if (bottom > 0 && top < viewportH)
            {
                _ = _vm.OnMessageVisibleAsync(msg);
            }
            else
            {
                // Не в viewport — убираем из seen, проверим позже
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

        if (_sv != null)
            _sv.ScrollChanged -= OnScrollChanged;

        Detach();

        _svInitialized = false;
        _sv = null;
        _list = null;

        base.OnUnloaded(e);
    }

    #endregion
}