using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MessengerDesktop.ViewModels.Chat;

namespace MessengerDesktop.Views.Chat
{
    public partial class ChatView : UserControl
    {
        private ScrollViewer? _scrollViewer;
        private ItemsControl? _messagesList;
        private ChatViewModel? _currentVm;
        private bool _pendingScrollToEnd;

        // ��� ������������ ������� ���������
        private readonly HashSet<int> _processedMessageIds = [];
        private readonly DispatcherTimer _visibilityCheckTimer;

        public ChatView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;

            // ������ ��� debounce �������� ���������
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

                if (_currentVm.Messages != null)
                {
                    _currentVm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
                }
            }

            _currentVm = DataContext as ChatViewModel;
            _processedMessageIds.Clear();

            if (_currentVm != null)
            {
                _currentVm.PropertyChanged += OnViewModelPropertyChanged;
                _currentVm.ScrollToMessageRequested += OnScrollToMessageRequested;
                _currentVm.ScrollToIndexRequested += OnScrollToIndexRequested;

                if (_currentVm.Messages != null)
                {
                    _currentVm.Messages.CollectionChanged += OnMessagesCollectionChanged;
                }

                _pendingScrollToEnd = true;
            }
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

            if (e.PropertyName == nameof(ChatViewModel.IsInitialLoading) &&
                !_currentVm.IsInitialLoading && _pendingScrollToEnd)
            {
                _pendingScrollToEnd = false;
            }
        }

        private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add &&
                _currentVm?.IsScrolledToBottom == true &&
                e.NewStartingIndex == (_currentVm?.Messages.Count - 1))
            {
                ScrollToEndAfterRender();
            }
        }

        private void OnScrollToIndexRequested(int index)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await System.Threading.Tasks.Task.Delay(100);

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (_currentVm == null || index < 0 || index >= _currentVm.Messages.Count)
                            return;

                        var message = _currentVm.Messages[index];
                        ScrollToMessage(message);

                        Debug.WriteLine($"[ChatView] Scrolled to index {index}, message {message.Id}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ChatView] Error scrolling to index: {ex.Message}");
                    }
                }, DispatcherPriority.Loaded);

            }, DispatcherPriority.Background);
        }

        private void OnScrollToMessageRequested(MessageViewModel message)
        {
            if (message == null || _currentVm == null) return;

            Dispatcher.UIThread.Post(async () =>
            {
                await System.Threading.Tasks.Task.Delay(100);

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        ScrollToMessage(message);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ChatView] Error scrolling to message: {ex.Message}");
                    }
                }, DispatcherPriority.Loaded);

            }, DispatcherPriority.Background);
        }

        private void ScrollToMessage(MessageViewModel message)
        {
            if (_scrollViewer == null || _currentVm == null || _messagesList == null) return;

            var messages = _currentVm.Messages;
            var index = -1;

            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].Id == message.Id)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                Debug.WriteLine($"[ChatView] Message {message.Id} not found in collection");
                return;
            }

            var container = _messagesList.ContainerFromIndex(index);
            if (container is Control control)
            {
                control.BringIntoView();
                Debug.WriteLine($"[ChatView] Scrolled to message {message.Id} at index {index}");
            }
            else
            {
                Debug.WriteLine($"[ChatView] Container for index {index} not found, trying manual scroll");

                const double approximateItemHeight = 80.0;
                var targetOffset = index * approximateItemHeight;

                _scrollViewer.Offset = new Avalonia.Vector(
                    _scrollViewer.Offset.X,
                    Math.Max(0, targetOffset - (_scrollViewer.Viewport.Height / 2))
                );
            }
        }

        private void ScrollToEndAfterRender()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await System.Threading.Tasks.Task.Delay(50);

                Dispatcher.UIThread.Post(() =>
                {
                    if (MessagesScrollViewer != null)
                    {
                        MessagesScrollViewer.ScrollToEnd();
                        Debug.WriteLine("[ChatView] ScrollToEnd executed");
                    }
                }, DispatcherPriority.Loaded);

            }, DispatcherPriority.Background);
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            _scrollViewer = this.FindControl<ScrollViewer>("MessagesScrollViewer")
                       ?? this.FindDescendantOfType<ScrollViewer>();

            _messagesList = this.FindControl<ItemsControl>("MessagesList");

            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged += OnScrollChanged;
            }
        }

        private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_scrollViewer == null || DataContext is not ChatViewModel vm) return;

            _visibilityCheckTimer.Stop();
            _visibilityCheckTimer.Start();

            if (vm.IsSearchMode) return;

            if (_scrollViewer.Offset.Y < 100 && !vm.IsLoadingOlderMessages && !vm.IsInitialLoading)
            {
                var previousExtentHeight = _scrollViewer.Extent.Height;

                await vm.LoadOlderMessagesCommand.ExecuteAsync(null);

                Dispatcher.UIThread.Post(() =>
                {
                    if (_scrollViewer != null)
                    {
                        var newExtentHeight = _scrollViewer.Extent.Height;
                        var heightDiff = newExtentHeight - previousExtentHeight;

                        if (heightDiff > 0)
                        {
                            _scrollViewer.Offset = new Avalonia.Vector(
                                _scrollViewer.Offset.X,
                                _scrollViewer.Offset.Y + heightDiff
                            );
                        }
                    }
                }, DispatcherPriority.Loaded);
            }

            var distanceFromBottom = _scrollViewer.Extent.Height
                - _scrollViewer.Viewport.Height
                - _scrollViewer.Offset.Y;

            if (distanceFromBottom < 100 && vm.HasMoreNewer && !vm.IsInitialLoading)
            {
                await vm.LoadNewerMessagesCommand.ExecuteAsync(null);
            }

            var isAtBottom = distanceFromBottom < 50;
            vm.IsScrolledToBottom = isAtBottom;
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

                var topLeft = transform.Value.Transform(new Avalonia.Point(0, 0));
                var bottomRight = transform.Value.Transform(new Avalonia.Point(
                    control.Bounds.Width, control.Bounds.Height));

                var isVisible = bottomRight.Y > 0 && topLeft.Y < viewportHeight;

                if (isVisible)
                {
                    _processedMessageIds.Add(message.Id);
                    _ = _currentVm.OnMessageVisibleAsync(message);

                    Debug.WriteLine($"[ChatView] Message {message.Id} became visible, marking as read");
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

                if (_currentVm.Messages != null)
                {
                    _currentVm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
                }
            }

            base.OnUnloaded(e);
        }
    }
}