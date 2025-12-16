using Avalonia.Controls;
using Avalonia.Input;
using MessengerDesktop.ViewModels;

namespace MessengerDesktop.Views
{
    public partial class ChatEditDialog : UserControl
    {
        public ChatEditDialog()
        {
            InitializeComponent();
        }

        private void OnUserItemPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border &&
                border.DataContext is ChatEditDialogViewModel.SelectableUserItem user)
            {
                user.IsSelected = !user.IsSelected;
                e.Handled = true;
            }
        }
    }
}