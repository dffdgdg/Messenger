using Avalonia.Controls;
using Avalonia.Interactivity;
using MessengerDesktop.ViewModels;
using System;

namespace MessengerDesktop.Views.Chat
{
    public partial class ChatView : UserControl
    {
        public ChatView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is ChatViewModel vm)
            {
                vm.Messages.CollectionChanged += (_, __) =>
                {
                    MessagesScrollViewer?.ScrollToEnd();
                };
            }
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                MessagesScrollViewer?.ScrollToEnd();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }
}