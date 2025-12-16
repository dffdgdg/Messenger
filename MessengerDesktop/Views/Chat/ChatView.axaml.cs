using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MessengerDesktop.ViewModels;
using System;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MessengerDesktop.Views.Chat
{
    public partial class ChatView : UserControl
    {
        private ScrollViewer? _scrollViewer;
        private ChatViewModel? _currentVm;
        private bool _pendingScrollToEnd;

        public ChatView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_currentVm != null)
            {
                _currentVm.PropertyChanged -= OnViewModelPropertyChanged;
                if (_currentVm.Messages != null)
                {
                    _currentVm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
                }
            }

            _currentVm = DataContext as ChatViewModel;

            if (_currentVm != null)
            {
                _currentVm.PropertyChanged += OnViewModelPropertyChanged;

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

                ScrollToEndAfterRender();
            }

            if (e.PropertyName == nameof(ChatViewModel.IsInitialLoading) &&
                !_currentVm.IsInitialLoading && _pendingScrollToEnd)
            {
                _pendingScrollToEnd = false;
                ScrollToEndAfterRender();
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

        /// <summary>
        /// Скролл к концу с ожиданием рендеринга элементов
        /// </summary>
        private void ScrollToEndAfterRender()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                await System.Threading.Tasks.Task.Delay(50);

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (MessagesScrollViewer != null)
                    {
                        MessagesScrollViewer.ScrollToEnd();
                        System.Diagnostics.Debug.WriteLine("[ChatView] ScrollToEnd executed");
                    }
                }, Avalonia.Threading.DispatcherPriority.Loaded);

            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            _scrollViewer = this.FindControl<ScrollViewer>("MessagesScrollViewer")
                       ?? this.FindDescendantOfType<ScrollViewer>();

            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged += OnScrollChanged;
            }

            if (_currentVm != null && !_currentVm.IsInitialLoading)
            {
                ScrollToEndAfterRender();
            }
        }

        private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_scrollViewer == null || DataContext is not ChatViewModel vm) return;

            if (_scrollViewer.Offset.Y < 100 && !vm.IsLoadingOlderMessages && !vm.IsInitialLoading)
            {
                var previousExtentHeight = _scrollViewer.Extent.Height;

                await vm.LoadOlderMessagesCommand.ExecuteAsync(null);

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
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
                }, Avalonia.Threading.DispatcherPriority.Loaded);
            }

            var isAtBottom = _scrollViewer.Offset.Y >=
                _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height - 50;

            vm.IsScrolledToBottom = isAtBottom;
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged -= OnScrollChanged;
            }

            if (_currentVm != null)
            {
                _currentVm.PropertyChanged -= OnViewModelPropertyChanged;
                if (_currentVm.Messages != null)
                {
                    _currentVm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
                }
            }

            base.OnUnloaded(e);
        }
    }
}